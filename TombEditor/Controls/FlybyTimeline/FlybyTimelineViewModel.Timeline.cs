#nullable enable

using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using TombLib;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

public partial class FlybyTimelineViewModel
{
    #region Camera property editing

    /// <summary>
    /// Captures the flyby camera properties tracked by the per-edit undo guard.
    /// </summary>
    private readonly struct FlybyCameraPropertySnapshot
    {
        public ushort Sequence { get; init; }
        public ushort Number { get; init; }
        public short Timer { get; init; }
        public ushort Flags { get; init; }
        public float Speed { get; init; }
        public float Fov { get; init; }
        public float Roll { get; init; }
        public float RotationX { get; init; }
        public float RotationY { get; init; }

        /// <summary>
        /// Captures the current tracked property values from a flyby camera.
        /// </summary>
        /// <param name="camera">Camera whose tracked editable properties should be snapshotted.</param>
        /// <returns>A snapshot containing the camera state used to detect no-op edits.</returns>
        public static FlybyCameraPropertySnapshot Capture(FlybyCameraInstance camera) => new()
        {
            Sequence = camera.Sequence,
            Number = camera.Number,
            Timer = camera.Timer,
            Flags = camera.Flags,
            Speed = camera.Speed,
            Fov = camera.Fov,
            Roll = camera.Roll,
            RotationX = camera.RotationX,
            RotationY = camera.RotationY
        };
    }

    /// <summary>
    /// Applies a speed edit and refreshes timing-dependent timeline state.
    /// </summary>
    partial void OnCameraSpeedChanged(float value)
        => ApplyPropertyToCamera(c => c.Speed = value, invalidateSequenceTiming: true, refreshTimeline: true);

    /// <summary>
    /// Applies a field-of-view edit to the selected camera.
    /// </summary>
    partial void OnCameraFovChanged(float value)
        => ApplyPropertyToCamera(c => c.Fov = value, invalidateSequenceTiming: false, refreshTimeline: false);

    /// <summary>
    /// Applies a roll edit to the selected camera.
    /// </summary>
    partial void OnCameraRollChanged(float value)
        => ApplyPropertyToCamera(c => c.Roll = value, invalidateSequenceTiming: false, refreshTimeline: false);

    /// <summary>
    /// Applies an X rotation edit to the selected camera.
    /// </summary>
    partial void OnCameraRotationXChanged(float value)
        => ApplyPropertyToCamera(c => c.RotationX = value, invalidateSequenceTiming: false, refreshTimeline: false);

    /// <summary>
    /// Applies a Y rotation edit to the selected camera.
    /// </summary>
    partial void OnCameraRotationYChanged(float value)
        => ApplyPropertyToCamera(c => c.RotationY = value, invalidateSequenceTiming: false, refreshTimeline: false);

    /// <summary>
    /// Applies a timer edit to the selected camera.
    /// </summary>
    partial void OnCameraTimerChanged(short value)
        => ApplyPropertyToCamera(c => c.Timer = value, invalidateSequenceTiming: true, refreshTimeline: true);

    /// <summary>
    /// Applies a flag edit to the selected camera.
    /// </summary>
    partial void OnCameraFlagsChanged(ushort value)
        => ApplyPropertyToCamera(c => c.Flags = value, invalidateSequenceTiming: true, refreshTimeline: true);

    /// <summary>
    /// Applies a property change to the selected camera and records undo state.
    /// </summary>
    /// <param name="setter">Action that applies the property change to the camera.</param>
    /// <param name="invalidateSequenceTiming">Whether to invalidate the sequence timing after the change.</param>
    /// <param name="refreshTimeline">Whether to refresh the timeline after the change.</param>
    private void ApplyPropertyToCamera(Action<FlybyCameraInstance> setter,
        bool invalidateSequenceTiming, bool refreshTimeline)
    {
        if (_isUpdating || SelectedCamera is null)
            return;

        var camera = SelectedCamera.Camera;
        var originalState = FlybyCameraPropertySnapshot.Capture(camera);
        var undoInstance = new ChangeObjectPropertyUndoInstance(_editor.UndoManager, camera);

        _isApplyingProperty = true;

        try
        {
            setter(camera);
        }
        finally
        {
            _isApplyingProperty = false;
        }

        if (!HasTrackedPropertyChanges(camera, originalState))
            return;

        _editor.ObjectChange(camera, ObjectChangeType.Change);

        _preview.InvalidateCache();

        if (invalidateSequenceTiming)
            InvalidateSequenceTiming();

        QueueTimelineRefresh(refreshCameraList: false, refreshTimeline: refreshTimeline);
        PushUndoIfAny([undoInstance]);
    }

    /// <summary>
    /// Returns whether the camera changed in any property tracked by the edit helper.
    /// </summary>
    /// <param name="camera">Camera after the attempted property edit.</param>
    /// <param name="originalState">Snapshot captured before the edit was applied.</param>
    /// <returns><see langword="true"/> when any tracked property changed; otherwise <see langword="false"/>.</returns>
    private static bool HasTrackedPropertyChanges(FlybyCameraInstance camera, FlybyCameraPropertySnapshot originalState)
    {
        return camera.Sequence != originalState.Sequence ||
            camera.Number != originalState.Number ||
            camera.Timer != originalState.Timer ||
            camera.Flags != originalState.Flags ||
            camera.Speed != originalState.Speed ||
            camera.Fov != originalState.Fov ||
            camera.Roll != originalState.Roll ||
            camera.RotationX != originalState.RotationX ||
            camera.RotationY != originalState.RotationY;
    }

    #endregion Camera property editing

    #region Preview and playback

    /// <summary>
    /// Starts or stops sequence playback.
    /// </summary>
    [RelayCommand]
    private void TogglePlayStop()
    {
        if (IsPlaying)
            _preview.StopPlayback();
        else if (SelectedSequence.HasValue)
            _preview.StartPlayback(GetCamerasAsList(), SelectedSequence.Value);
    }

    /// <summary>
    /// Scrubs the timeline to a specific time in seconds.
    /// </summary>
    public void ScrubToTime(float timeSeconds)
    {
        if (!SelectedSequence.HasValue || CameraList.Count == 0 || !float.IsFinite(timeSeconds))
            return;

        var cameras = GetCamerasAsList();
        _preview.ScrubToTime(cameras, SelectedSequence.Value, timeSeconds);

        var frame = _preview.GetInterpolatedFrameAtTime(cameras, SelectedSequence.Value, timeSeconds);

        if (frame.HasValue)
            UpdateSelectedRoomByPosition(frame.Value.Position);
    }

    /// <summary>
    /// Updates the selected room when the preview position crosses into a different room.
    /// </summary>
    public void UpdateSelectedRoomByPosition(Vector3 worldPosition)
    {
        if (_editor.Level is null || !worldPosition.IsFinite())
            return;

        var room = _editor.GetRoomAtPosition(worldPosition);

        if (room is null || room == _editor.SelectedRoom)
            return;

        _editor.SelectedRoom = room;
        _editor.ResetCamera(false, room);
    }

    /// <summary>
    /// Called when a camera is dragged on the timeline to a new timecode position.
    /// </summary>
    public void OnTimelineCameraDragged(int cameraIndex, float newTimeSeconds)
    {
        if (cameraIndex <= 0 || cameraIndex >= CameraList.Count || !float.IsFinite(newTimeSeconds))
            return;

        _preview.StopPlayback();

        EnsureTimelineDragUndoSnapshot(cameraIndex);

        var cameras = GetCamerasAsList();
        var timing = GetSequenceTiming(cameras);
        var previousCamera = CameraList[cameraIndex - 1].Camera;
        float prevTime = timing.GetCameraTime(cameraIndex - 1);
        float freezeAtPrev = timing.GetFreezeDuration(cameraIndex - 1);
        float minTargetTime = prevTime + freezeAtPrev + FlybyConstants.TimeStep;
        float targetTime = Math.Max(newTimeSeconds, minTargetTime);

        float newSpeed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(
            cameras,
            cameraIndex - 1,
            cameraIndex,
            targetTime,
            UseSmoothPause);

        _isApplyingProperty = true;

        try
        {
            previousCamera.Speed = newSpeed;
            _editor.ObjectChange(previousCamera, ObjectChangeType.Change);
        }
        finally
        {
            _isApplyingProperty = false;
        }

        _preview.InvalidateCache();
        InvalidateSequenceTiming();
        RefreshTimelineState(false);
    }

    /// <summary>
    /// Clears temporary drag state after a timeline drag finishes.
    /// </summary>
    public void OnTimelineCameraDragCompleted() => _activeDraggedCameraIndex = -1;

    #endregion Preview and playback

    #region Timecode helpers

    /// <summary>
    /// Returns the timeline time for a camera index in the current sequence.
    /// </summary>
    private float GetTimecodeForCamera(int index) => GetSequenceTiming().GetCameraTime(index);

    /// <summary>
    /// Builds the marker and cache state needed by the timeline control.
    /// </summary>
    public TimelineRenderState BuildTimelineRenderState()
    {
        var cameras = GetCamerasAsList();
        var cache = GetSequenceCache(cameras);
        FlybySequenceTiming timing;

        if (cache is not null && cache.Timing.CameraCount == cameras.Count)
        {
            timing = cache.Timing;
            CacheSequenceTiming(cameras, timing);
        }
        else
        {
            timing = GetSequenceTiming(cameras);
        }

        var selectedIndices = GetSelectedIndices();
        var cutBypassedSegments = GetCutBypassedSegments(cameras);
        var markers = new List<FlybyTimelineControl.TimelineMarker>(CameraList.Count);

        for (int i = 0; i < CameraList.Count; i++)
            markers.Add(BuildTimelineMarker(CameraList[i], i, selectedIndices, cutBypassedSegments, timing));

        return new TimelineRenderState(markers, cache, GetNormalizedTimelineDuration(timing));
    }

    /// <summary>
    /// Builds the rendered timeline marker data for a single camera.
    /// </summary>
    private static FlybyTimelineControl.TimelineMarker BuildTimelineMarker(FlybyCameraItemViewModel item, int index,
        IReadOnlySet<int> selectedIndices, IReadOnlySet<int> cutBypassedSegments, FlybySequenceTiming timing)
    {
        var camera = item.Camera;

        return new FlybyTimelineControl.TimelineMarker
        {
            TimeSeconds = timing.GetCameraTime(index),
            IsDuplicate = item.IsDuplicateIndex,
            IsSelected = selectedIndices.Contains(index),
            HasCameraCut = (camera.Flags & FlybyConstants.FlagCameraCut) != 0,
            IsInCutBypass = cutBypassedSegments.Contains(index),
            CutBypassDuration = timing.GetCutBypassDuration(index),
            SegmentDuration = index < timing.CameraCount - 1 ? timing.GetSegmentDuration(index) : 0.0f,
            HasFreeze = (camera.Flags & FlybyConstants.FlagFreezeCamera) != 0,
            FreezeDuration = timing.GetFreezeDuration(index)
        };
    }

    /// <summary>
    /// Returns the duration the timeline should expose to the control.
    /// </summary>
    private static float GetNormalizedTimelineDuration(FlybySequenceTiming timing)
        => float.IsFinite(timing.TotalDuration) ? Math.Max(timing.TotalDuration, 1.0f) : 1.0f;

    /// <summary>
    /// Returns segment indices whose outgoing spans are bypassed by camera cuts.
    /// </summary>
    private static IReadOnlySet<int> GetCutBypassedSegments(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        var cutBypassedSegments = new HashSet<int>();

        for (int i = 0; i < cameras.Count; i++)
        {
            if (!FlybySequenceHelper.TryResolveCutTargetIndex(cameras, i, out int targetIndex))
                continue;

            for (int j = i; j < targetIndex && j < cameras.Count - 1; j++)
                cutBypassedSegments.Add(j);
        }

        return cutBypassedSegments;
    }

    /// <summary>
    /// Returns the sequence cache for the current selection when available.
    /// </summary>
    private FlybySequenceCache? GetSequenceCache(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        if (!SelectedSequence.HasValue)
            return null;

        var cache = _preview.GetOrBuildCache(cameras, SelectedSequence.Value);

        if (cache is not null && cache.Timing.CameraCount == cameras.Count)
            CacheSequenceTiming(cameras, cache.Timing);

        return cache;
    }

    /// <summary>
    /// Recomputes formatted timecodes for every visible camera item.
    /// </summary>
    private void RecalculateTimecodes()
    {
        var timing = GetSequenceTiming();

        for (int i = 0; i < CameraList.Count; i++)
            CameraList[i].Timecode = FlybySequenceHelper.FormatTimecode(timing.GetCameraTime(i));
    }

    /// <summary>
    /// Updates the formatted playhead timecode whenever the playhead position changes.
    /// </summary>
    partial void OnPlayheadSecondsChanged(float value)
    {
        float seconds = float.IsFinite(value) && value >= 0.0f ? value : 0.0f;
        PlayheadTimecode = FlybySequenceHelper.FormatTimecode(seconds);
    }

    /// <summary>
    /// Clears the visible playhead when sequence context changes.
    /// </summary>
    private void ResetPlayhead() => PlayheadSeconds = -1.0f;

    #endregion Timecode helpers

    #region Data refresh

    /// <summary>
    /// Refreshes the full timeline state after underlying data changes.
    /// </summary>
    private void OnDataChanged() => RefreshTimelineState(true);

    /// <summary>
    /// Requests the view to zoom the timeline to fit the current sequence.
    /// </summary>
    private void RequestZoomToFit() => ZoomToFitRequested?.Invoke();

    /// <summary>
    /// Deletes the provided cameras and restores the visible timeline state if any camera remains.
    /// </summary>
    /// <param name="cameras">The cameras that should be deleted.</param>
    /// <param name="dialogOwner">Optional owner for confirmation or error dialogs raised by the delete action.</param>
    /// <returns><see langword="true"/> when all requested cameras were deleted successfully; <see langword="false"/> when any requested camera remains in the level after the delete action.</returns>
    private bool TryDeleteCameras(IReadOnlyCollection<FlybyCameraInstance> cameras, IWin32Window? dialogOwner)
    {
        _preview.StopPlayback();

        _isApplyingProperty = true;

        try
        {
            EditorActions.DeleteObjects(cameras.Cast<ObjectInstance>(), dialogOwner, false);
        }
        finally
        {
            _isApplyingProperty = false;
        }

        if (cameras.Any(camera => camera.Room is not null)) // Are there any remaining cameras after deletion?
        {
            RefreshTimelineState(true, false);
            return false;
        }

        _preview.InvalidateCache();
        return true;
    }

    /// <summary>
    /// Rebuilds the available sequence list and preserves selection when possible.
    /// </summary>
    private void RefreshSequenceList()
    {
        var currentSelection = SelectedSequence;
        var sequences = new HashSet<ushort>(_userAddedSequences);

        if (_editor.Level is not null)
        {
            foreach (ushort seq in FlybySequenceHelper.GetAllSequences(_editor.Level))
                sequences.Add(seq);
        }

        AvailableSequences.Clear();

        foreach (ushort seq in sequences.OrderBy(s => s))
            AvailableSequences.Add(seq);

        if (currentSelection.HasValue && AvailableSequences.Contains(currentSelection.Value))
            SelectedSequence = currentSelection.Value;
        else if (AvailableSequences.Count > 0)
            SelectedSequence = AvailableSequences[0];
        else
            SelectedSequence = null;
    }

    /// <summary>
    /// Rebuilds the camera list for the currently selected sequence.
    /// </summary>
    private void RefreshCameraList()
    {
        CameraList.Clear();
        InvalidateVisibleCameraState();

        if (!SelectedSequence.HasValue || _editor.Level is null)
        {
            RestoreSelectedCameraState();
            return;
        }

        var cameras = FlybySequenceHelper.GetCameras(_editor.Level, SelectedSequence.Value);
        _cachedVisibleCameras = [.. cameras];

        var duplicateNumbers = cameras
            .GroupBy(c => c.Number)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var cam in cameras)
        {
            CameraList.Add(new FlybyCameraItemViewModel(cam)
            {
                IsDuplicateIndex = duplicateNumbers.Contains(cam.Number)
            });
        }

        RestoreSelectedCameraState();
    }

    /// <summary>
    /// Inserts a sequence id into the available sequence list in sorted order.
    /// </summary>
    private void InsertSequenceSorted(ushort sequence)
    {
        int insertIndex = 0;

        for (int i = 0; i < AvailableSequences.Count; i++)
        {
            if (AvailableSequences[i] > sequence)
                break;

            insertIndex = i + 1;
        }

        AvailableSequences.Insert(insertIndex, sequence);
    }

    /// <summary>
    /// Renumbers cameras in a sequence and updates cut targets to match.
    /// </summary>
    private void RenumberSequence(ushort sequence, FlybyCameraInstance? excludeFromEvent = null)
    {
        if (_editor.Level is null)
            return;

        var cameras = FlybySequenceHelper.GetCameras(_editor.Level, sequence);
        var oldTargetByNumber = BuildCameraLookupByNumber(cameras);

        _isApplyingProperty = true;

        try
        {
            ApplySequentialCameraNumbers(cameras, excludeFromEvent);
            UpdateCameraCutTargets(cameras, oldTargetByNumber, camera => camera.Timer, excludeFromEvent);
        }
        finally
        {
            _isApplyingProperty = false;
        }
    }

    /// <summary>
    /// Builds a lookup of cameras keyed by their current number.
    /// </summary>
    private static Dictionary<int, FlybyCameraInstance> BuildCameraLookupByNumber(IReadOnlyList<FlybyCameraInstance> cameras)
        => cameras.GroupBy(camera => (int)camera.Number).ToDictionary(group => group.Key, group => group.First());

    /// <summary>
    /// Renumbers cameras to match their current order and raises editor change events for updates.
    /// </summary>
    private void ApplySequentialCameraNumbers(IReadOnlyList<FlybyCameraInstance> cameras, FlybyCameraInstance? excludeFromEvent = null)
    {
        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].Number != (ushort)i)
            {
                cameras[i].Number = (ushort)i;

                if (cameras[i] != excludeFromEvent)
                    _editor.ObjectChange(cameras[i], ObjectChangeType.Change);
            }
        }
    }

    /// <summary>
    /// Updates camera-cut timers after camera numbering changes.
    /// </summary>
    private void UpdateCameraCutTargets(IReadOnlyList<FlybyCameraInstance> cameras,
        IReadOnlyDictionary<int, FlybyCameraInstance> oldTargetByNumber,
        Func<FlybyCameraInstance, short> getOriginalTimer,
        FlybyCameraInstance? excludeFromEvent = null)
    {
        foreach (var camera in cameras)
        {
            if ((camera.Flags & FlybyConstants.FlagCameraCut) == 0)
                continue;

            ushort originalFlags = camera.Flags;
            short originalTimer = getOriginalTimer(camera);

            if (oldTargetByNumber.TryGetValue(originalTimer, out var targetCamera) && cameras.Contains(targetCamera))
            {
                camera.Timer = (short)targetCamera.Number;
            }
            else
            {
                camera.Flags = (ushort)(camera.Flags & ~FlybyConstants.FlagCameraCut);
                camera.Timer = 0;
            }

            if ((camera.Flags != originalFlags || camera.Timer != originalTimer) && camera != excludeFromEvent)
                _editor.ObjectChange(camera, ObjectChangeType.Change);
        }
    }

    /// <summary>
    /// Refreshes camera data and optionally syncs the preview output.
    /// </summary>
    private void RefreshTimelineState(bool refreshCameraList, bool syncPreview = true, bool refreshTimeline = true)
    {
        if (_isDisposed)
            return;

        if (refreshCameraList)
            RefreshCameraList();

        if (refreshTimeline)
        {
            RecalculateTimecodes();
            TimelineRefreshRequested?.Invoke();
        }

        if (syncPreview)
            RefreshPreviewState();
    }

    /// <summary>
    /// Updates the preview camera or scrub state from the current selection.
    /// </summary>
    private void RefreshPreviewState()
    {
        if (_isDisposed)
            return;

        if (!TryGetSequenceContext(out var cameras, out var sequence))
        {
            if (SelectedCamera is not null)
                _preview.ShowCamera(SelectedCamera.Camera);

            return;
        }

        if (IsPreviewActive && PlayheadSeconds >= 0)
            _preview.ScrubToTime(cameras, sequence, PlayheadSeconds);
        else if (SelectedCamera is not null)
            _preview.ShowCamera(SelectedCamera.Camera);
    }

    /// <summary>
    /// Returns the active sequence context needed for preview operations.
    /// </summary>
    /// <param name="cameras">Receives the currently visible flyby cameras when sequence context is available.</param>
    /// <param name="sequence">Receives the currently selected sequence id when sequence context is available.</param>
    /// <returns><see langword="true"/> when a sequence is selected and the current camera list is available for preview operations; <see langword="false"/> when preview operations should be skipped.</returns>
    private bool TryGetSequenceContext(out IReadOnlyList<FlybyCameraInstance> cameras, out ushort sequence)
    {
        if (SelectedSequence.HasValue && CameraList.Count > 0)
        {
            sequence = SelectedSequence.Value;
            cameras = GetCamerasAsList();
            return true;
        }

        cameras = [];
        sequence = 0;
        return false;
    }

    #endregion Data refresh

    #region Preview state sync

    /// <summary>
    /// Synchronizes bindable preview flags from the preview controller.
    /// </summary>
    private void OnPreviewStateChanged()
    {
        if (_isDisposed)
            return;

        IsPlaying = _preview.IsPlaying;
        OnPropertyChanged(nameof(IsPreviewActive));
        OnPropertyChanged(nameof(CanEditProperties));
    }

    /// <summary>
    /// Synchronizes the playhead position from preview playback.
    /// </summary>
    private void OnPreviewPlayheadChanged()
    {
        if (_isDisposed)
            return;

        PlayheadSeconds = _preview.PlayheadSeconds;
    }

    #endregion Preview state sync
}
