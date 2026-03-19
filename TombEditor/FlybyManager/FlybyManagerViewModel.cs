#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TombLib;
using TombLib.LevelData;

namespace TombEditor.FlybyManager;

/// <summary>
/// Main view model for the Flyby Sequence Manager window.
/// Delegates data operations to FlybySequenceData and preview to FlybyPreviewController.
/// </summary>
public partial class FlybyManagerViewModel : ObservableObject
{
    private const float DefaultSpeed = 1.0f;
    private const float DefaultFov = 80.0f;

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

    [ObservableProperty]
    private bool _isPreviewActive;

    // Camera properties for the selected camera.
    [ObservableProperty]
    private float _cameraSpeed = DefaultSpeed;

    [ObservableProperty]
    private float _cameraFov = DefaultFov;

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

    public string PlayStopIcon => IsPlaying ? "Stop" : "Play";
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

    public FlybyManagerViewModel(Editor editor, Dispatcher dispatcher)
    {
        _editor = editor;
        _dispatcher = dispatcher;

        _preview = new FlybyPreviewController(editor, dispatcher);
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
        _preview.InvalidateScrubPreview();

        RefreshCameraList();
        RecalculateTimecodes();

        // Auto-select first camera in the new sequence.
        SelectedCamera = CameraList.Count > 0 ? CameraList[0] : null;
    }

    #endregion Sequence management

    #region Camera list management

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
            Speed = DefaultSpeed,
            Fov = DefaultFov
        };

        // Insert at cursor position when playhead is valid and there are existing cameras.
        if (PlayheadSeconds >= 0 && CameraList.Count >= 2)
        {
            float cursorTime = PlayheadSeconds;
            var cameras = GetCamerasAsList();

            // If cursor is at the last camera and nothing ahead, advance 1 second.
            float lastCameraTime = FlybySequenceData.GetTimecodeForCamera(cameras, cameras.Count - 1);

            if (Math.Abs(cursorTime - lastCameraTime) < 0.05f &&
                FlybySequenceData.FindInsertionIndex(cameras, cursorTime) >= cameras.Count)
            {
                cursorTime += 1.0f;
                PlayheadSeconds = cursorTime;
            }

            int insertIndex = FlybySequenceData.FindInsertionIndex(cameras, cursorTime);

            if (insertIndex < cameras.Count)
            {
                cam.Number = (ushort)insertIndex;
                ApplyCameraPositionAtCursor(cam, cameras, seq, cursorTime, room, out room);

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

    private void ApplyCameraPositionAtCursor(
        FlybyCameraInstance cam, IReadOnlyList<FlybyCameraInstance> cameras,
        ushort sequence, float cursorTime, Room room, out Room targetRoom)
    {
        targetRoom = room;

        // Use interpolated spline position when preview is active.
        if (_preview.IsPreviewActive)
        {
            float progress = FlybySequenceData.TimeToProgress(cameras, cursorTime);
            var frame = _preview.GetInterpolatedFrame(sequence, progress);

            if (frame.HasValue)
            {
                targetRoom = FlybySequenceData.FindRoomAtPosition(_editor.Level, frame.Value.Position) ?? room;
                cam.Position = frame.Value.Position - targetRoom.WorldPos;
                cam.RotationY = MathC.RadToDeg(frame.Value.RotationY);
                cam.RotationX = -MathC.RadToDeg(frame.Value.RotationX);
                cam.Roll = -MathC.RadToDeg(frame.Value.Roll);
                cam.Fov = MathC.RadToDeg(frame.Value.Fov);
                return;
            }
        }

        ApplyEditorCameraPosition(cam, room);
    }

    private void ApplyEditorCameraPosition(FlybyCameraInstance cam, Room room)
    {
        var editorCamera = _editor.GetViewportCamera?.Invoke();

        if (editorCamera != null)
        {
            cam.Position = editorCamera.GetPosition() - room.WorldPos;
            FlybySequenceData.ApplyEditorCameraRotation(editorCamera, cam);
        }
        else
        {
            cam.Position = room.GetLocalCenter();
        }
    }

    [RelayCommand]
    private void DeleteSelectedCameras()
    {
        if (_selectedCameras.Count == 0)
            return;

        var toDelete = _selectedCameras
            .Select(vm => new { vm.Camera, Room = vm.Camera.Room })
            .Where(x => x.Room != null)
            .ToList();

        foreach (var item in toDelete)
            EditorActions.DeleteObjectWithoutUpdate(item.Camera);

        foreach (var item in toDelete)
            _editor.ObjectChange(item.Camera, ObjectChangeType.Remove, item.Room);

        SelectedCamera = null;
        _selectedCameras.Clear();

        OnDataChanged();
    }

    [RelayCommand]
    private void MoveCameraUp()
    {
        if (SelectedCamera == null)
            return;

        int index = CameraList.IndexOf(SelectedCamera);

        if (index <= 0)
            return;

        SwapCameraNumbers(index, index - 1);
        RefreshCameraList();
        SelectedCamera = CameraList[index - 1];
    }

    [RelayCommand]
    private void MoveCameraDown()
    {
        if (SelectedCamera == null)
            return;

        int index = CameraList.IndexOf(SelectedCamera);

        if (index < 0 || index >= CameraList.Count - 1)
            return;

        SwapCameraNumbers(index, index + 1);
        RefreshCameraList();
        SelectedCamera = CameraList[index + 1];
    }

    public void UpdateSelectedCameras(IEnumerable<FlybyCameraItemViewModel> items)
    {
        _selectedCameras.Clear();
        _selectedCameras.AddRange(items);

        if (_selectedCameras.Count == 1)
            SelectedCamera = _selectedCameras[0];
        else if (_selectedCameras.Count == 0)
            SelectedCamera = null;
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

            _preview.ShowCamera(value.Camera);

            // Sync editor selection.
            if (!_isSyncingSelection)
            {
                _isSyncingSelection = true;
                _editor.SelectedObject = value.Camera;
                _isSyncingSelection = false;
            }
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

    partial void OnCameraTimerChanged(short value)
    {
        ApplyPropertyToCamera(c => c.Timer = value);

        // Timer affects duration when freeze camera flag is set.
        if ((CameraFlags & FlybySequenceData.FlagFreezeCamera) != 0)
            RequestTimelineRefresh();
    }

    partial void OnCameraFlagsChanged(ushort value)
    {
        ApplyPropertyToCamera(c => c.Flags = value);

        if ((value & FlybySequenceData.FlagFreezeCamera) != 0 && CameraTimer > 0)
            RequestTimelineRefresh();
    }

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

        _preview.InvalidateScrubPreview();
        RecalculateTimecodes();

        _preview.ShowCamera(SelectedCamera.Camera);
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
    /// Scrubs the timeline to a specific time in seconds.
    /// </summary>
    public void ScrubToTime(float timeSeconds)
    {
        if (!SelectedSequence.HasValue || CameraList.Count == 0)
            return;

        _preview.ScrubToTime(GetCamerasAsList(), SelectedSequence.Value, timeSeconds);
    }

    /// <summary>
    /// Called when a camera is dragged on the timeline to a new timecode position.
    /// </summary>
    public void OnTimelineCameraDragged(int cameraIndex, float newTimeSeconds)
    {
        if (cameraIndex <= 0 || cameraIndex >= CameraList.Count)
            return;

        float prevTime = GetTimecodeForCamera(cameraIndex - 1);
        float gap = Math.Max(newTimeSeconds - prevTime, 0.01f);

        float newSpeed = 1.0f / (gap * FlybyPreview.SpeedScale);
        CameraList[cameraIndex - 1].Camera.Speed = newSpeed;

        _editor.ObjectChange(CameraList[cameraIndex - 1].Camera, ObjectChangeType.Change);

        _preview.InvalidateScrubPreview();
        RecalculateTimecodes();
    }

    #endregion Preview and playback

    #region Timecode helpers

    public float GetTimecodeForCamera(int index)
    {
        return FlybySequenceData.GetTimecodeForCamera(GetCamerasAsList(), index);
    }

    public float GetDisplayDuration()
    {
        return FlybySequenceData.GetDisplayDuration(GetCamerasAsList());
    }

    private void RecalculateTimecodes()
    {
        var cameras = GetCamerasAsList();

        for (int i = 0; i < CameraList.Count; i++)
            CameraList[i].Timecode = FlybySequenceData.FormatTimecode(
                FlybySequenceData.GetTimecodeForCamera(cameras, i));
    }

    private void UpdatePlayheadTimecode()
    {
        float seconds = PlayheadSeconds >= 0 ? PlayheadSeconds : 0;
        PlayheadTimecode = FlybySequenceData.FormatTimecode(seconds);
    }

    #endregion Timecode helpers

    #region Data refresh

    private void OnDataChanged()
    {
        _preview.InvalidateScrubPreview();
        RefreshCameraList();
        RecalculateTimecodes();
        TimelineRefreshRequested?.Invoke();
    }

    private void RequestTimelineRefresh()
    {
        RecalculateTimecodes();
        TimelineRefreshRequested?.Invoke();
    }

    private void RefreshSequenceList()
    {
        var currentSelection = SelectedSequence;
        var sequences = new HashSet<ushort>(_userAddedSequences);

        if (_editor.Level != null)
        {
            foreach (ushort seq in FlybySequenceData.GetAllSequences(_editor.Level))
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

        var cameras = FlybySequenceData.GetCameras(_editor.Level, SelectedSequence.Value);

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

    private void SwapCameraNumbers(int indexA, int indexB)
    {
        var camA = CameraList[indexA].Camera;
        var camB = CameraList[indexB].Camera;

        (camA.Number, camB.Number) = (camB.Number, camA.Number);

        _editor.ObjectChange(camA, ObjectChangeType.Change);
        _editor.ObjectChange(camB, ObjectChangeType.Change);
    }

    private void RenumberSequence(ushort sequence, FlybyCameraInstance? excludeFromEvent = null)
    {
        var cameras = FlybySequenceData.GetCameras(_editor.Level, sequence);

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

        _isApplyingProperty = false;
    }

    private List<FlybyCameraInstance> GetCamerasForCurrentSequence()
    {
        if (!SelectedSequence.HasValue || _editor.Level == null)
            return new List<FlybyCameraInstance>();

        return FlybySequenceData.GetCameras(_editor.Level, SelectedSequence.Value);
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

    #endregion Data refresh

    #region Preview state sync

    private void OnPreviewStateChanged()
    {
        IsPreviewActive = _preview.IsPreviewActive;
        IsPlaying = _preview.IsPlaying;
        OnPropertyChanged(nameof(CanEditProperties));

        // Show selected camera in static preview after playback stops.
        if (!IsPlaying && IsPreviewActive && SelectedCamera != null)
            _preview.ShowCamera(SelectedCamera.Camera);
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

        if (obj is Editor.LevelChangedEvent)
        {
            _preview.StopPlayback();

            if (_preview.IsPreviewActive)
                _preview.ExitPreview();

            _userAddedSequences.Clear();
            RefreshSequenceList();
            return;
        }

        if (obj is Editor.ObjectChangedEvent changeEvent)
        {
            if (changeEvent.Object is FlybyCameraInstance && !_isApplyingProperty)
                OnDataChanged();
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
