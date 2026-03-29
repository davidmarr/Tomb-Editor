#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TombEditor.Controls.FlybyTimeline;

public partial class FlybyTimelineControl
{
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

        context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));
        context.DrawRectangle(RulerBrush, null, new Rect(0, 0, w, FlybyConstants.TimelineRulerHeight));

        DrawTimeRuler(context, w);

        const float trackY = FlybyConstants.TimelineRulerHeight;
        float trackHeight = Math.Max(1.0f, h - FlybyConstants.TimelineRulerHeight);
        context.DrawRectangle(TrackBrush, null, new Rect(0, trackY, w, trackHeight));

        DrawSegmentRegions(context, w, trackY, trackHeight);
        DrawSpeedCurve(context, w, trackY, trackHeight);
        DrawMarkers(context, w, trackY, trackHeight);

        if (_isRangeSelecting)
        {
            float selLeft = Math.Min(_rangeStartX, _rangeEndX);
            float selRight = Math.Max(_rangeStartX, _rangeEndX);
            context.DrawRectangle(SelectionBrush, null, new Rect(selLeft, trackY, selRight - selLeft, trackHeight));
        }

        if (_isMouseOver && _mouseX >= 0 && _mouseX <= w)
            context.DrawLine(CursorLinePen, new Point(_mouseX, 0), new Point(_mouseX, h));

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

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        float pixelsPerSecond = width / visibleDuration;
        float tickInterval = CalculateTickInterval(pixelsPerSecond);
        float startTick = MathF.Floor(_visibleStartSeconds / tickInterval) * tickInterval;

        for (float t = startTick; t <= _visibleEndSeconds; t += tickInterval)
        {
            float x = TimeToPixel(t, width);

            if (x < 0 || x > width)
                continue;

            context.DrawLine(GridLinePen, new Point(x, 0), new Point(x, ActualHeight));

            string label = FlybySequenceHelper.FormatRulerLabel(t);
            var formattedText = GetRulerLabelText(label, pixelsPerDip);

            context.DrawText(formattedText, new Point(x + 2, 2));
        }
    }

    /// <summary>
    /// Returns cached formatted ruler text for the provided label.
    /// </summary>
    private FormattedText GetRulerLabelText(string label, double pixelsPerDip)
    {
        if (Math.Abs(_rulerTextPixelsPerDip - pixelsPerDip) > double.Epsilon)
        {
            _rulerTextCache.Clear();
            _rulerTextPixelsPerDip = pixelsPerDip;
        }

        if (_rulerTextCache.TryGetValue(label, out var formattedText))
            return formattedText;

        if (_rulerTextCache.Count >= 128)
            _rulerTextCache.Clear();

        formattedText = new FormattedText(
            label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            DefaultTypeface, 9, RulerTextBrush, pixelsPerDip);

        _rulerTextCache[label] = formattedText;
        return formattedText;
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

        DrawMarker(context, _markers[_repositionFromIndex], GhostMarkerBrush, GhostMarkerPen, _repositionGhostX, centerY);

        if (_repositionTargetIndex >= 0 && _repositionTargetIndex < _markers.Count &&
            _repositionTargetIndex != _repositionFromIndex)
        {
            if (TryGetMarkerPixel(_markers[_repositionTargetIndex], width, out float targetX))
                DrawMarker(context, _markers[_repositionTargetIndex], GhostMarkerBrush, GhostMarkerPen, targetX, centerY);
        }
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
}
