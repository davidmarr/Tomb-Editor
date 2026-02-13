using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TombEditor.ViewModels;

namespace TombEditor.Views;

public partial class ContentBrowserView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;

    public ContentBrowserView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Records the mouse position when the left button is pressed for drag-drop detection.
    /// </summary>
    private void AssetListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start drag when clicking on the scrollbar
        if (IsOverScrollbar(e))
            return;

        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    /// <summary>
    /// Initiates a drag-drop operation by delegating to the WinForms host.
    /// WPF's DragDrop.DoDragDrop uses COM OLE that doesn't interop cleanly
    /// with WinForms drop targets. Instead, we raise DragDropRequested on the
    /// ViewModel, and the WinForms ContentBrowser host calls Control.DoDragDrop.
    /// </summary>
    private void AssetListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _isDragging = false;
            return;
        }

        // Don't drag when interacting with the scrollbar
        if (IsOverScrollbar(e))
            return;

        Point currentPos = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPos;

        // Check if the movement exceeds the system drag threshold
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDragging)
            return;

        // Find the asset item under the cursor
        if (AssetListBox.SelectedItem is AssetItemViewModel selectedItem &&
            DataContext is ContentBrowserViewModel vm)
        {
            _isDragging = true;

            // Delegate drag-drop to WinForms host via ViewModel event.
            // The host calls WinForms Control.DoDragDrop which is compatible
            // with Panel3D's OnDragDrop handler.
            vm.RequestDragDrop(selectedItem);

            _isDragging = false;
        }
    }

    /// <summary>
    /// Handles keyboard navigation in the asset grid.
    /// Arrow keys move selection, Home/End jump to first/last item,
    /// and Enter triggers selection confirmation.
    /// </summary>
    private void AssetListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (AssetListBox.Items.Count == 0)
            return;

        var items = AssetListBox.Items.Cast<object>().ToList();
        int currentIndex = AssetListBox.SelectedItem != null
            ? items.IndexOf(AssetListBox.SelectedItem)
            : -1;

        int newIndex = currentIndex;

        switch (e.Key)
        {
            case Key.Right:
                newIndex = Math.Min(currentIndex + 1, items.Count - 1);
                e.Handled = true;
                break;

            case Key.Left:
                newIndex = Math.Max(currentIndex - 1, 0);
                e.Handled = true;
                break;

            case Key.Down:
                {
                    // Move down by one row — estimate columns from panel width
                    int columns = EstimateColumnsInRow();
                    newIndex = Math.Min(currentIndex + columns, items.Count - 1);
                    e.Handled = true;
                }
                break;

            case Key.Up:
                {
                    int columns = EstimateColumnsInRow();
                    newIndex = Math.Max(currentIndex - columns, 0);
                    e.Handled = true;
                }
                break;

            case Key.Home:
                newIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                newIndex = items.Count - 1;
                e.Handled = true;
                break;

            case Key.PageDown:
                {
                    int columns = EstimateColumnsInRow();
                    int pageItems = columns * 4; // ~4 rows per page
                    newIndex = Math.Min(currentIndex + pageItems, items.Count - 1);
                    e.Handled = true;
                }
                break;

            case Key.PageUp:
                {
                    int columns = EstimateColumnsInRow();
                    int pageItems = columns * 4;
                    newIndex = Math.Max(currentIndex - pageItems, 0);
                    e.Handled = true;
                }
                break;

            default:
                return;
        }

        if (newIndex >= 0 && newIndex < items.Count && newIndex != currentIndex)
        {
            AssetListBox.SelectedItem = items[newIndex];
            AssetListBox.ScrollIntoView(AssetListBox.SelectedItem);
        }
    }

    /// <summary>
    /// Estimates how many tile columns fit in the current ListBox width.
    /// </summary>
    private int EstimateColumnsInRow()
    {
        double tileWidth = 92; // default tile width + margin
        if (DataContext is ContentBrowserViewModel vm)
            tileWidth = vm.TileWidth + 4; // 2px margin on each side

        double availableWidth = AssetListBox.ActualWidth - 20; // account for scrollbar
        int columns = Math.Max(1, (int)(availableWidth / tileWidth));
        return columns;
    }

    /// <summary>
    /// Returns true if the mouse event originated over a ScrollBar or its children
    /// (Thumb, RepeatButton, Track, etc.), so drag-drop should not be initiated.
    /// </summary>
    private static bool IsOverScrollbar(MouseEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is ScrollBar)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
        }
        return false;
    }
}
