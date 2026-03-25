#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Threading;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Main view model for the Flyby Sequence Manager window.
/// Delegates data operations to FlybySequenceHelper and preview to FlybyPreviewController.
/// </summary>
public partial class FlybyTimelineViewModel : ObservableObject
{
    private readonly Editor _editor;
    private readonly FlybyPreviewController _preview;
    private readonly Dispatcher _dispatcher;

    private bool _isUpdating;
    private bool _isApplyingProperty;
    private bool _isSyncingSelection;

    public ObservableCollection<ushort> AvailableSequences { get; } = new();
    public ObservableCollection<FlybyCameraItemViewModel> CameraList { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequenceSelected))]
    private ushort? _selectedSequence;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProperties))]
    private FlybyCameraItemViewModel? _selectedCamera;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayStopIcon))]
    private bool _isPlaying;

    public bool IsPreviewActive => _editor.CameraPreviewMode != CameraPreviewType.None;

    // Camera properties for the selected camera.
    [ObservableProperty]
    private float _cameraSpeed;

    [ObservableProperty]
    private float _cameraFov;

    [ObservableProperty]
    private float _cameraRoll;

    [ObservableProperty]
    private float _cameraRotationX;

    [ObservableProperty]
    private float _cameraRotationY;

    [ObservableProperty]
    private short _cameraTimer;

    [ObservableProperty]
    private ushort _cameraFlags;

    // Playhead timecode display.
    [ObservableProperty]
    private string _playheadTimecode = "00:00.00";

    // Playhead position in seconds (negative = hidden).
    [ObservableProperty]
    private float _playheadSeconds = -1.0f;

    public string PlayStopIcon => IsPlaying
        ? "pack://application:,,,/TombEditor;component/Resources/icons_transport/transport-stop-24.png"
        : "pack://application:,,,/TombEditor;component/Resources/icons_transport/transport-play-24.png";
    public bool HasSequenceSelected => SelectedSequence.HasValue;
    public bool CanEditProperties => SelectedCamera != null && !IsPlaying;

    // Selected items for multi-select support.
    private readonly List<FlybyCameraItemViewModel> _selectedCameras = new();
    public IReadOnlyList<FlybyCameraItemViewModel> SelectedCameras => _selectedCameras;

    // Temporary sequences added by user (persist until window closes).
    private readonly HashSet<ushort> _userAddedSequences = new();

    /// <summary>
    /// Fired when the timeline needs a visual refresh.
    /// </summary>
    public event Action? TimelineRefreshRequested;

    public FlybyTimelineViewModel(Editor editor, Dispatcher dispatcher)
    {
        _editor = editor;
        _dispatcher = dispatcher;

        _preview = new FlybyPreviewController(editor);
        _preview.StateChanged += OnPreviewStateChanged;
        _preview.PlayheadChanged += OnPreviewPlayheadChanged;

        _editor.EditorEventRaised += OnEditorEventRaised;

        RefreshSequenceList();

        if (AvailableSequences.Count > 0)
            SelectedSequence = AvailableSequences[0];
    }

    public void Cleanup()
    {
        _preview.StateChanged -= OnPreviewStateChanged;
        _preview.PlayheadChanged -= OnPreviewPlayheadChanged;
        _preview.Dispose();

        _editor.EditorEventRaised -= OnEditorEventRaised;
    }

    #region Sequence management

    [RelayCommand]
    private void AddSequence()
    {
        ushort newIndex = 0;

        while (AvailableSequences.Contains(newIndex))
            newIndex++;

        _userAddedSequences.Add(newIndex);
        InsertSequenceSorted(newIndex);
        SelectedSequence = newIndex;
    }

    [RelayCommand]
    private void RemoveSequence()
    {
        if (!SelectedSequence.HasValue)
            return;

        ushort seq = SelectedSequence.Value;
        var cameras = GetCamerasForCurrentSequence();

        if (cameras.Count > 0)
        {
            var result = MessageBox.Show(
                $"Deleting sequence {seq} will remove {cameras.Count} flyby camera(s). Continue?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            var rooms = cameras.ToDictionary(c => c, c => c.Room);

            foreach (var cam in cameras)
                EditorActions.DeleteObjectWithoutUpdate(cam);

            foreach (var cam in cameras)
                _editor.ObjectChange(cam, ObjectChangeType.Remove, rooms[cam]);
        }

        _userAddedSequences.Remove(seq);
        AvailableSequences.Remove(seq);

        SelectedSequence = AvailableSequences.Count > 0 ? AvailableSequences[0] : null;
    }

    partial void OnSelectedSequenceChanged(ushort? value)
    {
        _preview.StopPlayback();
        _preview.InvalidateCache();

        RefreshCameraList();
        RecalculateTimecodes();

        // Auto-select first camera in the new sequence.
        SelectedCamera = CameraList.Count > 0 ? CameraList[0] : null;
    }

    #endregion Sequence management

    #region Camera list management

    /// <summary>
    /// Deletes all currently selected cameras from the level.
    /// </summary>
    public void DeleteSelectedCameras()
    {
        if (_selectedCameras.Count == 0 || !SelectedSequence.HasValue)
            return;

        var toDelete = _selectedCameras.Select(vm => vm.Camera).ToList();

        _isApplyingProperty = true;
        EditorActions.DeleteObjects(toDelete);
        _isApplyingProperty = false;

        _selectedCameras.Clear();

        RenumberSequence(SelectedSequence.Value);
        OnDataChanged();

        SelectedCamera = null;
    }

    [RelayCommand]
    private void AddCamera()
    {
        if (!SelectedSequence.HasValue || _editor.Level == null)
            return;

        ushort seq = SelectedSequence.Value;
        var room = _editor.SelectedRoom;

        if (room == null)
            return;

        var cam = new FlybyCameraInstance
        {
            Sequence = seq,
        };

        // Insert at cursor position when playhead is valid and there are existing cameras.
        if (PlayheadSeconds >= 0 && CameraList.Count >= 1)
        {
            float cursorTime = PlayheadSeconds;
            var cameras = GetCamerasAsList();
            float clampedCursorTime = Math.Max(cursorTime, 0.01f);

            float lastCameraTime = FlybySequenceHelper.GetTimecodeForCamera(cameras, cameras.Count - 1);

            // Cursor is at or past the last camera: append with appropriate speed.
            if (cursorTime >= lastCameraTime - 0.01f)
            {
                cam.Speed = cameras[^1].Speed;
                cam.Number = (ushort)cameras.Count;

                // Use editor camera view for cameras appended beyond existing spline.
                ApplyEditorCameraPosition(cam, room);

                var tempCameras = cameras.ToList();
                tempCameras.Add(cam);
                float targetTime = Math.Max(clampedCursorTime, lastCameraTime + 0.01f);
                float newSpeed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(tempCameras, tempCameras.Count - 2, tempCameras.Count - 1, targetTime);

                cameras[^1].Speed = newSpeed;
                _editor.ObjectChange(cameras[^1], ObjectChangeType.Change);

                room.AddObject(_editor.Level, cam);
                _editor.UndoManager.PushObjectCreated(cam);
                _editor.ObjectChange(cam, ObjectChangeType.Add);

                RenumberSequence(seq, cam);
                OnDataChanged();
                SelectCameraByInstance(cam);
                return;
            }

            int insertIndex = FlybySequenceHelper.FindInsertionIndex(cameras, cursorTime);

            if (insertIndex > 0 && insertIndex < cameras.Count)
            {
                int prevIndex = insertIndex - 1;

                float segStart = FlybySequenceHelper.GetTimecodeForCamera(cameras, prevIndex);
                float segEnd = FlybySequenceHelper.GetTimecodeForCamera(cameras, insertIndex);
                cam.Number = (ushort)insertIndex;
                cam.Speed = cameras[prevIndex].Speed;

                // Always place new camera at the current editor viewport position.
                ApplyEditorCameraPosition(cam, room);

                var tempCameras = cameras.ToList();
                tempCameras.Insert(insertIndex, cam);
                float insertTime = Math.Clamp(clampedCursorTime, segStart + 0.01f, segEnd - 0.01f);

                cameras[prevIndex].Speed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(tempCameras, prevIndex, insertIndex, insertTime);
                cam.Speed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(tempCameras, insertIndex, insertIndex + 1, segEnd);

                _editor.ObjectChange(cameras[prevIndex], ObjectChangeType.Change);

                room.AddObject(_editor.Level, cam);
                _editor.UndoManager.PushObjectCreated(cam);
                _editor.ObjectChange(cam, ObjectChangeType.Add);

                RenumberSequence(seq, cam);
                OnDataChanged();
                SelectCameraByInstance(cam);
                return;
            }
        }

        // Default: append camera at end using current editor viewport.
        int nextNumber = CameraList.Count > 0 ? CameraList.Max(c => c.Number) + 1 : 0;
        cam.Number = (ushort)nextNumber;

        ApplyEditorCameraPosition(cam, room);

        room.AddObject(_editor.Level, cam);
        _editor.UndoManager.PushObjectCreated(cam);
        _editor.ObjectChange(cam, ObjectChangeType.Add);

        OnDataChanged();

        if (CameraList.Count > 0)
            SelectedCamera = CameraList.Last();
    }

    private void ApplyEditorCameraPosition(FlybyCameraInstance cam, Room room)
    {
        var editorCamera = _editor.GetViewportCamera?.Invoke();

        if (editorCamera != null)
        {
            cam.Position = editorCamera.GetPosition() - room.WorldPos;
            FlybySequenceHelper.ApplyEditorCameraRotation(editorCamera, cam);
        }
        else
        {
            cam.Position = room.GetLocalCenter();
        }
    }

    public void UpdateSelectedCameras(IEnumerable<FlybyCameraItemViewModel> items)
    {
        _selectedCameras.Clear();
        _selectedCameras.AddRange(items);

        if (_selectedCameras.Count == 1)
            SelectedCamera = _selectedCameras[0];
        else if (_selectedCameras.Count == 0)
            SelectedCamera = null;

        SyncEditorSelection();
    }

    /// <summary>
    /// Pushes current timeline selection into Editor.SelectedObject as an ObjectGroup.
    /// </summary>
    private void SyncEditorSelection()
    {
        if (_isSyncingSelection)
            return;

        _isSyncingSelection = true;

        if (_selectedCameras.Count == 0)
        {
            if (_editor.SelectedObject is FlybyCameraInstance || _editor.SelectedObject is ObjectGroup)
                _editor.SelectedObject = null;
        }
        else if (_selectedCameras.Count == 1)
        {
            _editor.SelectedObject = _selectedCameras[0].Camera;
        }
        else
        {
            var objects = _selectedCameras
                .Select(vm => (PositionBasedObjectInstance)vm.Camera)
                .ToList();

            _editor.SelectedObject = new ObjectGroup(objects);
        }

        _isSyncingSelection = false;
    }

    /// <summary>
    /// Returns indices of all currently selected cameras.
    /// </summary>
    public HashSet<int> GetSelectedIndices()
    {
        var result = new HashSet<int>();

        for (int i = 0; i < CameraList.Count; i++)
        {
            if (_selectedCameras.Contains(CameraList[i]))
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Moves a camera from one list index to another via drag-reorder.
    /// </summary>
    public void MoveCameraToIndex(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= CameraList.Count ||
            toIndex < 0 || toIndex >= CameraList.Count ||
            fromIndex == toIndex)
            return;

        var movedCamera = CameraList[fromIndex].Camera;
        var cameras = CameraList.Select(vm => vm.Camera).ToList();

        cameras.RemoveAt(fromIndex);
        cameras.Insert(toIndex, movedCamera);

        _isApplyingProperty = true;

        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].Number != (ushort)i)
            {
                cameras[i].Number = (ushort)i;
                _editor.ObjectChange(cameras[i], ObjectChangeType.Change);
            }
        }

        _isApplyingProperty = false;

        OnDataChanged();
        SelectCameraByInstance(movedCamera);
    }

    partial void OnSelectedCameraChanged(FlybyCameraItemViewModel? value)
    {
        _isUpdating = true;

        if (value != null)
        {
            CameraSpeed = value.Camera.Speed;
            CameraFov = value.Camera.Fov;
            CameraRoll = value.Camera.Roll;
            CameraRotationX = value.Camera.RotationX;
            CameraRotationY = value.Camera.RotationY;
            CameraTimer = value.Camera.Timer;
            CameraFlags = value.Camera.Flags;
        }

        _isUpdating = false;

        OnPropertyChanged(nameof(CanEditProperties));
    }

    #endregion Camera list management

    #region Camera property editing

    partial void OnCameraSpeedChanged(float value)
    {
        ApplyPropertyToCamera(c => c.Speed = value);
        RequestTimelineRefresh();
    }

    partial void OnCameraFovChanged(float value) => ApplyPropertyToCamera(c => c.Fov = value);
    partial void OnCameraRollChanged(float value) => ApplyPropertyToCamera(c => c.Roll = value);
    partial void OnCameraRotationXChanged(float value) => ApplyPropertyToCamera(c => c.RotationX = value);
    partial void OnCameraRotationYChanged(float value) => ApplyPropertyToCamera(c => c.RotationY = value);
    partial void OnCameraTimerChanged(short value) => ApplyPropertyToCamera(c => c.Timer = value);
    partial void OnCameraFlagsChanged(ushort value) => ApplyPropertyToCamera(c => c.Flags = value);

    public bool GetFlag(int bit) => (CameraFlags & (1 << bit)) != 0;

    public void SetFlag(int bit, bool value)
    {
        if (value)
            CameraFlags = (ushort)(CameraFlags | (1 << bit));
        else
            CameraFlags = (ushort)(CameraFlags & ~(1 << bit));
    }

    private void ApplyPropertyToCamera(Action<FlybyCameraInstance> setter)
    {
        if (_isUpdating || SelectedCamera == null)
            return;

        _isApplyingProperty = true;
        setter(SelectedCamera.Camera);
        _editor.ObjectChange(SelectedCamera.Camera, ObjectChangeType.Change);
        _isApplyingProperty = false;

        RefreshTimelineState(false);
    }

    #endregion Camera property editing

    #region Preview and playback

    [RelayCommand]
    private void TogglePreview()
    {
        _preview.TogglePreview(SelectedCamera?.Camera);
    }

    [RelayCommand]
    private void TogglePlayStop()
    {
        if (IsPlaying)
            _preview.StopPlayback();
        else if (SelectedSequence.HasValue)
            _preview.StartPlayback(GetCamerasAsList(), SelectedSequence.Value);
    }

    /// <summary>
    /// Ensures preview is active, entering it if needed.
    /// Called when any timeline interaction should show the camera in Panel3D.
    /// </summary>
    public void EnsurePreviewActive()
    {
        if (!IsPreviewActive)
            _preview.EnterPreview(SelectedCamera?.Camera);
        else if (SelectedCamera != null)
            _preview.ShowCamera(SelectedCamera.Camera);
    }

    /// <summary>
    /// Scrubs the timeline to a specific time in seconds.
    /// </summary>
    public void ScrubToTime(float timeSeconds)
    {
        if (!SelectedSequence.HasValue || CameraList.Count == 0)
            return;

        var cameras = GetCamerasAsList();
        _preview.ScrubToTime(cameras, SelectedSequence.Value, timeSeconds);

        var frame = _preview.GetInterpolatedFrameAtTime(cameras, SelectedSequence.Value, timeSeconds);
        if (frame.HasValue)
            UpdateSelectedRoomByPosition(frame.Value.Position);
    }

    // Updates the editor's selected room when position crosses into a different room,
    // and resets the camera (or backup camera during preview) to the new room.
    public void UpdateSelectedRoomByPosition(Vector3 worldPosition)
    {
        if (_editor.Level == null)
            return;

        var room = _editor.GetRoomAtPosition(worldPosition);

        if (room == null || room == _editor.SelectedRoom)
            return;

        _editor.SelectedRoom = room;
        _editor.ResetCamera(false, room);
    }

    /// <summary>
    /// Called when a camera is dragged on the timeline to a new timecode position.
    /// </summary>
    public void OnTimelineCameraDragged(int cameraIndex, float newTimeSeconds)
    {
        if (cameraIndex <= 0 || cameraIndex >= CameraList.Count)
            return;

        float prevTime = GetTimecodeForCamera(cameraIndex - 1);
        float freezeAtPrev = GetFreezeDurationSeconds(cameraIndex - 1);
        float targetTime = Math.Max(newTimeSeconds - freezeAtPrev, prevTime + 0.01f);

        var cameras = GetCamerasAsList().ToList();
        float newSpeed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(
            cameras,
            cameraIndex - 1,
            cameraIndex,
            targetTime);

        _isApplyingProperty = true;
        CameraList[cameraIndex - 1].Camera.Speed = newSpeed;
        _editor.ObjectChange(CameraList[cameraIndex - 1].Camera, ObjectChangeType.Change);
        _isApplyingProperty = false;

        RefreshTimelineState(false);
    }

    #endregion Preview and playback

    #region Timecode helpers

    /// <summary>
    /// Returns the current or freshly built sequence cache for use by the timeline.
    /// </summary>
    public FlybySequenceCache? GetSequenceCache()
    {
        if (!SelectedSequence.HasValue)
            return null;

        return _preview.GetOrBuildCache(GetCamerasAsList(), SelectedSequence.Value);
    }

    public float GetTimecodeForCamera(int index)
    {
        return FlybySequenceHelper.GetTimecodeForCamera(GetCamerasAsList(), index);
    }

    /// <summary>
    /// Returns the total display duration from the cache when available,
    /// falling back to the static computation.
    /// </summary>
    public float GetCacheDisplayDuration(FlybySequenceCache? cache)
    {
        if (cache != null && cache.IsValid)
            return Math.Max(cache.TotalDuration, 1.0f);

        return FlybySequenceHelper.GetDisplayDuration(GetCamerasAsList());
    }

    public float GetDisplayDuration()
    {
        return FlybySequenceHelper.GetDisplayDuration(GetCamerasAsList());
    }

    /// <summary>
    /// Returns true if the camera at the given index has the camera cut flag (bit 7) set.
    /// </summary>
    public bool GetCameraCutFlag(int index)
    {
        if (index < 0 || index >= CameraList.Count)
            return false;

        return (CameraList[index].Camera.Flags & FlybyConstants.FlagCameraCut) != 0;
    }

    public float GetFreezeDurationSeconds(int index)
    {
        if (index < 0 || index >= CameraList.Count)
            return 0;

        return FlybySequenceHelper.GetFreezeDuration(CameraList[index].Camera);
    }

    /// <summary>
    /// Returns the duration (in seconds) of the bypassed region for a camera cut at the given index.
    /// When the camera cut flag is set, Timer holds the target camera index. The bypassed region spans
    /// from the cut camera's timecode to the target camera's timecode.
    /// </summary>
    public float GetCutBypassDuration(int index)
    {
        if (index < 0 || index >= CameraList.Count)
            return 0;

        var cam = CameraList[index].Camera;

        if ((cam.Flags & FlybyConstants.FlagCameraCut) == 0)
            return 0;

        int targetIndex = cam.Timer;

        if (targetIndex <= index || targetIndex >= CameraList.Count)
            return 0;

        var cameras = GetCamerasAsList();
        float fromTime = FlybySequenceHelper.GetTimecodeForCamera(cameras, index);
        float toTime = FlybySequenceHelper.GetTimecodeForCamera(cameras, targetIndex);
        return Math.Max(0, toTime - fromTime);
    }

    private void RecalculateTimecodes()
    {
        var cameras = GetCamerasAsList();

        for (int i = 0; i < CameraList.Count; i++)
            CameraList[i].Timecode = FlybySequenceHelper.FormatTimecode(
                FlybySequenceHelper.GetTimecodeForCamera(cameras, i));
    }

    private void UpdatePlayheadTimecode()
    {
        float seconds = PlayheadSeconds >= 0 ? PlayheadSeconds : 0;
        PlayheadTimecode = FlybySequenceHelper.FormatTimecode(seconds);
    }

    #endregion Timecode helpers

    #region Data refresh

    private void OnDataChanged()
    {
        RefreshTimelineState(true);
    }

    private void RequestTimelineRefresh()
    {
        RefreshTimelineState(false, false);
    }

    private void RefreshSequenceList()
    {
        var currentSelection = SelectedSequence;
        var sequences = new HashSet<ushort>(_userAddedSequences);

        if (_editor.Level != null)
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

    private void RefreshCameraList()
    {
        CameraList.Clear();

        if (!SelectedSequence.HasValue || _editor.Level == null)
            return;

        var cameras = FlybySequenceHelper.GetCameras(_editor.Level, SelectedSequence.Value);

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
    }

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

    private void RenumberSequence(ushort sequence, FlybyCameraInstance? excludeFromEvent = null)
    {
        var cameras = FlybySequenceHelper.GetCameras(_editor.Level, sequence);
        var oldTargetByNumber = cameras
            .GroupBy(camera => camera.Number)
            .ToDictionary(group => (int)group.Key, group => group.First());

        _isApplyingProperty = true;

        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].Number != (ushort)i)
            {
                cameras[i].Number = (ushort)i;

                if (cameras[i] != excludeFromEvent)
                    _editor.ObjectChange(cameras[i], ObjectChangeType.Change);
            }
        }

        foreach (var camera in cameras)
        {
            if ((camera.Flags & FlybyConstants.FlagCameraCut) == 0)
                continue;

            ushort originalFlags = camera.Flags;
            short originalTimer = camera.Timer;

            if (oldTargetByNumber.TryGetValue(camera.Timer, out var targetCamera) && cameras.Contains(targetCamera))
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

        _isApplyingProperty = false;
    }

    private List<FlybyCameraInstance> GetCamerasForCurrentSequence()
    {
        if (!SelectedSequence.HasValue || _editor.Level == null)
            return new List<FlybyCameraInstance>();

        return FlybySequenceHelper.GetCameras(_editor.Level, SelectedSequence.Value);
    }

    private IReadOnlyList<FlybyCameraInstance> GetCamerasAsList()
    {
        return CameraList.Select(vm => vm.Camera).ToList();
    }

    private void SelectCameraByInstance(FlybyCameraInstance camera)
    {
        var item = CameraList.FirstOrDefault(c => c.Camera == camera);

        if (item != null)
            SelectedCamera = item;
    }

    private void RefreshTimelineState(bool refreshCameraList, bool syncPreview = true)
    {
        if (refreshCameraList)
            RefreshCameraList();

        RecalculateTimecodes();
        TimelineRefreshRequested?.Invoke();

        if (syncPreview)
            RefreshPreviewState();
    }

    private void RefreshPreviewState()
    {
        if (!TryGetSequenceContext(out var cameras, out var sequence))
        {
            if (SelectedCamera != null)
                _preview.ShowCamera(SelectedCamera.Camera);

            return;
        }

        if (IsPreviewActive && PlayheadSeconds >= 0)
            _preview.ScrubToTime(cameras, sequence, PlayheadSeconds);
        else if (SelectedCamera != null)
            _preview.ShowCamera(SelectedCamera.Camera);
    }

    private bool TryGetSequenceContext(out IReadOnlyList<FlybyCameraInstance> cameras, out ushort sequence)
    {
        if (SelectedSequence.HasValue && CameraList.Count > 0)
        {
            sequence = SelectedSequence.Value;
            cameras = GetCamerasAsList();
            return true;
        }

        cameras = Array.Empty<FlybyCameraInstance>();
        sequence = 0;
        return false;
    }

    #endregion Data refresh

    #region Preview state sync

    private void OnPreviewStateChanged()
    {
        IsPlaying = _preview.IsPlaying;
        OnPropertyChanged(nameof(IsPreviewActive));
        OnPropertyChanged(nameof(CanEditProperties));
    }

    private void OnPreviewPlayheadChanged()
    {
        PlayheadSeconds = _preview.PlayheadSeconds;
        UpdatePlayheadTimecode();
    }

    #endregion Preview state sync

    #region Editor event handling

    private void OnEditorEventRaised(IEditorEvent obj)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnEditorEventRaised(obj));
            return;
        }

        if (obj is Editor.LevelChangedEvent || obj is Editor.GameVersionChangedEvent)
        {
            _preview.StopPlayback();

            if (IsPreviewActive)
                _preview.ExitPreview();

            _preview.InvalidateCache();

            if (obj is Editor.LevelChangedEvent)
                _userAddedSequences.Clear();

            RefreshSequenceList();
            RequestTimelineRefresh();
            return;
        }

        if (obj is Editor.ObjectChangedEvent changeEvent)
        {
            if (changeEvent.Object is FlybyCameraInstance flyby &&
                SelectedSequence.HasValue && flyby.Sequence == SelectedSequence.Value)
            {
                _preview.InvalidateCache();

                if (!_isApplyingProperty)
                    OnDataChanged();
            }
        }

        // Detect external preview exit (ESC key in Panel3D).
        if (obj is Editor.ToggleCameraPreviewEvent previewEvent && !previewEvent.PreviewState)
            _preview.OnExternalPreviewExit();

        // Selection changed in editor - sync to flyby manager.
        if (obj is Editor.SelectedObjectChangedEvent selEvent && !_isSyncingSelection)
        {
            if (selEvent.Current is FlybyCameraInstance flyby && !_isUpdating)
            {
                _isSyncingSelection = true;

                if (AvailableSequences.Contains(flyby.Sequence))
                {
                    SelectedSequence = flyby.Sequence;
                    SelectCameraByInstance(flyby);
                }

                _isSyncingSelection = false;
            }
        }
    }

    #endregion Editor event handling
}
