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
/// Construction uses a three-pass approach: pass 1 sequentially resolves the spline
/// parameter for every time slot using spline-interpolated speed (handling freeze, cut,
/// smooth pause), then pass 2 evaluates all spline channels in parallel, and pass 3
/// unwraps frame angles for smooth interpolation.
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

    // Time range bypassed by a camera cut flag.
    public struct CutRegion
    {
        public float StartTime;
        public float EndTime;
    }

    private readonly CachedFrame[] _frames;
    private readonly float _totalDuration;
    private readonly int _frameCount;
    private readonly CutRegion[] _cutRegions;
    private readonly float[] _smoothedSpeeds;
    private readonly float[] _segmentSpeedScales;

    public float TotalDuration => _totalDuration;
    public bool IsValid => _frameCount > 0;

    public FlybySequenceCache(IReadOnlyList<FlybyCameraInstance> cameras, bool useSmoothPause)
    {
        if (cameras.Count < 2)
        {
            _frames = Array.Empty<CachedFrame>();
            _cutRegions = Array.Empty<CutRegion>();
            _smoothedSpeeds = Array.Empty<float>();
            _segmentSpeedScales = Array.Empty<float>();
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
        _segmentSpeedScales = BuildSegmentSpeedScales(cameras, speedKnots, numSegments);

        // Pass 1: sequentially build the spline parameter timeline using
        // spline-interpolated speed for smooth advancement between cameras.
        float[] splineParams = BuildSplineTimeline(cameras, speedKnots, _segmentSpeedScales, numSegments, useSmoothPause, out var cutRegionsList);

        _cutRegions = cutRegionsList.ToArray();

        // Pass 2: evaluate all spline channels in parallel.
        _frames = EvaluateFramesParallel(splineParams, posX, posY, posZ, tgtX, tgtY, tgtZ, rollKnots, fovKnots, numSegments);

        // Pass 3: unwrap yaw/pitch/roll to prevent discontinuities from atan2 wrapping.
        UnwrapFrameAngles(_frames);

        _frameCount = _frames.Length;
        _totalDuration = _frameCount > 0 ? (_frameCount - 1) * FlybyConstants.TimeStep : 0;

        // Pass 4: pre-compute smoothed speed curve.
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

    #region Pass 1: spline parameter timeline

    /// <summary>
    /// Sequentially resolves the spline parameter (t) for every time slot.
    /// Speed is interpolated along the spline, eliminating abrupt speed changes at camera
    /// boundaries. Per-segment speed concepts are only used for freeze and cut scenarios.
    /// </summary>
    private static float[] BuildSplineTimeline(
        IReadOnlyList<FlybyCameraInstance> cameras,
        float[] speedKnots,
        float[] segmentSpeedScales,
        int numSegments,
        bool useSmoothPause,
        out List<CutRegion> cutRegions)
    {
        cutRegions = new List<CutRegion>();
        int numCameras = cameras.Count;

        var timeline = new List<float>(numCameras * 200);

        float currentT = 0;
        int processedBoundary = 0;

        while (processedBoundary < numSegments)
        {
            int nextBoundary = processedBoundary + 1;
            int nextCamIdx = nextBoundary;
            ushort nextFlags = cameras[nextCamIdx].Flags;
            short nextTimer = cameras[nextCamIdx].Timer;

            bool hasCut = (nextFlags & FlybyConstants.FlagCameraCut) != 0;
            bool hasFreeze = !hasCut && (nextFlags & FlybyConstants.FlagFreezeCamera) != 0 && nextTimer > 0;
            float boundaryT = (float)nextBoundary;

            if (useSmoothPause && hasFreeze)
            {
                // Advance to the ease-out zone with spline-interpolated speed.
                float easeOutStartT = boundaryT - FlybyConstants.FreezeEaseDistance;
                AdvanceToTarget(timeline, speedKnots, segmentSpeedScales, numSegments, easeOutStartT, ref currentT);

                // Ease-out: decelerate to zero at the boundary.
                EmitEaseOut(timeline, speedKnots, segmentSpeedScales, numSegments, boundaryT, ref currentT);
                currentT = boundaryT;

                // Hold at boundary.
                int holdFrames = cameras[nextCamIdx].TimerToFrames;
                int holdSlots = Math.Max(0, (int)(holdFrames / FlybyConstants.TickRate / FlybyConstants.TimeStep));

                for (int f = 0; f < holdSlots; f++)
                    timeline.Add(currentT);

                // Ease-in: accelerate from zero on the next region.
                if (nextBoundary < numSegments)
                    EmitEaseIn(timeline, speedKnots, segmentSpeedScales, numSegments, ref currentT);
            }
            else
            {
                // Advance through the boundary with spline-interpolated speed.
                AdvanceToTarget(timeline, speedKnots, segmentSpeedScales, numSegments, boundaryT, ref currentT);
                currentT = boundaryT;

                if (hasFreeze)
                {
                    // Hard freeze: hold at boundary.
                    int gameFrames = Math.Max(0, cameras[nextCamIdx].TimerToFrames);
                    int freezeSlots = (int)(gameFrames / FlybyConstants.TickRate / FlybyConstants.TimeStep);

                    for (int f = 0; f < freezeSlots; f++)
                        timeline.Add(currentT);
                }
            }

            processedBoundary = nextBoundary;

            // Handle camera cut: fill the bypassed region with freeze frames
            // at the target camera, then jump to the target.
            if (hasCut && nextCamIdx < numCameras)
            {
                int targetCam = Math.Clamp(nextTimer, 0, numCameras - 1);

                if (targetCam > nextCamIdx && targetCam <= numSegments)
                {
                    float bypassedTime = 0;

                    for (int i = nextCamIdx; i < targetCam; i++)
                    {
                        if (i < numCameras - 1)
                            bypassedTime += FlybySequenceHelper.GetSegmentDuration(cameras[i]);
                        bypassedTime += FlybySequenceHelper.GetFreezeDuration(cameras[i]);
                    }

                    float cutStartTime = timeline.Count * FlybyConstants.TimeStep;
                    float targetSplineT = (float)targetCam;
                    int bypassSlots = Math.Max(1, (int)(bypassedTime / FlybyConstants.TimeStep));

                    for (int f = 0; f < bypassSlots; f++)
                        timeline.Add(targetSplineT);

                    cutRegions.Add(new CutRegion
                    {
                        StartTime = cutStartTime,
                        EndTime = cutStartTime + bypassSlots * FlybyConstants.TimeStep
                    });

                    processedBoundary = targetCam;
                    currentT = targetCam;

                    // Emit freeze at target camera if applicable.
                    float targetFreeze = FlybySequenceHelper.GetFreezeDuration(cameras[targetCam]);

                    if (targetFreeze > 0)
                    {
                        int freezeSlots = (int)(targetFreeze / FlybyConstants.TimeStep);

                        for (int f = 0; f < freezeSlots; f++)
                            timeline.Add(targetSplineT);
                    }
                }
                else if (targetCam >= numSegments)
                {
                    break;
                }
            }
        }

        // Emit final frame.
        float finalT = Math.Min(currentT, numSegments);
        timeline.Add(finalT);

        return timeline.ToArray();
    }

    /// <summary>
    /// Advances currentT toward targetT using spline-interpolated speed.
    /// Each tick evaluates the speed spline at the current position, producing
    /// smooth transitions between cameras.
    /// </summary>
    private static void AdvanceToTarget(List<float> timeline, float[] speedKnots, float[] segmentSpeedScales,
        int numSegments, float targetT, ref float currentT)
    {
        float tickFactor = FlybyConstants.SpeedScale * FlybyConstants.TimeStep;

        while (currentT < targetT)
        {
            timeline.Add(currentT);
            float speed = GetScaledSegmentSpeed(currentT, speedKnots, segmentSpeedScales, numSegments);
            currentT += Math.Max(speed, FlybyConstants.MinSpeed) * tickFactor;
        }

        currentT = Math.Min(currentT, targetT);
    }

    /// <summary>
    /// Emits a quadratic ease-out deceleration from the current position to the boundary.
    /// Uses the spline-interpolated speed at the ease start as the initial speed.
    /// </summary>
    private static void EmitEaseOut(List<float> timeline, float[] speedKnots, float[] segmentSpeedScales,
        int numSegments, float boundaryT, ref float currentT)
    {
        float easeStartT = currentT;
        float remainingDist = Math.Max(boundaryT - easeStartT, FlybyConstants.MinSpeed);

        float speed = GetScaledSegmentSpeed(easeStartT, speedKnots, segmentSpeedScales, numSegments);
        float speedPerSec = Math.Max(speed, FlybyConstants.MinSpeed) * FlybyConstants.SpeedScale;

        float easeStep = speedPerSec / (2.0f * remainingDist);
        float easeProgress = 0;

        while (easeProgress < 1.0f)
        {
            easeProgress = Math.Min(easeProgress + easeStep * FlybyConstants.TimeStep, 1.0f);
            currentT = easeStartT + remainingDist * easeProgress * (2.0f - easeProgress);
            timeline.Add(Math.Min(currentT, boundaryT));
        }
    }

    /// <summary>
    /// Emits a quadratic ease-in acceleration from zero speed at the current position.
    /// Uses the spline-interpolated speed at the boundary as the target speed.
    /// </summary>
    private static void EmitEaseIn(List<float> timeline, float[] speedKnots, float[] segmentSpeedScales, int numSegments, ref float currentT)
    {
        float speed = GetScaledSegmentSpeed(currentT, speedKnots, segmentSpeedScales, numSegments);
        float speedPerSec = Math.Max(speed, FlybyConstants.MinSpeed) * FlybyConstants.SpeedScale;

        float easeInStep = speedPerSec / (2.0f * FlybyConstants.FreezeEaseDistance);
        float easeInProgress = 0;

        while (easeInProgress < 1.0f)
        {
            easeInProgress = Math.Min(easeInProgress + easeInStep * FlybyConstants.TimeStep, 1.0f);
            float speedFactor = easeInProgress * easeInProgress;
            currentT += speedPerSec * speedFactor * FlybyConstants.TimeStep;
            timeline.Add(currentT);
        }
    }

    #endregion Pass 1: spline parameter timeline

    private static float[] BuildSegmentSpeedScales(
        IReadOnlyList<FlybyCameraInstance> cameras,
        float[] speedKnots,
        int numSegments)
    {
        var scales = new float[numSegments];

        for (int segmentIndex = 0; segmentIndex < numSegments; segmentIndex++)
        {
            float inverseSpeedIntegral = IntegrateInverseSegmentSpeed(segmentIndex, speedKnots, numSegments);
            float targetSpeed = Math.Max(cameras[segmentIndex].Speed, FlybyConstants.MinSpeed);
            scales[segmentIndex] = targetSpeed * inverseSpeedIntegral;
        }

        return scales;
    }

    private static float IntegrateInverseSegmentSpeed(int segmentIndex, float[] speedKnots, int numSegments)
    {
        const int sampleCount = 64;
        float integral = 0;

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float localT = (sampleIndex + 0.5f) / sampleCount;
            float splineT = Math.Clamp(segmentIndex + localT, 0, numSegments);
            float speed = Math.Max(CatmullRomSpline.Evaluate(splineT, speedKnots), FlybyConstants.MinSpeed);
            integral += 1.0f / speed;
        }

        return integral / sampleCount;
    }

    private static float GetScaledSegmentSpeed(float currentT, float[] speedKnots, float[] segmentSpeedScales, int numSegments)
    {
        float clampedT = Math.Clamp(currentT, 0, numSegments);
        int segmentIndex = Math.Min((int)MathF.Floor(clampedT), segmentSpeedScales.Length - 1);
        float baseSpeed = Math.Max(CatmullRomSpline.Evaluate(clampedT, speedKnots), FlybyConstants.MinSpeed);

        if (segmentIndex < 0)
            return baseSpeed;

        return baseSpeed * segmentSpeedScales[segmentIndex];
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
