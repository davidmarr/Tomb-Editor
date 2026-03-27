#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TombLib.WPF;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// A custom WPF timeline control that displays draggable camera keyframes.
/// Supports zoom via mouse wheel, panning, full-zoom-out via double-click, cursor line,
/// and range selection by dragging on empty space.
/// </summary>
public class FlybyTimelineControl : Control
{
    private static readonly Brush BackgroundBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(43, 43, 43));
    private static readonly Brush RulerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(55, 55, 55));
    private static readonly Brush GridLineBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(65, 65, 65));
    private static readonly Brush RulerTextBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(180, 180, 180));
    private static readonly Brush MarkerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(104, 151, 187));
    private static readonly Brush MarkerSelectedBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(230, 180, 60));
    private static readonly Brush MarkerErrorBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(230, 80, 80));
    private static readonly Brush TrackBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(49, 51, 53));
    private static readonly Brush SelectionBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(60, 104, 151, 187));
    private static readonly Brush PlayheadBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(153, 178, 178, 178));
    private static readonly Brush FreezeRegionBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(96, 120, 120, 120));

    private static readonly Pen MarkerOutlinePen = BrushHelpers.CreateFrozenPen(Color.FromRgb(178, 178, 178), 2.0f);
    private static readonly Pen CursorLinePen = BrushHelpers.CreateFrozenPen(Color.FromArgb(100, 178, 178, 178), 1.0f);
    private static readonly Brush SpeedCurveFillBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(64, 104, 151, 187));
    private static readonly Pen CameraCutPen = BrushHelpers.CreateFrozenPen(Color.FromArgb(80, 160, 160, 160), 1.0f);
    private static readonly Brush GhostMarkerBrush = BrushHelpers.CreateFrozenBrush(Color.FromArgb(160, 100, 100, 100));
    private static readonly Pen GhostMarkerPen = BrushHelpers.CreateFrozenPen(Color.FromArgb(140, 200, 200, 200), 2.0f);
    private static readonly Pen GridLinePen = BrushHelpers.CreateFrozenPen(GridLineBrush, 1.0f);
    private static readonly Pen PlayheadPen = BrushHelpers.CreateFrozenPen(PlayheadBrush, 2.0f);
    private static readonly StreamGeometry CameraCutMarkerGeometry = CreateTriangleMarkerGeometry();

    private static readonly Typeface DefaultTypeface = new("Segoe UI");

    // Timeline data: list of (timecodeSeconds, isDuplicate, isSelected) per camera.
    private List<TimelineMarker> _markers = new();

    // Cache reference for distance-based speed sampling.
    private FlybySequenceCache? _cache;

    // Peak speed for normalization of the speed curve display.
    private float _peakSpeed;

    // Zoom and scroll state.
    private float _visibleStartSeconds;
    private float _visibleEndSeconds = 10.0f;
    private float _totalDurationSeconds = 10.0f;
    private readonly DispatcherTimer _smoothViewportTimer;
    private float _smoothViewportTargetStartSeconds;
    private float _smoothViewportTargetEndSeconds;

    // Drag state for marker dragging.
    private int _dragIndex = -1;
    private bool _isDragging;
    private Point _dragStartPoint;
    private float _dragMouseOffsetSeconds;

    // Mouse cursor tracking.
    private float _mouseX = -1;
    private bool _isMouseOver;

    // Range selection state.
    private bool _isRangeSelecting;
    private float _rangeStartX;
    private float _rangeEndX;

    // Scrub state.
    private bool _isScrubbing;

    // Playhead position in seconds (negative = hidden).
    private float _playheadSeconds = -1.0f;

    // Reposition state (Alt+LMB drag for renumbering).
    private bool _isRepositioning;
    private float _repositionGhostX;
    private int _repositionTargetIndex = -1;
    private int _repositionFromIndex = -1;

    public event Action<int, float>? MarkerDragged;
    public event Action<int>? MarkerClicked;
    public event Action<int>? MarkerDoubleClicked;
    public event Action<int>? MarkerDragCompleted;
    public event Action<List<int>>? RangeSelected;
    public event Action<float>? ScrubRequested;
    public event Action? PlayStopRequested;
    public event Action? DeleteRequested;
    public event Action<int, int>? MarkerReordered;

    static FlybyTimelineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FlybyTimelineControl),
            new FrameworkPropertyMetadata(typeof(FlybyTimelineControl)));
    }

    public FlybyTimelineControl()
    {
        ClipToBounds = true;
        Focusable = true;
        Unloaded += OnUnloaded;

        _smoothViewportTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FlybyConstants.TimelineSmoothViewportTimerInterval)
        };

        _smoothViewportTimer.Tick += OnSmoothViewportTick;
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
        public bool HasFreeze;
        public float FreezeDuration;
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
    public void SetMarkers(List<TimelineMarker> markers, float totalDuration, FlybySequenceCache? cache = null)
    {
        _markers = markers ?? new List<TimelineMarker>();
        _totalDurationSeconds = Math.Max(1.0f, totalDuration);
        _cache = cache;
        _peakSpeed = ComputePeakSpeed();

        StopSmoothViewport(false);
        NormalizeVisibleViewport();

        InvalidateVisual();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StopSmoothViewport(false);
    }

    private void OnSmoothViewportTick(object? sender, EventArgs e)
    {
        float startDelta = _smoothViewportTargetStartSeconds - _visibleStartSeconds;
        float endDelta = _smoothViewportTargetEndSeconds - _visibleEndSeconds;

        if (Math.Abs(startDelta) <= FlybyConstants.TimelineSmoothViewportEpsilon &&
            Math.Abs(endDelta) <= FlybyConstants.TimelineSmoothViewportEpsilon)
        {
            _visibleStartSeconds = _smoothViewportTargetStartSeconds;
            _visibleEndSeconds = _smoothViewportTargetEndSeconds;
            StopSmoothViewport(false);
            InvalidateVisual();
            return;
        }

        _visibleStartSeconds += startDelta * FlybyConstants.TimelineSmoothViewportLerpFactor;
        _visibleEndSeconds += endDelta * FlybyConstants.TimelineSmoothViewportLerpFactor;

        InvalidateVisual();
    }

    /// <summary>
    /// Scans the cache to find the maximum speed value for normalizing the speed curve display.
    /// </summary>
    private float ComputePeakSpeed()
    {
        if (_cache == null || !_cache.IsValid)
            return 0;

        float peak = 0;
        float duration = _cache.TotalDuration;
        float step = duration / 200.0f;

        if (step < FlybyConstants.TimeStep)
            step = FlybyConstants.TimeStep;

        for (float t = 0; t <= duration; t += step)
        {
            float speed = _cache.GetSpeedAtTime(t);

            if (speed > peak)
                peak = speed;
        }

        return peak;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        float w = (float)ActualWidth;
        float h = (float)ActualHeight;

        if (w <= 0 || h <= 0)
            return;

        // Background.
        context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        // Time ruler area.
        context.DrawRectangle(RulerBrush, null, new Rect(0, 0, w, FlybyConstants.TimelineRulerHeight));

        // Draw time ruler marks and labels.
        DrawTimeRuler(context, w);

        // Track area uses all remaining height below the ruler.
        float trackY = FlybyConstants.TimelineRulerHeight;
        float trackHeight = Math.Max(1.0f, h - FlybyConstants.TimelineRulerHeight);
        context.DrawRectangle(TrackBrush, null, new Rect(0, trackY, w, trackHeight));

        // Draw segment regions (freeze and camera cut indicators).
        DrawSegmentRegions(context, w, trackY, trackHeight);

        // Draw relative speed curve behind markers.
        DrawSpeedCurve(context, w, trackY, trackHeight);

        // Draw markers.
        DrawMarkers(context, w, trackY, trackHeight);

        // Draw range selection highlight.
        if (_isRangeSelecting)
        {
            float selLeft = Math.Min(_rangeStartX, _rangeEndX);
            float selRight = Math.Max(_rangeStartX, _rangeEndX);
            context.DrawRectangle(SelectionBrush, null, new Rect(selLeft, trackY, selRight - selLeft, trackHeight));
        }

        // Draw cursor line at mouse position.
        if (_isMouseOver && _mouseX >= 0 && _mouseX <= w)
            context.DrawLine(CursorLinePen, new Point(_mouseX, 0), new Point(_mouseX, h));

        // Draw playhead line.
        if (_playheadSeconds >= 0)
        {
            float phX = TimeToPixel(_playheadSeconds, w);

            if (phX >= 0 && phX <= w)
                context.DrawLine(PlayheadPen, new Point(phX, 0), new Point(phX, h));
        }
    }

    private void DrawTimeRuler(DrawingContext context, float width)
    {
        float visibleDuration = _visibleEndSeconds - _visibleStartSeconds;

        if (visibleDuration <= 0)
            return;

        // Determine tick interval.
        float pixelsPerSecond = width / visibleDuration;
        float tickInterval = CalculateTickInterval(pixelsPerSecond);

        float startTick = MathF.Floor(_visibleStartSeconds / tickInterval) * tickInterval;

        for (float t = startTick; t <= _visibleEndSeconds; t += tickInterval)
        {
            float x = TimeToPixel(t, width);

            if (x < 0 || x > width)
                continue;

            // Draw grid line.
            context.DrawLine(GridLinePen, new Point(x, FlybyConstants.TimelineRulerHeight), new Point(x, ActualHeight));

            // Draw tick mark.
            context.DrawLine(GridLinePen, new Point(x, 0), new Point(x, FlybyConstants.TimelineRulerHeight));

            // Draw label.
            string label = FormatRulerLabel(t);
            var formattedText = new FormattedText(
                label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                DefaultTypeface, 9, RulerTextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            context.DrawText(formattedText, new Point(x + 2, 2));
        }
    }

    private void DrawSegmentRegions(DrawingContext context, float width, float trackY, float trackHeight)
    {
        for (int i = 0; i < _markers.Count - 1; i++)
        {
            var marker = _markers[i];
            float startX = TimeToPixel(marker.TimeSeconds, width);

            if (marker.HasFreeze && marker.FreezeDuration > 0)
            {
                float freezeRight = Math.Min(width, TimeToPixel(marker.TimeSeconds + marker.FreezeDuration, width));
                float freezeLeft = Math.Max(0.0f, startX);

                if (freezeRight > freezeLeft)
                    context.DrawRectangle(FreezeRegionBrush, null, new Rect(freezeLeft, trackY, freezeRight - freezeLeft, trackHeight));
            }

            // Draw camera cut region as diagonal hatch lines.
            if (marker.HasCameraCut)
            {
                float bypassEnd = marker.CutBypassDuration > 0
                    ? marker.TimeSeconds + marker.CutBypassDuration
                    : marker.TimeSeconds + marker.SegmentDuration;

                float cutLeft = Math.Max(0.0f, startX);
                float cutRight = Math.Min(width, TimeToPixel(bypassEnd, width));

                if (cutRight > cutLeft)
                    DrawDiagonalHatch(context, cutLeft, trackY, cutRight - cutLeft, trackHeight, CameraCutPen);
            }
        }
    }

    private static void DrawDiagonalHatch(DrawingContext context, float x, float y, float w, float h, Pen pen)
    {
        float spacing = 6.0f;
        var clip = new RectangleGeometry(new Rect(x, y, w, h));

        context.PushClip(clip);

        for (float offset = -h; offset < w + h; offset += spacing)
        {
            context.DrawLine(pen,
                new Point(x + offset, y + h),
                new Point(x + offset + h, y));
        }

        context.Pop();
    }

    // Returns the normalized speed (0-1) at the given timeline time by sampling
    // the pre-calculated cache for actual inter-frame distances.
    // Returns negative when the time is outside the sequence or within a cut region.
    private float GetSpeedAtTime(float timeSeconds)
    {
        if (_cache == null || !_cache.IsValid || _markers.Count < 2)
            return -1;

        float speed = _cache.GetSpeedAtTime(timeSeconds);

        if (speed < 0)
            return -1;

        // Normalize against the peak speed for this sequence.
        return _peakSpeed > 0.001f ? speed / _peakSpeed : 0;
    }

    // Draws a filled speed waveform centred at the track's vertical midpoint.
    // The fill is semi-transparent and mirrors above and below the centre line.
    private void DrawSpeedCurve(DrawingContext context, float width, float trackY, float trackHeight)
    {
        if (_markers.Count < 2)
            return;

        float maxHalfAmplitude = trackHeight / 2.0f - 5.0f;
        float centerY = trackY + trackHeight / 2.0f;
        int sampleCount = Math.Max(2, (int)(width / 2.0f));

        // Collect visible sample spans (skipping gaps outside the sequence).
        var spans = new List<(int Start, int End)>();
        int spanStart = -1;

        for (int i = 0; i <= sampleCount; i++)
        {
            float x = width * i / sampleCount;
            float speed = GetSpeedAtTime(PixelToTime(x, width));

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

        using (var streamContext = geometry.Open())
        {
            foreach (var (start, end) in spans)
                DrawFilledWaveformSpan(streamContext, width, centerY, sampleCount, start, end, maxHalfAmplitude);
        }

        geometry.Freeze();

        context.PushClip(new RectangleGeometry(new Rect(0, trackY, width, trackHeight)));
        context.DrawGeometry(SpeedCurveFillBrush, null, geometry);
        context.Pop();
    }

    private void DrawFilledWaveformSpan(StreamGeometryContext context, float width, float centerY,
        int sampleCount, int start, int end, float maxHalf)
    {
        int count = end - start + 1;
        var upper = new Point[count];
        var lower = new Point[count];

        for (int i = 0; i < count; i++)
        {
            int step = start + i;
            float x = width * step / sampleCount;
            float speed = Math.Max(0.0f, GetSpeedAtTime(PixelToTime(x, width)));
            float half = Math.Min(maxHalf, Math.Max(1.0f, speed * maxHalf));
            upper[i] = new Point(x, centerY - half);
            lower[i] = new Point(x, centerY + half);
        }

        // Begin at the first upper point, trace upper edge left-to-right,
        // then lower edge right-to-left, forming a closed filled shape.
        context.BeginFigure(upper[0], true, true);

        for (int i = 1; i < count; i++)
            context.LineTo(upper[i], true, false);

        for (int i = count - 1; i >= 0; i--)
            context.LineTo(lower[i], true, false);
    }

    private void DrawMarkers(DrawingContext context, float width, float trackY, float trackHeight)
    {
        float centerY = trackY + trackHeight / 2.0f;

        for (int i = 0; i < _markers.Count; i++)
        {
            var marker = _markers[i];
            float x = TimeToPixel(marker.TimeSeconds, width);

            if (x < -FlybyConstants.TimelineMarkerRadius || x > width + FlybyConstants.TimelineMarkerRadius)
                continue;

            Brush fill;

            if (marker.IsDuplicate)
                fill = MarkerErrorBrush;
            else if (marker.IsSelected)
                fill = MarkerSelectedBrush;
            else
                fill = MarkerBrush;

            if (marker.HasCameraCut)
            {
                DrawTriangleMarker(context, fill, x, centerY);
            }
            else if (marker.HasFreeze)
            {
                float half = FlybyConstants.TimelineMarkerRadius;
                context.DrawRectangle(fill, MarkerOutlinePen, new Rect(x - half, centerY - half, half * 2, half * 2));
            }
            else
            {
                context.DrawEllipse(fill, MarkerOutlinePen, new Point(x, centerY), FlybyConstants.TimelineMarkerRadius, FlybyConstants.TimelineMarkerRadius);
            }
        }

        // Draw ghost markers during reposition drag.
        if (_isRepositioning)
            DrawGhostMarkers(context, width, centerY);
    }

    private static StreamGeometry CreateTriangleMarkerGeometry()
    {
        var geometry = new StreamGeometry();
        float radius = FlybyConstants.TimelineMarkerRadius;

        using (var streamContext = geometry.Open())
        {
            streamContext.BeginFigure(new Point(radius, 0.0f), true, true);
            streamContext.LineTo(new Point(-radius, -radius), true, false);
            streamContext.LineTo(new Point(-radius, radius), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawTriangleMarker(DrawingContext context, Brush fill, float centerX, float centerY)
    {
        context.PushTransform(new TranslateTransform(centerX, centerY));
        context.DrawGeometry(fill, MarkerOutlinePen, CameraCutMarkerGeometry);
        context.Pop();
    }

    private void DrawGhostMarkers(DrawingContext context, float width, float centerY)
    {
        // Draw ghost at the dragged camera's current mouse position.
        context.DrawEllipse(GhostMarkerBrush, GhostMarkerPen,
            new Point(_repositionGhostX, centerY),
            FlybyConstants.TimelineMarkerRadius, FlybyConstants.TimelineMarkerRadius);

        // Highlight the target marker.
        if (_repositionTargetIndex >= 0 && _repositionTargetIndex < _markers.Count &&
            _repositionTargetIndex != _repositionFromIndex)
        {
            float targetX = TimeToPixel(_markers[_repositionTargetIndex].TimeSeconds, width);
            context.DrawEllipse(GhostMarkerBrush, GhostMarkerPen,
                new Point(targetX, centerY),
                FlybyConstants.TimelineMarkerRadius, FlybyConstants.TimelineMarkerRadius);
        }
    }

    #region Input handling

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        // Skip normal processing for double-click; OnMouseDoubleClick will handle it.
        if (e.ClickCount >= 2)
            return;

        var pos = e.GetPosition(this);

        // Scrub if clicking in the ruler area.
        if (pos.Y < FlybyConstants.TimelineRulerHeight)
        {
            _isScrubbing = true;
            CaptureMouse();

            float scrubTime = PixelToTime((float)pos.X, (float)ActualWidth);
            ScrubRequested?.Invoke(Math.Max(0, scrubTime));
            return;
        }

        int hitIndex = HitTestMarker(pos);

        if (hitIndex >= 0)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                // Re-select the clicked camera first to enforce singular selection.
                MarkerClicked?.Invoke(hitIndex);

                // Alt+drag: reposition/renumber mode.
                _isRepositioning = true;
                _repositionGhostX = (float)pos.X;
                _repositionFromIndex = hitIndex;
                _repositionTargetIndex = hitIndex;
                CaptureMouse();
            }
            else
            {
                _dragIndex = hitIndex;
                _isDragging = false;
                _dragStartPoint = pos;
                _dragMouseOffsetSeconds = PixelToTime((float)pos.X, (float)ActualWidth) - _markers[hitIndex].TimeSeconds;
                CaptureMouse();
                MarkerClicked?.Invoke(hitIndex);
            }
        }
        else
        {
            // Start range selection on empty space.
            _isRangeSelecting = true;
            _rangeStartX = (float)pos.X;
            _rangeEndX = (float)pos.X;
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var pos = e.GetPosition(this);
        _mouseX = (float)pos.X;
        _isMouseOver = true;

        if (_isRepositioning && e.LeftButton == MouseButtonState.Pressed)
        {
            _repositionGhostX = (float)pos.X;
            _repositionTargetIndex = ComputeReorderTargetIndex((float)pos.X, (float)ActualWidth);
        }
        else if (_dragIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_isDragging && HasExceededDragThreshold(pos))
                _isDragging = true;

            if (_isDragging)
            {
                float newTime = PixelToTime((float)pos.X, (float)ActualWidth) - _dragMouseOffsetSeconds;
                newTime = Math.Max(0, newTime);
                MarkerDragged?.Invoke(_dragIndex, newTime);
            }
        }
        else if (_isScrubbing && e.LeftButton == MouseButtonState.Pressed)
        {
            float scrubTime = PixelToTime((float)pos.X, (float)ActualWidth);
            ScrubRequested?.Invoke(Math.Max(0, scrubTime));
        }
        else if (_isRangeSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            _rangeEndX = (float)pos.X;

            if (Math.Abs(_rangeEndX - _rangeStartX) >= 3)
                RangeSelected?.Invoke(GetRangeSelection());
        }

        InvalidateVisual();
    }

    private bool HasExceededDragThreshold(Point currentPoint)
    {
        return Math.Abs(currentPoint.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(currentPoint.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_isRepositioning)
        {
            _isRepositioning = false;
            CommitRepositioning();
        }
        else if (_isRangeSelecting)
        {
            _isRangeSelecting = false;
            CommitRangeSelection();
        }

        if (_isDragging && _dragIndex >= 0)
            MarkerDragCompleted?.Invoke(_dragIndex);

        _isScrubbing = false;
        _dragIndex = -1;
        _isDragging = false;
        _dragMouseOffsetSeconds = 0;
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

        float newStart = pivotTime - (pivotTime - baseStart) * zoomFactor;
        float newEnd = pivotTime + (baseEnd - pivotTime) * zoomFactor;

        ClampViewportToBounds(ref newStart, ref newEnd);

        if (newEnd - newStart < 0.1f)
            return;

        ApplyViewport(newStart, newEnd, FlybyConstants.TimelineSmoothZoomEnabled);
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
        ZoomToFit();
    }

    public void ZoomToFit()
    {
        ApplyViewport(0.0f, _totalDurationSeconds * FlybyConstants.TimelineZoomOutScale, false);
    }

    public void PanLeft(bool smooth = false)
    {
        PanBy(-(_visibleEndSeconds - _visibleStartSeconds) * FlybyConstants.TimelinePanStepFraction, smooth);
    }

    public void PanRight(bool smooth = false)
    {
        PanBy((_visibleEndSeconds - _visibleStartSeconds) * FlybyConstants.TimelinePanStepFraction, smooth);
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
        else if (e.Key == Key.Left)
        {
            PanLeft();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            PanRight();
            e.Handled = true;
        }
    }

    #endregion Input handling

    #region Range selection

    private void CommitRangeSelection()
    {
        // Treat a tiny drag as a click on empty space (deselect).
        if (Math.Abs(_rangeEndX - _rangeStartX) < 3)
        {
            RangeSelected?.Invoke(new List<int>());
            return;
        }

        RangeSelected?.Invoke(GetRangeSelection());
    }

    private List<int> GetRangeSelection()
    {
        float leftX = Math.Min(_rangeStartX, _rangeEndX);
        float rightX = Math.Max(_rangeStartX, _rangeEndX);

        var selected = new List<int>();

        for (int i = 0; i < _markers.Count; i++)
        {
            float x = TimeToPixel(_markers[i].TimeSeconds, (float)ActualWidth);

            if (x >= leftX && x <= rightX)
                selected.Add(i);
        }

        return selected;
    }

    #endregion Range selection

    #region Reposition

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

    #endregion Reposition

    #region Coordinate conversion

    private void PanBy(float deltaSeconds, bool smooth)
    {
        GetInteractiveViewport(out float startSeconds, out float endSeconds);
        float visibleRange = endSeconds - startSeconds;

        if (visibleRange <= 0)
            return;

        float targetStart = ClampVisibleStart(startSeconds + deltaSeconds, visibleRange);
        ApplyViewport(targetStart, targetStart + visibleRange, smooth);
    }

    private float ClampVisibleStart(float newStart, float visibleRange)
    {
        float maxEnd = Math.Max(_totalDurationSeconds * 1.5f, visibleRange);
        float maxStart = Math.Max(0.0f, maxEnd - visibleRange);
        return Math.Clamp(newStart, 0.0f, maxStart);
    }

    private void NormalizeVisibleViewport()
    {
        if (_visibleStartSeconds >= _visibleEndSeconds)
            _visibleStartSeconds = 0;

        float visibleRange = _visibleEndSeconds - _visibleStartSeconds;

        if (visibleRange <= 0)
            return;

        _visibleStartSeconds = ClampVisibleStart(_visibleStartSeconds, visibleRange);
        _visibleEndSeconds = _visibleStartSeconds + visibleRange;
    }

    private void GetInteractiveViewport(out float startSeconds, out float endSeconds)
    {
        if (_smoothViewportTimer.IsEnabled)
        {
            startSeconds = _smoothViewportTargetStartSeconds;
            endSeconds = _smoothViewportTargetEndSeconds;
            return;
        }

        startSeconds = _visibleStartSeconds;
        endSeconds = _visibleEndSeconds;
    }

    private void ClampViewportToBounds(ref float startSeconds, ref float endSeconds)
    {
        float maxEnd = _totalDurationSeconds * 1.5f;

        if (startSeconds < 0.0f)
            startSeconds = 0.0f;

        if (endSeconds > maxEnd)
            endSeconds = maxEnd;
    }

    private void ApplyViewport(float startSeconds, float endSeconds, bool smooth)
    {
        if (smooth)
        {
            StartSmoothViewport(startSeconds, endSeconds);
            return;
        }

        StopSmoothViewport(false);
        _visibleStartSeconds = startSeconds;
        _visibleEndSeconds = endSeconds;
        InvalidateVisual();
    }

    private void StartSmoothViewport(float targetStart, float targetEnd)
    {
        _smoothViewportTargetStartSeconds = targetStart;
        _smoothViewportTargetEndSeconds = targetEnd;

        if (!_smoothViewportTimer.IsEnabled)
            _smoothViewportTimer.Start();
    }

    private void StopSmoothViewport(bool snapToTarget)
    {
        if (!_smoothViewportTimer.IsEnabled)
            return;

        _smoothViewportTimer.Stop();

        if (snapToTarget)
        {
            _visibleStartSeconds = _smoothViewportTargetStartSeconds;
            _visibleEndSeconds = _smoothViewportTargetEndSeconds;
        }
    }

    private float TimeToPixel(float timeSeconds, float width)
    {
        float range = _visibleEndSeconds - _visibleStartSeconds;

        if (range <= 0)
            return 0;

        return (timeSeconds - _visibleStartSeconds) / range * width;
    }

    private float PixelToTime(float pixel, float width)
    {
        return PixelToTime(pixel, width, _visibleStartSeconds, _visibleEndSeconds);
    }

    private static float PixelToTime(float pixel, float width, float visibleStartSeconds, float visibleEndSeconds)
    {
        float range = visibleEndSeconds - visibleStartSeconds;

        if (width <= 0)
            return visibleStartSeconds;

        return visibleStartSeconds + pixel / width * range;
    }

    private int HitTestMarker(Point pos)
    {
        float trackY = FlybyConstants.TimelineRulerHeight;

        // Only hit-test within the track area.
        if (pos.Y < trackY - 4 || pos.Y > ActualHeight)
            return -1;

        float closestDist = float.MaxValue;
        int closestIndex = -1;

        for (int i = 0; i < _markers.Count; i++)
        {
            float x = TimeToPixel(_markers[i].TimeSeconds, (float)ActualWidth);
            float dist = Math.Abs((float)pos.X - x);

            if (dist < FlybyConstants.TimelineMarkerRadius + 4 && dist < closestDist)
            {
                closestDist = dist;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private int ComputeReorderTargetIndex(float mouseX, float width)
    {
        int lastBefore = -1;

        for (int i = 0; i < _markers.Count; i++)
        {
            if (TimeToPixel(_markers[i].TimeSeconds, width) <= mouseX)
                lastBefore = i;
            else
                break;
        }

        // Forward drag: source is at or before the last marker the ghost passed.
        if (_repositionFromIndex <= lastBefore)
            return lastBefore;

        // Backward drag: place the dragged camera before the first marker to the right of the ghost.
        return Math.Min(lastBefore + 1, _markers.Count - 1);
    }

    private static float CalculateTickInterval(float pixelsPerSecond)
    {
        float[] candidates = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1, 2, 5, 10, 20, 30, 60, 120, 300 };

        foreach (float interval in candidates)
        {
            if (interval * pixelsPerSecond >= FlybyConstants.TimelineMinTickSpacing)
                return interval;
        }

        return 300;
    }

    private static string FormatRulerLabel(float seconds)
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
