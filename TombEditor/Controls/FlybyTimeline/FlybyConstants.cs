using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline
{
	public static class FlybyConstants
	{
		// The game logic runs at 30 ticks per second.
		public const float TickRate = 30.0f;
		public const float TimeStep = 1.0f / TickRate;
		public const float SpeedScale = ushort.MaxValue / 100 * TickRate / ushort.MaxValue;

		// Native camera flags.
		public const int FlagCameraCut = 1 << 7;
		public const int FlagFreezeCamera = 1 << 8;

		// Spline constants.
		public const float FreezeEaseDistance = 0.15f;
		public const float MinSpeed = 0.001f;

		// Preview constants.
		public const float PreviewTimerInterval = 16;

		// Timeline constants.
		public const float TimelineZoomOutScale = 1.05f;
		public const float TimelineMarkerRadius = 6.375f;
		public const float TimelineMinTickSpacing = 40.0f;
		public const float TimelineRulerHeight = 20.0f;

		// Distance from camera to target point, matching the level compiler.
		public const float TargetDistance = Level.SectorSizeUnit;
	}
}
