#nullable enable

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TombLib.WPF;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// A custom WPF timeline control that displays draggable camera keyframes.
/// Supports zoom via mouse wheel, panning, full-zoom-out via double-click, cursor line,
/// and range selection by dragging on empty space.
/// </summary>
public partial class FlybyTimelineControl : Control
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
    private readonly Dictionary<string, FormattedText> _rulerTextCache = [];
    private double _rulerTextPixelsPerDip = -1.0f;

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
        _peakSpeed = cache?.PeakSpeed ?? 0.0f;

        StopSmoothViewport(false);
        NormalizeVisibleViewport();

        InvalidateVisual();
    }
}
