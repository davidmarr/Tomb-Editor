#nullable enable

using System;
using System.Diagnostics;
using System.Numerics;
using TombLib;
using TombLib.Graphics;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Handles camera preview for flyby sequences. All frame interpolation is backed
/// by a pre-calculated <see cref="FlybySequenceCache"/>; real-time playback simply
/// advances a wall-clock timer and samples the cache.
/// </summary>
public sealed class FlybyPreview : IDisposable
{
    /// <summary>
    /// Stores the camera state for a sampled preview frame.
    /// </summary>
    public struct FrameState
    {
        public Vector3 Position;
        public float RotationY;
        public float RotationX;
        public float Roll;
        public float Fov;
        public bool Finished;
    }

    private readonly Stopwatch _stopwatch = new();
    private float _startTimeOffset;

    /// <summary>
    /// Gets the sequence cache used for playback and scrubbing.
    /// </summary>
    public FlybySequenceCache Cache { get; }

    /// <summary>
    /// Gets whether playback has reached the end of the sequence.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Gets the last sampled playback frame.
    /// </summary>
    public FrameState LastFrame { get; private set; }

    /// <summary>
    /// Gets the static preview frame when preview is pinned to one frame.
    /// </summary>
    public FrameState? StaticFrame { get; private set; }

    /// <summary>
    /// Gets or sets the camera state that should be restored after preview ends.
    /// </summary>
    public Camera SavedCamera { get; set; }

    /// <summary>
    /// Creates a sequence preview backed by a pre-calculated cache.
    /// </summary>
    /// <param name="level">The level containing the flyby sequence.</param>
    /// <param name="sequence">The flyby sequence index to preview.</param>
    /// <param name="savedCamera">The camera state to restore after preview ends.</param>
    public FlybyPreview(Level level, int sequence, Camera savedCamera)
        : this(
            new FlybySequenceCache(
                FlybySequenceHelper.GetCameras(level, sequence),
                useSmoothPause: level.Settings.GameVersion == TRVersion.Game.TombEngine),
            savedCamera)
    { }

    /// <summary>
    /// Creates a sequence preview from an existing cache (avoids re-computation).
    /// </summary>
    /// <param name="cache">The pre-calculated sequence cache to sample from.</param>
    /// <param name="savedCamera">The camera state to restore after preview ends.</param>
    public FlybyPreview(FlybySequenceCache cache, Camera savedCamera)
    {
        SavedCamera = savedCamera;
        Cache = cache;
        IsFinished = !Cache.IsValid;

        if (Cache.IsValid)
            LastFrame = Cache.SampleAtTime(0);
    }

    /// <summary>
    /// Creates a static preview session without sequence interpolation.
    /// </summary>
    /// <param name="savedCamera">The camera state to restore after preview ends.</param>
    public FlybyPreview(Camera savedCamera)
    {
        SavedCamera = savedCamera;
        IsFinished = true;
        Cache = new FlybySequenceCache([], false);
    }

    /// <summary>
    /// Starts the internal stopwatch for external update ticking.
    /// </summary>
    /// <param name="startTimeOffset">The playback-time offset, in seconds, from which preview should start.</param>
    public void BeginExternalUpdate(float startTimeOffset = 0)
    {
        if (!float.IsFinite(startTimeOffset))
            startTimeOffset = 0.0f;

        _stopwatch.Restart();
        _startTimeOffset = startTimeOffset;
        IsFinished = !Cache.IsValid;
        StaticFrame = null;

        if (IsFinished)
            return;

        float timelineTime = Math.Min(Cache.PlaybackToTimelineTime(_startTimeOffset), Cache.TotalDuration);

        if (timelineTime >= Cache.TotalDuration)
        {
            IsFinished = true;
            LastFrame = SampleFinishedFrame();
            return;
        }

        LastFrame = Cache.SampleAtTime(timelineTime);
    }

    /// <summary>
    /// Advances playback by wall-clock delta and returns the current frame.
    /// </summary>
    public FrameState Update()
    {
        if (IsFinished || !Cache.IsValid)
            return new FrameState { Finished = true };

        float timelineTime = GetTimelineTimeSeconds();

        if (!float.IsFinite(timelineTime))
        {
            IsFinished = true;
            LastFrame = SampleFinishedFrame();
            return LastFrame;
        }

        if (timelineTime >= Cache.TotalDuration)
        {
            IsFinished = true;
            LastFrame = SampleFinishedFrame();
            return LastFrame;
        }

        LastFrame = Cache.SampleAtTime(timelineTime);
        return LastFrame;
    }

    /// <summary>
    /// Returns the current playback time in seconds.
    /// </summary>
    public float GetCurrentTimeSeconds()
    {
        if (!Cache.IsValid)
            return 0.0f;

        float timelineTime = GetTimelineTimeSeconds();

        if (!float.IsFinite(timelineTime))
            return 0.0f;

        return Math.Min(timelineTime, Cache.TotalDuration);
    }

    /// <summary>
    /// Stops playback and marks this preview as finished.
    /// </summary>
    public void Dispose() => Stop();

    private void Stop()
    {
        _stopwatch.Stop();
        IsFinished = true;
    }

    private FrameState SampleFinishedFrame()
    {
        var frame = Cache.SampleAtTime(Cache.TotalDuration);
        frame.Finished = true;
        return frame;
    }

    /// <summary>
    /// Converts the current stopwatch time into timeline time.
    /// </summary>
    private float GetTimelineTimeSeconds()
    {
        float playbackElapsed = _startTimeOffset + (float)_stopwatch.Elapsed.TotalSeconds;

        if (!float.IsFinite(playbackElapsed))
            return 0.0f;

        return Cache.PlaybackToTimelineTime(playbackElapsed);
    }

    #region Static frame helpers

    /// <summary>
    /// Computes a single-camera FrameState from a flyby camera's current properties.
    /// </summary>
    /// <param name="camera">The flyby camera instance to sample.</param>
    /// <returns>The sampled frame, or <see langword="default"/> when the flyby camera is not assigned to a room.</returns>
    public static FrameState GetFrameForCamera(FlybyCameraInstance camera)
    {
        if (camera.Room is null)
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
    /// <param name="camera">The preview camera to update.</param>
    /// <param name="frame">The frame state to apply.</param>
    public static void ApplyFrame(Camera camera, FrameState frame)
    {
        camera.Position = frame.Position;
        camera.RotationY = frame.RotationY;
        camera.RotationX = frame.RotationX;
        camera.FieldOfView = frame.Fov;

        var rotation = CreateFrameRotation(frame);
        var look = MathC.HomogenousTransform(Vector3.UnitZ, rotation);
        camera.Target = frame.Position + (Level.SectorSizeUnit * look);
    }

    /// <summary>
    /// Sets an arbitrary frame as the static frame and applies it to the camera.
    /// Used for pinned preview updates such as flyby form edits and timeline scrubbing.
    /// </summary>
    /// <param name="camera">The preview camera to update.</param>
    /// <param name="frame">The frame state to pin as the static preview frame.</param>
    public void SetStaticFrame(Camera camera, FrameState frame)
    {
        StaticFrame = frame;
        ApplyFrame(camera, frame);
    }

    /// <summary>
    /// Builds a view-projection matrix with roll support for the current preview frame.
    /// </summary>
    /// <param name="width">The viewport width in pixels.</param>
    /// <param name="height">The viewport height in pixels.</param>
    /// <param name="defaultFov">The fallback field of view, in radians, used when the frame FOV is not valid.</param>
    /// <returns>The combined view-projection matrix for the active preview frame.</returns>
    public Matrix4x4 BuildViewProjection(float width, float height, float defaultFov)
    {
        var frame = StaticFrame ?? LastFrame;

        var rotation = CreateFrameRotation(frame);
        var look = MathC.HomogenousTransform(Vector3.UnitZ, rotation);
        var right = MathC.HomogenousTransform(Vector3.UnitX, rotation);
        var up = Vector3.Cross(look, right);

        if (MathF.Abs(frame.Roll) > MathC.Epsilon)
        {
            var rollMatrix = Matrix4x4.CreateFromAxisAngle(look, frame.Roll);
            up = Vector3.TransformNormal(up, rollMatrix);
        }

        var target = frame.Position + (Level.SectorSizeUnit * look);
        float fov = frame.Fov > FlybyConstants.PreviewMinFieldOfView ? frame.Fov : defaultFov;

        if (fov < FlybyConstants.PreviewMinFieldOfView)
            fov = MathC.DegToRad(80);

        var view = MathC.Matrix4x4CreateLookAtLH(frame.Position, target, up);
        float aspectRatio = height != 0.0f ? width / height : 1.0f;
        var projection = MathC.Matrix4x4CreatePerspectiveFieldOfViewLH(fov, aspectRatio, 20.0f, 1000000.0f);

        return view * projection;
    }

    /// <summary>
    /// Builds the yaw-pitch rotation matrix for a frame.
    /// </summary>
    private static Matrix4x4 CreateFrameRotation(FrameState frame)
        => Matrix4x4.CreateFromYawPitchRoll(frame.RotationY, frame.RotationX, 0);

    #endregion Static frame helpers
}
