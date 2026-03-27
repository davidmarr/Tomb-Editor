using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.FlybyTimeline;

public readonly struct FlybyCutRegion
{
    public float StartTime { get; init; }
    public float EndTime { get; init; }
}

public class FlybySequenceTiming
{
    private readonly float[] _cameraTimes;
    private readonly float[] _segmentDurations;
    private readonly float[] _freezeDurations;
    private readonly float[] _cutBypassDurations;
    private readonly float[] _splineTimeline;
    private readonly FlybyCutRegion[] _cutRegions;
    private readonly ReadOnlyCollection<float> _splineTimelineView;
    private readonly ReadOnlyCollection<FlybyCutRegion> _cutRegionsView;

    public static FlybySequenceTiming Empty { get; } = new(Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<FlybyCutRegion>(), 0);

    public float TotalDuration { get; }
    public int CameraCount => _cameraTimes.Length;
    public IReadOnlyList<float> SplineTimeline => _splineTimelineView;
    public IReadOnlyList<FlybyCutRegion> CutRegions => _cutRegionsView;

    private FlybySequenceTiming(float[] cameraTimes, float[] segmentDurations, float[] freezeDurations, float[] cutBypassDurations,
        float[] splineTimeline, FlybyCutRegion[] cutRegions, float totalDuration)
    {
        _cameraTimes = cameraTimes;
        _segmentDurations = segmentDurations;
        _freezeDurations = freezeDurations;
        _cutBypassDurations = cutBypassDurations;
        _splineTimeline = splineTimeline;
        _cutRegions = cutRegions;
        _splineTimelineView = Array.AsReadOnly(_splineTimeline);
        _cutRegionsView = Array.AsReadOnly(_cutRegions);
        TotalDuration = totalDuration;
    }

    public float GetCameraTime(int index)
    {
        if (_cameraTimes.Length == 0)
            return 0;

        return _cameraTimes[Math.Clamp(index, 0, _cameraTimes.Length - 1)];
    }

    public float GetSegmentDuration(int index)
    {
        if (index < 0 || index >= _segmentDurations.Length)
            return 0;

        return _segmentDurations[index];
    }

    public float GetFreezeDuration(int index)
    {
        if (index < 0 || index >= _freezeDurations.Length)
            return 0;

        return _freezeDurations[index];
    }

    public float GetCutBypassDuration(int index)
    {
        if (index < 0 || index >= _cutBypassDurations.Length)
            return 0;

        return _cutBypassDurations[index];
    }

    public static FlybySequenceTiming Build(IReadOnlyList<FlybyCameraInstance> cameras, bool useSmoothPause)
    {
        if (cameras.Count == 0)
            return Empty;

        var cameraTimes = new float[cameras.Count];
        var segmentDurations = new float[Math.Max(0, cameras.Count - 1)];
        var freezeDurations = BuildFreezeDurations(cameras);
        var cutBypassDurations = new float[cameras.Count];

        if (cameras.Count < 2)
            return new FlybySequenceTiming(cameraTimes, segmentDurations, freezeDurations, cutBypassDurations,
                Array.Empty<float>(), Array.Empty<FlybyCutRegion>(), 0);

        float[] speedKnots = BuildSpeedKnots(cameras);
        int numSegments = cameras.Count - 1;
        PopulateCameraTimes(cameras, speedKnots, useSmoothPause, cameraTimes);

        for (int i = 0; i < segmentDurations.Length; i++)
            segmentDurations[i] = Math.Max(0, cameraTimes[i + 1] - cameraTimes[i]);

        for (int i = 0; i < cameras.Count; i++)
        {
            if ((cameras[i].Flags & FlybyConstants.FlagCameraCut) == 0)
                continue;

            int targetIndex = cameras[i].Timer;

            if (targetIndex > i && targetIndex < cameras.Count)
                cutBypassDurations[i] = Math.Max(0, cameraTimes[targetIndex] - cameraTimes[i]);
        }

        float[] splineTimeline = BuildPlaybackTimeline(cameras, speedKnots, numSegments, useSmoothPause, segmentDurations, freezeDurations, out FlybyCutRegion[] cutRegions);
        float totalDuration = splineTimeline.Length > 0 ? (splineTimeline.Length - 1) * FlybyConstants.TimeStep : 0;

        return new FlybySequenceTiming(cameraTimes, segmentDurations, freezeDurations, cutBypassDurations,
            splineTimeline, cutRegions, totalDuration);
    }

    internal static float GetCameraTimeForSpeed(IReadOnlyList<FlybyCameraInstance> cameras,
        int targetIndex, bool useSmoothPause, int speedCameraIndex, float speed)
    {
        if (cameras.Count == 0)
            return 0;

        int clampedTargetIndex = Math.Clamp(targetIndex, 0, cameras.Count - 1);

        if (clampedTargetIndex <= 0 || cameras.Count < 2)
            return 0;

        float[] speedKnots = BuildSpeedKnots(cameras, speedCameraIndex, speed);
        var cameraTimes = new float[clampedTargetIndex + 1];
        PopulateCameraTimes(cameras, speedKnots, useSmoothPause, cameraTimes, clampedTargetIndex);
        return cameraTimes[clampedTargetIndex];
    }

    private static float[] BuildFreezeDurations(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        var freezeDurations = new float[cameras.Count];

        for (int i = 0; i < cameras.Count; i++)
            freezeDurations[i] = FlybySequenceHelper.GetFreezeDuration(cameras[i]);

        return freezeDurations;
    }

    private static float[] BuildSpeedKnots(IReadOnlyList<FlybyCameraInstance> cameras,
        int overrideCameraIndex = -1, float overrideSpeed = 0)
    {
        var rawSpeed = new float[cameras.Count];

        for (int i = 0; i < cameras.Count; i++)
        {
            float speed = i == overrideCameraIndex ? overrideSpeed : cameras[i].Speed;
            rawSpeed[i] = Math.Max(speed, FlybyConstants.MinSpeed);
        }

        return CatmullRomSpline.PadKnots(rawSpeed);
    }

    private static void PopulateCameraTimes(IReadOnlyList<FlybyCameraInstance> cameras,
        float[] speedKnots, bool useSmoothPause, float[] cameraTimes, int lastCameraIndexToPopulate = int.MaxValue)
    {
        int numSegments = cameras.Count - 1;
        int emittedSlots = 0;
        float currentT = 0;
        int processedBoundary = 0;
        int lastCameraIndex = Math.Min(lastCameraIndexToPopulate, cameras.Count - 1);

        while (processedBoundary < numSegments)
        {
            int nextBoundary = processedBoundary + 1;
            int nextCamIdx = nextBoundary;
            ushort nextFlags = cameras[nextCamIdx].Flags;
            short nextTimer = cameras[nextCamIdx].Timer;

            bool hasCut = (nextFlags & FlybyConstants.FlagCameraCut) != 0;
            bool hasFreeze = !hasCut && (nextFlags & FlybyConstants.FlagFreezeCamera) != 0 && nextTimer > 0;
            float boundaryT = nextBoundary;

            if (useSmoothPause && hasFreeze)
            {
                float easeOutStartT = boundaryT - FlybyConstants.FreezeEaseDistance;
                AdvanceToTarget(speedKnots, numSegments, easeOutStartT, ref currentT, ref emittedSlots);
                EmitEaseOut(speedKnots, numSegments, boundaryT, ref currentT, ref emittedSlots);
                currentT = boundaryT;
                cameraTimes[nextCamIdx] = emittedSlots * FlybyConstants.TimeStep;

                if (nextCamIdx >= lastCameraIndex)
                    return;

                int holdFrames = cameras[nextCamIdx].TimerToFrames;
                int holdSlots = Math.Max(0, (int)(holdFrames / FlybyConstants.TickRate / FlybyConstants.TimeStep));
                emittedSlots += holdSlots;

                if (nextBoundary < numSegments)
                    EmitEaseIn(speedKnots, numSegments, ref currentT, ref emittedSlots);
            }
            else
            {
                AdvanceToTarget(speedKnots, numSegments, boundaryT, ref currentT, ref emittedSlots);
                currentT = boundaryT;
                cameraTimes[nextCamIdx] = emittedSlots * FlybyConstants.TimeStep;

                if (nextCamIdx >= lastCameraIndex)
                    return;

                if (hasFreeze)
                {
                    int freezeSlots = (int)(Math.Max(0, cameras[nextCamIdx].TimerToFrames) / FlybyConstants.TickRate / FlybyConstants.TimeStep);
                    emittedSlots += freezeSlots;
                }
            }

            processedBoundary = nextBoundary;
        }
    }

    private static float[] BuildPlaybackTimeline(IReadOnlyList<FlybyCameraInstance> cameras,
        float[] speedKnots, int numSegments, bool useSmoothPause, float[] segmentDurations,
        float[] freezeDurations, out FlybyCutRegion[] cutRegions)
    {
        var regions = new List<FlybyCutRegion>();
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
            float boundaryT = nextBoundary;

            if (useSmoothPause && hasFreeze)
            {
                float easeOutStartT = boundaryT - FlybyConstants.FreezeEaseDistance;
                AdvanceToTarget(timeline, speedKnots, numSegments, easeOutStartT, ref currentT);
                EmitEaseOut(timeline, speedKnots, numSegments, boundaryT, ref currentT);
                currentT = boundaryT;

                int holdSlots = Math.Max(0, (int)(cameras[nextCamIdx].TimerToFrames / FlybyConstants.TickRate / FlybyConstants.TimeStep));

                for (int f = 0; f < holdSlots; f++)
                    timeline.Add(currentT);

                if (nextBoundary < numSegments)
                    EmitEaseIn(timeline, speedKnots, numSegments, ref currentT);
            }
            else
            {
                AdvanceToTarget(timeline, speedKnots, numSegments, boundaryT, ref currentT);
                currentT = boundaryT;

                if (hasFreeze)
                {
                    int freezeSlots = (int)(Math.Max(0, cameras[nextCamIdx].TimerToFrames) / FlybyConstants.TickRate / FlybyConstants.TimeStep);

                    for (int f = 0; f < freezeSlots; f++)
                        timeline.Add(currentT);
                }
            }

            processedBoundary = nextBoundary;

            if (hasCut && nextCamIdx < numCameras)
            {
                int targetCam = Math.Clamp(nextTimer, 0, numCameras - 1);

                if (targetCam > nextCamIdx && targetCam <= numSegments)
                {
                    float bypassedTime = 0;

                    for (int i = nextCamIdx; i < targetCam; i++)
                    {
                        if (i < numCameras - 1)
                            bypassedTime += segmentDurations[i];

                        bypassedTime += freezeDurations[i];
                    }

                    float cutStartTime = timeline.Count * FlybyConstants.TimeStep;
                    float targetSplineT = targetCam;
                    int bypassSlots = Math.Max(1, (int)(bypassedTime / FlybyConstants.TimeStep));

                    for (int f = 0; f < bypassSlots; f++)
                        timeline.Add(targetSplineT);

                    regions.Add(new FlybyCutRegion
                    {
                        StartTime = cutStartTime,
                        EndTime = cutStartTime + bypassSlots * FlybyConstants.TimeStep
                    });

                    processedBoundary = targetCam;
                    currentT = targetCam;

                    if (freezeDurations[targetCam] > 0)
                    {
                        int freezeSlots = (int)(freezeDurations[targetCam] / FlybyConstants.TimeStep);

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

        timeline.Add(Math.Min(currentT, numSegments));
        cutRegions = regions.ToArray();
        return timeline.ToArray();
    }

    private static void AdvanceToTarget(float[] speedKnots, int numSegments, float targetT, ref float currentT, ref int emittedSlots)
    {
        float tickFactor = FlybyConstants.SpeedScale * FlybyConstants.TimeStep;

        while (currentT < targetT)
        {
            emittedSlots++;
            float speed = GetClampedSplineSpeed(currentT, speedKnots, numSegments);
            currentT += Math.Max(speed, FlybyConstants.MinSpeed) * tickFactor;
        }

        currentT = Math.Min(currentT, targetT);
    }

    private static void EmitEaseOut(float[] speedKnots, int numSegments, float boundaryT, ref float currentT, ref int emittedSlots)
    {
        float easeStartT = currentT;
        float remainingDist = Math.Max(boundaryT - easeStartT, FlybyConstants.MinSpeed);
        float speed = GetClampedSplineSpeed(easeStartT, speedKnots, numSegments);
        float speedPerSec = Math.Max(speed, FlybyConstants.MinSpeed) * FlybyConstants.SpeedScale;
        float easeStep = speedPerSec / (2.0f * remainingDist);
        float easeProgress = 0;

        while (easeProgress < 1.0f)
        {
            easeProgress = Math.Min(easeProgress + easeStep * FlybyConstants.TimeStep, 1.0f);
            currentT = easeStartT + remainingDist * easeProgress * (2.0f - easeProgress);
            emittedSlots++;
        }
    }

    private static void EmitEaseIn(float[] speedKnots, int numSegments, ref float currentT, ref int emittedSlots)
    {
        float speed = GetClampedSplineSpeed(currentT, speedKnots, numSegments);
        float speedPerSec = Math.Max(speed, FlybyConstants.MinSpeed) * FlybyConstants.SpeedScale;
        float easeInStep = speedPerSec / (2.0f * FlybyConstants.FreezeEaseDistance);
        float easeInProgress = 0;

        while (easeInProgress < 1.0f)
        {
            easeInProgress = Math.Min(easeInProgress + easeInStep * FlybyConstants.TimeStep, 1.0f);
            float speedFactor = easeInProgress * easeInProgress;
            currentT += speedPerSec * speedFactor * FlybyConstants.TimeStep;
            emittedSlots++;
        }
    }

    private static void AdvanceToTarget(List<float> timeline, float[] speedKnots, int numSegments, float targetT, ref float currentT)
    {
        float tickFactor = FlybyConstants.SpeedScale * FlybyConstants.TimeStep;

        while (currentT < targetT)
        {
            timeline.Add(currentT);
            float speed = GetClampedSplineSpeed(currentT, speedKnots, numSegments);
            currentT += Math.Max(speed, FlybyConstants.MinSpeed) * tickFactor;
        }

        currentT = Math.Min(currentT, targetT);
    }

    private static void EmitEaseOut(List<float> timeline, float[] speedKnots, int numSegments, float boundaryT, ref float currentT)
    {
        float easeStartT = currentT;
        float remainingDist = Math.Max(boundaryT - easeStartT, FlybyConstants.MinSpeed);
        float speed = GetClampedSplineSpeed(easeStartT, speedKnots, numSegments);
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

    private static void EmitEaseIn(List<float> timeline, float[] speedKnots, int numSegments, ref float currentT)
    {
        float speed = GetClampedSplineSpeed(currentT, speedKnots, numSegments);
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

    private static float GetClampedSplineSpeed(float t, float[] speedKnots, int numSegments)
    {
        float clampedT = Math.Clamp(t, 0, numSegments);
        float speed = Math.Max(CatmullRomSpline.Evaluate(clampedT, speedKnots), FlybyConstants.MinSpeed);

        int span = Math.Min((int)clampedT, numSegments - 1);

        if (span < 0)
            return speed;

        float p1 = Math.Max(speedKnots[span + 1], FlybyConstants.MinSpeed);
        float p2 = Math.Max(speedKnots[span + 2], FlybyConstants.MinSpeed);
        float minSpeed = Math.Min(p1, p2);
        float maxSpeed = Math.Max(p1, p2);

        return Math.Clamp(speed, minSpeed, maxSpeed);
    }
}