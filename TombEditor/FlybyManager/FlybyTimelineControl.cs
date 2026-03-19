using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TombEditor.FlybyManager;

/// <summary>
/// A custom WPF timeline control that displays draggable camera keyframes.
/// Supports zoom via mouse wheel, full-zoom-out via double-click, cursor line,
/// and range selection by dragging on empty space.
/// </summary>
public class FlybyTimelineControl : Control
{
    private const double HeaderHeight = 20.0;
    private const double MarkerRadius = 5.0;
    private const double MinTickSpacing = 40.0;
    private const double TimeRulerHeight = 20.0;
    private const double TrackHeight = 24.0;

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private static readonly Brush RulerBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55));
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(65, 65, 65));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
    private static readonly Brush MarkerBrush = new SolidColorBrush(Color.FromRgb(90, 160, 230));
    private static readonly Brush MarkerSelectedBrush = new SolidColorBrush(Color.FromRgb(230, 180, 60));
    private static readonly Brush MarkerErrorBrush = new SolidColorBrush(Color.FromRgb(230, 80, 80));
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50));
    private static readonly Brush CursorLineBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly Brush PlayheadBrush = new SolidColorBrush(Color.FromRgb(230, 120, 50));
    private static readonly Brush SelectionBrush;
    private static readonly Pen GridLinePen = new(GridLineBrush, 1.0);
    private static readonly Pen MarkerOutlinePen = new(Brushes.Black, 1.0);
    private static readonly Pen CursorLinePen = new(CursorLineBrush, 1.0);
    private static readonly Pen PlayheadPen = new(PlayheadBrush, 2.0);

    private static readonly Typeface DefaultTypeface = new("Segoe UI");

    // Timeline data: list of (timecodeSeconds, isDuplicate, isSelected) per camera.
    private List<TimelineMarker> _markers = new();

    // Zoom and scroll state.
    private double _visibleStartSeconds;
    private double _visibleEndSeconds = 10.0;
    private double _totalDurationSeconds = 10.0;

    // Drag state for marker dragging.
    private int _dragIndex = -1;
    private bool _isDragging;
    private double _dragStartX;

    // Mouse cursor tracking.
    private double _mouseX = -1;
    private bool _isMouseOver;

    // Range selection state.
    private bool _isRangeSelecting;
    private double _rangeStartX;
    private double _rangeEndX;

    // Scrub state.
    private bool _isScrubbing;

    // Playhead position in seconds (negative = hidden).
    private float _playheadSeconds = -1.0f;

    public event Action<int, float>? MarkerDragged;
    public event Action<int>? MarkerClicked;
    public event Action<List<int>>? RangeSelected;
    public event Action<float>? ScrubRequested;

    static FlybyTimelineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FlybyTimelineControl),
            new FrameworkPropertyMetadata(typeof(FlybyTimelineControl)));

        var selBrush = new SolidColorBrush(Color.FromArgb(60, 90, 160, 230));
        selBrush.Freeze();
        SelectionBrush = selBrush;
    }

    public FlybyTimelineControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public struct TimelineMarker
    {
        public float TimeSeconds;
        public bool IsDuplicate;
        public bool IsSelected;
    }

    /// <summary>
    /// Sets the playhead position. Negative value hides the playhead.
    /// </summary>
    public void SetPlayheadSeconds(float seconds)
    {
        _playheadSeconds = seconds;
        InvalidateVisual();
    }

    /// <summary>
    /// Updates the marker data and redraws the timeline.
    /// </summary>
    public void SetMarkers(List<TimelineMarker> markers, float totalDuration)
    {
        _markers = markers ?? new List<TimelineMarker>();
        _totalDurationSeconds = Math.Max(1.0, totalDuration);

        if (_visibleStartSeconds >= _visibleEndSeconds)
            _visibleStartSeconds = 0;

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;

        if (w <= 0 || h <= 0)
            return;

        // Background.
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        // Time ruler area.
        dc.DrawRectangle(RulerBrush, null, new Rect(0, 0, w, TimeRulerHeight));

        // Draw time ruler marks and labels.
        DrawTimeRuler(dc, w);

        // Track area.
        double trackY = TimeRulerHeight;
        dc.DrawRectangle(TrackBrush, null, new Rect(0, trackY, w, TrackHeight));

        // Draw markers.
        DrawMarkers(dc, w, trackY);

        // Draw range selection highlight.
        if (_isRangeSelecting)
        {
            double selLeft = Math.Min(_rangeStartX, _rangeEndX);
            double selRight = Math.Max(_rangeStartX, _rangeEndX);
            dc.DrawRectangle(SelectionBrush, null, new Rect(selLeft, trackY, selRight - selLeft, TrackHeight));
        }

        // Draw cursor line at mouse position.
        if (_isMouseOver && _mouseX >= 0 && _mouseX <= w)
        {
            dc.DrawLine(CursorLinePen, new Point(_mouseX, 0), new Point(_mouseX, TimeRulerHeight + TrackHeight));
        }

        // Draw playhead line.
        if (_playheadSeconds >= 0)
        {
            double phX = TimeToPixel(_playheadSeconds, w);

            if (phX >= 0 && phX <= w)
                dc.DrawLine(PlayheadPen, new Point(phX, 0), new Point(phX, TimeRulerHeight + TrackHeight));
        }
    }

    private void DrawTimeRuler(DrawingContext dc, double width)
    {
        double visibleDuration = _visibleEndSeconds - _visibleStartSeconds;

        if (visibleDuration <= 0)
            return;

        // Determine tick interval.
        double pixelsPerSecond = width / visibleDuration;
        double tickInterval = CalculateTickInterval(pixelsPerSecond);

        double startTick = Math.Floor(_visibleStartSeconds / tickInterval) * tickInterval;

        for (double t = startTick; t <= _visibleEndSeconds; t += tickInterval)
        {
            double x = TimeToPixel(t, width);

            if (x < 0 || x > width)
                continue;

            // Draw grid line.
            dc.DrawLine(GridLinePen, new Point(x, TimeRulerHeight), new Point(x, TimeRulerHeight + TrackHeight));

            // Draw tick mark.
            dc.DrawLine(GridLinePen, new Point(x, 0), new Point(x, TimeRulerHeight));

            // Draw label.
            string label = FormatRulerLabel(t);
            var formattedText = new FormattedText(
                label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                DefaultTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formattedText, new Point(x + 2, 2));
        }
    }

    private void DrawMarkers(DrawingContext dc, double width, double trackY)
    {
        double centerY = trackY + TrackHeight / 2.0;

        for (int i = 0; i < _markers.Count; i++)
        {
            var marker = _markers[i];
            double x = TimeToPixel(marker.TimeSeconds, width);

            if (x < -MarkerRadius || x > width + MarkerRadius)
                continue;

            Brush fill;

            if (marker.IsDuplicate)
                fill = MarkerErrorBrush;
            else if (marker.IsSelected)
                fill = MarkerSelectedBrush;
            else
                fill = MarkerBrush;

            // Draw diamond shape.
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x, centerY - MarkerRadius), true, true);
                ctx.LineTo(new Point(x + MarkerRadius, centerY), true, false);
                ctx.LineTo(new Point(x, centerY + MarkerRadius), true, false);
                ctx.LineTo(new Point(x - MarkerRadius, centerY), true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(fill, MarkerOutlinePen, geometry);

            // Draw index label below.
            var indexText = new FormattedText(
                i.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                DefaultTypeface, 9, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(indexText, new Point(x - indexText.Width / 2, centerY + MarkerRadius + 2));
        }
    }

    #region Input handling

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var pos = e.GetPosition(this);

        // Scrub if clicking in the ruler area.
        if (pos.Y < TimeRulerHeight)
        {
            _isScrubbing = true;
            CaptureMouse();

            float scrubTime = (float)PixelToTime(pos.X, ActualWidth);
            ScrubRequested?.Invoke(Math.Max(0, scrubTime));
            return;
        }

        int hitIndex = HitTestMarker(pos);

        if (hitIndex >= 0)
        {
            _dragIndex = hitIndex;
            _isDragging = false;
            _dragStartX = pos.X;
            CaptureMouse();
            MarkerClicked?.Invoke(hitIndex);
        }
        else
        {
            // Start range selection on empty space.
            _isRangeSelecting = true;
            _rangeStartX = pos.X;
            _rangeEndX = pos.X;
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var pos = e.GetPosition(this);
        _mouseX = pos.X;
        _isMouseOver = true;

        if (_dragIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_isDragging && Math.Abs(pos.X - _dragStartX) > 3)
                _isDragging = true;

            if (_isDragging)
            {
                float newTime = (float)PixelToTime(pos.X, ActualWidth);
                newTime = Math.Max(0, newTime);
                MarkerDragged?.Invoke(_dragIndex, newTime);
            }
        }
        else if (_isScrubbing && e.LeftButton == MouseButtonState.Pressed)
        {
            float scrubTime = (float)PixelToTime(pos.X, ActualWidth);
            ScrubRequested?.Invoke(Math.Max(0, scrubTime));
        }
        else if (_isRangeSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            _rangeEndX = pos.X;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_isRangeSelecting)
        {
            _isRangeSelecting = false;
            CommitRangeSelection();
        }

        _isScrubbing = false;
        _dragIndex = -1;
        _isDragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isMouseOver = false;
        InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _isMouseOver = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var pos = e.GetPosition(this);
        double pivotTime = PixelToTime(pos.X, ActualWidth);
        double zoomFactor = e.Delta > 0 ? 0.8 : 1.25;

        double newStart = pivotTime - (pivotTime - _visibleStartSeconds) * zoomFactor;
        double newEnd = pivotTime + (_visibleEndSeconds - pivotTime) * zoomFactor;

        // Clamp to reasonable bounds.
        double maxEnd = _totalDurationSeconds * 1.5;

        if (newStart < 0)
            newStart = 0;

        if (newEnd > maxEnd)
            newEnd = maxEnd;

        if (newEnd - newStart < 0.1)
            return;

        _visibleStartSeconds = newStart;
        _visibleEndSeconds = newEnd;

        InvalidateVisual();
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        // Full zoom out on double-click.
        _visibleStartSeconds = 0;
        _visibleEndSeconds = _totalDurationSeconds * 1.1;

        InvalidateVisual();
    }

    #endregion Input handling

    #region Range selection

    private void CommitRangeSelection()
    {
        double leftX = Math.Min(_rangeStartX, _rangeEndX);
        double rightX = Math.Max(_rangeStartX, _rangeEndX);

        // Only commit if the drag distance is meaningful.
        if (rightX - leftX < 3)
            return;

        var selected = new List<int>();

        for (int i = 0; i < _markers.Count; i++)
        {
            double x = TimeToPixel(_markers[i].TimeSeconds, ActualWidth);

            if (x >= leftX && x <= rightX)
                selected.Add(i);
        }

        RangeSelected?.Invoke(selected);
    }

    #endregion Range selection

    #region Coordinate conversion

    private double TimeToPixel(double timeSeconds, double width)
    {
        double range = _visibleEndSeconds - _visibleStartSeconds;

        if (range <= 0)
            return 0;

        return (timeSeconds - _visibleStartSeconds) / range * width;
    }

    private double PixelToTime(double pixel, double width)
    {
        double range = _visibleEndSeconds - _visibleStartSeconds;

        if (width <= 0)
            return _visibleStartSeconds;

        return _visibleStartSeconds + pixel / width * range;
    }

    private int HitTestMarker(Point pos)
    {
        double trackY = TimeRulerHeight;
        double centerY = trackY + TrackHeight / 2.0;

        // Only hit-test within the track area.
        if (pos.Y < trackY - 4 || pos.Y > trackY + TrackHeight + 4)
            return -1;

        double closestDist = double.MaxValue;
        int closestIndex = -1;

        for (int i = 0; i < _markers.Count; i++)
        {
            double x = TimeToPixel(_markers[i].TimeSeconds, ActualWidth);
            double dist = Math.Abs(pos.X - x);

            if (dist < MarkerRadius + 4 && dist < closestDist)
            {
                closestDist = dist;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private static double CalculateTickInterval(double pixelsPerSecond)
    {
        double[] candidates = { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300 };

        foreach (double interval in candidates)
        {
            if (interval * pixelsPerSecond >= MinTickSpacing)
                return interval;
        }

        return 300;
    }

    private static string FormatRulerLabel(double seconds)
    {
        int totalCs = Math.Max(0, (int)(seconds * 100));
        int minutes = totalCs / 6000;
        int secs = (totalCs % 6000) / 100;
        int cs = totalCs % 100;

        if (minutes > 0)
            return $"{minutes}:{secs:D2}.{cs:D2}";

        return $"{secs}.{cs:D2}";
    }

    #endregion Coordinate conversion
}
