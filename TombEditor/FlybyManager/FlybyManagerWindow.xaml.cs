using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TombEditor.FlybyManager;

public partial class FlybyManagerWindow : Window
{
    private readonly FlybyManagerViewModel _viewModel;
    private bool _isUpdatingFlags;
    private bool _isUpdatingSelection;

    // Drag-reorder state.
    private FlybyCameraItemViewModel? _dragItem;
    private Point _dragStartPoint;
    private bool _isDragReorderActive;

    public FlybyManagerWindow(Editor editor)
    {
        InitializeComponent();

        _viewModel = new FlybyManagerViewModel(editor, Dispatcher);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.TimelineRefreshRequested += RefreshTimeline;

        timelineControl.MarkerClicked += OnTimelineMarkerClicked;
        timelineControl.MarkerDragged += OnTimelineMarkerDragged;
        timelineControl.RangeSelected += OnTimelineRangeSelected;
        timelineControl.ScrubRequested += OnTimelineScrubRequested;

        RefreshTimeline();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.TimelineRefreshRequested -= RefreshTimeline;

        timelineControl.MarkerClicked -= OnTimelineMarkerClicked;
        timelineControl.MarkerDragged -= OnTimelineMarkerDragged;
        timelineControl.RangeSelected -= OnTimelineRangeSelected;
        timelineControl.ScrubRequested -= OnTimelineScrubRequested;

        _viewModel.Cleanup();
        base.OnClosing(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FlybyManagerViewModel.CameraFlags):
                UpdateFlagCheckBoxes();
                break;

            case nameof(FlybyManagerViewModel.SelectedCamera):
                SyncListBoxSelection();
                RefreshTimeline();
                break;

            case nameof(FlybyManagerViewModel.PlayheadSeconds):
                timelineControl.SetPlayheadSeconds(_viewModel.PlayheadSeconds);
                break;
        }
    }

    private void CameraListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isDragReorderActive || _isUpdatingSelection)
            return;

        _isUpdatingSelection = true;

        var selectedItems = cameraListBox.SelectedItems
            .Cast<FlybyCameraItemViewModel>()
            .ToList();

        _viewModel.UpdateSelectedCameras(selectedItems);
        RefreshTimeline();

        _isUpdatingSelection = false;
    }

    private void SyncListBoxSelection()
    {
        if (_isUpdatingSelection)
            return;

        _isUpdatingSelection = true;

        if (_viewModel.SelectedCamera != null && cameraListBox.SelectedItem != _viewModel.SelectedCamera)
            cameraListBox.SelectedItem = _viewModel.SelectedCamera;

        _isUpdatingSelection = false;
    }

    #region Flag checkbox handling

    private void FlagCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingFlags)
            return;

        if (sender is CheckBox cb && int.TryParse(cb.Tag?.ToString(), out int bit))
            _viewModel.SetFlag(bit, cb.IsChecked == true);
    }

    private void UpdateFlagCheckBoxes()
    {
        _isUpdatingFlags = true;

        SetCheckBoxFlag(cbBit0, 0);
        SetCheckBoxFlag(cbBit1, 1);
        SetCheckBoxFlag(cbBit2, 2);
        SetCheckBoxFlag(cbBit3, 3);
        SetCheckBoxFlag(cbBit4, 4);
        SetCheckBoxFlag(cbBit5, 5);
        SetCheckBoxFlag(cbBit6, 6);
        SetCheckBoxFlag(cbBit7, 7);
        SetCheckBoxFlag(cbBit8, 8);
        SetCheckBoxFlag(cbBit9, 9);
        SetCheckBoxFlag(cbBit10, 10);
        SetCheckBoxFlag(cbBit11, 11);
        SetCheckBoxFlag(cbBit12, 12);
        SetCheckBoxFlag(cbBit13, 13);
        SetCheckBoxFlag(cbBit14, 14);
        SetCheckBoxFlag(cbBit15, 15);

        _isUpdatingFlags = false;
    }

    private void SetCheckBoxFlag(CheckBox cb, int bit)
    {
        cb.IsChecked = _viewModel.GetFlag(bit);
    }

    #endregion Flag checkbox handling

    #region Timeline handling

    private void RefreshTimeline()
    {
        var markers = new List<FlybyTimelineControl.TimelineMarker>();

        for (int i = 0; i < _viewModel.CameraList.Count; i++)
        {
            var item = _viewModel.CameraList[i];
            float timeSeconds = _viewModel.GetTimecodeForCamera(i);

            // Use actual ListBox selection state to avoid stale data.
            bool isSelected = cameraListBox.SelectedItems.Contains(item);

            markers.Add(new FlybyTimelineControl.TimelineMarker
            {
                TimeSeconds = timeSeconds,
                IsDuplicate = item.IsDuplicateIndex,
                IsSelected = isSelected
            });
        }

        float totalDuration = _viewModel.GetDisplayDuration();

        if (totalDuration < 1.0f)
            totalDuration = 10.0f;

        timelineControl.SetMarkers(markers, totalDuration);
    }

    private void OnTimelineMarkerClicked(int index)
    {
        if (index < 0 || index >= _viewModel.CameraList.Count)
            return;

        _isUpdatingSelection = true;

        var item = _viewModel.CameraList[index];
        cameraListBox.SelectedItem = item;
        _viewModel.UpdateSelectedCameras(new[] { item });
        _viewModel.SelectedCamera = item;

        _isUpdatingSelection = false;

        RefreshTimeline();
    }

    private void OnTimelineMarkerDragged(int index, float newTimeSeconds)
    {
        _viewModel.OnTimelineCameraDragged(index, newTimeSeconds);
        RefreshTimeline();
    }

    private void OnTimelineRangeSelected(List<int> selectedIndices)
    {
        if (selectedIndices.Count == 0)
            return;

        _isUpdatingSelection = true;

        cameraListBox.SelectedItems.Clear();
        var selectedItems = new List<FlybyCameraItemViewModel>();

        foreach (int i in selectedIndices)
        {
            if (i >= 0 && i < _viewModel.CameraList.Count)
            {
                var item = _viewModel.CameraList[i];
                cameraListBox.SelectedItems.Add(item);
                selectedItems.Add(item);
            }
        }

        _viewModel.UpdateSelectedCameras(selectedItems);

        _isUpdatingSelection = false;

        RefreshTimeline();
    }

    private void OnTimelineScrubRequested(float timeSeconds)
    {
        _viewModel.ScrubToTime(timeSeconds);
    }

    #endregion Timeline handling

    #region Camera list drag reorder

    private void CameraListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(cameraListBox);
        _dragItem = GetItemAtPosition(e);
    }

    private void CameraListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var diff = e.GetPosition(cameraListBox) - _dragStartPoint;

        if (Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _isDragReorderActive = true;
        DragDrop.DoDragDrop(cameraListBox, _dragItem, DragDropEffects.Move);
        _isDragReorderActive = false;
        _dragItem = null;
    }

    private void CameraListBox_Drop(object sender, DragEventArgs e)
    {
        var droppedItem = e.Data.GetData(typeof(FlybyCameraItemViewModel)) as FlybyCameraItemViewModel;

        if (droppedItem == null)
            return;

        var targetItem = GetItemAtPosition(e, cameraListBox);

        if (targetItem == null || targetItem == droppedItem)
            return;

        int fromIndex = _viewModel.CameraList.IndexOf(droppedItem);
        int toIndex = _viewModel.CameraList.IndexOf(targetItem);

        if (fromIndex >= 0 && toIndex >= 0)
            _viewModel.MoveCameraToIndex(fromIndex, toIndex);
    }

    private FlybyCameraItemViewModel? GetItemAtPosition(MouseButtonEventArgs e)
    {
        var element = cameraListBox.InputHitTest(e.GetPosition(cameraListBox)) as DependencyObject;
        return FindAncestorData<FlybyCameraItemViewModel>(element);
    }

    private FlybyCameraItemViewModel? GetItemAtPosition(DragEventArgs e, ListBox listBox)
    {
        var element = listBox.InputHitTest(e.GetPosition(listBox)) as DependencyObject;
        return FindAncestorData<FlybyCameraItemViewModel>(element);
    }

    private static T? FindAncestorData<T>(DependencyObject? element) where T : class
    {
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.DataContext is T data)
                return data;

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    #endregion Camera list drag reorder
}
