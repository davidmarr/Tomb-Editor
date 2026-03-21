using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using TombLib;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.FlybyManager;

/// <summary>
/// Pre-calculates a flyby sequence into a frame array at fixed time resolution.
/// Both timeline scrubbing and playback sample from this cache via linear interpolation,
/// eliminating real-time state tracking for freeze, cut, and smooth pause flags.
///
/// Construction uses a two-pass approach for speed: pass 1 sequentially resolves the
/// spline parameter for every time slot (handling freeze, cut, smooth pause), then
/// pass 2 evaluates all spline channels in parallel.
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

    // The game logic runs at 30 ticks per second.
    private const float GameTickRate = 30.0f;

    // Time resolution: one frame per game tick.
    private const float TimeStep = 1.0f / GameTickRate;

	/// <summary>
	/// Public accessor for the time step between cached frames.
	/// </summary>
	public static readonly float TimeStepValue = 1.0f / GameTickRate;

    // Distance from camera to target point, matching the level compiler.
    private const float TargetDistance = Level.SectorSizeUnit;

    // TombEngine smooth pause constants.
    private const float EaseDistance = 0.15f;
    private const float MinSpeed = 0.001f;

    // SCF flag bits.
    private const int FlagStopMovement = 1 << 8;
    private const int FlagCutToCam = 1 << 7;

    private readonly CachedFrame[] _frames;
    private readonly float _totalDuration;
    private readonly int _frameCount;
    private readonly CutRegion[] _cutRegions;
    private readonly float[] _cameraTimeSeconds;
    private readonly float[] _easeOutStartSeconds;

    public float TotalDuration => _totalDuration;
    public int FrameCount => _frameCount;
    public bool IsValid => _frameCount > 0;
    public IReadOnlyList<CutRegion> CutRegions => _cutRegions;

    /// <summary>
    /// Per-camera timeline time in seconds, as resolved by the cache build pass.
    /// Accounts for ease-in/out phases in TombEngine smooth pause mode.
    /// </summary>
    public IReadOnlyList<float> CameraTimeSeconds => _cameraTimeSeconds;

    /// <summary>
    /// Per-camera ease-out start time in seconds for TombEngine smooth-pause display.
    /// For cameras with a smooth-pause freeze, this is the time where deceleration begins.
    /// For other cameras, this equals the camera's own time.
    /// </summary>
    public IReadOnlyList<float> EaseOutStartSeconds => _easeOutStartSeconds;

    public FlybySequenceCache(IReadOnlyList<FlybyCameraInstance> cameras, bool useSmoothPause)
    {
        if (cameras.Count < 2)
        {
            _frames = Array.Empty<CachedFrame>();
            _cutRegions = Array.Empty<CutRegion>();
            _cameraTimeSeconds = Array.Empty<float>();
            _easeOutStartSeconds = Array.Empty<float>();
            _totalDuration = 0;
            _frameCount = 0;
            return;
        }

        // Build Catmull-Rom knot arrays.
        BuildKnotArrays(cameras,
            out float[] posX, out float[] posY, out float[] posZ,
            out float[] tgtX, out float[] tgtY, out float[] tgtZ,
            out float[] rollKnots, out float[] fovKnots);

        int numCameras = cameras.Count;
        int numSegments = numCameras - 1;

        // Pre-compute per-segment durations.
        var segmentDurations = new float[numSegments];

        for (int i = 0; i < numSegments; i++)
        {
            float speed = Math.Max(cameras[i].Speed, float.MinValue);
            segmentDurations[i] = 1.0f / (speed * FlybyPreview.SpeedScale);
        }

        // Pass 1: sequentially build the spline parameter timeline (fast, no spline math).
        float[] splineParams = BuildSplineTimeline(cameras, segmentDurations, useSmoothPause,
            out var cutRegionsList, out var cameraTimesResult, out var easeOutStartResult);
        _cutRegions = cutRegionsList.ToArray();
        _cameraTimeSeconds = cameraTimesResult;
        _easeOutStartSeconds = easeOutStartResult;

        // Pass 2: evaluate all spline channels in parallel.
        _frames = EvaluateFramesParallel(splineParams, posX, posY, posZ,
            tgtX, tgtY, tgtZ, rollKnots, fovKnots, numSegments);

        _frameCount = _frames.Length;
        _totalDuration = _frameCount > 0 ? (_frameCount - 1) * TimeStep : 0;
    }

    /// <summary>
    /// Samples the cache at a given time, linearly interpolating between adjacent frames.
    /// </summary>
    public FlybyPreview.FrameState SampleAtTime(float timeSeconds)
    {
        if (_frameCount == 0)
            return new FlybyPreview.FrameState { Finished = true };

        float index = timeSeconds / TimeStep;
        int i0 = (int)index;
        float frac = index - i0;

        if (i0 < 0)
            return FrameToState(_frames[0]);

        if (i0 >= _frameCount - 1)
            return FrameToState(_frames[_frameCount - 1]);

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
    /// Returns the speed (world units per second) at the given timeline time,
    /// measured from distance between adjacent cached frames.
    /// Returns negative if the time is outside the sequence or within a cut region.
    /// </summary>
    public float GetSpeedAtTime(float timeSeconds)
    {
        if (_frameCount < 2)
            return -1;

        if (timeSeconds < 0 || timeSeconds > _totalDuration)
            return -1;

        // Skip cut regions.
        foreach (var cut in _cutRegions)
        {
            if (timeSeconds >= cut.StartTime && timeSeconds <= cut.EndTime)
                return -1;
        }

        int index = (int)(timeSeconds / TimeStep);
        index = Math.Clamp(index, 0, _frameCount - 2);

        var delta = _frames[index + 1].Position - _frames[index].Position;
        return delta.Length() / TimeStep;
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

    #region Pass 1: spline parameter timeline

    /// <summary>
    /// Sequentially resolves the spline parameter (t) for every time slot by walking through
    /// segments, freeze regions, camera cuts, and smooth pause phases. No spline math here,
    /// only float arithmetic, so this pass is very fast.
    /// </summary>
    private static float[] BuildSplineTimeline(
        IReadOnlyList<FlybyCameraInstance> cameras,
        float[] segmentDurations,
        bool useSmoothPause,
        out List<CutRegion> cutRegions,
        out float[] cameraTimeSeconds,
        out float[] easeOutStartSeconds)
    {
        cutRegions = new List<CutRegion>();
        int numCameras = cameras.Count;
        int numSegments = numCameras - 1;

        // Track per-camera timeline time in seconds.
        cameraTimeSeconds = new float[numCameras];
        easeOutStartSeconds = new float[numCameras];
        cameraTimeSeconds[0] = 0;
        easeOutStartSeconds[0] = 0;

        // Estimate total duration to pre-allocate.
        float estimatedDuration = 0;

        for (int i = 0; i < numSegments; i++)
            estimatedDuration += segmentDurations[i];

        for (int i = 0; i < numCameras; i++)
            estimatedDuration += FlybySequenceData.GetFreezeDuration(cameras[i]);

        int estimatedSlots = (int)(estimatedDuration * 1.15f / TimeStep) + 256;
        var timeline = new List<float>(estimatedSlots);

        // Use a running accumulator for currentT, matching the original engine's
        // continuous spline parameter advancement (t += speed * SpeedScale per tick).
        float currentT = 0;
        int currentSegment = 0;

        while (currentSegment < numSegments)
        {
            int nextCamera = currentSegment + 1;
            ushort nextFlags = cameras[nextCamera].Flags;
            short nextTimer = cameras[nextCamera].Timer;

            bool hasCut = (nextFlags & FlagCutToCam) != 0;
            bool hasFreeze = !hasCut && (nextFlags & FlagStopMovement) != 0 && nextTimer > 0;

            float segEnd = currentSegment + 1.0f;
            float speedPerTick = segmentDurations[currentSegment] > 0
                ? TimeStep / segmentDurations[currentSegment] : 0;

            if (useSmoothPause && hasFreeze)
            {
                EmitSmoothPauseSegment(timeline, currentSegment, segmentDurations,
                    numSegments, nextTimer, speedPerTick, ref currentT,
                    out float easeOutStart);

                // Record the ease-out start time for the next camera's freeze region display.
                if (nextCamera < numCameras)
                    easeOutStartSeconds[nextCamera] = easeOutStart;
            }
            else
            {
                EmitLinearSegment(timeline, segEnd, speedPerTick, ref currentT);

                if (hasFreeze)
                {
                    int gameFrames = Math.Max(0, nextTimer >> 3);
                    float freezeDuration = gameFrames / GameTickRate;
                    currentT = segEnd;
                    int freezeSlots = (int)(freezeDuration / TimeStep);

                    for (int f = 0; f < freezeSlots; f++)
                        timeline.Add(currentT);
                }
            }

            currentSegment++;

            // Record the timeline time for this camera.
            if (currentSegment < numCameras)
            {
                cameraTimeSeconds[currentSegment] = timeline.Count * TimeStep;

                // Default ease-out start to camera time when not set by smooth pause.
                if (easeOutStartSeconds[currentSegment] == 0 && currentSegment > 0)
                    easeOutStartSeconds[currentSegment] = cameraTimeSeconds[currentSegment];
            }

            // Handle camera cut: fill the bypassed region with freeze frames
            // at the target camera, then jump to the target.
            if (hasCut && currentSegment < numCameras)
            {
                int targetCam = Math.Clamp(nextTimer, 0, numCameras - 1);

                if (targetCam > currentSegment && targetCam <= numSegments)
                {
                    float bypassedTime = 0;

                    for (int i = currentSegment; i < targetCam; i++)
                    {
                        if (i < numSegments)
                            bypassedTime += segmentDurations[i];
                        bypassedTime += FlybySequenceData.GetFreezeDuration(cameras[i]);
                    }

                    float cutStartTime = timeline.Count * TimeStep;
                    float targetSplineT = (float)targetCam;
                    int bypassSlots = Math.Max(1, (int)(bypassedTime / TimeStep));

                    for (int f = 0; f < bypassSlots; f++)
                        timeline.Add(targetSplineT);

                    cutRegions.Add(new CutRegion
                    {
                        StartTime = cutStartTime,
                        EndTime = cutStartTime + bypassedTime
                    });

                    currentSegment = targetCam;
                    currentT = targetCam;

                    // Record timeline time for the target camera.
                    if (targetCam < numCameras)
                        cameraTimeSeconds[targetCam] = timeline.Count * TimeStep;

                    // Emit freeze at target camera if it was supposed to be
                    // processed in the bypassed segment's iteration.
                    float targetFreeze = FlybySequenceData.GetFreezeDuration(cameras[targetCam]);

                    if (targetFreeze > 0)
                    {
                        int freezeSlots = (int)(targetFreeze / TimeStep);

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
    /// Advances currentT through a segment using a running accumulator, matching
    /// the original engine's per-tick t += speed * SpeedScale advancement.
    /// </summary>
    private static void EmitLinearSegment(List<float> timeline, float segEnd,
        float speedPerTick, ref float currentT)
    {
        while (currentT < segEnd)
        {
            timeline.Add(currentT);
            currentT += speedPerTick;
        }
    }

    /// <summary>
    /// Emits spline parameters for a TombEngine smooth-pause segment: linear traversal up to
    /// the ease-out zone, ease-out deceleration, hold, and ease-in acceleration on the next
    /// segment. Uses the running currentT accumulator throughout all phases.
    /// </summary>
    private static void EmitSmoothPauseSegment(
        List<float> timeline, int segment, float[] segmentDurations,
        int numSegments, short timer, float speedPerTick, ref float currentT,
        out float easeOutStartTimeSeconds)
    {
        float segEnd = segment + 1.0f;
        float speedPerSec = segmentDurations[segment] > 0 ? 1.0f / segmentDurations[segment] : 0;

        // Phase 1: Linear advance until the ease-out zone.
        float easeOutStartT = segEnd - EaseDistance;

        while (currentT < easeOutStartT)
        {
            timeline.Add(currentT);
            currentT += speedPerTick;
        }

        // Record the timeline time where ease-out begins.
        easeOutStartTimeSeconds = timeline.Count * TimeStep;

        // Phase 2: Ease-out deceleration.
        float easeStartT = currentT;
        float remainingT = Math.Max(segEnd - easeStartT, MinSpeed);
        float clampedSpeed = Math.Max(speedPerSec, MinSpeed);
        float easeStep = clampedSpeed / (2.0f * remainingT);
        float easeProgress = 0;

        while (easeProgress < 1.0f)
        {
            easeProgress = Math.Min(easeProgress + easeStep * TimeStep, 1.0f);
            currentT = easeStartT + remainingT * easeProgress * (2.0f - easeProgress);
            timeline.Add(Math.Min(currentT, segEnd));
        }

        currentT = segEnd;

        // Phase 3: Hold at boundary.
        int holdGameFrames = timer >> 3;
        float holdDuration = holdGameFrames / GameTickRate;
        int holdSlots = (int)(holdDuration / TimeStep);

        for (int i = 0; i < holdSlots; i++)
            timeline.Add(currentT);

        // Phase 4: Ease-in acceleration on the next segment.
        int nextSegment = segment + 1;

        if (nextSegment < numSegments)
        {
            float nextSpeedPerSec = segmentDurations[nextSegment] > 0
                ? 1.0f / segmentDurations[nextSegment] : 0;
            float nextClampedSpeed = Math.Max(nextSpeedPerSec, MinSpeed);
            float easeInStep = nextClampedSpeed / (2.0f * EaseDistance);
            float easeInProgress = 0;

            while (easeInProgress < 1.0f)
            {
                easeInProgress = Math.Min(easeInProgress + easeInStep * TimeStep, 1.0f);
                float speedFactor = easeInProgress * easeInProgress;
                currentT += nextSpeedPerSec * speedFactor * TimeStep;
                timeline.Add(currentT);
            }
        }
    }

    #endregion Pass 1: spline parameter timeline

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
            Roll = -MathC.DegToRad(roll),
            Fov = MathC.DegToRad(fov)
        };
    }

    #endregion Pass 2: parallel spline evaluation

    #region Knot building

    private static void BuildKnotArrays(
        IReadOnlyList<FlybyCameraInstance> cameras,
        out float[] posX, out float[] posY, out float[] posZ,
        out float[] tgtX, out float[] tgtY, out float[] tgtZ,
        out float[] rollKnots, out float[] fovKnots)
    {
        int n = cameras.Count;

        var rawPosX = new float[n];
        var rawPosY = new float[n];
        var rawPosZ = new float[n];
        var rawTgtX = new float[n];
        var rawTgtY = new float[n];
        var rawTgtZ = new float[n];
        var rawRoll = new float[n];
        var rawFov = new float[n];

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
            rawTgtX[i] = worldPos.X + TargetDistance * cosPitch * MathF.Sin(yawRad);
            rawTgtY[i] = worldPos.Y + TargetDistance * MathF.Sin(pitchRad);
            rawTgtZ[i] = worldPos.Z + TargetDistance * cosPitch * MathF.Cos(yawRad);
            rawRoll[i] = cam.Roll;
            rawFov[i] = cam.Fov;
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

    private static void UnwrapAngles(float[] angles)
    {
        for (int i = 1; i < angles.Length; i++)
        {
            float delta = angles[i] - angles[i - 1];
            angles[i] -= MathF.Round(delta / 360.0f) * 360.0f;
        }
    }

    #endregion Knot building
}
