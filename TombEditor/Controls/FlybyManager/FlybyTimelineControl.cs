using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TombEditor.Controls.FlybyManager;

/// <summary>
/// A custom WPF timeline control that displays draggable camera keyframes.
/// Supports zoom via mouse wheel, full-zoom-out via double-click, cursor line,
/// and range selection by dragging on empty space.
/// </summary>
public class FlybyTimelineControl : Control
{
    private const double MarkerRadius = 6.375;
    private const double MinTickSpacing = 40.0;
    private const double TimeRulerHeight = 20.0;
    private const double TrackHeight = 19.0;
    private const double LabelHeight = 14.0;
    private const double TrackPadding = 3.0;

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private static readonly Brush RulerBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55));
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(65, 65, 65));
    private static readonly Brush RulerTextBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
    private static readonly Brush MarkerBrush = new SolidColorBrush(Color.FromRgb(104, 151, 187));
    private static readonly Brush MarkerSelectedBrush = new SolidColorBrush(Color.FromRgb(230, 180, 60));
    private static readonly Brush MarkerErrorBrush = new SolidColorBrush(Color.FromRgb(230, 80, 80));
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(49, 51, 53));
    private static readonly Brush FreezeBrush = new SolidColorBrush(Color.FromArgb(100, 70, 70, 70));
    private static readonly Brush SelectionBrush;
    private static readonly Brush PlayheadBrush;

    private static readonly Pen GridLinePen = new(GridLineBrush, 1.0);
    private static readonly Pen MarkerOutlinePen = new(new SolidColorBrush(Color.FromRgb(178, 178, 178)), 2.0);
    private static readonly Pen CursorLinePen = new(new SolidColorBrush(Color.FromArgb(100, 178, 178, 178)), 1.0);
    private static readonly Pen PlayheadPen;
    private static readonly Pen CameraCutPen;

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
    public event Action<int>? MarkerDoubleClicked;
    public event Action<List<int>>? RangeSelected;
    public event Action<float>? ScrubRequested;
    public event Action? PlayStopRequested;

    static FlybyTimelineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FlybyTimelineControl),
            new FrameworkPropertyMetadata(typeof(FlybyTimelineControl)));

        var selBrush = new SolidColorBrush(Color.FromArgb(60, 104, 151, 187));
        selBrush.Freeze();
        SelectionBrush = selBrush;

        var phBrush = new SolidColorBrush(Color.FromArgb(153, 178, 178, 178));
        phBrush.Freeze();
        PlayheadBrush = phBrush;
        PlayheadPen = new Pen(phBrush, 2.0);
        PlayheadPen.Freeze();

        // Diagonal hatch pen for camera cuts.
        CameraCutPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 230, 80, 80)), 1.0);
        CameraCutPen.Freeze();
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
        public bool HasCameraCut;
        public float CutBypassDuration;
        public bool IsFrozen;
        public float FreezeDuration;
        public float SegmentDuration;
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
        double totalTrackHeight = TrackHeight + LabelHeight;
        dc.DrawRectangle(TrackBrush, null, new Rect(0, trackY, w, totalTrackHeight));

        // Draw segment regions (freeze and camera cut indicators).
        DrawSegmentRegions(dc, w, trackY);

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
            dc.DrawLine(CursorLinePen, new Point(_mouseX, 0), new Point(_mouseX, trackY + totalTrackHeight));

        // Draw playhead line.
        if (_playheadSeconds >= 0)
        {
            double phX = TimeToPixel(_playheadSeconds, w);

            if (phX >= 0 && phX <= w)
                dc.DrawLine(PlayheadPen, new Point(phX, 0), new Point(phX, trackY + totalTrackHeight));
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
                DefaultTypeface, 9, RulerTextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formattedText, new Point(x + 2, 2));
        }
    }

    private void DrawSegmentRegions(DrawingContext dc, double width, double trackY)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int i = 0; i < _markers.Count - 1; i++)
        {
            var marker = _markers[i];
            double startX = TimeToPixel(marker.TimeSeconds, width);
            double endX = TimeToPixel(marker.TimeSeconds + marker.SegmentDuration, width);

            // Draw freeze region as solid dark gray immediately after the marker.
            if (marker.IsFrozen && marker.FreezeDuration > 0)
            {
                double freezeEndX = TimeToPixel(marker.TimeSeconds + marker.FreezeDuration, width);
                double fLeft = Math.Max(0, startX);
                double fRight = Math.Min(width, freezeEndX);

                if (fRight > fLeft)
                    dc.DrawRectangle(FreezeBrush, null, new Rect(fLeft, trackY, fRight - fLeft, TrackHeight));
            }

            // Draw camera cut region as diagonal hatch lines.
            if (marker.HasCameraCut)
            {
                float bypassEnd = marker.CutBypassDuration > 0
                    ? marker.TimeSeconds + marker.CutBypassDuration
                    : marker.TimeSeconds + marker.SegmentDuration;

                double cutLeft = Math.Max(0, startX);
                double cutRight = Math.Min(width, TimeToPixel(bypassEnd, width));

                if (cutRight > cutLeft)
                    DrawDiagonalHatch(dc, cutLeft, trackY, cutRight - cutLeft, TrackHeight);
            }
        }
    }

    private void DrawDiagonalHatch(DrawingContext dc, double x, double y, double w, double h)
    {
        double spacing = 6.0;
        var clip = new RectangleGeometry(new Rect(x, y, w, h));

        dc.PushClip(clip);

        for (double offset = -h; offset < w + h; offset += spacing)
        {
            dc.DrawLine(CameraCutPen,
                new Point(x + offset, y + h),
                new Point(x + offset + h, y));
        }

        dc.Pop();
    }

    private void DrawMarkers(DrawingContext dc, double width, double trackY)
    {
        double centerY = trackY + TrackHeight / 2.0;
        double labelY = trackY + TrackHeight + 1.0;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

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

            // Draw circle marker.
            dc.DrawEllipse(fill, MarkerOutlinePen, new Point(x, centerY), MarkerRadius, MarkerRadius);

            // Draw index label in lower label region.
            var indexText = new FormattedText(
                i.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                DefaultTypeface, 9, LabelBrush, dpi);

            dc.DrawText(indexText, new Point(x - indexText.Width / 2.0, labelY));
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

        var pos = e.GetPosition(this);
        int hitIndex = HitTestMarker(pos);

        // Double-click on a marker invokes edit.
        if (hitIndex >= 0)
        {
            MarkerDoubleClicked?.Invoke(hitIndex);
            return;
        }

        // Full zoom out on double-click on empty space.
        _visibleStartSeconds = 0;
        _visibleEndSeconds = _totalDurationSeconds * 1.1;

        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Space)
        {
            PlayStopRequested?.Invoke();
            e.Handled = true;
        }
    }

    #endregion Input handling

    #region Range selection

    private void CommitRangeSelection()
    {
        double leftX = Math.Min(_rangeStartX, _rangeEndX);
        double rightX = Math.Max(_rangeStartX, _rangeEndX);

        // Treat a tiny drag as a click on empty space (deselect).
        if (rightX - leftX < 3)
        {
            RangeSelected?.Invoke(new List<int>());
            return;
        }

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

        // Only hit-test within the track and label area.
        if (pos.Y < trackY - 4 || pos.Y > trackY + TrackHeight + LabelHeight)
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
