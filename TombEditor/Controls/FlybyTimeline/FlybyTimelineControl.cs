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

    private static readonly float[] RulerTickIntervals =
    [
        0.01f, 0.02f, 0.05f,
        0.1f, 0.2f, 0.5f,
        1.0f, 2.0f, 5.0f,
        10.0f, 20.0f, 30.0f,
        60.0f, 120.0f, 300.0f
    ];

    private static readonly Typeface DefaultTypeface = new("Segoe UI");

    // Timeline data: markers displayed by the control.
    private IReadOnlyList<TimelineMarker> _markers = [];

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

    // Pan state for middle/right mouse button dragging.
    private bool _isPanning;
    private float _panStartPixelX;
    private float _panStartViewSeconds;
    private float _panStartViewRange;
    private bool _rightButtonPanned;

    public event Action<int, float>? MarkerDragged;
    public event Action<int>? MarkerClicked;
    public event Action<int>? MarkerDoubleClicked;
    public event Action<int>? MarkerDragCompleted;
    public event Action<IReadOnlyList<int>>? RangeSelected;
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

    /// <summary>
    /// Initializes the timeline control and its smooth viewport timer.
    /// </summary>
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

    /// <summary>
    /// Represents one rendered marker on the timeline.
    /// </summary>
    public readonly struct TimelineMarker
    {
        /// <summary>
        /// Gets the marker time on the timeline.
        /// </summary>
        public float TimeSeconds { get; init; }

        /// <summary>
        /// Gets whether this marker has a duplicate camera index.
        /// </summary>
        public bool IsDuplicate { get; init; }

        /// <summary>
        /// Gets whether this marker is currently selected.
        /// </summary>
        public bool IsSelected { get; init; }

        /// <summary>
        /// Gets whether this marker starts a camera cut.
        /// </summary>
        public bool HasCameraCut { get; init; }

        /// <summary>
        /// Gets whether this marker lies inside a cut-bypassed region.
        /// </summary>
        public bool IsInCutBypass { get; init; }

        /// <summary>
        /// Gets the duration bypassed by a cut starting at this marker.
        /// </summary>
        public float CutBypassDuration { get; init; }

        /// <summary>
        /// Gets the duration of the outgoing segment starting at this marker.
        /// </summary>
        public float SegmentDuration { get; init; }

        /// <summary>
        /// Gets whether this marker starts a freeze region.
        /// </summary>
        public bool HasFreeze { get; init; }

        /// <summary>
        /// Gets the duration of the freeze starting at this marker.
        /// </summary>
        public float FreezeDuration { get; init; }
    }

    /// <summary>
    /// Sets the playhead position. Negative value hides the playhead.
    /// </summary>
    public void SetPlayheadSeconds(float seconds)
    {
        if (!float.IsFinite(seconds))
            seconds = -1.0f;

        _playheadSeconds = seconds;
        InvalidateVisual();
    }

    /// <summary>
    /// Updates the marker data and redraws the timeline.
    /// </summary>
    /// <param name="markers">Markers to render on the timeline.</param>
    /// <param name="totalDuration">Total analyzed sequence duration in seconds.</param>
    /// <param name="cache">Optional cache used to draw the speed curve.</param>
    public void SetMarkers(IReadOnlyList<TimelineMarker> markers, float totalDuration, FlybySequenceCache? cache = null)
    {
        _markers = markers ?? [];
        _totalDurationSeconds = float.IsFinite(totalDuration)
            ? Math.Max(1.0f, totalDuration)
            : 1.0f;
        _cache = cache;
        _peakSpeed = ComputePeakSpeed();

        StopSmoothViewport(false);
        NormalizeVisibleViewport();

        InvalidateVisual();
    }

    /// <summary>
    /// Stops viewport animation when the control is unloaded.
    /// </summary>
    private void OnUnloaded(object? sender, RoutedEventArgs e)
        => StopSmoothViewport(false);

    /// <summary>
    /// Advances the smooth viewport animation toward its target range.
    /// </summary>
    private void OnSmoothViewportTick(object? sender, EventArgs e)
    {
        float startDelta = _smoothViewportTargetStartSeconds - _visibleStartSeconds;
        float endDelta = _smoothViewportTargetEndSeconds - _visibleEndSeconds;

        if (MathF.Abs(startDelta) <= FlybyConstants.TimelineSmoothViewportEpsilon &&
            MathF.Abs(endDelta) <= FlybyConstants.TimelineSmoothViewportEpsilon)
        {
            SetViewport(_smoothViewportTargetStartSeconds, _smoothViewportTargetEndSeconds, false);
            StopSmoothViewport(false);
            InvalidateVisual();
            return;
        }

        SetViewport(
            _visibleStartSeconds + (startDelta * FlybyConstants.TimelineSmoothViewportLerpFactor),
            _visibleEndSeconds + (endDelta * FlybyConstants.TimelineSmoothViewportLerpFactor),
            false);

        InvalidateVisual();
    }

    /// <summary>
    /// Scans the cache to find the maximum speed value for normalizing the speed curve display.
    /// </summary>
    private float ComputePeakSpeed()
    {
        if (_cache?.IsValid != true)
            return 0.0f;

        float duration = _cache.TotalDuration;

        if (!float.IsFinite(duration) || duration <= 0.0f)
            return 0.0f;

        float peak = 0.0f;
        float step = duration / 200.0f;

        if (step < FlybyConstants.TimeStep)
            step = FlybyConstants.TimeStep;

        for (float t = 0; t <= duration; t += step)
        {
            float speed = _cache.GetSpeedAtTime(t);

            if (float.IsFinite(speed) && speed > peak)
                peak = speed;
        }

        return peak;
    }

    /// <summary>
    /// Renders the ruler, track, markers, selection, and playhead.
    /// </summary>
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
        const float trackY = FlybyConstants.TimelineRulerHeight;
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

    /// <summary>
    /// Draws ruler ticks and labels for the current viewport.
    /// </summary>
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
            string label = FlybySequenceHelper.FormatRulerLabel(t);
            var formattedText = new FormattedText(
                label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                DefaultTypeface, 9, RulerTextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            context.DrawText(formattedText, new Point(x + 2, 2));
        }
    }

    /// <summary>
    /// Draws freeze and camera-cut overlays for visible segments.
    /// </summary>
    private void DrawSegmentRegions(DrawingContext context, float width, float trackY, float trackHeight)
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            var marker = _markers[i];

            if (!float.IsFinite(marker.TimeSeconds))
                continue;

            float startX = TimeToPixel(marker.TimeSeconds, width);

            if (marker.HasFreeze && float.IsFinite(marker.FreezeDuration) && marker.FreezeDuration > 0.0f)
            {
                float freezeRight = Math.Min(width, TimeToPixel(marker.TimeSeconds + marker.FreezeDuration, width));
                float freezeLeft = Math.Max(0.0f, startX);

                if (freezeRight > freezeLeft)
                    context.DrawRectangle(FreezeRegionBrush, null, new Rect(freezeLeft, trackY, freezeRight - freezeLeft, trackHeight));
            }

            if (i >= _markers.Count - 1 || !marker.HasCameraCut)
                continue;

            // Draw camera cut region as diagonal hatch lines.
            float bypassDuration = marker.CutBypassDuration > 0.0f
                ? marker.CutBypassDuration
                : marker.SegmentDuration;

            if (!float.IsFinite(bypassDuration) || bypassDuration <= 0.0f)
                continue;

            float cutLeft = Math.Max(0.0f, startX);
            float cutRight = Math.Min(width, TimeToPixel(marker.TimeSeconds + bypassDuration, width));

            if (cutRight > cutLeft)
                DrawDiagonalHatch(context, cutLeft, trackY, cutRight - cutLeft, trackHeight, CameraCutPen);
        }
    }

    /// <summary>
    /// Draws diagonal hatch lines inside the provided rectangle.
    /// </summary>
    private static void DrawDiagonalHatch(DrawingContext context, float x, float y, float w, float h, Pen pen)
    {
        const float spacing = 6.0f;
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

    /// <summary>
    /// Returns the normalized speed sample for the given timeline time, or a negative value when no sample is available.
    /// </summary>
    private float GetSpeedAtTime(float timeSeconds)
    {
        if (!float.IsFinite(timeSeconds) || _cache?.IsValid != true || _markers.Count < 2)
            return -1.0f;

        float speed = _cache.GetSpeedAtTime(timeSeconds);

        if (!float.IsFinite(speed) || speed < 0.0f)
            return -1.0f;

        // Normalize against the peak speed for this sequence.
        return float.IsFinite(_peakSpeed) && _peakSpeed > FlybyConstants.MinSpeed ? speed / _peakSpeed : 0.0f;
    }

    /// <summary>
    /// Draws the mirrored cached speed waveform behind the timeline markers.
    /// </summary>
    private void DrawSpeedCurve(DrawingContext context, float width, float trackY, float trackHeight)
    {
        if (_markers.Count < 2)
            return;

        float maxHalfAmplitude = (trackHeight / 2.0f) - 5.0f;
        float centerY = trackY + (trackHeight / 2.0f);
        int sampleCount = Math.Max(2, (int)(width / 2.0f));

        // Collect visible sample spans (skipping gaps outside the sequence).
        List<(int Start, int End)> spans = [];
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

    /// <summary>
    /// Draws one continuous filled speed span for the waveform.
    /// </summary>
    /// <param name="context">Geometry writer receiving the filled waveform points.</param>
    /// <param name="width">Current control width in pixels.</param>
    /// <param name="centerY">Vertical center of the waveform.</param>
    /// <param name="sampleCount">Total number of horizontal samples across the control.</param>
    /// <param name="start">Inclusive starting sample index for this continuous span.</param>
    /// <param name="end">Inclusive ending sample index for this continuous span.</param>
    /// <param name="maxHalf">Maximum half-height used for waveform amplitude.</param>
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

    /// <summary>
    /// Draws all visible timeline markers and reposition ghosts.
    /// </summary>
    private void DrawMarkers(DrawingContext context, float width, float trackY, float trackHeight)
    {
        float centerY = trackY + (trackHeight / 2.0f);

        for (int i = 0; i < _markers.Count; i++)
        {
            var marker = _markers[i];

            if (!TryGetMarkerPixel(marker, width, out float x))
                continue;

            if (x < -FlybyConstants.TimelineMarkerRadius || x > width + FlybyConstants.TimelineMarkerRadius)
                continue;

            var fill = GetMarkerFillBrush(marker);
            DrawMarker(context, marker, fill, x, centerY);
        }

        // Draw ghost markers during reposition drag.
        if (_isRepositioning)
            DrawGhostMarkers(context, width, centerY);
    }

    /// <summary>
    /// Returns the fill brush for a marker based on its state.
    /// </summary>
    private static Brush GetMarkerFillBrush(TimelineMarker marker)
    {
        if (marker.IsDuplicate)
            return MarkerErrorBrush;

        if (marker.IsSelected)
            return MarkerSelectedBrush;

        return MarkerBrush;
    }

    /// <summary>
    /// Draws a single marker using the shape implied by its flags.
    /// </summary>
    private static void DrawMarker(DrawingContext context, TimelineMarker marker, Brush fill, float x, float centerY)
        => DrawMarker(context, marker, fill, MarkerOutlinePen, x, centerY);

    /// <summary>
    /// Draws a single marker using the provided outline pen.
    /// </summary>
    private static void DrawMarker(DrawingContext context, TimelineMarker marker, Brush fill, Pen outlinePen, float x, float centerY)
    {
        if (marker.HasCameraCut)
        {
            DrawTriangleMarker(context, fill, outlinePen, x, centerY);
            return;
        }

        if (marker.HasFreeze)
        {
            const float half = FlybyConstants.TimelineMarkerRadius;
            context.DrawRectangle(fill, outlinePen, new Rect(x - half, centerY - half, half * 2, half * 2));
            return;
        }

        context.DrawEllipse(fill, outlinePen, new Point(x, centerY), FlybyConstants.TimelineMarkerRadius, FlybyConstants.TimelineMarkerRadius);
    }

    /// <summary>
    /// Creates the triangle geometry used for camera-cut markers.
    /// </summary>
    private static StreamGeometry CreateTriangleMarkerGeometry()
    {
        var geometry = new StreamGeometry();
        const float radius = FlybyConstants.TimelineMarkerRadius;

        using (var streamContext = geometry.Open())
        {
            streamContext.BeginFigure(new Point(radius, 0.0f), true, true);
            streamContext.LineTo(new Point(-radius, -radius), true, false);
            streamContext.LineTo(new Point(-radius, radius), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Draws a triangle marker centered at the provided position.
    /// </summary>
    private static void DrawTriangleMarker(DrawingContext context, Brush fill, Pen outlinePen, float centerX, float centerY)
    {
        context.PushTransform(new TranslateTransform(centerX, centerY));
        context.DrawGeometry(fill, outlinePen, CameraCutMarkerGeometry);
        context.Pop();
    }

    /// <summary>
    /// Draws ghost markers used while reordering cameras.
    /// </summary>
    private void DrawGhostMarkers(DrawingContext context, float width, float centerY)
    {
        if (_repositionFromIndex < 0 || _repositionFromIndex >= _markers.Count)
            return;

        // Draw ghost at the dragged camera's current mouse position.
        DrawMarker(context, _markers[_repositionFromIndex], GhostMarkerBrush, GhostMarkerPen, _repositionGhostX, centerY);

        // Highlight the target marker.
        if (_repositionTargetIndex >= 0 && _repositionTargetIndex < _markers.Count &&
            _repositionTargetIndex != _repositionFromIndex)
        {
            if (TryGetMarkerPixel(_markers[_repositionTargetIndex], width, out float targetX))
                DrawMarker(context, _markers[_repositionTargetIndex], GhostMarkerBrush, GhostMarkerPen, targetX, centerY);
        }
    }

    #region Input handling

    /// <summary>
    /// Starts scrubbing, dragging, repositioning, or range selection from a left click.
    /// </summary>
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

        if (_isPanning)
            UpdatePan(mouseX);
        else if (_isRepositioning && e.LeftButton == MouseButtonState.Pressed)
            UpdateReposition(mouseX);
        else if (_dragIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
            UpdateMarkerDrag(pos);
        else if (_isScrubbing && e.LeftButton == MouseButtonState.Pressed)
            UpdateScrub(mouseX);
        else if (_isRangeSelecting && e.LeftButton == MouseButtonState.Pressed)
            UpdateRangeSelection(mouseX);

        InvalidateVisual();
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
        _isScrubbing = true;

        CaptureMouse();
        UpdateScrub(mouseX);
    }

    /// <summary>
    /// Begins marker reposition mode using Alt-drag.
    /// </summary>
    private void BeginReposition(int hitIndex, float mouseX)
    {
        // Re-select the clicked camera first to enforce singular selection.
        MarkerClicked?.Invoke(hitIndex);

        // Alt+drag: reposition/renumber mode.
        _isRepositioning = true;
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
        _isRangeSelecting = true;
        _rangeStartX = mouseX;
        _rangeEndX = mouseX;
        CaptureMouse();
        InvalidateVisual();
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
    /// Updates marquee selection and emits selected markers after threshold is met.
    /// </summary>
    private void UpdateRangeSelection(float mouseX)
    {
        _rangeEndX = mouseX;

        if (!HasExceededSelectionThreshold(_rangeStartX, _rangeEndX))
            return;

        RangeSelected?.Invoke(GetRangeSelection());
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

        if (e.ChangedButton == MouseButton.Middle && _isPanning)
            EndPan();
    }

    /// <summary>
    /// Ends right-button panning and suppresses the context menu after a drag.
    /// </summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        if (!_isPanning)
            return;

        EndPan();

        // Suppress context menu when the drag moved enough to count as a pan.
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

        // Double-click on a marker invokes edit.
        if (hitIndex >= 0)
        {
            MarkerDoubleClicked?.Invoke(hitIndex);
            return;
        }

        // Full zoom out on double-click on empty space.
        ZoomToFit();
    }

    /// <summary>
    /// Expands the viewport to show the full sequence range.
    /// </summary>
    public void ZoomToFit()
        => ApplyViewport(0.0f, _totalDurationSeconds * FlybyConstants.TimelineZoomOutScale, FlybyConstants.TimelineSmoothZoomEnabled);

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

        _isPanning = true;
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
    /// Ends the current viewport pan operation.
    /// </summary>
    private void EndPan()
    {
        _isPanning = false;
        Cursor = null;
        ReleaseMouseCapture();
    }

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

    #endregion Input handling

    #region Range selection

    /// <summary>
    /// Finalizes marquee selection and handles click-to-clear behavior.
    /// </summary>
    private void CommitRangeSelection()
    {
        // Treat a tiny drag as a click on empty space (deselect).
        if (!HasExceededSelectionThreshold(_rangeStartX, _rangeEndX))
        {
            RangeSelected?.Invoke([]);
            return;
        }

        RangeSelected?.Invoke(GetRangeSelection());
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

    #endregion Range selection

    #region Reposition

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

    #endregion Reposition

    #region Coordinate conversion

    /// <summary>
    /// Pans the viewport by a time delta.
    /// </summary>
    private void PanBy(float deltaSeconds, bool smooth)
    {
        GetInteractiveViewport(out float startSeconds, out float endSeconds);
        float visibleRange = endSeconds - startSeconds;

        if (visibleRange <= 0)
            return;

        float targetStart = ClampVisibleStart(startSeconds + deltaSeconds, visibleRange);
        ApplyViewport(targetStart, targetStart + visibleRange, smooth);
    }

    /// <summary>
    /// Clamps the viewport start so the visible range stays within bounds.
    /// </summary>
    private float ClampVisibleStart(float newStart, float visibleRange)
    {
        if (!float.IsFinite(newStart) || !float.IsFinite(visibleRange) || visibleRange <= 0.0f)
            return 0.0f;

        float maxEnd = Math.Max(_totalDurationSeconds * 1.5f, visibleRange);
        float maxStart = Math.Max(0.0f, maxEnd - visibleRange);
        return Math.Clamp(newStart, 0.0f, maxStart);
    }

    /// <summary>
    /// Normalizes the stored viewport so it remains valid.
    /// </summary>
    private void NormalizeVisibleViewport()
    {
        if (!float.IsFinite(_visibleStartSeconds) || !float.IsFinite(_visibleEndSeconds))
        {
            _visibleStartSeconds = 0.0f;
            _visibleEndSeconds = Math.Max(1.0f, _totalDurationSeconds);
            return;
        }

        if (_visibleStartSeconds >= _visibleEndSeconds)
        {
            _visibleStartSeconds = 0.0f;
            _visibleEndSeconds = Math.Max(1.0f, _totalDurationSeconds);
            return;
        }

        float visibleRange = _visibleEndSeconds - _visibleStartSeconds;

        if (visibleRange <= 0)
        {
            _visibleStartSeconds = 0.0f;
            _visibleEndSeconds = Math.Max(1.0f, _totalDurationSeconds);
            return;
        }

        _visibleStartSeconds = ClampVisibleStart(_visibleStartSeconds, visibleRange);
        _visibleEndSeconds = _visibleStartSeconds + visibleRange;
    }

    /// <summary>
    /// Returns the viewport that should be used for active interactions.
    /// </summary>
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

    /// <summary>
    /// Clamps a viewport range to the allowed timeline bounds.
    /// </summary>
    private void ClampViewportToBounds(ref float startSeconds, ref float endSeconds)
    {
        if (!float.IsFinite(startSeconds) || !float.IsFinite(endSeconds))
        {
            startSeconds = 0.0f;
            endSeconds = Math.Max(1.0f, _totalDurationSeconds);
            return;
        }

        float maxEnd = _totalDurationSeconds * 1.5f;

        if (startSeconds < 0.0f)
            startSeconds = 0.0f;

        if (endSeconds > maxEnd)
            endSeconds = maxEnd;
    }

    /// <summary>
    /// Validates and clamps a viewport before it is stored or animated.
    /// </summary>
    private bool TryNormalizeViewport(ref float startSeconds, ref float endSeconds)
    {
        ClampViewportToBounds(ref startSeconds, ref endSeconds);
        return endSeconds > startSeconds;
    }

    /// <summary>
    /// Applies a viewport immediately or through smooth animation.
    /// </summary>
    private void ApplyViewport(float startSeconds, float endSeconds, bool smooth)
    {
        if (!TryNormalizeViewport(ref startSeconds, ref endSeconds))
            return;

        if (smooth)
        {
            StartSmoothViewport(startSeconds, endSeconds);
            return;
        }

        StopSmoothViewport(false);
        SetViewport(startSeconds, endSeconds, true);
    }

    /// <summary>
    /// Starts smooth animation toward a target viewport.
    /// </summary>
    private void StartSmoothViewport(float targetStart, float targetEnd)
    {
        if (!TryNormalizeViewport(ref targetStart, ref targetEnd))
            return;

        _smoothViewportTargetStartSeconds = targetStart;
        _smoothViewportTargetEndSeconds = targetEnd;

        if (!_smoothViewportTimer.IsEnabled)
            _smoothViewportTimer.Start();
    }

    /// <summary>
    /// Stops smooth viewport animation and optionally snaps to its target.
    /// </summary>
    private void StopSmoothViewport(bool snapToTarget)
    {
        if (!_smoothViewportTimer.IsEnabled)
            return;

        _smoothViewportTimer.Stop();

        if (snapToTarget)
            SetViewport(_smoothViewportTargetStartSeconds, _smoothViewportTargetEndSeconds, false);
    }

    /// <summary>
    /// Stores the current viewport and optionally redraws the control.
    /// </summary>
    private void SetViewport(float startSeconds, float endSeconds, bool invalidateVisual)
    {
        if (!TryNormalizeViewport(ref startSeconds, ref endSeconds))
            return;

        _visibleStartSeconds = startSeconds;
        _visibleEndSeconds = endSeconds;

        if (invalidateVisual)
            InvalidateVisual();
    }

    /// <summary>
    /// Snaps the stored viewport to the currently interactive viewport state.
    /// </summary>
    private void SnapViewportToInteractiveState()
    {
        GetInteractiveViewport(out float startSeconds, out float endSeconds);
        StopSmoothViewport(false);
        SetViewport(startSeconds, endSeconds, false);
    }

    /// <summary>
    /// Ends any active left-button interaction and clears temporary state.
    /// </summary>
    private void EndLeftMouseInteraction()
    {
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

        if (IsMouseCaptured && !_isPanning)
            ReleaseMouseCapture();

        InvalidateVisual();
    }

    /// <summary>
    /// Returns whether a left-button-driven interaction is currently active or pending.
    /// </summary>
    private bool HasActiveLeftMouseInteraction()
        => _dragIndex >= 0 || _isScrubbing || _isRangeSelecting || _isRepositioning;

    /// <summary>
    /// Converts a timeline time to an x-coordinate in the current viewport.
    /// </summary>
    private float TimeToPixel(float timeSeconds, float width)
    {
        float range = _visibleEndSeconds - _visibleStartSeconds;

        if (width <= 0.0f || !float.IsFinite(timeSeconds) || !float.IsFinite(range) || range <= 0.0f)
            return 0.0f;

        return (timeSeconds - _visibleStartSeconds) / range * width;
    }

    /// <summary>
    /// Converts a marker time to a pixel position when the marker has a valid timeline time.
    /// </summary>
    private bool TryGetMarkerPixel(TimelineMarker marker, float width, out float x)
    {
        x = 0.0f;

        float range = _visibleEndSeconds - _visibleStartSeconds;

        if (width <= 0.0f || !float.IsFinite(marker.TimeSeconds) || !float.IsFinite(range) || range <= 0.0f)
            return false;

        x = TimeToPixel(marker.TimeSeconds, width);
        return float.IsFinite(x);
    }

    /// <summary>
    /// Converts an x-coordinate to a timeline time using the current viewport.
    /// </summary>
    private float PixelToTime(float pixel, float width)
        => PixelToTime(pixel, width, _visibleStartSeconds, _visibleEndSeconds);

    /// <summary>
    /// Converts an x-coordinate to a timeline time for the provided viewport.
    /// </summary>
    private static float PixelToTime(float pixel, float width, float visibleStartSeconds, float visibleEndSeconds)
    {
        if (width <= 0.0f || !float.IsFinite(pixel) || !float.IsFinite(visibleStartSeconds) || !float.IsFinite(visibleEndSeconds))
            return float.IsFinite(visibleStartSeconds) ? visibleStartSeconds : 0.0f;

        float range = visibleEndSeconds - visibleStartSeconds;

        if (!float.IsFinite(range) || range <= 0.0f)
            return visibleStartSeconds;

        return visibleStartSeconds + (pixel / width * range);
    }

    /// <summary>
    /// Returns the closest marker under the given mouse position.
    /// </summary>
    private int HitTestMarker(Point pos)
    {
        const float trackY = FlybyConstants.TimelineRulerHeight;

        // Only hit-test within the track area.
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

        // Forward drag: source is at or before the last marker the ghost passed.
        if (_repositionFromIndex <= lastBefore)
            return lastBefore;

        // Backward drag: place the dragged camera before the first marker to the right of the ghost.
        return Math.Min(lastBefore + 1, _markers.Count - 1);
    }

    /// <summary>
    /// Chooses a ruler tick interval based on the current zoom level.
    /// </summary>
    private static float CalculateTickInterval(float pixelsPerSecond)
    {
        foreach (float interval in RulerTickIntervals)
        {
            if (interval * pixelsPerSecond >= FlybyConstants.TimelineMinTickSpacing)
                return interval;
        }

        return RulerTickIntervals[^1];
    }

    #endregion Coordinate conversion
}
