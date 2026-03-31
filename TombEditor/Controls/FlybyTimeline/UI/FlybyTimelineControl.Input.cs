#nullable enable

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace TombEditor.Controls.FlybyTimeline.UI;

// Mouse and keyboard input handling for the timeline control.
public partial class FlybyTimelineControl
{
    /// <summary>
    /// Starts scrubbing, dragging, repositioning, or range selection from a left click.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        if (e.ClickCount >= 2)
            return;

        var pos = e.GetPosition(this);

        if (pos.Y < FlybyConstants.TimelineRulerHeight)
        {
            BeginScrub((float)pos.X);
            return;
        }

        int hitIndex = HitTestMarker(pos);

        if (hitIndex >= 0)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                BeginReposition(hitIndex, (float)pos.X);
            else
                BeginMarkerDrag(hitIndex, pos);

            return;
        }

        BeginRangeSelection((float)pos.X);
    }

    /// <summary>
    /// Updates the active interaction while the mouse moves over the control.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var pos = e.GetPosition(this);
        float mouseX = (float)pos.X;

        UpdateMouseTracking(mouseX);

        if (_interactionMode == InteractionMode.Panning)
            UpdatePan(mouseX);
        else if (_interactionMode == InteractionMode.Repositioning && e.LeftButton == MouseButtonState.Pressed)
            UpdateReposition(mouseX);
        else if (_interactionMode == InteractionMode.MarkerDrag && _dragIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
            UpdateMarkerDrag(pos);
        else if (_interactionMode == InteractionMode.Scrubbing && e.LeftButton == MouseButtonState.Pressed)
            UpdateScrub(mouseX);
        else if (_interactionMode == InteractionMode.RangeSelecting && e.LeftButton == MouseButtonState.Pressed)
            UpdateRangeSelection(mouseX);

        InvalidateVisual();
    }

    /// <summary>
    /// Completes the current left-button interaction.
    /// </summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndLeftMouseInteraction();
    }

    /// <summary>
    /// Hides hover visuals when the pointer leaves the control.
    /// </summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isMouseOver = false;
        _mouseX = -1.0f;

        InvalidateVisual();
    }

    /// <summary>
    /// Enables hover visuals when the pointer enters the control.
    /// </summary>
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateMouseTracking((float)e.GetPosition(this).X);
        InvalidateVisual();
    }

    /// <summary>
    /// Zooms or pans the timeline viewport in response to the mouse wheel.
    /// </summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (e.Delta > 0)
                PanLeft(FlybyConstants.TimelineSmoothPanEnabled);
            else if (e.Delta < 0)
                PanRight(FlybyConstants.TimelineSmoothPanEnabled);

            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);
        GetInteractiveViewport(out float baseStart, out float baseEnd);
        float pivotTime = PixelToTime((float)pos.X, (float)ActualWidth, baseStart, baseEnd);
        float zoomFactor = e.Delta > 0 ? 0.8f : 1.25f;

        float newStart = pivotTime - ((pivotTime - baseStart) * zoomFactor);
        float newEnd = pivotTime + ((baseEnd - pivotTime) * zoomFactor);

        ClampViewportToBounds(ref newStart, ref newEnd);

        if (newEnd - newStart < FlybyConstants.TimelineMinViewportRange)
            return;

        ApplyViewport(newStart, newEnd, FlybyConstants.TimelineSmoothZoomEnabled);
    }

    /// <summary>
    /// Starts middle-button panning.
    /// </summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.ChangedButton == MouseButton.Middle)
            BeginPan(e.GetPosition(this));
    }

    /// <summary>
    /// Starts right-button panning and tracks whether it becomes a drag.
    /// </summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        _rightButtonPanned = false;

        BeginPan(e.GetPosition(this));
    }

    /// <summary>
    /// Ends middle-button panning when active.
    /// </summary>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton == MouseButton.Middle && _interactionMode == InteractionMode.Panning)
            EndPan();
    }

    /// <summary>
    /// Ends right-button panning and suppresses the context menu after a drag.
    /// </summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        if (_interactionMode != InteractionMode.Panning)
            return;

        EndPan();

        if (_rightButtonPanned)
            e.Handled = true;
    }

    /// <summary>
    /// Opens marker editing or zooms the viewport out on double-click.
    /// </summary>
    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        var pos = e.GetPosition(this);
        int hitIndex = HitTestMarker(pos);

        if (hitIndex >= 0)
        {
            MarkerDoubleClicked?.Invoke(hitIndex);
            return;
        }

        ZoomToFit();
    }

    /// <summary>
    /// Handles keyboard shortcuts for playback, deletion, and panning.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Space:
                PlayStopRequested?.Invoke();
                break;

            case Key.Delete or Key.Back:
                DeleteRequested?.Invoke();
                break;

            case Key.Left:
                PanLeft();
                break;

            case Key.Right:
                PanRight();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Expands the viewport to show the full sequence range.
    /// </summary>
    public void ZoomToFit()
        => ApplyViewport(0.0f, _totalDurationSeconds * FlybyConstants.TimelineZoomOutScale, FlybyConstants.TimelineSmoothZoomEnabled);

    /// <summary>
    /// Pans the viewport left by one configured step.
    /// </summary>
    public void PanLeft(bool smooth = false)
    {
        GetInteractiveViewport(out float startSeconds, out float endSeconds);
        PanBy(-(endSeconds - startSeconds) * FlybyConstants.TimelinePanStepFraction, smooth);
    }

    /// <summary>
    /// Pans the viewport right by one configured step.
    /// </summary>
    public void PanRight(bool smooth = false)
    {
        GetInteractiveViewport(out float startSeconds, out float endSeconds);
        PanBy((endSeconds - startSeconds) * FlybyConstants.TimelinePanStepFraction, smooth);
    }

    /// <summary>
    /// Updates mouse position state used for cursor-line rendering.
    /// </summary>
    private void UpdateMouseTracking(float mouseX)
    {
        _mouseX = mouseX;
        _isMouseOver = true;
    }

    /// <summary>
    /// Begins timeline scrubbing from the ruler.
    /// </summary>
    private void BeginScrub(float mouseX)
    {
        _interactionMode = InteractionMode.Scrubbing;

        CaptureMouse();
        UpdateScrub(mouseX);
    }

    /// <summary>
    /// Begins marker reposition mode using Alt-drag.
    /// </summary>
    private void BeginReposition(int hitIndex, float mouseX)
    {
        MarkerClicked?.Invoke(hitIndex);

        _interactionMode = InteractionMode.Repositioning;
        _repositionGhostX = mouseX;
        _repositionFromIndex = hitIndex;
        _repositionTargetIndex = hitIndex;
        CaptureMouse();
    }

    /// <summary>
    /// Begins dragging a marker to adjust its timeline position.
    /// </summary>
    private void BeginMarkerDrag(int hitIndex, Point mousePosition)
    {
        _interactionMode = InteractionMode.MarkerDrag;
        _dragIndex = hitIndex;
        _isDragging = false;
        _dragStartPoint = mousePosition;
        _dragMouseOffsetSeconds = PixelToTime((float)mousePosition.X, (float)ActualWidth) - _markers[hitIndex].TimeSeconds;
        CaptureMouse();

        MarkerClicked?.Invoke(hitIndex);
    }

    /// <summary>
    /// Begins marquee selection on empty track space.
    /// </summary>
    private void BeginRangeSelection(float mouseX)
    {
        _interactionMode = InteractionMode.RangeSelecting;
        _rangeStartX = mouseX;
        _rangeEndX = mouseX;
        CaptureMouse();
        InvalidateVisual();
    }

    /// <summary>
    /// Starts viewport panning from the given mouse position.
    /// </summary>
    private void BeginPan(Point startPosition)
    {
        if (HasActiveLeftMouseInteraction())
            return;

        SnapViewportToInteractiveState();
        float start = _visibleStartSeconds;
        float end = _visibleEndSeconds;

        _interactionMode = InteractionMode.Panning;
        _panStartPixelX = (float)startPosition.X;
        _panStartViewSeconds = start;
        _panStartViewRange = end - start;

        Cursor = Cursors.SizeWE;
        CaptureMouse();
        Focus();
    }

    /// <summary>
    /// Updates the viewport while a pan drag is in progress.
    /// </summary>
    private void UpdatePan(float currentPixelX)
    {
        float w = (float)ActualWidth;

        if (w <= 0 || _panStartViewRange <= 0)
            return;

        float deltaPixels = currentPixelX - _panStartPixelX;
        float deltaSeconds = -(deltaPixels / w) * _panStartViewRange;
        float newStart = ClampVisibleStart(_panStartViewSeconds + deltaSeconds, _panStartViewRange);

        SetViewport(newStart, newStart + _panStartViewRange, false);

        if (MathF.Abs(deltaPixels) >= SystemParameters.MinimumHorizontalDragDistance)
            _rightButtonPanned = true;
    }

    /// <summary>
    /// Updates the reorder target while a marker is being repositioned.
    /// </summary>
    private void UpdateReposition(float mouseX)
    {
        _repositionGhostX = mouseX;
        _repositionTargetIndex = ComputeReorderTargetIndex(mouseX, (float)ActualWidth);
    }

    /// <summary>
    /// Updates a dragged marker and emits its requested target time.
    /// </summary>
    private void UpdateMarkerDrag(Point mousePosition)
    {
        if (!_isDragging && HasExceededDragThreshold(mousePosition))
            _isDragging = true;

        if (!_isDragging)
            return;

        float newTime = PixelToTime((float)mousePosition.X, (float)ActualWidth) - _dragMouseOffsetSeconds;
        MarkerDragged?.Invoke(_dragIndex, Math.Max(0.0f, newTime));
    }

    /// <summary>
    /// Updates scrub playback time from the current mouse position.
    /// </summary>
    private void UpdateScrub(float mouseX)
    {
        float scrubTime = PixelToTime(mouseX, (float)ActualWidth);
        ScrubRequested?.Invoke(Math.Max(0.0f, scrubTime));
    }

    /// <summary>
    /// Updates marquee selection visuals during drag.
    /// </summary>
    private void UpdateRangeSelection(float mouseX) => _rangeEndX = mouseX;

    /// <summary>
    /// Ends the current viewport pan operation.
    /// </summary>
    private void EndPan()
    {
        _interactionMode = InteractionMode.None;
        Cursor = null;
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Ends any active left-button interaction and clears temporary state.
    /// </summary>
    private void EndLeftMouseInteraction()
    {
        if (_interactionMode == InteractionMode.Repositioning)
        {
            _interactionMode = InteractionMode.None;
            CommitRepositioning();
        }
        else if (_interactionMode == InteractionMode.RangeSelecting)
        {
            _interactionMode = InteractionMode.None;
            CommitRangeSelection();
        }

        if (_isDragging && _dragIndex >= 0)
            MarkerDragCompleted?.Invoke(_dragIndex);

        if (_interactionMode != InteractionMode.Panning)
            _interactionMode = InteractionMode.None;

        _dragIndex = -1;
        _isDragging = false;
        _dragMouseOffsetSeconds = 0;

        if (IsMouseCaptured && _interactionMode != InteractionMode.Panning)
            ReleaseMouseCapture();

        InvalidateVisual();
    }

    /// <summary>
    /// Finalizes marquee selection and handles click-to-clear behavior.
    /// </summary>
    private void CommitRangeSelection()
    {
        if (!HasExceededSelectionThreshold(_rangeStartX, _rangeEndX))
        {
            RangeSelected?.Invoke([]);
            return;
        }

        RangeSelected?.Invoke(GetRangeSelection());
    }

    /// <summary>
    /// Commits a marker reorder operation when the target index changed.
    /// </summary>
    private void CommitRepositioning()
    {
        if (_repositionFromIndex < 0 || _repositionTargetIndex < 0 || _repositionFromIndex == _repositionTargetIndex)
        {
            _repositionFromIndex = -1;
            _repositionTargetIndex = -1;
            return;
        }

        MarkerReordered?.Invoke(_repositionFromIndex, _repositionTargetIndex);

        _repositionFromIndex = -1;
        _repositionTargetIndex = -1;
    }

    /// <summary>
    /// Returns marker indices inside the current marquee selection.
    /// </summary>
    private IReadOnlyList<int> GetRangeSelection()
    {
        float leftX = Math.Min(_rangeStartX, _rangeEndX);
        float rightX = Math.Max(_rangeStartX, _rangeEndX);

        List<int> selected = [];

        for (int i = 0; i < _markers.Count; i++)
        {
            if (!TryGetMarkerPixel(_markers[i], (float)ActualWidth, out float x))
                continue;

            if (x >= leftX && x <= rightX)
                selected.Add(i);
        }

        return selected;
    }

    /// <summary>
    /// Returns the closest marker under the given mouse position.
    /// </summary>
    private int HitTestMarker(Point pos)
    {
        const float trackY = FlybyConstants.TimelineRulerHeight;

        if (pos.Y < trackY - 4 || pos.Y > ActualHeight)
            return -1;

        float closestDist = float.MaxValue;
        int closestIndex = -1;

        for (int i = 0; i < _markers.Count; i++)
        {
            if (!TryGetMarkerPixel(_markers[i], (float)ActualWidth, out float x))
                continue;

            float dist = Math.Abs((float)pos.X - x);

            if (dist < FlybyConstants.TimelineMarkerRadius + 4 && dist < closestDist)
            {
                closestDist = dist;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    /// <summary>
    /// Calculates the marker index that a reposition drag should target.
    /// </summary>
    private int ComputeReorderTargetIndex(float mouseX, float width)
    {
        bool hasValidMarker = false;
        int lastBefore = -1;

        for (int i = 0; i < _markers.Count; i++)
        {
            if (!TryGetMarkerPixel(_markers[i], width, out float markerX))
                continue;

            hasValidMarker = true;

            if (markerX <= mouseX)
                lastBefore = i;
            else
                break;
        }

        if (!hasValidMarker)
            return _repositionFromIndex;

        if (_repositionFromIndex <= lastBefore)
            return lastBefore;

        return Math.Min(lastBefore + 1, _markers.Count - 1);
    }

    /// <summary>
    /// Returns whether marker dragging has exceeded the system drag threshold.
    /// </summary>
    private bool HasExceededDragThreshold(Point currentPoint)
        => Math.Abs(currentPoint.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(currentPoint.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>
    /// Returns whether a marquee selection drag is large enough to count.
    /// </summary>
    private static bool HasExceededSelectionThreshold(float startX, float endX)
        => Math.Abs(endX - startX) >= FlybyConstants.TimelineSelectionThresholdPixels;

    /// <summary>
    /// Returns whether a left-button-driven interaction is currently active or pending.
    /// </summary>
    private bool HasActiveLeftMouseInteraction()
        => _interactionMode is InteractionMode.MarkerDrag or InteractionMode.Scrubbing
            or InteractionMode.RangeSelecting or InteractionMode.Repositioning;
}
