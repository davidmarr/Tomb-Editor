using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using TombLib;
using TombLib.Graphics;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Handles camera preview for flyby sequences. All frame interpolation is backed
/// by a pre-calculated <see cref="FlybySequenceCache"/>; real-time playback simply
/// advances a wall-clock timer and samples the cache.
/// </summary>
public class FlybyPreview
{
	public struct FrameState
	{
		public Vector3 Position;
		public float RotationY;
		public float RotationX;
		public float Roll;
		public float Fov;
		public bool Finished;
	}

	private readonly FlybySequenceCache _cache;
	private readonly Stopwatch _stopwatch = new();
	private Timer _sequenceTimer;
	private float _startTimeOffset;

	public bool IsFinished { get; private set; }
	public FrameState LastFrame { get; private set; }
	public Camera SavedCamera { get; }
	public FrameState? StaticFrame { get; private set; }
	public FlybySequenceCache Cache => _cache;

	/// <summary>
	/// Creates a sequence preview backed by a pre-calculated cache.
	/// </summary>
	public FlybyPreview(Level level, int sequence, Camera savedCamera)
	{
		SavedCamera = savedCamera;
		bool useSmoothPause = level.Settings.GameVersion == TRVersion.Game.TombEngine;

		var cameras = level.ExistingRooms
			.SelectMany(r => r.Objects.OfType<FlybyCameraInstance>())
			.Where(c => c.Sequence == sequence)
			.OrderBy(c => c.Number)
			.ToList();

		_cache = new FlybySequenceCache(cameras, useSmoothPause);
		IsFinished = !_cache.IsValid;

		if (_cache.IsValid)
			LastFrame = _cache.SampleAtTime(0);
	}

	/// <summary>
	/// Creates a sequence preview from an existing cache (avoids re-computation).
	/// </summary>
	public FlybyPreview(FlybySequenceCache cache, Camera savedCamera)
	{
		SavedCamera = savedCamera;
		_cache = cache;
		IsFinished = !_cache.IsValid;

		if (_cache.IsValid)
			LastFrame = _cache.SampleAtTime(0);
	}

	/// <summary>
	/// Creates a static preview session without sequence interpolation.
	/// </summary>
	public FlybyPreview(Camera savedCamera)
	{
		SavedCamera = savedCamera;
		IsFinished = true;
		_cache = new FlybySequenceCache(Array.Empty<FlybyCameraInstance>(), false);
	}

	/// <summary>
	/// Starts playback with a WinForms timer (used by Panel3D's ESC-based preview).
	/// </summary>
	public void BeginSequence(EventHandler timerTick)
	{
		_stopwatch.Restart();
		_startTimeOffset = 0;
		_sequenceTimer = new Timer { Interval = 16 };
		_sequenceTimer.Tick += timerTick;
		_sequenceTimer.Start();
	}

	/// <summary>
	/// Starts the internal stopwatch for external update ticking.
	/// </summary>
	public void BeginExternalUpdate(float startTimeOffset = 0)
	{
		_stopwatch.Restart();
		_startTimeOffset = startTimeOffset;
	}

	/// <summary>
	/// Advances playback by wall-clock delta and returns the current frame.
	/// </summary>
	public FrameState Update()
	{
		if (IsFinished || !_cache.IsValid)
			return new FrameState { Finished = true };

		float playbackElapsed = _startTimeOffset + (float)_stopwatch.Elapsed.TotalSeconds;
		float timelineTime = _cache.PlaybackToTimelineTime(playbackElapsed);

		if (timelineTime >= _cache.TotalDuration)
		{
			IsFinished = true;
			LastFrame = _cache.SampleAtTime(_cache.TotalDuration);
			LastFrame = new FrameState
			{
				Position = LastFrame.Position,
				RotationY = LastFrame.RotationY,
				RotationX = LastFrame.RotationX,
				Roll = LastFrame.Roll,
				Fov = LastFrame.Fov,
				Finished = true
			};
			return LastFrame;
		}

		LastFrame = _cache.SampleAtTime(timelineTime);
		return LastFrame;
	}

	/// <summary>
	/// Returns the current playback time in seconds.
	/// </summary>
	public float GetCurrentTimeSeconds()
	{
		if (!_cache.IsValid)
			return 0;

		float playbackElapsed = _startTimeOffset + (float)_stopwatch.Elapsed.TotalSeconds;
		float timelineTime = _cache.PlaybackToTimelineTime(playbackElapsed);
		return Math.Min(timelineTime, _cache.TotalDuration);
	}

	public void Stop()
	{
		_stopwatch.Stop();
		_sequenceTimer?.Stop();
		IsFinished = true;
	}

	public void Dispose()
	{
		Stop();
		_sequenceTimer?.Dispose();
	}

	#region Static frame helpers

	/// <summary>
	/// Computes a single-camera FrameState from a flyby camera's current properties.
	/// </summary>
	public static FrameState GetFrameForCamera(FlybyCameraInstance camera)
	{
		if (camera.Room == null)
			return default;

		var worldPos = camera.Position + camera.Room.WorldPos;

		return new FrameState
		{
			Position = worldPos,
			RotationY = MathC.DegToRad(camera.RotationY),
			RotationX = -MathC.DegToRad(camera.RotationX),
			Roll = MathC.DegToRad(camera.Roll),
			Fov = MathC.DegToRad(camera.Fov)
		};
	}

	/// <summary>
	/// Applies a frame state to the given camera, updating position, rotation, FOV and target.
	/// </summary>
	public static void ApplyFrame(Camera camera, FrameState frame)
	{
		camera.Position = frame.Position;
		camera.RotationY = frame.RotationY;
		camera.RotationX = frame.RotationX;
		camera.FieldOfView = frame.Fov;

		var rotation = Matrix4x4.CreateFromYawPitchRoll(frame.RotationY, frame.RotationX, 0);
		var look = MathC.HomogenousTransform(Vector3.UnitZ, rotation);
		camera.Target = frame.Position + Level.SectorSizeUnit * look;
	}

	/// <summary>
	/// Computes and sets the static frame for a flyby camera, then applies it to the given camera.
	/// </summary>
	public void SetFlybyStaticFrame(Camera camera, FlybyCameraInstance flybyCamera)
	{
		SetStaticFrame(camera, GetFrameForCamera(flybyCamera));
	}

	/// <summary>
	/// Sets an arbitrary frame as the static frame and applies it to the camera.
	/// </summary>
	public void SetStaticFrame(Camera camera, FrameState frame)
	{
		StaticFrame = frame;
		ApplyFrame(camera, frame);
	}

	/// <summary>
	/// Builds a view-projection matrix with roll support for the current preview frame.
	/// </summary>
	public Matrix4x4 BuildViewProjection(float width, float height, float defaultFov)
	{
		var frame = StaticFrame ?? LastFrame;

		var rotation = Matrix4x4.CreateFromYawPitchRoll(frame.RotationY, frame.RotationX, 0);
		var look = MathC.HomogenousTransform(Vector3.UnitZ, rotation);
		var right = MathC.HomogenousTransform(Vector3.UnitX, rotation);
		var up = Vector3.Cross(look, right);

		if (Math.Abs(frame.Roll) > 0.001f)
		{
			var rollMatrix = Matrix4x4.CreateFromAxisAngle(look, frame.Roll);
			up = Vector3.TransformNormal(up, rollMatrix);
		}

		var target = frame.Position + Level.SectorSizeUnit * look;
		float fov = frame.Fov > 0.01f ? frame.Fov : defaultFov;

		if (fov < 0.01f)
			fov = MathC.DegToRad(80);

		var view = MathC.Matrix4x4CreateLookAtLH(frame.Position, target, up);
		float aspectRatio = height != 0.0f ? width / height : 1.0f;
		var projection = MathC.Matrix4x4CreatePerspectiveFieldOfViewLH(fov, aspectRatio, 20.0f, 1000000.0f);

		return view * projection;
	}

	#endregion Static frame helpers
}
