using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TombEditor.Controls.FlybyTimeline;

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

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private static readonly Brush RulerBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55));
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(65, 65, 65));
    private static readonly Brush RulerTextBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
    private static readonly Brush MarkerBrush = new SolidColorBrush(Color.FromRgb(104, 151, 187));
    private static readonly Brush MarkerSelectedBrush = new SolidColorBrush(Color.FromRgb(230, 180, 60));
    private static readonly Brush MarkerErrorBrush = new SolidColorBrush(Color.FromRgb(230, 80, 80));
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(49, 51, 53));
    private static readonly Brush SelectionBrush;
    private static readonly Brush PlayheadBrush;

    private static readonly Pen GridLinePen = new(GridLineBrush, 1.0);
    private static readonly Pen MarkerOutlinePen = new(new SolidColorBrush(Color.FromRgb(178, 178, 178)), 2.0);
    private static readonly Pen CursorLinePen = new(new SolidColorBrush(Color.FromArgb(100, 178, 178, 178)), 1.0);
    private static readonly Brush SpeedCurveFillBrush;
    private static readonly Pen PlayheadPen;
    private static readonly Pen CameraCutPen;
    private static readonly Brush FreezeBrush;

    private static readonly Typeface DefaultTypeface = new("Segoe UI");

    // Timeline data: list of (timecodeSeconds, isDuplicate, isSelected) per camera.
    private List<TimelineMarker> _markers = new();

    // Cache reference for distance-based speed sampling.
    private FlybySequenceCache? _cache;

    // Peak speed for normalization of the speed curve display.
    private double _peakSpeed;

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
    public event Action? DeleteRequested;

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

        var scBrush = new SolidColorBrush(Color.FromArgb(64, 104, 151, 187));
        scBrush.Freeze();
        SpeedCurveFillBrush = scBrush;

        // Diagonal hatch pen for camera cuts.
        CameraCutPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 230, 80, 80)), 1.0);
        CameraCutPen.Freeze();

        var freezeBrush = new SolidColorBrush(Color.FromArgb(64, 160, 160, 160));
        freezeBrush.Freeze();
        FreezeBrush = freezeBrush;
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
        public bool IsInCutBypass;
        public float CutBypassDuration;
        public float SegmentDuration;
        public float FreezeDurationSeconds;

        // For TombEngine targets, the time where ease-out deceleration starts.
        // When set (> 0 and < TimeSeconds), the freeze region extends from here.
        public float EaseOutStartSeconds;
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
    public void SetMarkers(List<TimelineMarker> markers, float totalDuration,
        FlybySequenceCache? cache = null)
    {
        _markers = markers ?? new List<TimelineMarker>();
        _totalDurationSeconds = Math.Max(1.0, totalDuration);
        _cache = cache;
        _peakSpeed = ComputePeakSpeed();

        if (_visibleStartSeconds >= _visibleEndSeconds)
            _visibleStartSeconds = 0;

        InvalidateVisual();
    }

    /// <summary>
    /// Scans the cache to find the maximum speed value for normalizing the speed curve display.
    /// </summary>
    private double ComputePeakSpeed()
    {
        if (_cache == null || !_cache.IsValid)
            return 0;

        double peak = 0;
        float duration = _cache.TotalDuration;
        float step = duration / 200.0f;

        if (step < FlybySequenceCache.TimeStep)
            step = FlybySequenceCache.TimeStep;

        for (float t = 0; t <= duration; t += step)
        {
            float speed = _cache.GetSpeedAtTime(t);

            if (speed > peak)
                peak = speed;
        }

        return peak;
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

        // Track area uses all remaining height below the ruler.
        double trackY = TimeRulerHeight;
        double trackHeight = Math.Max(1.0, h - TimeRulerHeight);
        dc.DrawRectangle(TrackBrush, null, new Rect(0, trackY, w, trackHeight));

        // Draw segment regions (freeze and camera cut indicators).
        DrawSegmentRegions(dc, w, trackY, trackHeight);

        // Draw relative speed curve behind markers.
        DrawSpeedCurve(dc, w, trackY, trackHeight);

        // Draw markers.
        DrawMarkers(dc, w, trackY, trackHeight);

        // Draw range selection highlight.
        if (_isRangeSelecting)
        {
            double selLeft = Math.Min(_rangeStartX, _rangeEndX);
            double selRight = Math.Max(_rangeStartX, _rangeEndX);
            dc.DrawRectangle(SelectionBrush, null, new Rect(selLeft, trackY, selRight - selLeft, trackHeight));
        }

        // Draw cursor line at mouse position.
        if (_isMouseOver && _mouseX >= 0 && _mouseX <= w)
            dc.DrawLine(CursorLinePen, new Point(_mouseX, 0), new Point(_mouseX, h));

        // Draw playhead line.
        if (_playheadSeconds >= 0)
        {
            double phX = TimeToPixel(_playheadSeconds, w);

            if (phX >= 0 && phX <= w)
                dc.DrawLine(PlayheadPen, new Point(phX, 0), new Point(phX, h));
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
            dc.DrawLine(GridLinePen, new Point(x, TimeRulerHeight), new Point(x, ActualHeight));

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

    private void DrawSegmentRegions(DrawingContext dc, double width, double trackY, double trackHeight)
    {
        for (int i = 0; i < _markers.Count - 1; i++)
        {
            var marker = _markers[i];
            double startX = TimeToPixel(marker.TimeSeconds, width);

            // Draw camera cut region as diagonal hatch lines.
            if (marker.HasCameraCut)
            {
                float bypassEnd = marker.CutBypassDuration > 0
                    ? marker.TimeSeconds + marker.CutBypassDuration
                    : marker.TimeSeconds + marker.SegmentDuration;

                double cutLeft = Math.Max(0, startX);
                double cutRight = Math.Min(width, TimeToPixel(bypassEnd, width));

                if (cutRight > cutLeft)
                    DrawDiagonalHatch(dc, cutLeft, trackY, cutRight - cutLeft, trackHeight, CameraCutPen);
            }
        }

        // Draw freeze regions detected from cache frame data.
        if (_cache != null)
        {
            foreach (var region in _cache.FreezeRegions)
            {
                double left = Math.Max(0, TimeToPixel(region.StartSeconds, width));
                double right = Math.Min(width, TimeToPixel(region.EndSeconds, width));

                if (right > left)
                    dc.DrawRectangle(FreezeBrush, null, new Rect(left, trackY, right - left, trackHeight));
            }
        }
    }

    private void DrawDiagonalHatch(DrawingContext dc, double x, double y, double w, double h, Pen pen)
    {
        double spacing = 6.0;
        var clip = new RectangleGeometry(new Rect(x, y, w, h));

        dc.PushClip(clip);

        for (double offset = -h; offset < w + h; offset += spacing)
        {
            dc.DrawLine(pen,
                new Point(x + offset, y + h),
                new Point(x + offset + h, y));
        }

        dc.Pop();
    }

    // Returns the normalized speed (0-1) at the given timeline time by sampling
    // the pre-calculated cache for actual inter-frame distances.
    // Returns negative when the time is outside the sequence or within a cut region.
    private double GetSpeedAtTime(double timeSeconds)
    {
        if (_cache == null || !_cache.IsValid || _markers.Count < 2)
            return -1;

        float speed = _cache.GetSpeedAtTime((float)timeSeconds);

        if (speed < 0)
            return -1;

        // Normalize against the peak speed for this sequence.
        return _peakSpeed > 0.001 ? speed / _peakSpeed : 0;
    }

    // Draws a filled speed waveform centred at the track's vertical midpoint.
    // The fill is semi-transparent and mirrors above and below the centre line.
    private void DrawSpeedCurve(DrawingContext dc, double width, double trackY, double trackHeight)
    {
        if (_markers.Count < 2)
            return;

        double maxHalfAmplitude = trackHeight / 2.0 - 5.0;
        double centerY = trackY + trackHeight / 2.0;
        int sampleCount = Math.Max(2, (int)(width / 2.0));

        // Collect visible sample spans (skipping gaps outside the sequence).
        var spans = new List<(int Start, int End)>();
        int spanStart = -1;

        for (int i = 0; i <= sampleCount; i++)
        {
            double x = width * i / sampleCount;
            double speed = GetSpeedAtTime(PixelToTime(x, width));

            if (speed >= 0)
            {
                if (spanStart < 0)
                    spanStart = i;
            }
            else if (spanStart >= 0)
            {
                spans.Add((spanStart, i - 1));
                spanStart = -1;
            }
        }

        if (spanStart >= 0)
            spans.Add((spanStart, sampleCount));

        if (spans.Count == 0)
            return;

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            foreach (var (start, end) in spans)
                DrawFilledWaveformSpan(ctx, width, centerY, sampleCount, start, end, maxHalfAmplitude);
        }

        geometry.Freeze();
        dc.DrawGeometry(SpeedCurveFillBrush, null, geometry);
    }

    private void DrawFilledWaveformSpan(StreamGeometryContext ctx, double width, double centerY,
        int sampleCount, int start, int end, double maxHalf)
    {
        int count = end - start + 1;
        var upper = new Point[count];
        var lower = new Point[count];

        for (int i = 0; i < count; i++)
        {
            int step = start + i;
            double x = width * step / sampleCount;
            double speed = Math.Max(0, GetSpeedAtTime(PixelToTime(x, width)));
            double half = Math.Max(1.0, speed * maxHalf);
            upper[i] = new Point(x, centerY - half);
            lower[i] = new Point(x, centerY + half);
        }

        // Begin at the first upper point, trace upper edge left-to-right,
        // then lower edge right-to-left, forming a closed filled shape.
        ctx.BeginFigure(upper[0], true, true);

        for (int i = 1; i < count; i++)
            ctx.LineTo(upper[i], true, false);

        for (int i = count - 1; i >= 0; i--)
            ctx.LineTo(lower[i], true, false);
    }

    private void DrawMarkers(DrawingContext dc, double width, double trackY, double trackHeight)
    {
        double centerY = trackY + trackHeight / 2.0;

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

        // Update tooltip to show camera number when hovering over a marker.
        int hoverIndex = HitTestMarker(pos);

        if (hoverIndex >= 0)
            ToolTip = $"Camera {hoverIndex}";
        else
            ToolTip = null;

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
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteRequested?.Invoke();
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

        // Only hit-test within the track area.
        if (pos.Y < trackY - 4 || pos.Y > ActualHeight)
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
