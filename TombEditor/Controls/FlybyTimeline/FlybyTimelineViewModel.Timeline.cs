#nullable enable

using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using System.Windows.Threading;
using TombLib;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

public partial class FlybyTimelineViewModel
{
    #region Camera property editing

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

        var undoInstance = new ChangeObjectPropertyUndoInstance(_editor.UndoManager, SelectedCamera.Camera);

        _isApplyingProperty = true;

        try
        {
            setter(SelectedCamera.Camera);
            _editor.ObjectChange(SelectedCamera.Camera, ObjectChangeType.Change);
        }
        finally
        {
            _isApplyingProperty = false;
        }

        _preview.InvalidateCache();

        if (invalidateSequenceTiming)
            InvalidateSequenceTiming();

        QueueTimelineRefresh(refreshCameraList: false, refreshTimeline: refreshTimeline);
        PushUndoIfAny([undoInstance]);
    }

    #endregion Camera property editing

    #region Preview and playback

    /// <summary>
    /// Toggles static preview mode for the selected camera.
    /// </summary>
    [RelayCommand]
    private void TogglePreview() => _preview.TogglePreview(SelectedCamera?.Camera);

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
    /// Returns the current or freshly built sequence cache for use by the timeline.
    /// </summary>
    private FlybySequenceTiming GetSequenceTiming(IReadOnlyList<FlybyCameraInstance>? cameras = null)
    {
        var sequenceCameras = cameras ?? GetCamerasAsList();

        if (HasMatchingCachedSequenceTiming(sequenceCameras))
            return _cachedSequenceTiming!;

        var timing = FlybySequenceHelper.AnalyzeSequence(sequenceCameras, UseSmoothPause);
        CacheSequenceTiming(sequenceCameras, timing);
        return timing;
    }

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
        => float.IsFinite(timing.TotalDuration)
            ? Math.Max(timing.TotalDuration, 1.0f)
            : 1.0f;

    /// <summary>
    /// Returns segment indices whose outgoing spans are bypassed by camera cuts.
    /// </summary>
    private static IReadOnlySet<int> GetCutBypassedSegments(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        var cutBypassedSegments = new HashSet<int>();

        for (int i = 0; i < cameras.Count; i++)
        {
            var camera = cameras[i];

            if ((camera.Flags & FlybyConstants.FlagCameraCut) == 0)
                continue;

            int target = camera.Timer;

            for (int j = i; j < target && j < cameras.Count - 1; j++)
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
    /// Returns whether the cached sequence timing matches the provided camera list.
    /// </summary>
    private bool HasMatchingCachedSequenceTiming(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        var cachedTimingCameras = _cachedSequenceTimingCameras;

        if (_cachedSequenceTiming is null || cachedTimingCameras is null)
            return false;

        if (_cachedSequenceTiming.CameraCount != cameras.Count || cachedTimingCameras.Length != cameras.Count)
            return false;

        for (int i = 0; i < cameras.Count; i++)
        {
            if (!ReferenceEquals(cachedTimingCameras[i], cameras[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Stores sequence timing together with the camera list it was derived from.
    /// </summary>
    private void CacheSequenceTiming(IReadOnlyList<FlybyCameraInstance> cameras, FlybySequenceTiming timing)
    {
        _cachedSequenceTiming = timing;
        _cachedSequenceTimingCameras = [.. cameras];
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
    /// Deletes the provided cameras and refreshes the timeline when the editor rejects the operation.
    /// </summary>
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

        if (!WereAllCamerasDeleted(cameras))
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
    /// Returns whether every camera in the collection was removed from its room.
    /// </summary>
    private static bool WereAllCamerasDeleted(IEnumerable<FlybyCameraInstance> cameras)
        => cameras.All(camera => camera.Room is null);

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
    /// Returns the current sequence cameras from the editor state.
    /// </summary>
    private IReadOnlyList<FlybyCameraInstance> GetCamerasForCurrentSequence()
    {
        if (!SelectedSequence.HasValue || _editor.Level is null)
            return [];

        if (CameraList.Count > 0)
            return GetCamerasAsList();

        return FlybySequenceHelper.GetCameras(_editor.Level, SelectedSequence.Value);
    }

    /// <summary>
    /// Returns the visible camera items as a materialized camera list.
    /// </summary>
    private IReadOnlyList<FlybyCameraInstance> GetCamerasAsList()
    {
        if (_cachedVisibleCameras is not null && _cachedVisibleCameras.Count == CameraList.Count)
            return _cachedVisibleCameras;

        _cachedVisibleCameras = [.. CameraList.Select(vm => vm.Camera)];
        return _cachedVisibleCameras;
    }

    /// <summary>
    /// Clears the cached visible camera list and any timing derived from it.
    /// </summary>
    private void InvalidateVisibleCameraState()
    {
        _cachedVisibleCameras = null;
        _cachedSequenceTiming = null;
        _cachedSequenceTimingCameras = null;
    }

    /// <summary>
    /// Clears cached sequence timing while keeping the visible camera list.
    /// </summary>
    private void InvalidateSequenceTiming()
    {
        _cachedSequenceTiming = null;
        _cachedSequenceTimingCameras = null;
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
    /// Queues a batched timeline and preview refresh on the dispatcher so rapid property changes collapse into one update.
    /// </summary>
    private void QueueTimelineRefresh(bool refreshCameraList, bool syncPreview = true, bool refreshTimeline = true)
    {
        if (_isDisposed)
            return;

        _queuedTimelineRefreshCameraList |= refreshCameraList;
        _queuedTimelineRefreshTimeline |= refreshTimeline;
        _queuedTimelineRefreshPreview |= syncPreview;

        if (_isTimelineRefreshQueued)
            return;

        _isTimelineRefreshQueued = true;

        _queuedTimelineRefreshOperation = _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ProcessQueuedTimelineRefresh));
    }

    /// <summary>
    /// Cancels any queued dispatcher refresh and clears the accumulated refresh state.
    /// </summary>
    private void AbortQueuedTimelineRefresh()
    {
        if (_queuedTimelineRefreshOperation is not null &&
            _queuedTimelineRefreshOperation.Status is DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing)
        {
            _queuedTimelineRefreshOperation.Abort();
        }

        _queuedTimelineRefreshOperation = null;
        ClearQueuedTimelineRefreshState();
    }

    /// <summary>
    /// Runs the accumulated queued refresh work unless cleanup already disposed the view model.
    /// </summary>
    private void ProcessQueuedTimelineRefresh()
    {
        _queuedTimelineRefreshOperation = null;

        if (_isDisposed)
        {
            ClearQueuedTimelineRefreshState();
            return;
        }

        bool queuedRefreshCameraList = _queuedTimelineRefreshCameraList;
        bool queuedRefreshTimeline = _queuedTimelineRefreshTimeline;
        bool queuedRefreshPreview = _queuedTimelineRefreshPreview;

        ClearQueuedTimelineRefreshState();
        RefreshTimelineState(queuedRefreshCameraList, queuedRefreshPreview, queuedRefreshTimeline);
    }

    /// <summary>
    /// Clears the batched refresh flags after queued work is consumed or cancelled.
    /// </summary>
    private void ClearQueuedTimelineRefreshState()
    {
        _isTimelineRefreshQueued = false;
        _queuedTimelineRefreshCameraList = false;
        _queuedTimelineRefreshTimeline = false;
        _queuedTimelineRefreshPreview = false;
    }

    /// <summary>
    /// Updates the cached smooth-pause mode from the current editor state.
    /// </summary>
    private void RefreshTimingMode()
        => _useSmoothPause = _editor.Level?.Settings.GameVersion == TRVersion.Game.TombEngine;

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
