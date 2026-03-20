using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyManager;

/// <summary>
/// WPF UserControl that embeds the flyby timeline and its controls.
/// Hosted inside MainView via ElementHost.
/// </summary>
public partial class FlybyTimelineView : UserControl
{
    private FlybyManagerViewModel _viewModel;
    private bool _isUpdatingSelection;
    private System.Windows.Forms.IWin32Window _parentForm;

    public FlybyTimelineView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the view model and wires up all event handlers.
    /// Called once when the hosting MainView is ready.
    /// </summary>
    public void Initialize(Editor editor, System.Windows.Forms.IWin32Window parentForm = null)
    {
        if (_viewModel != null)
            return;

        _parentForm = parentForm;

        _viewModel = new FlybyManagerViewModel(editor, Dispatcher);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TimelineRefreshRequested += RefreshTimeline;

        timelineControl.MarkerClicked += OnTimelineMarkerClicked;
        timelineControl.MarkerDoubleClicked += OnTimelineMarkerDoubleClicked;
        timelineControl.MarkerDragged += OnTimelineMarkerDragged;
        timelineControl.RangeSelected += OnTimelineRangeSelected;
        timelineControl.ScrubRequested += OnTimelineScrubRequested;
        timelineControl.PlayStopRequested += OnTimelinePlayStopRequested;

        editor.EditorEventRaised += OnEditorEventRaised;

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
            case nameof(FlybyManagerViewModel.SelectedCamera):
                RefreshTimeline();
                break;

            case nameof(FlybyManagerViewModel.PlayheadSeconds):
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
            {
                _viewModel.UpdateSelectedCameras(new[] { item });
                _viewModel.SelectedCamera = item;
            }
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

        // Pre-compute normalized speed range for the speed curve.
        float minSpeed = float.MaxValue;
        float maxSpeed = float.MinValue;

        for (int i = 0; i < cameras.Count - 1; i++)
        {
            float s = cameras[i].Camera.Speed;

            if (s < minSpeed)
                minSpeed = s;

            if (s > maxSpeed)
                maxSpeed = s;
        }

        float speedRange = cameras.Count > 1 ? maxSpeed - minSpeed : 0;

        for (int i = 0; i < cameras.Count; i++)
        {
            var item = cameras[i];
            float timeSeconds = _viewModel.GetTimecodeForCamera(i);
            float relativeSpeed = speedRange > 0.001f
                ? (item.Camera.Speed - minSpeed) / speedRange
                : 1.0f;

            markers.Add(new FlybyTimelineControl.TimelineMarker
            {
                TimeSeconds = timeSeconds,
                IsDuplicate = item.IsDuplicateIndex,
                IsSelected = selectedIndices.Contains(i),
                HasCameraCut = _viewModel.GetCameraCutFlag(i),
                CutBypassDuration = _viewModel.GetCutBypassDuration(i),
                SegmentDuration = i < cameras.Count - 1
                    ? FlybySequenceData.GetSegmentDuration(item.Camera)
                    : 0,
                RelativeSpeed = relativeSpeed,
                FreezeDurationSeconds = _viewModel.GetFreezeDurationSeconds(i)
            });
        }

        float totalDuration = _viewModel.GetDisplayDuration();

        if (totalDuration < 1.0f)
            totalDuration = 10.0f;

        timelineControl.SetMarkers(markers, totalDuration);
    }

    private void OnTimelineMarkerClicked(int index)
    {
        if (_viewModel == null || index < 0 || index >= _viewModel.CameraList.Count)
            return;

        _isUpdatingSelection = true;

        var item = _viewModel.CameraList[index];
        _viewModel.UpdateSelectedCameras(new[] { item });
        _viewModel.SelectedCamera = item;

        _isUpdatingSelection = false;

        RefreshTimeline();
    }

    private void OnTimelineMarkerDoubleClicked(int index)
    {
        if (_viewModel == null || index < 0 || index >= _viewModel.CameraList.Count)
            return;

        var item = _viewModel.CameraList[index];

        _isUpdatingSelection = true;
        _viewModel.UpdateSelectedCameras(new[] { item });
        _viewModel.SelectedCamera = item;
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

    #endregion Timeline event handlers
}
