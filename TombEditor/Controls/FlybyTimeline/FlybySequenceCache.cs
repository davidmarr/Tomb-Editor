#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using TombLib;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// <para>
/// Pre-calculates a flyby sequence into a frame array at fixed time resolution.
/// Both timeline scrubbing and playback sample from this cache via linear interpolation,
/// eliminating real-time state tracking for freeze, cut, and smooth pause flags.
/// </para>
/// <para>
/// Sequence timing analysis, spline playback timeline, and cut regions come from
/// <see cref="FlybySequenceTiming"/>. The cache only evaluates spline channels in
/// parallel and post-processes the resulting frames for interpolation.
/// </para>
/// </summary>
public sealed class FlybySequenceCache
{
    private const float InvalidSpeed = -1.0f;

    /// <summary>
    /// Stores one cached flyby frame ready for interpolation.
    /// </summary>
    private struct CachedFrame
    {
        public Vector3 Position;
        public float RotationY;
        public float RotationX;
        public float Roll;
        public float Fov;
    }

    /// <summary>
    /// Stores the non-null camera data needed to build spline knots.
    /// </summary>
    private readonly struct CameraSplineData
    {
        public Vector3 WorldPosition { get; init; }
        public float RotationY { get; init; }
        public float RotationX { get; init; }
        public float Roll { get; init; }
        public float Fov { get; init; }
    }

    private readonly CachedFrame[] _frames;
    private readonly FlybyCutRegion[] _cutRegions;
    private readonly float[] _smoothedSpeeds;

    /// <summary>
    /// Gets the analyzed timing data used to build this cache.
    /// </summary>
    public FlybySequenceTiming Timing { get; }

    /// <summary>
    /// Gets the total duration of the cached sequence in seconds.
    /// </summary>
    public float TotalDuration => Timing.TotalDuration;

    /// <summary>
    /// Gets whether the cache contains enough data for interpolation.
    /// </summary>
    public bool IsValid => _frames.Length > 0;

    /// <summary>
    /// Gets the peak precomputed speed used to normalize the timeline speed curve.
    /// </summary>
    public float PeakSpeed { get; }

    /// <summary>
    /// Builds a new sequence cache for the provided cameras.
    /// </summary>
    /// <param name="cameras">The flyby cameras to cache.</param>
    /// <param name="useSmoothPause">Whether TombEngine smooth-pause behavior should be applied.</param>
    public FlybySequenceCache(IReadOnlyList<FlybyCameraInstance> cameras, bool useSmoothPause)
    {
        var validCameras = new List<FlybyCameraInstance>(cameras.Count);
        var splineData = new List<CameraSplineData>(cameras.Count);

        foreach (var camera in cameras)
        {
            var room = camera.Room;

            if (room is null)
                continue;

            validCameras.Add(camera);
            splineData.Add(new CameraSplineData
            {
                WorldPosition = camera.Position + room.WorldPos,
                RotationY = camera.RotationY,
                RotationX = camera.RotationX,
                Roll = camera.Roll,
                Fov = camera.Fov
            });
        }

        Timing = FlybySequenceTiming.Build(validCameras, useSmoothPause);

        if (validCameras.Count < 2)
        {
            _frames = [];
            _cutRegions = [];
            _smoothedSpeeds = [];
            PeakSpeed = 0.0f;
            return;
        }

        // Build Catmull-Rom knot arrays for position, look target, roll, and FOV.
        BuildKnotArrays(splineData,
            out float[] posX, out float[] posY, out float[] posZ,
            out float[] tgtX, out float[] tgtY, out float[] tgtZ,
            out float[] rollKnots, out float[] fovKnots);

        int numCameras = validCameras.Count;
        int numSegments = numCameras - 1;

        // Precomputed spline timeline and cut regions come from FlybySequenceTiming.
        float[] splineParams = [.. Timing.SplineTimeline];

        _cutRegions = [.. Timing.CutRegions];

        // Evaluate all spline channels in parallel.
        _frames = EvaluateFramesParallel(splineParams, posX, posY, posZ, tgtX, tgtY, tgtZ, rollKnots, fovKnots, numSegments);

        // Unwrap yaw/pitch/roll to prevent discontinuities from atan2 wrapping.
        UnwrapFrameAngles(_frames);

        // Pre-compute smoothed speed curve.
        _smoothedSpeeds = ComputeSmoothedSpeeds();
        PeakSpeed = ComputePeakSpeed(_smoothedSpeeds);
    }

    /// <summary>
    /// Samples the cache at a given time, linearly interpolating between adjacent frames.
    /// Interpolation is suppressed at cut region boundaries to prevent blending between
    /// valid sequence frames and dummy still frames filling the cut gap.
    /// </summary>
    /// <param name="timeSeconds">The timeline time, in seconds, to sample.</param>
    /// <returns>The sampled preview frame, or a finished frame when the cache is invalid.</returns>
    public FlybyPreview.FrameState SampleAtTime(float timeSeconds)
    {
        if (!IsValid)
            return new FlybyPreview.FrameState { Finished = true };

        if (float.IsNaN(timeSeconds) || float.IsNegativeInfinity(timeSeconds))
            return FrameToState(_frames[0]);

        if (float.IsPositiveInfinity(timeSeconds))
            return FrameToState(_frames[^1]);

        if (timeSeconds <= 0.0f)
            return FrameToState(_frames[0]);

        float index = timeSeconds / FlybyConstants.TimeStep;
        int frameIndex = (int)MathF.Floor(index);
        float interpolationFactor = index - frameIndex;

        if (frameIndex >= _frames.Length - 1)
            return FrameToState(_frames[^1]);

        // Suppress interpolation across cut region boundaries.
        if (interpolationFactor > 0.0f && IsAtCutBoundary(frameIndex))
            return FrameToState(_frames[IsInsideCutRegion(frameIndex) ? frameIndex + 1 : frameIndex]);

        return LerpFrames(_frames[frameIndex], _frames[frameIndex + 1], interpolationFactor);
    }

    /// <summary>
    /// Maps wall-clock playback time to timeline time, skipping over cut regions.
    /// </summary>
    /// <param name="playbackTime">The wall-clock playback time, in seconds.</param>
    /// <returns>The corresponding timeline time, in seconds.</returns>
    public float PlaybackToTimelineTime(float playbackTime)
    {
        float accumulatedCutTime = 0;

        foreach (var cut in _cutRegions)
        {
            float cutPlaybackStart = cut.StartTime - accumulatedCutTime;

            if (playbackTime < cutPlaybackStart)
                break;

            accumulatedCutTime += cut.EndTime - cut.StartTime;
        }

        return playbackTime + accumulatedCutTime;
    }

    /// <summary>
    /// Maps timeline time to wall-clock playback time (inverse of <see cref="PlaybackToTimelineTime"/>).
    /// </summary>
    /// <param name="timelineTime">The timeline time, in seconds.</param>
    /// <returns>The corresponding wall-clock playback time, in seconds.</returns>
    public float TimelineToPlaybackTime(float timelineTime)
    {
        float accumulatedCutTime = 0;

        foreach (var cut in _cutRegions)
        {
            if (timelineTime <= cut.StartTime)
                break;

            if (timelineTime < cut.EndTime)
                return cut.StartTime - accumulatedCutTime;

            accumulatedCutTime += cut.EndTime - cut.StartTime;
        }

        return timelineTime - accumulatedCutTime;
    }

    /// <summary>
    /// Returns the pre-computed smoothed speed (world units per second) at the given timeline time.
    /// Returns <c>-1.0f</c> if the time is outside the sequence, within a cut region,
    /// or the cache is not valid.
    /// </summary>
    /// <param name="timeSeconds">The timeline time, in seconds, to evaluate.</param>
    /// <returns>The smoothed speed in world units per second, or <c>-1.0f</c> when no speed should be shown.</returns>
    public float GetSpeedAtTime(float timeSeconds)
    {
        if (_smoothedSpeeds.Length == 0)
            return InvalidSpeed;

        if (!float.IsFinite(timeSeconds))
            return InvalidSpeed;

        if (timeSeconds < 0.0f || timeSeconds > TotalDuration)
            return InvalidSpeed;

        // Skip cut regions.
        foreach (var cut in _cutRegions)
        {
            if (timeSeconds >= cut.StartTime && timeSeconds < cut.EndTime)
                return InvalidSpeed;
        }

        float index = timeSeconds / FlybyConstants.TimeStep;
        int i0 = Math.Clamp((int)index, 0, _smoothedSpeeds.Length - 1);
        int i1 = Math.Min(i0 + 1, _smoothedSpeeds.Length - 1);
        float frac = index - (int)index;

        return _smoothedSpeeds[i0] + ((_smoothedSpeeds[i1] - _smoothedSpeeds[i0]) * frac);
    }

    /// <summary>
    /// Converts two cached frames into an interpolated preview frame.
    /// </summary>
    private static FlybyPreview.FrameState LerpFrames(CachedFrame a, CachedFrame b, float t) => new()
    {
        Position = Vector3.Lerp(a.Position, b.Position, t),
        RotationY = a.RotationY + ((b.RotationY - a.RotationY) * t),
        RotationX = a.RotationX + ((b.RotationX - a.RotationX) * t),
        Roll = a.Roll + ((b.Roll - a.Roll) * t),
        Fov = a.Fov + ((b.Fov - a.Fov) * t),
        Finished = false
    };

    /// <summary>
    /// Converts a cached frame into a preview frame state.
    /// </summary>
    private static FlybyPreview.FrameState FrameToState(CachedFrame frame) => new()
    {
        Position = frame.Position,
        RotationY = frame.RotationY,
        RotationX = frame.RotationX,
        Roll = frame.Roll,
        Fov = frame.Fov,
        Finished = false
    };

    /// <summary>
    /// Returns whether the given frame index lies inside a cut region.
    /// </summary>
    private bool IsInsideCutRegion(int frameIndex)
    {
        float time = frameIndex * FlybyConstants.TimeStep;

        foreach (var cut in _cutRegions)
        {
            if (time >= cut.StartTime && time < cut.EndTime)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether a frame pair crosses a cut boundary.
    /// </summary>
    private bool IsAtCutBoundary(int frameIndex)
    {
        float t0 = frameIndex * FlybyConstants.TimeStep;
        float t1 = (frameIndex + 1) * FlybyConstants.TimeStep;

        foreach (var cut in _cutRegions)
        {
            // Interpolation pair straddles the cut start or cut end.
            if (t0 < cut.StartTime && t1 >= cut.StartTime)
                return true;

            if (t0 < cut.EndTime && t1 >= cut.EndTime)
                return true;
        }

        return false;
    }

    #region Parallel spline evaluation

    /// <summary>
    /// Builds all cached frames by evaluating spline parameters in parallel.
    /// </summary>
    /// <param name="splineParams">Playback spline parameters sampled at fixed timeline steps.</param>
    /// <param name="posX">Padded X-position spline knots.</param>
    /// <param name="posY">Padded Y-position spline knots.</param>
    /// <param name="posZ">Padded Z-position spline knots.</param>
    /// <param name="tgtX">Padded X target-position spline knots.</param>
    /// <param name="tgtY">Padded Y target-position spline knots.</param>
    /// <param name="tgtZ">Padded Z target-position spline knots.</param>
    /// <param name="rollKnots">Padded roll spline knots in degrees.</param>
    /// <param name="fovKnots">Padded field-of-view spline knots in degrees.</param>
    /// <param name="numSegments">Number of spline segments in the sequence.</param>
    /// <returns>The fully evaluated cached frame array.</returns>
    private static CachedFrame[] EvaluateFramesParallel(
        float[] splineParams,
        float[] posX, float[] posY, float[] posZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        float[] rollKnots, float[] fovKnots,
        int numSegments)
    {
        int count = splineParams.Length;
        var result = new CachedFrame[count];

        Parallel.For(0, count, i =>
        {
            result[i] = EvaluateSplineFrame(splineParams[i], posX, posY, posZ,
                tgtX, tgtY, tgtZ, rollKnots, fovKnots, numSegments);
        });

        return result;
    }

    /// <summary>
    /// Evaluates all spline channels at parameter t and converts to a CachedFrame.
    /// Pure function of the knot arrays, safe for parallel invocation.
    /// </summary>
    /// <param name="t">Spline parameter to evaluate.</param>
    /// <param name="posX">Padded X-position spline knots.</param>
    /// <param name="posY">Padded Y-position spline knots.</param>
    /// <param name="posZ">Padded Z-position spline knots.</param>
    /// <param name="tgtX">Padded X target-position spline knots.</param>
    /// <param name="tgtY">Padded Y target-position spline knots.</param>
    /// <param name="tgtZ">Padded Z target-position spline knots.</param>
    /// <param name="rollKnots">Padded roll spline knots in degrees.</param>
    /// <param name="fovKnots">Padded field-of-view spline knots in degrees.</param>
    /// <param name="numSegments">Number of spline segments in the sequence.</param>
    /// <returns>The evaluated cached frame for the requested spline position.</returns>
    private static CachedFrame EvaluateSplineFrame(
        float t,
        float[] posX, float[] posY, float[] posZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        float[] rollKnots, float[] fovKnots,
        int numSegments)
    {
        t = Math.Clamp(t, 0.0f, numSegments);

        float px = CatmullRomSpline.Evaluate(t, posX);
        float py = CatmullRomSpline.Evaluate(t, posY);
        float pz = CatmullRomSpline.Evaluate(t, posZ);
        float tx = CatmullRomSpline.Evaluate(t, tgtX);
        float ty = CatmullRomSpline.Evaluate(t, tgtY);
        float tz = CatmullRomSpline.Evaluate(t, tgtZ);
        float roll = CatmullRomSpline.Evaluate(t, rollKnots);
        float fov = CatmullRomSpline.Evaluate(t, fovKnots);

        float dx = tx - px;
        float dy = ty - py;
        float dz = tz - pz;
        float horizontalDist = MathF.Sqrt((dx * dx) + (dz * dz));

        float yaw = 0.0f;
        float pitch = 0.0f;

        if (horizontalDist > FlybyConstants.RotationSolveDistanceEpsilon || MathF.Abs(dy) > FlybyConstants.RotationSolveDistanceEpsilon)
        {
            yaw = MathF.Atan2(dx, dz);
            pitch = MathF.Atan2(-dy, horizontalDist);
        }

        return new CachedFrame
        {
            Position = new Vector3(px, py, pz),
            RotationY = yaw,
            RotationX = pitch,
            Roll = MathC.DegToRad(roll),
            Fov = MathC.DegToRad(fov)
        };
    }

    #endregion Parallel spline evaluation

    #region Knot building

    /// <summary>
    /// Builds all spline knot arrays needed for cache frame evaluation.
    /// </summary>
    /// <param name="splineData">Ordered camera samples that define the sequence.</param>
    /// <param name="posX">Receives padded X-position spline knots.</param>
    /// <param name="posY">Receives padded Y-position spline knots.</param>
    /// <param name="posZ">Receives padded Z-position spline knots.</param>
    /// <param name="tgtX">Receives padded X target-position spline knots.</param>
    /// <param name="tgtY">Receives padded Y target-position spline knots.</param>
    /// <param name="tgtZ">Receives padded Z target-position spline knots.</param>
    /// <param name="rollKnots">Receives padded roll spline knots in degrees.</param>
    /// <param name="fovKnots">Receives padded field-of-view spline knots in degrees.</param>
    private static void BuildKnotArrays(
        IReadOnlyList<CameraSplineData> splineData,
        out float[] posX, out float[] posY, out float[] posZ,
        out float[] tgtX, out float[] tgtY, out float[] tgtZ,
        out float[] rollKnots, out float[] fovKnots)
    {
        int cameraCount = splineData.Count;

        var rawPosX = new float[cameraCount];
        var rawPosY = new float[cameraCount];
        var rawPosZ = new float[cameraCount];
        var rawTgtX = new float[cameraCount];
        var rawTgtY = new float[cameraCount];
        var rawTgtZ = new float[cameraCount];
        var rawRoll = new float[cameraCount];
        var rawFov = new float[cameraCount];

        for (int i = 0; i < cameraCount; i++)
        {
            var camera = splineData[i];

            float yawRad = MathC.DegToRad(camera.RotationY);
            float pitchRad = MathC.DegToRad(camera.RotationX);
            float cosPitch = MathF.Cos(pitchRad);

            rawPosX[i] = camera.WorldPosition.X;
            rawPosY[i] = camera.WorldPosition.Y;
            rawPosZ[i] = camera.WorldPosition.Z;
            rawTgtX[i] = camera.WorldPosition.X + (FlybyConstants.TargetDistance * cosPitch * MathF.Sin(yawRad));
            rawTgtY[i] = camera.WorldPosition.Y + (FlybyConstants.TargetDistance * MathF.Sin(pitchRad));
            rawTgtZ[i] = camera.WorldPosition.Z + (FlybyConstants.TargetDistance * cosPitch * MathF.Cos(yawRad));
            rawRoll[i] = camera.Roll;
            rawFov[i] = camera.Fov;
        }

        UnwrapAngles(rawRoll);

        posX = CatmullRomSpline.PadKnots(rawPosX);
        posY = CatmullRomSpline.PadKnots(rawPosY);
        posZ = CatmullRomSpline.PadKnots(rawPosZ);
        tgtX = CatmullRomSpline.PadKnots(rawTgtX);
        tgtY = CatmullRomSpline.PadKnots(rawTgtY);
        tgtZ = CatmullRomSpline.PadKnots(rawTgtZ);
        rollKnots = CatmullRomSpline.PadKnots(rawRoll);
        fovKnots = CatmullRomSpline.PadKnots(rawFov);
    }

    /// <summary>
    /// Unwraps degree-based angle knots so spline interpolation stays continuous.
    /// </summary>
    private static void UnwrapAngles(float[] angles)
    {
        for (int i = 1; i < angles.Length; i++)
        {
            float delta = angles[i] - angles[i - 1];
            angles[i] -= MathF.Round(delta / 360.0f) * 360.0f;
        }
    }

    /// <summary>
    /// Unwraps RotationY, RotationX and Roll across consecutive cached frames to prevent
    /// discontinuities from atan2 wrapping at the +/-pi boundary.
    /// </summary>
    private static void UnwrapFrameAngles(CachedFrame[] frames)
    {
        for (int i = 1; i < frames.Length; i++)
        {
            float dyaw = frames[i].RotationY - frames[i - 1].RotationY;
            frames[i].RotationY -= MathF.Round(dyaw / MathC.TwoPi) * MathC.TwoPi;

            float dpitch = frames[i].RotationX - frames[i - 1].RotationX;
            frames[i].RotationX -= MathF.Round(dpitch / MathC.TwoPi) * MathC.TwoPi;

            float droll = frames[i].Roll - frames[i - 1].Roll;
            frames[i].Roll -= MathF.Round(droll / MathC.TwoPi) * MathC.TwoPi;
        }
    }

    #endregion Knot building

    #region Speed smoothing

    /// <summary>
    /// Pre-computes a smoothed speed curve from position deltas between consecutive frames.
    /// Uses three passes of box averaging to produce a visually smooth graph.
    /// </summary>
    private float[] ComputeSmoothedSpeeds()
    {
        if (_frames.Length < 2)
            return [];

        int count = _frames.Length - 1;
        var speeds = new float[count];
        var buffer = new float[count];

        for (int i = 0; i < count; i++)
        {
            if (IsInsideCutRegion(i) || IsInsideCutRegion(i + 1))
            {
                speeds[i] = 0.0f;
            }
            else
            {
                var delta = _frames[i + 1].Position - _frames[i].Position;
                speeds[i] = delta.Length() / FlybyConstants.TimeStep;
            }
        }

        // Three passes of box smoothing approximate a Gaussian filter.
        for (int pass = 0; pass < 3; pass++)
        {
            BoxSmooth(speeds, buffer, 5);
            (speeds, buffer) = (buffer, speeds);
        }

        return speeds;
    }

    /// <summary>
    /// Applies one pass of box smoothing to the provided data.
    /// </summary>
    private static void BoxSmooth(float[] source, float[] destination, int radius)
    {
        int len = source.Length;

        for (int i = 0; i < len; i++)
        {
            float sum = 0;
            int low = Math.Max(0, i - radius);
            int high = Math.Min(len - 1, i + radius);

            for (int j = low; j <= high; j++)
                sum += source[j];

            destination[i] = sum / (high - low + 1);
        }
    }

    /// <summary>
    /// Returns the maximum valid speed value contained in the provided array.
    /// </summary>
    private static float ComputePeakSpeed(float[] speeds)
    {
        float peak = 0.0f;

        for (int i = 0; i < speeds.Length; i++)
        {
            float speed = speeds[i];

            if (float.IsFinite(speed) && speed > peak)
                peak = speed;
        }

        return peak;
    }

    #endregion Speed smoothing
}
