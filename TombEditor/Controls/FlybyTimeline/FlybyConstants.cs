namespace TombEditor.Controls.FlybyTimeline
{
	public static class FlybyConstants
	{
		public const float GameTickRate = 30.0f;
		public const float SpeedScale = ushort.MaxValue / 100 * GameTickRate / ushort.MaxValue;

		public const int FlagCameraCut = 1 << 7;
		public const int FlagFreezeCamera = 1 << 8;

		public const float PreviewTimerInterval = 16;

		public const float TimelineZoomOutScale = 1.05f;
	}
}
