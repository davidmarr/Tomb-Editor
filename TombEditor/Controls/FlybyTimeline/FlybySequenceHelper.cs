#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TombLib;
using TombLib.Graphics;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Pure static helpers for flyby sequence data queries and timecode calculations.
/// </summary>
public static class FlybySequenceHelper
{
    public static List<FlybyCameraInstance> GetCameras(Level level, int sequence)
    {
        return level.ExistingRooms
            .SelectMany(r => r.Objects.OfType<FlybyCameraInstance>())
            .Where(c => c.Sequence == sequence)
            .OrderBy(c => c.Number)
            .ToList();
    }

    public static HashSet<ushort> GetAllSequences(Level level)
    {
        var result = new HashSet<ushort>();

        foreach (var room in level.ExistingRooms)
            foreach (var cam in room.Objects.OfType<FlybyCameraInstance>())
                result.Add(cam.Sequence);

        return result;
    }

    public static float GetFreezeDuration(FlybyCameraInstance camera)
    {
        if ((camera.Flags & FlybyConstants.FlagFreezeCamera) == 0)
            return 0;

        // When the cut flag is set, Timer holds the target camera index, not freeze frames.
        if ((camera.Flags & FlybyConstants.FlagCameraCut) != 0)
            return 0;

        int frames = camera.TimerToFrames;
        return frames > 0 ? frames / FlybyConstants.TickRate : 0;
    }

    public static float GetSegmentDuration(FlybyCameraInstance camera)
    {
        float speed = camera.Speed;

        if (speed <= 0.001f)
            speed = 0.001f;

        return 1.0f / (speed * FlybyConstants.SpeedScale);
    }

    public static float GetTimecodeForCamera(IReadOnlyList<FlybyCameraInstance> cameras, int index)
    {
        if (index <= 0 || cameras.Count == 0)
            return 0;

        if (index >= cameras.Count)
            index = cameras.Count - 1;

        var speedKnots = BuildSpeedKnots(cameras);
        float time = 0;

        for (int i = 0; i < index && i < cameras.Count; i++)
        {
            if (i < cameras.Count - 1)
                time += GetSplineSegmentDuration(speedKnots, i, cameras.Count - 1);

            time += GetFreezeDuration(cameras[i]);
        }

        return time;
    }

    public static float GetTotalDuration(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        if (cameras.Count < 2)
            return 0;

        var speedKnots = BuildSpeedKnots(cameras);
        float total = 0;

        for (int i = 0; i < cameras.Count; i++)
        {
            if (i < cameras.Count - 1)
                total += GetSplineSegmentDuration(speedKnots, i, cameras.Count - 1);

            total += GetFreezeDuration(cameras[i]);
        }

        return total;
    }

    public static float GetDisplayDuration(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        return Math.Max(GetTotalDuration(cameras), 1.0f);
    }

    /// <summary>
    /// Converts a time in seconds to a normalized progress (0-1) on the spline.
    /// Freeze regions are skipped: scrubbing into a freeze zone maps to the camera boundary.
    /// </summary>
    public static float TimeToProgress(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds)
    {
        if (cameras.Count < 2)
            return 0;

        float accumulatedTime = 0;
        int segmentCount = cameras.Count - 1;

        for (int i = 0; i < segmentCount; i++)
        {
            float segmentDuration = GetSegmentDuration(cameras[i]);

            if (accumulatedTime + segmentDuration > timeSeconds)
            {
                float localT = (timeSeconds - accumulatedTime) / segmentDuration;
                return (i + Math.Min(localT, 0.999f)) / segmentCount;
            }

            accumulatedTime += segmentDuration;

            // Skip freeze region at camera i+1.
            float freeze = GetFreezeDuration(cameras[i + 1]);

            if (accumulatedTime + freeze > timeSeconds)
                return (float)(i + 1) / segmentCount;

            accumulatedTime += freeze;
        }

        return 1.0f;
    }

    public static int FindCameraIndexAtTime(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds)
    {
        int bestIndex = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < cameras.Count; i++)
        {
            float tc = GetTimecodeForCamera(cameras, i);
            float dist = Math.Abs(tc - timeSeconds);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    public static int FindInsertionIndex(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds)
    {
        for (int i = 0; i < cameras.Count - 1; i++)
        {
            float startTime = GetTimecodeForCamera(cameras, i);
            float endTime = GetTimecodeForCamera(cameras, i + 1);

            if (timeSeconds >= startTime && timeSeconds < endTime)
                return i + 1;
        }

        return cameras.Count;
    }

    public static string FormatTimecode(float seconds)
    {
        int totalCs = Math.Max(0, (int)(seconds * 100));
        int minutes = totalCs / 6000;
        int secs = (totalCs % 6000) / 100;
        int cs = totalCs % 100;
        return $"{minutes:D2}:{secs:D2}.{cs:D2}";
    }

    public static float SolveSegmentSpeedForTargetTime(IReadOnlyList<FlybyCameraInstance> cameras,
        int speedCameraIndex, int targetCameraIndex, float targetTimeSeconds)
    {
        if (speedCameraIndex < 0 || speedCameraIndex >= cameras.Count ||
            targetCameraIndex < 0 || targetCameraIndex >= cameras.Count)
            return cameras[Math.Clamp(speedCameraIndex, 0, cameras.Count - 1)].Speed;

        var targetCamera = cameras[speedCameraIndex];
        float originalSpeed = targetCamera.Speed;

        float low = FlybyConstants.MinSpeed;
        float high = Math.Max(originalSpeed, 1.0f);

        targetCamera.Speed = low;
        float lowTime = GetTimecodeForCamera(cameras, targetCameraIndex);

        targetCamera.Speed = high;
        float highTime = GetTimecodeForCamera(cameras, targetCameraIndex);

        while (highTime > targetTimeSeconds && high < 65535.0f / 655.0f)
        {
            high *= 2.0f;
            targetCamera.Speed = high;
            highTime = GetTimecodeForCamera(cameras, targetCameraIndex);
        }

        const int iterations = 24;

        for (int i = 0; i < iterations; i++)
        {
            float mid = (low + high) * 0.5f;
            targetCamera.Speed = mid;
            float midTime = GetTimecodeForCamera(cameras, targetCameraIndex);

            if (midTime > targetTimeSeconds)
                low = mid;
            else
                high = mid;
        }

        targetCamera.Speed = originalSpeed;
        return (low + high) * 0.5f;
    }

    /// <summary>
    /// Derives flyby RotationY, RotationX and FOV from the editor camera look direction.
    /// </summary>
    public static void ApplyEditorCameraRotation(Camera editorCamera, FlybyCameraInstance cam)
    {
        var cameraPos = editorCamera.GetPosition();
        var targetPos = editorCamera.GetTarget();
        var lookDir = targetPos - cameraPos;

        if (lookDir.LengthSquared() < 0.001f)
            return;

        lookDir = Vector3.Normalize(lookDir);

        float yaw = (float)Math.Atan2(lookDir.X, lookDir.Z);
        float pitch = -(float)Math.Asin(Math.Clamp(lookDir.Y, -1.0f, 1.0f));

        cam.RotationY = MathC.RadToDeg(yaw);
        cam.RotationX = -pitch * (180.0f / (float)Math.PI);
        cam.Fov = editorCamera.FieldOfView * (180.0f / (float)Math.PI);
    }

    private static float[] BuildSpeedKnots(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        var rawSpeed = new float[cameras.Count];

        for (int i = 0; i < cameras.Count; i++)
            rawSpeed[i] = Math.Max(cameras[i].Speed, FlybyConstants.MinSpeed);

        return CatmullRomSpline.PadKnots(rawSpeed);
    }

    private static float GetSplineSegmentDuration(float[] speedKnots, int segmentIndex, int numSegments)
    {
        const int sampleCount = 64;
        float integral = 0;

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float localT = (sampleIndex + 0.5f) / sampleCount;
            float splineT = Math.Clamp(segmentIndex + localT, 0, numSegments);
            float speed = GetClampedSplineSpeed(splineT, speedKnots, numSegments);
            integral += 1.0f / (speed * FlybyConstants.SpeedScale);
        }

        return integral / sampleCount;
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
