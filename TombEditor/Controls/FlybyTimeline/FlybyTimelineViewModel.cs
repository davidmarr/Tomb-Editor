#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using System.Windows.Threading;
using TombLib;
using TombLib.LevelData;
using TombLib.Utils;
using TombLib.WPF.Services;
using TombLib.WPF.Services.Abstract;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Main view model for the Flyby Sequence Manager window.
/// Delegates data operations to FlybySequenceHelper and preview to FlybyPreviewController.
/// </summary>
public partial class FlybyTimelineViewModel : ObservableObject
{
    /// <summary>
    /// Represents the data required to render the current timeline state.
    /// </summary>
    public readonly struct TimelineRenderState(IReadOnlyList<FlybyTimelineControl.TimelineMarker> markers, FlybySequenceCache? cache, float totalDuration)
    {
        /// <summary>
        /// Gets the markers that should be rendered by the timeline control.
        /// </summary>
        public IReadOnlyList<FlybyTimelineControl.TimelineMarker> Markers { get; } = markers;

        /// <summary>
        /// Gets the sequence cache associated with the rendered timeline.
        /// </summary>
        public FlybySequenceCache? Cache { get; } = cache;

        /// <summary>
        /// Gets the total duration to use for the visible timeline range.
        /// </summary>
        public float TotalDuration { get; } = totalDuration;
    }

    private readonly Editor _editor;
    private readonly FlybyPreviewController _preview;
    private readonly Dispatcher _dispatcher;
    private readonly IWin32Window? _dialogOwner;
    private readonly IMessageService _messageService;
    private readonly ILocalizationService _localizationService;

    private bool _isUpdating;
    private bool _isApplyingProperty;
    private bool _isSyncingSelection;
    private int _activeDraggedCameraIndex = -1;

    /// <summary>
    /// Gets the available flyby sequence ids shown in the UI.
    /// </summary>
    public ObservableCollection<ushort> AvailableSequences { get; } = [];

    /// <summary>
    /// Gets the camera items for the currently selected sequence.
    /// </summary>
    public ObservableCollection<FlybyCameraItemViewModel> CameraList { get; } = [];

    /// <summary>
    /// Stores the currently selected flyby sequence id.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSequenceSelected))]
    private ushort? _selectedSequence;

    /// <summary>
    /// Stores the currently selected camera item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProperties))]
    private FlybyCameraItemViewModel? _selectedCamera;

    /// <summary>
    /// Stores whether sequence playback is currently active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayStopIcon))]
    [NotifyPropertyChangedFor(nameof(PlayStopTooltip))]
    private bool _isPlaying;

    /// <summary>
    /// Gets whether the editor is currently showing a flyby preview.
    /// </summary>
    public bool IsPreviewActive => _editor.CameraPreviewMode != CameraPreviewType.None;

    /// <summary>
    /// Gets whether TombEngine smooth pause behavior should be used.
    /// </summary>
    private bool UseSmoothPause => _editor.Level?.Settings.GameVersion == TRVersion.Game.TombEngine;

    // Camera properties for the selected camera.
    /// <summary>
    /// Stores the editable speed value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private float _cameraSpeed;

    /// <summary>
    /// Stores the editable field-of-view value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private float _cameraFov;

    /// <summary>
    /// Stores the editable roll value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private float _cameraRoll;

    /// <summary>
    /// Stores the editable X rotation value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private float _cameraRotationX;

    /// <summary>
    /// Stores the editable Y rotation value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private float _cameraRotationY;

    /// <summary>
    /// Stores the editable timer value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private short _cameraTimer;

    /// <summary>
    /// Stores the editable flags value of the selected camera.
    /// </summary>
    [ObservableProperty]
    private ushort _cameraFlags;

    // Playhead timecode display.
    /// <summary>
    /// Stores the formatted playhead timecode shown in the UI.
    /// </summary>
    [ObservableProperty]
    private string _playheadTimecode = "00:00.00";

    // Playhead position in seconds (negative = hidden).
    /// <summary>
    /// Stores the current playhead position in timeline seconds.
    /// </summary>
    [ObservableProperty]
    private float _playheadSeconds = -1.0f;

    /// <summary>
    /// Gets the icon resource used for the play or stop button.
    /// </summary>
    public string PlayStopIcon => IsPlaying
        ? "pack://application:,,,/TombEditor;component/Resources/icons_transport/transport-stop-24.png"
        : "pack://application:,,,/TombEditor;component/Resources/icons_transport/transport-play-24.png";

    /// <summary>
    /// Gets the tooltip text used for the play or stop button.
    /// </summary>
    public string PlayStopTooltip => IsPlaying
        ? _localizationService["StopSequenceTooltip"]
        : _localizationService["PlaySequenceTooltip"];

    /// <summary>
    /// Gets whether a flyby sequence is currently selected.
    /// </summary>
    public bool HasSequenceSelected => SelectedSequence.HasValue;

    /// <summary>
    /// Gets whether the selected camera properties can currently be edited.
    /// </summary>
    public bool CanEditProperties => SelectedCamera is not null && !IsPlaying;

    // Selected cameras are tracked by instance so refreshes do not invalidate selection state.
    private readonly HashSet<FlybyCameraInstance> _selectedCameras = [];

    /// <summary>
    /// Gets the currently selected flyby cameras.
    /// </summary>
    public IReadOnlyCollection<FlybyCameraInstance> SelectedCameras => _selectedCameras;

    // Temporary sequences added by user (persist until window closes).
    private readonly HashSet<ushort> _userAddedSequences = [];

    /// <summary>
    /// Fired when the timeline needs a visual refresh.
    /// </summary>
    public event Action? TimelineRefreshRequested;

    /// <summary>
    /// Fired when the timeline should zoom to fit the current sequence.
    /// </summary>
    public event Action? ZoomToFitRequested;

    /// <summary>
    /// Creates the main view model for the flyby timeline UI.
    /// </summary>
    /// <param name="editor">Editor instance providing level, selection, and undo services.</param>
    /// <param name="dispatcher">UI dispatcher used to marshal editor events onto the UI thread.</param>
    /// <param name="dialogOwner">Optional WinForms owner used for modal dialogs.</param>
    /// <param name="messageService">Optional message service used for confirmations.</param>
    /// <param name="localizationService">Optional localization service used for UI strings.</param>
    public FlybyTimelineViewModel(
        Editor editor,
        Dispatcher dispatcher,
        IWin32Window? dialogOwner = null,
        IMessageService? messageService = null,
        ILocalizationService? localizationService = null)
    {
        _editor = editor;
        _dispatcher = dispatcher;
        _dialogOwner = dialogOwner;
        _messageService = ServiceLocator.ResolveService(messageService);
        _localizationService = ServiceLocator.ResolveService(localizationService)
            .WithKeysFor(this);

        _preview = new FlybyPreviewController(editor);
        _preview.StateChanged += OnPreviewStateChanged;
        _preview.PlayheadChanged += OnPreviewPlayheadChanged;

        _editor.EditorEventRaised += OnEditorEventRaised;

        RefreshSequenceList();
    }

    /// <summary>
    /// Unhooks preview and editor event subscriptions.
    /// </summary>
    public void Cleanup()
    {
        _preview.StateChanged -= OnPreviewStateChanged;
        _preview.PlayheadChanged -= OnPreviewPlayheadChanged;
        _preview.Dispose();

        _editor.EditorEventRaised -= OnEditorEventRaised;
    }

    #region Sequence management

    /// <summary>
    /// Adds a new empty sequence and selects it.
    /// </summary>
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

    /// <summary>
    /// Removes the selected sequence and any cameras it contains.
    /// </summary>
    [RelayCommand]
    private void RemoveSequence()
    {
        if (!SelectedSequence.HasValue)
            return;

        ushort seq = SelectedSequence.Value;
        var cameras = GetCamerasForCurrentSequence();

        if (cameras.Count > 0)
        {
            bool confirmed = _messageService.ShowConfirmation(
                _localizationService.Format("RemoveSequenceConfirmationMessage", seq, cameras.Count),
                _localizationService["RemoveSequenceConfirmationTitle"],
                defaultValue: false,
                isRisky: true);

            if (!confirmed)
                return;

            _preview.StopPlayback();

            var undoList = CreateFlybyCameraDeletionUndo(cameras).ToList();

            _isApplyingProperty = true;

            try
            {
                // Sequence removal already has its own explicit confirmation dialog.
                EditorActions.DeleteObjects(cameras.Cast<ObjectInstance>(), null, false);
            }
            finally
            {
                _isApplyingProperty = false;
            }

            if (!WereAllCamerasDeleted(cameras))
            {
                RefreshTimelineState(true, false);
                return;
            }

            _preview.InvalidateCache();
            PushUndoIfAny(undoList);
        }

        _userAddedSequences.Remove(seq);
        AvailableSequences.Remove(seq);

        SelectedSequence = AvailableSequences.Count > 0 ? AvailableSequences[0] : null;
    }

    /// <summary>
    /// Refreshes camera and timing state when the selected sequence changes.
    /// </summary>
    partial void OnSelectedSequenceChanged(ushort? value)
    {
        _preview.StopPlayback();
        _preview.InvalidateCache();
        ResetPlayhead();

        RefreshCameraList();
        RecalculateTimecodes();

        if (!_isSyncingSelection && _selectedCameras.Count == 0 && CameraList.Count > 0)
            SetSelectedCameras([CameraList[0].Camera], true);
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

        _preview.StopPlayback();

        var selectedCameras = _selectedCameras.ToList();

        var remainingCameras = GetCamerasForCurrentSequence()
            .Where(camera => !selectedCameras.Contains(camera))
            .ToList();

        var undoList = CreateFlybyCameraPropertyUndo(remainingCameras);
        undoList.AddRange(CreateFlybyCameraDeletionUndo(selectedCameras));

        _isApplyingProperty = true;

        try
        {
            EditorActions.DeleteObjects(selectedCameras.Cast<ObjectInstance>(), GetDialogOwner(), false);
        }
        finally
        {
            _isApplyingProperty = false;
        }

        if (!WereAllCamerasDeleted(selectedCameras))
        {
            RefreshTimelineState(true, false);
            return;
        }

        _preview.InvalidateCache();

        RenumberSequence(SelectedSequence.Value);
        PushUndoIfAny(undoList);
        OnDataChanged();

        SetSelectedCameras([], true);
        RefreshSequenceList();
    }

    /// <summary>
    /// Adds a new camera at the playhead or at the end of the current sequence.
    /// </summary>
    [RelayCommand]
    private void AddCamera()
    {
        if (!SelectedSequence.HasValue || _editor.Level is null)
            return;

        var room = _editor.SelectedRoom;

        if (room is null)
            return;

        _preview.StopPlayback();

        var cam = new FlybyCameraInstance
        {
            Sequence = SelectedSequence.Value,
        };

        if (TryAddCameraAtPlayhead(cam, room))
            return;

        AddCameraAtSequenceEnd(cam, room);
    }

    /// <summary>
    /// Tries to place a new camera using the current playhead position.
    /// </summary>
    /// <param name="cam">Camera instance being inserted.</param>
    /// <param name="room">Room that will own the inserted camera.</param>
    /// <returns><see langword="true"/> when the camera was placed using the current playhead time; otherwise <see langword="false"/>.</returns>
    private bool TryAddCameraAtPlayhead(FlybyCameraInstance cam, Room room)
    {
        if (!float.IsFinite(PlayheadSeconds) || PlayheadSeconds < 0.0f || CameraList.Count < 1)
            return false;

        const float minimumSegmentDuration = FlybyConstants.TimeStep;
        const float lastCameraTolerance = 0.0001f;

        float cursorTime = PlayheadSeconds;
        float clampedCursorTime = Math.Max(cursorTime, 0.01f);
 
        var cameras = GetCamerasAsList();
        float lastCameraTime = FlybySequenceHelper.GetTimecodeForCamera(cameras, cameras.Count - 1, UseSmoothPause);

        if (MathF.Abs(cursorTime - lastCameraTime) <= lastCameraTolerance)
            return false; // Cursor is at the end of the sequence, fall back to AddCameraAtSequenceEnd

        if (cursorTime > lastCameraTime + lastCameraTolerance)
        {
            AppendCameraAtPlayhead(cam, room, cameras, clampedCursorTime, lastCameraTime, minimumSegmentDuration);
            return true;
        }

        int insertIndex = FlybySequenceHelper.FindInsertionIndex(cameras, cursorTime, UseSmoothPause);

        if (insertIndex <= 0 || insertIndex >= cameras.Count)
            return false;

        InsertCameraAtPlayhead(cam, room, cameras, insertIndex, clampedCursorTime, minimumSegmentDuration);
        return true;
    }

    /// <summary>
    /// Appends a camera after the last camera and retimes the final segment.
    /// </summary>
    /// <param name="cam">Camera instance being inserted.</param>
    /// <param name="room">Room that will own the inserted camera.</param>
    /// <param name="cameras">Existing cameras in the selected sequence.</param>
    /// <param name="clampedCursorTime">Playhead time clamped into the valid append range.</param>
    /// <param name="lastCameraTime">Timeline time of the current last camera.</param>
    /// <param name="minimumSegmentDuration">Minimum allowed duration for the final segment.</param>
    private void AppendCameraAtPlayhead(FlybyCameraInstance cam, Room room,
        IReadOnlyList<FlybyCameraInstance> cameras, float clampedCursorTime, float lastCameraTime,
        float minimumSegmentDuration)
    {
        var undoList = CreateFlybyCameraPropertyUndo(cameras);

        cam.Speed = cameras[^1].Speed;
        cam.Number = GetNextCameraNumber(cameras);
        ApplyEditorCameraPosition(cam, room);

        var tempCameras = cameras.ToList();
        tempCameras.Add(cam);

        float targetTime = Math.Max(clampedCursorTime, lastCameraTime + minimumSegmentDuration);
        float newSpeed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(
            tempCameras,
            tempCameras.Count - 2,
            tempCameras.Count - 1,
            targetTime,
            UseSmoothPause);

        cameras[^1].Speed = newSpeed;
        _editor.ObjectChange(cameras[^1], ObjectChangeType.Change);

        room.AddObject(_editor.Level, cam);
        _editor.ObjectChange(cam, ObjectChangeType.Add);
        undoList.Add(new AddRemoveObjectUndoInstance(_editor.UndoManager, cam, true));

        PushUndoIfAny(undoList);
        FinalizeAddedCamera(cam, zoomToFit: true);
    }

    /// <summary>
    /// Inserts a camera between two existing cameras and updates both segment speeds.
    /// </summary>
    /// <param name="cam">Camera instance being inserted.</param>
    /// <param name="room">Room that will own the inserted camera.</param>
    /// <param name="cameras">Existing cameras in the selected sequence.</param>
    /// <param name="insertIndex">Index where the camera should be inserted.</param>
    /// <param name="clampedCursorTime">Playhead time clamped into the target segment.</param>
    /// <param name="minimumSegmentDuration">Minimum allowed duration for each adjacent segment.</param>
    private void InsertCameraAtPlayhead(FlybyCameraInstance cam, Room room,
        IReadOnlyList<FlybyCameraInstance> cameras, int insertIndex, float clampedCursorTime,
        float minimumSegmentDuration)
    {
        int prevIndex = insertIndex - 1;
        var undoList = CreateFlybyCameraPropertyUndo(cameras);

        float segmentStart = FlybySequenceHelper.GetTimecodeForCamera(cameras, prevIndex, UseSmoothPause);
        float segmentEnd = FlybySequenceHelper.GetTimecodeForCamera(cameras, insertIndex, UseSmoothPause);
        float minimumInsertTime = segmentStart + minimumSegmentDuration;
        float maximumInsertTime = segmentEnd - minimumSegmentDuration;
        float insertTime = maximumInsertTime >= minimumInsertTime
            ? Math.Clamp(clampedCursorTime, minimumInsertTime, maximumInsertTime)
            : minimumInsertTime;
        float nextTargetTime = Math.Max(segmentEnd, insertTime + minimumSegmentDuration);
        ushort insertionNumber = GetInsertionNumber(cameras, prevIndex, insertIndex);
        bool requiresNumberShift = cameras.Any(camera => camera.Number == insertionNumber);

        cam.Number = insertionNumber;
        cam.Speed = cameras[prevIndex].Speed;
        ApplyEditorCameraPosition(cam, room);

        var tempCameras = cameras.ToList();
        tempCameras.Insert(insertIndex, cam);

        cameras[prevIndex].Speed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(
            tempCameras,
            prevIndex,
            insertIndex,
            insertTime,
            UseSmoothPause);

        cam.Speed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(
            tempCameras,
            insertIndex,
            insertIndex + 1,
            nextTargetTime,
            UseSmoothPause);

        if (requiresNumberShift)
            PrepareCamerasForInsertion(cameras, insertionNumber);

        _editor.ObjectChange(cameras[prevIndex], ObjectChangeType.Change);

        room.AddObject(_editor.Level, cam);
        _editor.ObjectChange(cam, ObjectChangeType.Add);
        undoList.Add(new AddRemoveObjectUndoInstance(_editor.UndoManager, cam, true));

        PushUndoIfAny(undoList);
        FinalizeAddedCamera(cam, zoomToFit: false);
    }

    /// <summary>
    /// Adds a camera to the end of the sequence without changing earlier timing.
    /// </summary>
    private void AddCameraAtSequenceEnd(FlybyCameraInstance cam, Room room)
    {
        cam.Number = GetNextCameraNumber(GetCamerasAsList());

        ApplyEditorCameraPosition(cam, room);

        room.AddObject(_editor.Level, cam);
        _editor.UndoManager.PushObjectCreated(cam);
        _editor.ObjectChange(cam, ObjectChangeType.Add);

        FinalizeAddedCamera(cam, zoomToFit: true);
    }

    /// <summary>
    /// Copies the current editor camera position and rotation into a flyby camera.
    /// </summary>
    private void ApplyEditorCameraPosition(FlybyCameraInstance cam, Room room)
    {
        var editorCamera = _editor.GetViewportCamera?.Invoke();

        if (editorCamera is not null)
        {
            cam.Position = editorCamera.GetPosition() - room.WorldPos;
            FlybySequenceHelper.ApplyEditorCameraRotation(editorCamera, cam);
        }
        else
        {
            cam.Position = room.GetLocalCenter();
        }
    }

    /// <summary>
    /// Returns the next available camera number after the current highest numbered camera.
    /// </summary>
    private static ushort GetNextCameraNumber(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        return cameras.Count > 0
            ? (ushort)(cameras.Max(camera => (int)camera.Number) + 1)
            : (ushort)0;
    }

    /// <summary>
    /// Returns the number a new camera should receive when inserted at the given list position.
    /// </summary>
    private static ushort GetInsertionNumber(IReadOnlyList<FlybyCameraInstance> cameras, int prevIndex, int insertIndex)
    {
        int previousNumber = cameras[prevIndex].Number;
        int nextNumber = cameras[insertIndex].Number;

        return nextNumber > previousNumber + 1
            ? (ushort)(previousNumber + 1)
            : (ushort)nextNumber;
    }

    /// <summary>
    /// Shifts existing camera numbers and cut targets to make room for an inserted camera number.
    /// </summary>
    private void PrepareCamerasForInsertion(IReadOnlyList<FlybyCameraInstance> cameras, ushort insertionNumber)
    {
        _isApplyingProperty = true;

        try
        {
            foreach (var camera in cameras)
            {
                bool changed = false;

                if (camera.Number >= insertionNumber)
                {
                    camera.Number++;
                    changed = true;
                }

                if ((camera.Flags & FlybyConstants.FlagCameraCut) != 0 && camera.Timer >= insertionNumber)
                {
                    camera.Timer++;
                    changed = true;
                }

                if (changed)
                    _editor.ObjectChange(camera, ObjectChangeType.Change);
            }
        }
        finally
        {
            _isApplyingProperty = false;
        }
    }

    /// <summary>
    /// Refreshes timeline data after inserting a camera, then focuses it in selection and playhead state.
    /// </summary>
    private void FinalizeAddedCamera(FlybyCameraInstance camera, bool zoomToFit)
    {
        OnDataChanged();
        SelectCameraByInstance(camera);
        MovePlayheadToCamera(camera);

        if (zoomToFit)
            RequestZoomToFit();
    }

    /// <summary>
    /// Moves the playhead to the given camera's current timecode.
    /// </summary>
    private void MovePlayheadToCamera(FlybyCameraInstance camera)
    {
        for (int i = 0; i < CameraList.Count; i++)
        {
            if (CameraList[i].Camera != camera)
                continue;

            PlayheadSeconds = GetTimecodeForCamera(i);
            return;
        }
    }

    /// <summary>
    /// Replaces the current selection with the provided camera items.
    /// </summary>
    /// <param name="items">Camera view models that should become the active selection, or <see langword="null"/> to clear it.</param>
    public void UpdateSelectedCameras(IEnumerable<FlybyCameraItemViewModel>? items)
    {
        if (items is null)
        {
            SetSelectedCameras([], true);
            return;
        }

        SetSelectedCameras(items.Select(item => item.Camera), true);
    }

    /// <summary>
    /// Pushes current timeline selection into Editor.SelectedObject as an ObjectGroup.
    /// </summary>
    private void SyncEditorSelection()
    {
        if (_isSyncingSelection)
            return;

        _isSyncingSelection = true;

        try
        {
            SetEditorSelection(GetMergedEditorSelection());
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    /// <summary>
    /// Returns indices of all currently selected cameras.
    /// </summary>
    public IReadOnlySet<int> GetSelectedIndices()
    {
        var result = new HashSet<int>();

        for (int i = 0; i < CameraList.Count; i++)
        {
            if (_selectedCameras.Contains(CameraList[i].Camera))
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Moves a camera from one list index to another via drag-reorder.
    /// </summary>
    /// <param name="fromIndex">Current index of the camera being moved.</param>
    /// <param name="toIndex">Target index where the camera should be inserted.</param>
    public void MoveCameraToIndex(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= CameraList.Count ||
            toIndex < 0 || toIndex >= CameraList.Count ||
            fromIndex == toIndex)
        {
            return;
        }

        _preview.StopPlayback();

        var movedCamera = CameraList[fromIndex].Camera;
        var cameras = CameraList.Select(vm => vm.Camera).ToList();
        var oldTargetByNumber = BuildCameraLookupByNumber(cameras);
        var originalTimerByCamera = cameras.ToDictionary(camera => camera, camera => camera.Timer);
        var undoList = CreateFlybyCameraPropertyUndo(cameras);

        cameras.RemoveAt(fromIndex);
        cameras.Insert(toIndex, movedCamera);

        _isApplyingProperty = true;

        try
        {
            for (int i = 0; i < cameras.Count; i++)
            {
                if (cameras[i].Number != (ushort)i)
                {
                    cameras[i].Number = (ushort)i;
                    _editor.ObjectChange(cameras[i], ObjectChangeType.Change);
                }
            }

            UpdateCameraCutTargets(cameras, oldTargetByNumber, camera => originalTimerByCamera[camera]);
        }
        finally
        {
            _isApplyingProperty = false;
        }

        PushUndoIfAny(undoList);
        OnDataChanged();

        SelectCameraByInstance(movedCamera);
    }

    /// <summary>
    /// Updates the editable property fields when the selected camera changes.
    /// </summary>
    partial void OnSelectedCameraChanged(FlybyCameraItemViewModel? value)
    {
        _isUpdating = true;

        try
        {
            if (value is not null)
            {
                CameraSpeed = value.Camera.Speed;
                CameraFov = value.Camera.Fov;
                CameraRoll = value.Camera.Roll;
                CameraRotationX = value.Camera.RotationX;
                CameraRotationY = value.Camera.RotationY;
                CameraTimer = value.Camera.Timer;
                CameraFlags = value.Camera.Flags;
            }
        }
        finally
        {
            _isUpdating = false;
        }

        if (value is not null && IsPreviewActive && !IsPlaying)
            _preview.ShowCamera(value.Camera);
    }

    #endregion Camera list management

    #region Camera property editing

    /// <summary>
    /// Applies a speed edit and refreshes timing-dependent timeline state.
    /// </summary>
    partial void OnCameraSpeedChanged(float value) => ApplyPropertyToCamera(c => c.Speed = value);

    /// <summary>
    /// Applies a field-of-view edit to the selected camera.
    /// </summary>
    partial void OnCameraFovChanged(float value) => ApplyPropertyToCamera(c => c.Fov = value);

    /// <summary>
    /// Applies a roll edit to the selected camera.
    /// </summary>
    partial void OnCameraRollChanged(float value) => ApplyPropertyToCamera(c => c.Roll = value);

    /// <summary>
    /// Applies an X rotation edit to the selected camera.
    /// </summary>
    partial void OnCameraRotationXChanged(float value) => ApplyPropertyToCamera(c => c.RotationX = value);

    /// <summary>
    /// Applies a Y rotation edit to the selected camera.
    /// </summary>
    partial void OnCameraRotationYChanged(float value) => ApplyPropertyToCamera(c => c.RotationY = value);

    /// <summary>
    /// Applies a timer edit to the selected camera.
    /// </summary>
    partial void OnCameraTimerChanged(short value) => ApplyPropertyToCamera(c => c.Timer = value);

    /// <summary>
    /// Applies a flag edit to the selected camera.
    /// </summary>
    partial void OnCameraFlagsChanged(ushort value) => ApplyPropertyToCamera(c => c.Flags = value);

    /// <summary>
    /// Returns whether a flag bit is set in the editable flag value.
    /// </summary>
    /// <param name="bit">Zero-based bit index to test.</param>
    /// <returns><see langword="true"/> when the bit is set; otherwise <see langword="false"/>.</returns>
    public bool GetFlag(int bit) => FlybySequenceHelper.GetFlagBit(CameraFlags, bit);

    /// <summary>
    /// Sets or clears a flag bit in the editable flag value.
    /// </summary>
    /// <param name="bit">Zero-based bit index to modify.</param>
    /// <param name="value"><see langword="true"/> to set the bit; otherwise <see langword="false"/>.</param>
    public void SetFlag(int bit, bool value) => CameraFlags = FlybySequenceHelper.SetFlagBit(CameraFlags, bit, value);

    /// <summary>
    /// Applies a property change to the selected camera and records undo state.
    /// </summary>
    private void ApplyPropertyToCamera(Action<FlybyCameraInstance> setter)
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

        RefreshTimelineState(false);
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
    /// Ensures preview is active, entering it if needed.
    /// Called when any timeline interaction should show the camera in Panel3D.
    /// </summary>
    public void EnsurePreviewActive()
    {
        if (!IsPreviewActive)
            _preview.EnterPreview(SelectedCamera?.Camera);
        else if (SelectedCamera is not null)
            _preview.ShowCamera(SelectedCamera.Camera);
    }

    /// <summary>
    /// Scrubs the timeline to a specific time in seconds.
    /// </summary>
    /// <param name="timeSeconds">Timeline time, in seconds, to preview.</param>
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
    /// <param name="worldPosition">World-space position used to resolve the owning room.</param>
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
    /// <param name="cameraIndex">Index of the dragged camera in the visible sequence list.</param>
    /// <param name="newTimeSeconds">Requested new timeline time for the dragged camera.</param>
    public void OnTimelineCameraDragged(int cameraIndex, float newTimeSeconds)
    {
        if (cameraIndex <= 0 || cameraIndex >= CameraList.Count || !float.IsFinite(newTimeSeconds))
            return;

        _preview.StopPlayback();

        EnsureTimelineDragUndoSnapshot(cameraIndex);

        float prevTime = GetTimecodeForCamera(cameraIndex - 1);
        float freezeAtPrev = GetFreezeDurationSeconds(cameraIndex - 1);
        float minTargetTime = prevTime + freezeAtPrev + FlybyConstants.TimeStep;
        float targetTime = Math.Max(newTimeSeconds, minTargetTime);

        var cameras = GetCamerasAsList();
        float newSpeed = FlybySequenceHelper.SolveSegmentSpeedForTargetTime(
            cameras,
            cameraIndex - 1,
            cameraIndex,
            targetTime,
            UseSmoothPause);

        _isApplyingProperty = true;

        try
        {
            CameraList[cameraIndex - 1].Camera.Speed = newSpeed;
            _editor.ObjectChange(CameraList[cameraIndex - 1].Camera, ObjectChangeType.Change);
        }
        finally
        {
            _isApplyingProperty = false;
        }

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
    private FlybySequenceTiming GetSequenceTiming()
    {
        var cameras = GetCamerasAsList();
        return GetSequenceCache(cameras)?.Timing ?? FlybySequenceHelper.AnalyzeSequence(cameras, UseSmoothPause);
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
        var timing = cache?.Timing ?? FlybySequenceHelper.AnalyzeSequence(cameras, UseSmoothPause);
        var selectedIndices = GetSelectedIndices();
        var cutBypassedSegments = GetCutBypassedSegments(cameras);
        var markers = new List<FlybyTimelineControl.TimelineMarker>(CameraList.Count);

        for (int i = 0; i < CameraList.Count; i++)
        {
            var item = CameraList[i];
            var camera = item.Camera;
            bool hasCameraCut = (camera.Flags & FlybyConstants.FlagCameraCut) != 0;

            markers.Add(new FlybyTimelineControl.TimelineMarker
            {
                TimeSeconds = timing.GetCameraTime(i),
                IsDuplicate = item.IsDuplicateIndex,
                IsSelected = selectedIndices.Contains(i),
                HasCameraCut = hasCameraCut,
                IsInCutBypass = cutBypassedSegments.Contains(i),
                CutBypassDuration = timing.GetCutBypassDuration(i),
                SegmentDuration = i < CameraList.Count - 1 ? timing.GetSegmentDuration(i) : 0,
                HasFreeze = (camera.Flags & FlybyConstants.FlagFreezeCamera) != 0,
                FreezeDuration = timing.GetFreezeDuration(i)
            });
        }

        float totalDuration = float.IsFinite(timing.TotalDuration)
            ? Math.Max(timing.TotalDuration, 1.0f)
            : 1.0f;

        return new TimelineRenderState(markers, cache, totalDuration);
    }

    /// <summary>
    /// Returns the freeze duration for the given camera index.
    /// </summary>
    private float GetFreezeDurationSeconds(int index) => GetSequenceTiming().GetFreezeDuration(index);

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

        return _preview.GetOrBuildCache(cameras, SelectedSequence.Value);
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

        if (!SelectedSequence.HasValue || _editor.Level is null)
        {
            RestoreSelectedCameraState();
            return;
        }

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
    /// <param name="sequence">Sequence id whose cameras should be renumbered.</param>
    /// <param name="excludeFromEvent">Optional camera that should not raise change notifications during the renumber pass.</param>
    private void RenumberSequence(ushort sequence, FlybyCameraInstance? excludeFromEvent = null)
    {
        if (_editor.Level is null)
            return;

        var cameras = FlybySequenceHelper.GetCameras(_editor.Level, sequence);
        var oldTargetByNumber = BuildCameraLookupByNumber(cameras);

        _isApplyingProperty = true;

        try
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
    /// Returns whether every camera in the collection was removed from its room.
    /// </summary>
    private static bool WereAllCamerasDeleted(IEnumerable<FlybyCameraInstance> cameras)
        => cameras.All(camera => camera.Room is null);

    /// <summary>
    /// Updates camera-cut timers after camera numbering changes.
    /// </summary>
    /// <param name="cameras">Renumbered cameras that may need their cut targets updated.</param>
    /// <param name="oldTargetByNumber">Lookup of pre-renumber cameras keyed by their old number.</param>
    /// <param name="getOriginalTimer">Callback that returns each camera's original cut target timer.</param>
    /// <param name="excludeFromEvent">Optional camera that should not raise change notifications during the update.</param>
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

        return FlybySequenceHelper.GetCameras(_editor.Level, SelectedSequence.Value);
    }

    /// <summary>
    /// Returns the visible camera items as a materialized camera list.
    /// </summary>
    private IReadOnlyList<FlybyCameraInstance> GetCamerasAsList() => [.. CameraList.Select(vm => vm.Camera)];

    /// <summary>
    /// Selects a single camera in the timeline and synced editor state.
    /// </summary>
    private void SelectCameraByInstance(FlybyCameraInstance camera) => SetSelectedCameras([camera], true);

    /// <summary>
    /// Refreshes camera data and optionally syncs the preview output.
    /// </summary>
    /// <param name="refreshCameraList">Whether the visible camera list should be rebuilt first.</param>
    /// <param name="syncPreview">Whether preview state should be synchronized after refreshing timeline data.</param>
    private void RefreshTimelineState(bool refreshCameraList, bool syncPreview = true)
    {
        if (refreshCameraList)
            RefreshCameraList();

        RecalculateTimecodes();
        TimelineRefreshRequested?.Invoke();

        if (syncPreview)
            RefreshPreviewState();
    }

    /// <summary>
    /// Updates the preview camera or scrub state from the current selection.
    /// </summary>
    private void RefreshPreviewState()
    {
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
        IsPlaying = _preview.IsPlaying;
        OnPropertyChanged(nameof(IsPreviewActive));
        OnPropertyChanged(nameof(CanEditProperties));
    }

    /// <summary>
    /// Synchronizes the playhead position from preview playback.
    /// </summary>
    private void OnPreviewPlayheadChanged()
    {
        PlayheadSeconds = _preview.PlayheadSeconds;
    }

    #endregion Preview state sync

    #region Editor event handling

    /// <summary>
    /// Handles editor events that affect flyby data, preview, or selection.
    /// </summary>
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
            ResetPlayhead();

            if (obj is Editor.LevelChangedEvent)
                _userAddedSequences.Clear();

            RefreshSequenceList();
            RefreshTimelineState(true, false);
            return;
        }

        if (obj is Editor.ObjectChangedEvent changeEvent)
        {
            if (changeEvent.Object is FlybyCameraInstance flyby)
            {
                if (!_isApplyingProperty && changeEvent.ChangeType != ObjectChangeType.Change)
                    RefreshSequenceList();

                if (SelectedSequence.HasValue && flyby.Sequence == SelectedSequence.Value)
                {
                    _preview.InvalidateCache();

                    if (!_isApplyingProperty)
                        OnDataChanged();
                }
            }
        }

        // Detect external preview exit (ESC key in Panel3D).
        if (obj is Editor.ToggleCameraPreviewEvent previewEvent && !previewEvent.PreviewState)
            _preview.OnExternalPreviewExit();

        // Selection changed in editor - sync to flyby manager.
        if (obj is Editor.SelectedObjectChangedEvent selEvent && !_isSyncingSelection)
            SyncSelectionFromEditor(selEvent.Current);
    }

    #endregion Editor event handling

    /// <summary>
    /// Creates undo instances for property changes on the given cameras.
    /// </summary>
    private List<UndoRedoInstance> CreateFlybyCameraPropertyUndo(IEnumerable<FlybyCameraInstance> cameras)
    {
        return [.. cameras
            .Where(camera => camera.Room is not null)
            .Distinct()
            .Select(camera => (UndoRedoInstance)new ChangeObjectPropertyUndoInstance(_editor.UndoManager, camera))];
    }

    /// <summary>
    /// Creates undo instances for deleting the given cameras.
    /// </summary>
    private IEnumerable<UndoRedoInstance> CreateFlybyCameraDeletionUndo(IEnumerable<FlybyCameraInstance> cameras)
    {
        return cameras
            .Where(camera => camera.Room is not null)
            .Distinct()
            .Select(camera => (UndoRedoInstance)new AddRemoveObjectUndoInstance(_editor.UndoManager, camera, false));
    }

    /// <summary>
    /// Pushes undo instances only when there is captured undo state.
    /// </summary>
    private void PushUndoIfAny(List<UndoRedoInstance> undoInstances)
    {
        if (undoInstances.Count > 0)
            _editor.UndoManager.Push(undoInstances);
    }

    /// <summary>
    /// Creates an undo snapshot when a timeline drag starts affecting a segment.
    /// </summary>
    private void EnsureTimelineDragUndoSnapshot(int cameraIndex)
    {
        int speedCameraIndex = cameraIndex - 1;

        if (_activeDraggedCameraIndex == speedCameraIndex || speedCameraIndex < 0 || speedCameraIndex >= CameraList.Count)
            return;

        PushUndoIfAny(CreateFlybyCameraPropertyUndo([CameraList[speedCameraIndex].Camera]));
        _activeDraggedCameraIndex = speedCameraIndex;
    }

    /// <summary>
    /// Returns the active dialog owner for flyby editor windows.
    /// </summary>
    private IWin32Window? GetDialogOwner() => Form.ActiveForm ?? _dialogOwner;

    /// <summary>
    /// Replaces the selected flyby cameras and optionally syncs editor selection.
    /// </summary>
    private void SetSelectedCameras(IEnumerable<FlybyCameraInstance> cameras, bool syncEditorSelection)
    {
        var normalizedSelection = SelectedSequence.HasValue
            ? cameras.Where(camera => camera.Sequence == SelectedSequence.Value).ToHashSet()
            : [];

        _selectedCameras.Clear();

        foreach (var camera in normalizedSelection)
            _selectedCameras.Add(camera);

        RestoreSelectedCameraState();

        if (syncEditorSelection)
            SyncEditorSelection();

        TimelineRefreshRequested?.Invoke();
    }

    /// <summary>
    /// Restores selected camera state after the visible camera list changes.
    /// </summary>
    private void RestoreSelectedCameraState()
    {
        if (!SelectedSequence.HasValue)
        {
            _selectedCameras.Clear();
            SelectedCamera = null;
            return;
        }

        var visibleCameras = CameraList.Select(item => item.Camera).ToHashSet();
        _selectedCameras.RemoveWhere(camera => camera.Sequence != SelectedSequence.Value || !visibleCameras.Contains(camera));

        if (_selectedCameras.Count == 1)
            SelectedCamera = CameraList.FirstOrDefault(item => _selectedCameras.Contains(item.Camera));
        else
            SelectedCamera = null;
    }

    /// <summary>
    /// Mirrors the editor selection into the flyby timeline selection.
    /// </summary>
    private void SyncSelectionFromEditor(ObjectInstance? currentSelection)
    {
        var selectedCameras = GetSelectedFlybyCameras(currentSelection);

        _isSyncingSelection = true;

        try
        {
            AlignSequenceToSelection(selectedCameras);
            SetSelectedCameras(selectedCameras, false);
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    /// <summary>
    /// Switches the active sequence to match the current camera selection.
    /// </summary>
    private void AlignSequenceToSelection(IReadOnlyCollection<FlybyCameraInstance> selectedCameras)
    {
        if (selectedCameras.Count == 0)
            return;

        if (SelectedSequence.HasValue && selectedCameras.Any(camera => camera.Sequence == SelectedSequence.Value))
            return;

        ushort? sequence = selectedCameras
            .Select(camera => camera.Sequence)
            .Where(AvailableSequences.Contains)
            .Select(sequenceValue => (ushort?)sequenceValue)
            .OrderBy(sequenceValue => sequenceValue)
            .FirstOrDefault();

        if (sequence.HasValue)
            SelectedSequence = sequence.Value;
    }

    /// <summary>
    /// Extracts flyby cameras from the current editor selection object.
    /// </summary>
    private static IReadOnlyList<FlybyCameraInstance> GetSelectedFlybyCameras(ObjectInstance? currentSelection)
    {
        if (currentSelection is FlybyCameraInstance flybyCamera)
            return [flybyCamera];

        if (currentSelection is ObjectGroup group)
            return [.. group.OfType<FlybyCameraInstance>()];

        return [];
    }

    /// <summary>
    /// Merges timeline-selected cameras with non-flyby editor selection objects.
    /// </summary>
    private List<PositionBasedObjectInstance> GetMergedEditorSelection()
    {
        var mergedSelection = GetEditorSelectionObjects()
            .Where(objectInstance => objectInstance is not FlybyCameraInstance flybyCamera ||
                                     !SelectedSequence.HasValue ||
                                     flybyCamera.Sequence != SelectedSequence.Value)
            .ToList();

        mergedSelection.AddRange(_selectedCameras);

        return [.. mergedSelection.Distinct()];
    }

    /// <summary>
    /// Returns the current editor selection as position-based objects.
    /// </summary>
    private List<PositionBasedObjectInstance> GetEditorSelectionObjects()
    {
        if (_editor.SelectedObject is ObjectGroup group)
            return [.. group.Cast<PositionBasedObjectInstance>()];

        if (_editor.SelectedObject is PositionBasedObjectInstance positionBased)
            return [positionBased];

        return [];
    }

    /// <summary>
    /// Applies a new selection back into the editor.
    /// </summary>
    private void SetEditorSelection(IReadOnlyList<PositionBasedObjectInstance> selectedObjects)
    {
        var currentSelection = GetEditorSelectionObjects();

        if (currentSelection.Count == selectedObjects.Count && currentSelection.All(selectedObjects.Contains))
            return;

        _editor.SelectedObject = BuildSelectionObject(selectedObjects);
    }

    /// <summary>
    /// Builds the appropriate editor selection object for the given items.
    /// </summary>
    private static ObjectInstance? BuildSelectionObject(IReadOnlyList<PositionBasedObjectInstance> selectedObjects)
    {
        if (selectedObjects.Count == 0)
            return null;

        if (selectedObjects.Count == 1)
            return selectedObjects[0];

        return new ObjectGroup([.. selectedObjects]);
    }
}
