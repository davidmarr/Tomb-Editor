#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TombLib;
using TombLib.Graphics;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyManager;

/// <summary>
/// Pure static helpers for flyby sequence data queries and timecode calculations.
/// </summary>
public static class FlybySequenceData
{
    public const float GameTickRate = 30.0f;
    public const int FlagCameraCut = 1 << 7;
    public const int FlagFreezeCamera = 1 << 8;

    public static List<FlybyCameraInstance> GetCameras(Level level, ushort sequence)
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

    public static float GetSegmentDuration(FlybyCameraInstance camera)
    {
        float speed = camera.Speed;

        if (speed <= 0.001f)
            speed = 0.001f;

        return 1.0f / (speed * FlybyPreview.SpeedScale);
    }

    public static float GetFreezeDuration(FlybyCameraInstance camera)
    {
        if ((camera.Flags & FlagFreezeCamera) == 0 || camera.Timer <= 0)
            return 0;

        int gameFrames = Math.Max(0, camera.Timer >> 3);
        return gameFrames / GameTickRate;
    }

    public static float GetTimecodeForCamera(IReadOnlyList<FlybyCameraInstance> cameras, int index)
    {
        float time = 0;

        for (int i = 0; i < index && i < cameras.Count - 1; i++)
        {
            time += GetSegmentDuration(cameras[i]);
            time += GetFreezeDuration(cameras[i]);
        }

        return time;
    }

    public static float GetTotalDuration(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        if (cameras.Count < 2)
            return 0;

        float total = 0;

        for (int i = 0; i < cameras.Count - 1; i++)
        {
            total += GetSegmentDuration(cameras[i]);
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
    /// </summary>
    public static float TimeToProgress(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds)
    {
        if (cameras.Count < 2)
            return 0;

        float accumulatedTime = 0;
        int segmentCount = cameras.Count - 1;

        for (int i = 0; i < segmentCount; i++)
        {
            // Process freeze at camera i (camera pauses at its own position).
            float freezeSeconds = GetFreezeDuration(cameras[i]);

            if (freezeSeconds > 0)
            {
                if (accumulatedTime + freezeSeconds > timeSeconds)
                    return (float)i / segmentCount;

                accumulatedTime += freezeSeconds;
            }

            // Traverse segment from camera i to camera i+1.
            float segmentDuration = GetSegmentDuration(cameras[i]);

            if (accumulatedTime + segmentDuration > timeSeconds)
            {
                float localT = (timeSeconds - accumulatedTime) / segmentDuration;
                return (i + Math.Min(localT, 0.999f)) / segmentCount;
            }

            accumulatedTime += segmentDuration;
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

    /// <summary>
    /// Finds the room that contains the given world position.
    /// </summary>
    public static Room? FindRoomAtPosition(Level level, Vector3 worldPos)
    {
        foreach (var room in level.ExistingRooms)
        {
            var bb = room.WorldBoundingBox;

            if (worldPos.X >= bb.Minimum.X && worldPos.X <= bb.Maximum.X &&
                worldPos.Y >= bb.Minimum.Y && worldPos.Y <= bb.Maximum.Y &&
                worldPos.Z >= bb.Minimum.Z && worldPos.Z <= bb.Maximum.Z)
                return room;
        }

        return null;
    }
}
