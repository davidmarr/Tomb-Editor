using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using TombLib;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Pre-calculates a flyby sequence into a frame array at fixed time resolution.
/// Both timeline scrubbing and playback sample from this cache via linear interpolation,
/// eliminating real-time state tracking for freeze, cut, and smooth pause flags.
///
/// Sequence timing analysis, spline playback timeline, and cut regions come from
/// <see cref="FlybySequenceTiming"/>. The cache only evaluates spline channels in
/// parallel and post-processes the resulting frames for interpolation.
/// </summary>
public class FlybySequenceCache
{
    // Pre-calculated frame at a specific point in time.
    public struct CachedFrame
    {
        public Vector3 Position;
        public float RotationY;
        public float RotationX;
        public float Roll;
        public float Fov;
    }

    private readonly CachedFrame[] _frames;
    private readonly float _totalDuration;
    private readonly int _frameCount;
    private readonly FlybyCutRegion[] _cutRegions;
    private readonly float[] _smoothedSpeeds;

    public FlybySequenceTiming Timing { get; }
    public float TotalDuration => _totalDuration;
    public bool IsValid => _frameCount > 0;

    public FlybySequenceCache(IReadOnlyList<FlybyCameraInstance> cameras, bool useSmoothPause)
    {
        Timing = FlybySequenceTiming.Build(cameras, useSmoothPause);

        if (cameras.Count < 2)
        {
            _frames = Array.Empty<CachedFrame>();
            _cutRegions = Array.Empty<FlybyCutRegion>();
            _smoothedSpeeds = Array.Empty<float>();
            _totalDuration = 0;
            _frameCount = 0;
            return;
        }

        // Build Catmull-Rom knot arrays (includes speed as a spline channel).
        BuildKnotArrays(cameras,
            out float[] posX, out float[] posY, out float[] posZ,
            out float[] tgtX, out float[] tgtY, out float[] tgtZ,
            out float[] rollKnots, out float[] fovKnots, out float[] speedKnots);

        int numCameras = cameras.Count;
        int numSegments = numCameras - 1;

        // Precomputed spline timeline and cut regions come from FlybySequenceTiming.
        float[] splineParams = Timing.SplineTimeline;

        _cutRegions = Timing.CutRegions;

        // Evaluate all spline channels in parallel.
        _frames = EvaluateFramesParallel(splineParams, posX, posY, posZ, tgtX, tgtY, tgtZ, rollKnots, fovKnots, numSegments);

        // Unwrap yaw/pitch/roll to prevent discontinuities from atan2 wrapping.
        UnwrapFrameAngles(_frames);

        _frameCount = _frames.Length;
        _totalDuration = Timing.TotalDuration;

        // Pre-compute smoothed speed curve.
        _smoothedSpeeds = ComputeSmoothedSpeeds();
    }

    /// <summary>
    /// Samples the cache at a given time, linearly interpolating between adjacent frames.
    /// Interpolation is suppressed at cut region boundaries to prevent blending between
    /// valid sequence frames and dummy still frames filling the cut gap.
    /// </summary>
    public FlybyPreview.FrameState SampleAtTime(float timeSeconds)
    {
        if (_frameCount == 0)
            return new FlybyPreview.FrameState { Finished = true };

        float index = timeSeconds / FlybyConstants.TimeStep;
        int i0 = (int)index;
        float frac = index - i0;

        if (i0 < 0)
            return FrameToState(_frames[0]);

        if (i0 >= _frameCount - 1)
            return FrameToState(_frames[_frameCount - 1]);

        // Suppress interpolation across cut region boundaries.
        if (frac > 0 && IsAtCutBoundary(i0))
            return FrameToState(_frames[IsInsideCutRegion(i0) ? i0 + 1 : i0]);

        return LerpFrames(_frames[i0], _frames[i0 + 1], frac);
    }

    /// <summary>
    /// Samples the cache at a normalized progress value (0 to 1).
    /// </summary>
    public FlybyPreview.FrameState SampleAtProgress(float progress)
    {
        return SampleAtTime(Math.Clamp(progress, 0, 1) * _totalDuration);
    }

    /// <summary>
    /// Maps wall-clock playback time to timeline time, skipping over cut regions.
    /// </summary>
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
    /// Maps timeline time to wall-clock playback time (inverse of PlaybackToTimelineTime).
    /// </summary>
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
    /// Returns negative if the time is outside the sequence or within a cut region.
    /// </summary>
    public float GetSpeedAtTime(float timeSeconds)
    {
        if (_smoothedSpeeds.Length == 0)
            return -1;

        if (timeSeconds < 0 || timeSeconds > _totalDuration)
            return -1;

        // Skip cut regions.
        foreach (var cut in _cutRegions)
        {
            if (timeSeconds >= cut.StartTime && timeSeconds <= cut.EndTime)
                return -1;
        }

        float index = timeSeconds / FlybyConstants.TimeStep;
        int i0 = Math.Clamp((int)index, 0, _smoothedSpeeds.Length - 1);
        int i1 = Math.Min(i0 + 1, _smoothedSpeeds.Length - 1);
        float frac = index - (int)index;

        return _smoothedSpeeds[i0] + (_smoothedSpeeds[i1] - _smoothedSpeeds[i0]) * frac;
    }

    private static FlybyPreview.FrameState LerpFrames(CachedFrame a, CachedFrame b, float t)
    {
        return new FlybyPreview.FrameState
        {
            Position = Vector3.Lerp(a.Position, b.Position, t),
            RotationY = a.RotationY + (b.RotationY - a.RotationY) * t,
            RotationX = a.RotationX + (b.RotationX - a.RotationX) * t,
            Roll = a.Roll + (b.Roll - a.Roll) * t,
            Fov = a.Fov + (b.Fov - a.Fov) * t,
            Finished = false
        };
    }

    private static FlybyPreview.FrameState FrameToState(CachedFrame f)
    {
        return new FlybyPreview.FrameState
        {
            Position = f.Position,
            RotationY = f.RotationY,
            RotationX = f.RotationX,
            Roll = f.Roll,
            Fov = f.Fov,
            Finished = false
        };
    }

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

    #region Pass 2: parallel spline evaluation

    /// <summary>
    /// Evaluates all spline channels for the given parameter array in parallel.
    /// </summary>
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
    private static CachedFrame EvaluateSplineFrame(
        float t,
        float[] posX, float[] posY, float[] posZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        float[] rollKnots, float[] fovKnots,
        int numSegments)
    {
        t = Math.Clamp(t, 0, numSegments);

        float px   = CatmullRomSpline.Evaluate(t, posX);
        float py   = CatmullRomSpline.Evaluate(t, posY);
        float pz   = CatmullRomSpline.Evaluate(t, posZ);
        float tx   = CatmullRomSpline.Evaluate(t, tgtX);
        float ty   = CatmullRomSpline.Evaluate(t, tgtY);
        float tz   = CatmullRomSpline.Evaluate(t, tgtZ);
        float roll = CatmullRomSpline.Evaluate(t, rollKnots);
        float fov  = CatmullRomSpline.Evaluate(t, fovKnots);

        float dx = tx - px;
        float dy = ty - py;
        float dz = tz - pz;
        float horizontalDist = MathF.Sqrt(dx * dx + dz * dz);

        float yaw = 0, pitch = 0;

        if (horizontalDist > 0.001f || Math.Abs(dy) > 0.001f)
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

    #endregion Pass 2: parallel spline evaluation

    #region Knot building

    private static void BuildKnotArrays(
        IReadOnlyList<FlybyCameraInstance> cameras,
        out float[] posX, out float[] posY, out float[] posZ,
        out float[] tgtX, out float[] tgtY, out float[] tgtZ,
        out float[] rollKnots, out float[] fovKnots, out float[] speedKnots)
    {
        int n = cameras.Count;

        var rawPosX  = new float[n];
        var rawPosY  = new float[n];
        var rawPosZ  = new float[n];
        var rawTgtX  = new float[n];
        var rawTgtY  = new float[n];
        var rawTgtZ  = new float[n];
        var rawRoll  = new float[n];
        var rawFov   = new float[n];
        var rawSpeed = new float[n];

        for (int i = 0; i < n; i++)
        {
            var cam = cameras[i];
            var worldPos = cam.Position + cam.Room.WorldPos;

            float yawRad = MathC.DegToRad(cam.RotationY);
            float pitchRad = MathC.DegToRad(cam.RotationX);
            float cosPitch = MathF.Cos(pitchRad);

            rawPosX[i] = worldPos.X;
            rawPosY[i] = worldPos.Y;
            rawPosZ[i] = worldPos.Z;
            rawTgtX[i] = worldPos.X + FlybyConstants.TargetDistance * cosPitch * MathF.Sin(yawRad);
            rawTgtY[i] = worldPos.Y + FlybyConstants.TargetDistance * MathF.Sin(pitchRad);
            rawTgtZ[i] = worldPos.Z + FlybyConstants.TargetDistance * cosPitch * MathF.Cos(yawRad);
            rawRoll[i] = cam.Roll;
            rawFov[i]  = cam.Fov;
            rawSpeed[i] = Math.Max(cam.Speed, FlybyConstants.MinSpeed);
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
        speedKnots = CatmullRomSpline.PadKnots(rawSpeed);
    }

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
        if (_frameCount < 2)
            return Array.Empty<float>();

        int count = _frameCount - 1;
        var speeds = new float[count];

        for (int i = 0; i < count; i++)
        {
            if (IsInsideCutRegion(i) || IsInsideCutRegion(i + 1))
                speeds[i] = 0.0f;
            else
            {
                var delta = _frames[i + 1].Position - _frames[i].Position;
                speeds[i] = delta.Length() / FlybyConstants.TimeStep;
            }
        }

        // Three passes of box smoothing approximate a Gaussian filter.
        for (int pass = 0; pass < 3; pass++)
            speeds = BoxSmooth(speeds, 5);

        return speeds;
    }

    private static float[] BoxSmooth(float[] data, int radius)
    {
        int len = data.Length;
        var result = new float[len];

        for (int i = 0; i < len; i++)
        {
            float sum = 0;
            int low = Math.Max(0, i - radius);
            int high = Math.Min(len - 1, i + radius);

            for (int j = low; j <= high; j++)
                sum += data[j];

            result[i] = sum / (high - low + 1);
        }

        return result;
    }

    #endregion Speed smoothing
}
