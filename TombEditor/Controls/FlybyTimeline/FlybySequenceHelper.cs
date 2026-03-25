#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TombLib;
using TombLib.Graphics;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Static helpers for flyby sequence discovery, formatting, and editor-camera conversions.
/// Timing analysis is centralized in <see cref="FlybySequenceTiming"/>.
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

    public static FlybySequenceTiming AnalyzeSequence(IReadOnlyList<FlybyCameraInstance> cameras, bool useSmoothPause)
    {
        return FlybySequenceTiming.Build(cameras, useSmoothPause);
    }

    public static float GetTimecodeForCamera(IReadOnlyList<FlybyCameraInstance> cameras, int index, bool useSmoothPause)
    {
        if (index <= 0 || cameras.Count == 0)
            return 0;

        return AnalyzeSequence(cameras, useSmoothPause).GetCameraTime(index);
    }

    /// <summary>
    /// Converts a time in seconds to a normalized progress (0-1) on the spline.
    /// Freeze regions are skipped: scrubbing into a freeze zone maps to the camera boundary.
    /// </summary>
    public static float TimeToProgress(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds, bool useSmoothPause)
    {
        if (cameras.Count < 2)
            return 0;

        var timing = AnalyzeSequence(cameras, useSmoothPause);
        int segmentCount = cameras.Count - 1;

        for (int i = 0; i < segmentCount; i++)
        {
            float accumulatedTime = timing.GetCameraTime(i);
            float segmentDuration = timing.GetSegmentDuration(i);

            if (accumulatedTime + segmentDuration > timeSeconds)
            {
                float localT = (timeSeconds - accumulatedTime) / segmentDuration;
                return (i + Math.Min(localT, 0.999f)) / segmentCount;
            }

            // Skip freeze region at camera i+1.
            float freeze = timing.GetFreezeDuration(i + 1);

            if (accumulatedTime + segmentDuration + freeze > timeSeconds)
                return (float)(i + 1) / segmentCount;
        }

        return 1.0f;
    }

    public static int FindCameraIndexAtTime(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds, bool useSmoothPause)
    {
        int bestIndex = 0;
        float bestDist = float.MaxValue;
        var timing = AnalyzeSequence(cameras, useSmoothPause);

        for (int i = 0; i < cameras.Count; i++)
        {
            float tc = timing.GetCameraTime(i);
            float dist = Math.Abs(tc - timeSeconds);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    public static int FindInsertionIndex(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds, bool useSmoothPause)
    {
        var timing = AnalyzeSequence(cameras, useSmoothPause);

        for (int i = 0; i < cameras.Count - 1; i++)
        {
            float startTime = timing.GetCameraTime(i);
            float endTime = timing.GetCameraTime(i + 1);

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
        int speedCameraIndex, int targetCameraIndex, float targetTimeSeconds, bool useSmoothPause)
    {
        if (speedCameraIndex < 0 || speedCameraIndex >= cameras.Count ||
            targetCameraIndex < 0 || targetCameraIndex >= cameras.Count)
            return cameras[Math.Clamp(speedCameraIndex, 0, cameras.Count - 1)].Speed;

        var targetCamera = cameras[speedCameraIndex];
        float originalSpeed = targetCamera.Speed;

        float low = FlybyConstants.MinSpeed;
        float high = Math.Max(originalSpeed, 1.0f);

        targetCamera.Speed = low;
        float lowTime = GetTimecodeForCamera(cameras, targetCameraIndex, useSmoothPause);

        targetCamera.Speed = high;
        float highTime = GetTimecodeForCamera(cameras, targetCameraIndex, useSmoothPause);

        while (highTime > targetTimeSeconds && high < 65535.0f / 655.0f)
        {
            high *= 2.0f;
            targetCamera.Speed = high;
            highTime = GetTimecodeForCamera(cameras, targetCameraIndex, useSmoothPause);
        }

        const int iterations = 24;

        for (int i = 0; i < iterations; i++)
        {
            float mid = (low + high) * 0.5f;
            targetCamera.Speed = mid;
            float midTime = GetTimecodeForCamera(cameras, targetCameraIndex, useSmoothPause);

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
}
