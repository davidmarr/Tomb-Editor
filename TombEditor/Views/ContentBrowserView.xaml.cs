using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TombEditor.ViewModels;

namespace TombEditor.Views;

public partial class ContentBrowserView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private UIElement _animatedElement;

    public ContentBrowserView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles double-click on an asset to trigger the Add Item action.
    /// Plays a subtle zoom+fade animation on the tile to confirm the action.
    /// </summary>
    private void AssetListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        // Only trigger if double-click is on an actual item (not empty space)
        if (AssetListBox.SelectedItem is AssetItemViewModel &&
            DataContext is ContentBrowserViewModel vm)
        {
            // Find the tile visual under the cursor and animate it
            if (e.OriginalSource is DependencyObject source)
            {
                var container = FindAncestor<ListBoxItem>(source);
                if (container != null)
                    PlayAddItemAnimation(container);
            }

            vm.AddItemCommand.Execute(null);
        }
    }

    /// <summary>
    /// Plays a brief scale-up + fade-out animation on the given element
    /// to provide visual feedback that an item is being placed.
    /// The element stays grayed out until <see cref="RestoreLastAnimation"/> is called.
    /// </summary>
    private void PlayAddItemAnimation(UIElement element)
    {
        // Restore any previous animation before starting a new one
        RestoreLastAnimation();

        _animatedElement = element;

        var duration = new Duration(TimeSpan.FromSeconds(0.25));

        // Ensure a render transform exists
        if (element.RenderTransform is not ScaleTransform)
        {
            element.RenderTransform = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var scaleX = new DoubleAnimation(1.0, 1.08, duration) { EasingFunction = new QuadraticEase() };
        var scaleY = new DoubleAnimation(1.0, 1.08, duration) { EasingFunction = new QuadraticEase() };
        var fade = new DoubleAnimation(1.0, 0.4, duration) { EasingFunction = new QuadraticEase() };

        element.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        element.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// Restores the last animated tile to its normal appearance by clearing
    /// all running/held animations and resetting opacity and transform.
    /// Called when EditorActionPlace finishes or is canceled.
    /// </summary>
    public void RestoreLastAnimation()
    {
        if (_animatedElement == null)
            return;

        // Remove held animations so local values take effect again
        _animatedElement.BeginAnimation(UIElement.OpacityProperty, null);

        if (_animatedElement.RenderTransform is ScaleTransform)
        {
            _animatedElement.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _animatedElement.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        _animatedElement.Opacity = 1.0;
        _animatedElement.RenderTransform = new ScaleTransform(1, 1);
        _animatedElement = null;
    }

    /// <summary>
    /// Walks the visual tree upward to find an ancestor of the given type.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T target)
                return target;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
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
    /// When Ctrl or Alt is held, intercepts mouse wheel to zoom (change tile size)
    /// instead of scrolling.
    /// </summary>
    private void AssetListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            if (DataContext is ContentBrowserViewModel vm)
            {
                double step = 8;
                double newWidth = vm.TileWidth + (e.Delta > 0 ? step : -step);
                vm.TileWidth = Math.Clamp(newWidth, vm.MinTileWidth, vm.MaxTileWidth);
            }
            e.Handled = true;
        }
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
