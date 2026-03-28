using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

public static class FlybyConstants
{
    // The game logic runs at 30 ticks per second.
    public const float TickRate = 30.0f;
    public const float TimeStep = 1.0f / TickRate;
    public const float SpeedScale = TickRate / 100.0f;

    // Native camera flags.
    public const int FlagCameraCut = 1 << 7;
    public const int FlagFreezeCamera = 1 << 8;

    // Spline constants.
    public const float FreezeEaseDistance = 0.15f;
    public const float MinSpeed = 0.001f;
    public const float MaxSpeed = 65535.0f / 655.0f;

    // Preview constants.
    public const int PreviewTimerInterval = 16;

    // Timeline constants.
    public const float TimelineZoomOutScale = 1.05f;
    public const float TimelinePanStepFraction = 0.05f;
    public const bool TimelineSmoothPanEnabled = true;
    public const bool TimelineSmoothZoomEnabled = true;
    public const int TimelineSmoothViewportTimerInterval = 8;
    public const float TimelineSmoothViewportLerpFactor = 0.3f;
    public const float TimelineSmoothViewportEpsilon = 0.001f;
    public const float TimelineMarkerRadius = 6.375f;
    public const float TimelineMinTickSpacing = 40.0f;
    public const float TimelineRulerHeight = 20.0f;

    // Distance from camera to target point, matching the level compiler.
    public const float TargetDistance = Level.SectorSizeUnit;
}
