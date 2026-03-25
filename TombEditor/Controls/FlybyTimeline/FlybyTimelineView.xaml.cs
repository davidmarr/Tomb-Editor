using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// WPF UserControl that embeds the flyby timeline and its controls.
/// Hosted inside MainView via ElementHost.
/// </summary>
public partial class FlybyTimelineView : UserControl
{
    private readonly Editor _editor;

    private FlybyTimelineViewModel _viewModel;
    private bool _isUpdatingSelection;
    private System.Windows.Forms.IWin32Window _parentForm;

    public FlybyTimelineView()
    {
        InitializeComponent();
        _editor = Editor.Instance;
    }

    /// <summary>
    /// Initializes the view model and wires up all event handlers.
    /// Called once when the hosting MainView is ready.
    /// </summary>
    public void Initialize(System.Windows.Forms.IWin32Window parentForm = null)
    {
        if (_viewModel != null)
            return;

        _parentForm = parentForm;

        _viewModel = new FlybyTimelineViewModel(_editor, Dispatcher);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TimelineRefreshRequested += RefreshTimeline;

        timelineControl.MarkerClicked += OnTimelineMarkerClicked;
        timelineControl.MarkerDoubleClicked += OnTimelineMarkerDoubleClicked;
        timelineControl.MarkerDragged += OnTimelineMarkerDragged;
        timelineControl.RangeSelected += OnTimelineRangeSelected;
        timelineControl.ScrubRequested += OnTimelineScrubRequested;
        timelineControl.PlayStopRequested += OnTimelinePlayStopRequested;
        timelineControl.DeleteRequested += OnTimelineDeleteRequested;
        timelineControl.MarkerReordered += OnTimelineMarkerReordered;

        _editor.EditorEventRaised += OnEditorEventRaised;

        RefreshTimeline();
    }

    /// <summary>
    /// Cleans up all event subscriptions.
    /// </summary>
    public void Cleanup()
    {
        if (_viewModel == null)
            return;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.TimelineRefreshRequested -= RefreshTimeline;

        timelineControl.MarkerClicked -= OnTimelineMarkerClicked;
        timelineControl.MarkerDoubleClicked -= OnTimelineMarkerDoubleClicked;
        timelineControl.MarkerDragged -= OnTimelineMarkerDragged;
        timelineControl.RangeSelected -= OnTimelineRangeSelected;
        timelineControl.ScrubRequested -= OnTimelineScrubRequested;
        timelineControl.PlayStopRequested -= OnTimelinePlayStopRequested;
        timelineControl.DeleteRequested -= OnTimelineDeleteRequested;
        timelineControl.MarkerReordered -= OnTimelineMarkerReordered;

        _editor.EditorEventRaised -= OnEditorEventRaised;

        _viewModel.Cleanup();
        _viewModel = null;
    }

    /// <summary>
    /// Synchronizes timeline selection when flyby cameras are selected externally (e.g. Panel3D multiselect).
    /// </summary>
    public void SyncMultiselection(IEnumerable<FlybyCameraInstance> selectedCameras)
    {
        if (_viewModel == null || _isUpdatingSelection)
            return;

        _isUpdatingSelection = true;

        var matching = _viewModel.CameraList
            .Where(vm => selectedCameras.Contains(vm.Camera))
            .ToList();

        _viewModel.UpdateSelectedCameras(matching);
        RefreshTimeline();

        _isUpdatingSelection = false;
    }

    private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FlybyTimelineViewModel.SelectedSequence):
                RefreshTimeline();
                timelineControl.ZoomToFit();
                break;

            case nameof(FlybyTimelineViewModel.SelectedCamera):
                RefreshTimeline();
                break;

            case nameof(FlybyTimelineViewModel.PlayheadSeconds):
                timelineControl.SetPlayheadSeconds(_viewModel.PlayheadSeconds);
                break;
        }
    }

    private void OnEditorEventRaised(IEditorEvent obj)
    {
        if (_viewModel == null)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnEditorEventRaised(obj));
            return;
        }

        // Handle multiselection sync from Panel3D.
        if (obj is Editor.SelectedObjectChangedEvent && !_isUpdatingSelection)
        {
            var editor = Editor.Instance;
            var selectedObject = editor.SelectedObject;

            if (selectedObject is FlybyCameraInstance flyby)
            {
                _isUpdatingSelection = true;
                SyncSingleFlybySelection(flyby);
                _isUpdatingSelection = false;
                RefreshTimeline();
            }
            else if (selectedObject is ObjectGroup group)
            {
                var flybyCameras = group.OfType<FlybyCameraInstance>().ToList();

                if (flybyCameras.Count > 0)
                {
                    _isUpdatingSelection = true;
                    SyncMultiselection(flybyCameras);
                    _isUpdatingSelection = false;
                }
            }
        }
    }

    private void SyncSingleFlybySelection(FlybyCameraInstance flyby)
    {
        if (_viewModel.AvailableSequences.Contains(flyby.Sequence))
        {
            _viewModel.SelectedSequence = flyby.Sequence;

            var item = _viewModel.CameraList.FirstOrDefault(c => c.Camera == flyby);

            if (item != null)
                SelectSingleCamera(item);
        }
    }

    #region Timeline event handlers

    private void RefreshTimeline()
    {
        if (_viewModel == null)
            return;

        var cameras = _viewModel.CameraList;
        var cameraInstances = cameras.Select(vm => vm.Camera).ToList();
        var selectedIndices = _viewModel.GetSelectedIndices();
        var markers = new List<FlybyTimelineControl.TimelineMarker>();

        // Get cache for accurate timing and speed display.
        var cache = _viewModel.GetSequenceCache();

        // Determine which cameras have their outgoing segments bypassed by a cut.
        var cutBypassed = new HashSet<int>();

        for (int i = 0; i < cameras.Count; i++)
        {
            if (_viewModel.GetCameraCutFlag(i))
            {
                int target = cameras[i].Camera.Timer;

                for (int j = i; j < target && j < cameras.Count - 1; j++)
                    cutBypassed.Add(j);
            }
        }

        for (int i = 0; i < cameras.Count; i++)
        {
            var item = cameras[i];
            float timeSeconds = _viewModel.GetTimecodeForCamera(i);

            // Compute cut bypass duration from static times.
            float cutBypassDuration = 0;

            if (_viewModel.GetCameraCutFlag(i))
            {
                int target = item.Camera.Timer;

                if (target > i && target < cameras.Count)
                {
                    float targetTime = _viewModel.GetTimecodeForCamera(target);
                    cutBypassDuration = Math.Max(0, targetTime - timeSeconds);
                }
            }

            markers.Add(new FlybyTimelineControl.TimelineMarker
            {
                TimeSeconds = timeSeconds,
                IsDuplicate = item.IsDuplicateIndex,
                IsSelected = selectedIndices.Contains(i),
                HasCameraCut = _viewModel.GetCameraCutFlag(i),
                IsInCutBypass = cutBypassed.Contains(i),
                CutBypassDuration = cutBypassDuration,
                SegmentDuration = i < cameras.Count - 1 ? FlybySequenceHelper.GetSegmentDuration(cameraInstances, i) : 0,
                HasFreeze = (item.Camera.Flags & FlybyConstants.FlagFreezeCamera) != 0
            });
        }

        float totalDuration = _viewModel.GetCacheDisplayDuration(cache);

        if (totalDuration < 1.0f)
            totalDuration = 10.0f;

        timelineControl.SetMarkers(markers, totalDuration, cache);
    }

    private void OnTimelineMarkerClicked(int index)
    {
        if (_viewModel == null || index < 0 || index >= _viewModel.CameraList.Count)
            return;

        _isUpdatingSelection = true;

        var item = _viewModel.CameraList[index];
        SelectSingleCamera(item);

        _isUpdatingSelection = false;

        _viewModel.UpdateSelectedRoomByPosition(item.Camera.WorldPosition);
        RefreshTimeline();
    }

    private void OnTimelineMarkerDoubleClicked(int index)
    {
        if (_viewModel == null || index < 0 || index >= _viewModel.CameraList.Count)
            return;

        var item = _viewModel.CameraList[index];

        _isUpdatingSelection = true;
        SelectSingleCamera(item);
        _isUpdatingSelection = false;

        // Find parent WinForms control for the dialog owner.
        EditorActions.EditObject(item.Camera, _parentForm);
    }

    private void OnTimelineMarkerDragged(int index, float newTimeSeconds)
    {
        if (_viewModel == null)
            return;

        _viewModel.OnTimelineCameraDragged(index, newTimeSeconds);
        RefreshTimeline();
    }

    private void OnTimelineRangeSelected(List<int> selectedIndices)
    {
        if (_viewModel == null)
            return;

        // Empty range selection means deselection.
        if (selectedIndices.Count == 0)
        {
            _isUpdatingSelection = true;
            _viewModel.UpdateSelectedCameras(Array.Empty<FlybyCameraItemViewModel>());
            _viewModel.SelectedCamera = null;
            _isUpdatingSelection = false;

            RefreshTimeline();
            return;
        }

        _isUpdatingSelection = true;

        var selectedItems = new List<FlybyCameraItemViewModel>();

        foreach (int i in selectedIndices)
        {
            if (i >= 0 && i < _viewModel.CameraList.Count)
                selectedItems.Add(_viewModel.CameraList[i]);
        }

        _viewModel.UpdateSelectedCameras(selectedItems);

        _isUpdatingSelection = false;

        RefreshTimeline();
    }

    private void OnTimelineScrubRequested(float timeSeconds)
    {
        _viewModel?.ScrubToTime(timeSeconds);
    }

    private void OnTimelinePlayStopRequested()
    {
        if (_viewModel?.IsPreviewActive == true)
            _viewModel.TogglePlayStopCommand.Execute(null);
    }

    private void OnTimelineDeleteRequested()
    {
        _viewModel?.DeleteSelectedCameras();
        RefreshTimeline();
    }

    private void OnTimelineMarkerReordered(int fromIndex, int toIndex)
    {
        if (_viewModel == null)
            return;

        _viewModel.MoveCameraToIndex(fromIndex, toIndex);
        RefreshTimeline();
    }

    private void SelectSingleCamera(FlybyCameraItemViewModel item)
    {
        _viewModel.UpdateSelectedCameras(new[] { item });
        _viewModel.SelectedCamera = item;
    }

    #endregion Timeline event handlers
}
