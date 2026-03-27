using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// WPF UserControl that embeds the flyby timeline and its controls.
/// Hosted inside MainView via ElementHost.
/// </summary>
public partial class FlybyTimelineView : UserControl
{
    private FlybyTimelineViewModel _viewModel;
    private System.Windows.Forms.IWin32Window _parentForm;

    public FlybyTimelineView()
    {
        InitializeComponent();
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

        _viewModel = new FlybyTimelineViewModel(Editor.Instance, Dispatcher);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TimelineRefreshRequested += RefreshTimeline;

        timelineControl.MarkerClicked += OnTimelineMarkerClicked;
        timelineControl.MarkerDoubleClicked += OnTimelineMarkerDoubleClicked;
        timelineControl.MarkerDragged += OnTimelineMarkerDragged;
        timelineControl.MarkerDragCompleted += OnTimelineMarkerDragCompleted;
        timelineControl.RangeSelected += OnTimelineRangeSelected;
        timelineControl.ScrubRequested += OnTimelineScrubRequested;
        timelineControl.PlayStopRequested += OnTimelinePlayStopRequested;
        timelineControl.DeleteRequested += OnTimelineDeleteRequested;
        timelineControl.MarkerReordered += OnTimelineMarkerReordered;

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
        timelineControl.MarkerDragCompleted -= OnTimelineMarkerDragCompleted;
        timelineControl.RangeSelected -= OnTimelineRangeSelected;
        timelineControl.ScrubRequested -= OnTimelineScrubRequested;
        timelineControl.PlayStopRequested -= OnTimelinePlayStopRequested;
        timelineControl.DeleteRequested -= OnTimelineDeleteRequested;
        timelineControl.MarkerReordered -= OnTimelineMarkerReordered;

        _viewModel.Cleanup();
        _viewModel = null;
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

    #region Timeline event handlers

    private void RefreshTimeline()
    {
        if (_viewModel == null)
            return;

        var cameras = _viewModel.CameraList;
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

            float cutBypassDuration = _viewModel.GetCutBypassDuration(i);

            markers.Add(new FlybyTimelineControl.TimelineMarker
            {
                TimeSeconds = timeSeconds,
                IsDuplicate = item.IsDuplicateIndex,
                IsSelected = selectedIndices.Contains(i),
                HasCameraCut = _viewModel.GetCameraCutFlag(i),
                IsInCutBypass = cutBypassed.Contains(i),
                CutBypassDuration = cutBypassDuration,
                SegmentDuration = i < cameras.Count - 1 ? _viewModel.GetSegmentDurationSeconds(i) : 0,
                HasFreeze = (item.Camera.Flags & FlybyConstants.FlagFreezeCamera) != 0,
                FreezeDuration = _viewModel.GetFreezeDurationSeconds(i)
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

        var item = _viewModel.CameraList[index];
        SelectSingleCamera(item);

        _viewModel.UpdateSelectedRoomByPosition(item.Camera.WorldPosition);
        RefreshTimeline();
    }

    private void OnTimelineMarkerDoubleClicked(int index)
    {
        if (_viewModel == null || index < 0 || index >= _viewModel.CameraList.Count)
            return;

        var item = _viewModel.CameraList[index];

        SelectSingleCamera(item);

        // Find parent WinForms control for the dialog owner.
        EditorActions.EditObject(item.Camera, GetDialogOwner());
    }

    private void OnTimelineMarkerDragged(int index, float newTimeSeconds)
    {
        if (_viewModel == null)
            return;

        _viewModel.OnTimelineCameraDragged(index, newTimeSeconds);
        RefreshTimeline();
    }

    private void OnTimelineMarkerDragCompleted(int index)
    {
        _viewModel?.OnTimelineCameraDragCompleted();
    }

    private void OnTimelineRangeSelected(List<int> selectedIndices)
    {
        if (_viewModel == null)
            return;

        // Empty range selection means deselection.
        if (selectedIndices.Count == 0)
        {
            _viewModel.UpdateSelectedCameras(Array.Empty<FlybyCameraItemViewModel>());
            RefreshTimeline();
            return;
        }

        var selectedItems = new List<FlybyCameraItemViewModel>();

        foreach (int i in selectedIndices)
        {
            if (i >= 0 && i < _viewModel.CameraList.Count)
                selectedItems.Add(_viewModel.CameraList[i]);
        }

        _viewModel.UpdateSelectedCameras(selectedItems);

        RefreshTimeline();
    }

    private void OnTimelineScrubRequested(float timeSeconds)
    {
        _viewModel?.ScrubToTime(timeSeconds);
    }

    private void OnTimelinePlayStopRequested()
    {
        if (_viewModel == null)
            return;

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
    }

    private System.Windows.Forms.IWin32Window GetDialogOwner()
    {
        return System.Windows.Forms.Form.ActiveForm ?? _parentForm;
    }

    #endregion Timeline event handlers
}
