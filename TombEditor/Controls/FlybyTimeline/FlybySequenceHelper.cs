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
/// Static helpers for flyby sequence discovery, formatting, flag access, and editor-camera conversions.
/// Timing analysis is centralized in <see cref="FlybySequenceTiming"/>.
/// </summary>
public static class FlybySequenceHelper
{
    /// <summary>
    /// Returns all flyby cameras for the given sequence, ordered by camera number.
    /// </summary>
    /// <param name="level">The level whose rooms should be scanned.</param>
    /// <param name="sequence">The flyby sequence id to collect.</param>
    /// <returns>The ordered flyby cameras that belong to the requested sequence.</returns>
    public static IReadOnlyList<FlybyCameraInstance> GetCameras(Level level, int sequence)
    {
        return [.. level.ExistingRooms
            .SelectMany(room => room.Objects.OfType<FlybyCameraInstance>())
            .Where(camera => camera.Sequence == sequence)
            .OrderBy(camera => camera.Number)];
    }

    /// <summary>
    /// Collects all sequence ids currently used by flyby cameras in the level.
    /// </summary>
    /// <param name="level">The level whose rooms should be scanned.</param>
    /// <returns>The distinct sequence ids currently used by flyby cameras.</returns>
    public static IReadOnlySet<ushort> GetAllSequences(Level level)
    {
        var result = new HashSet<ushort>();

        foreach (var room in level.ExistingRooms)
        {
            foreach (var camera in room.Objects.OfType<FlybyCameraInstance>())
                result.Add(camera.Sequence);
        }

        return result;
    }

    /// <summary>
    /// Returns the freeze duration in seconds for a camera, if it has one.
    /// </summary>
    /// <param name="camera">The flyby camera whose freeze duration should be evaluated.</param>
    /// <returns>The freeze duration in seconds, or <c>0.0f</c> when the camera does not freeze.</returns>
    public static float GetFreezeDuration(FlybyCameraInstance camera)
    {
        if ((camera.Flags & FlybyConstants.FlagFreezeCamera) == 0)
            return 0.0f;

        // When the cut flag is set, Timer holds the target camera index, not freeze frames.
        if ((camera.Flags & FlybyConstants.FlagCameraCut) != 0)
            return 0.0f;

        int frames = camera.TimerToFrames;
        return frames > 0 ? frames / FlybyConstants.TickRate : 0.0f;
    }

    /// <summary>
    /// Returns the timeline time for a camera index using precomputed sequence timing.
    /// </summary>
    /// <param name="cameras">Ordered flyby cameras in the active sequence.</param>
    /// <param name="index">Camera index whose timeline time should be returned.</param>
    /// <param name="timing">Precomputed timing for the provided sequence.</param>
    /// <returns>The timeline time for the requested camera, or 0 when unavailable.</returns>
    public static float GetTimecodeForCamera(IReadOnlyList<FlybyCameraInstance> cameras, int index, FlybySequenceTiming timing)
    {
        if (index < 0 || cameras.Count == 0)
            return 0.0f;

        return timing.GetCameraTime(index);
    }

    /// <summary>
    /// Finds the camera whose timecode is closest to the given timeline time using precomputed timing.
    /// </summary>
    /// <param name="cameras">Ordered flyby cameras in the active sequence.</param>
    /// <param name="timeSeconds">Timeline time to test against.</param>
    /// <param name="timing">Precomputed timing for the provided sequence.</param>
    /// <returns>The index of the closest camera in timeline time, or <c>0</c> when the list is empty or the input time is not finite.</returns>
    public static int FindCameraIndexAtTime(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds, FlybySequenceTiming timing)
    {
        if (cameras.Count == 0 || !float.IsFinite(timeSeconds))
            return 0;

        int bestIndex = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < cameras.Count; i++)
        {
            float timecode = timing.GetCameraTime(i);
            float distance = MathF.Abs(timecode - timeSeconds);

            if (distance < bestDist)
            {
                bestDist = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Finds the insertion index for a new camera at the given timeline time using precomputed timing.
    /// </summary>
    /// <param name="cameras">Ordered flyby cameras in the active sequence.</param>
    /// <param name="timeSeconds">Timeline time where the new camera should be inserted.</param>
    /// <param name="timing">Precomputed timing for the provided sequence.</param>
    /// <returns>The list index where the new camera should be inserted, or the current camera count when no interior insertion point matches.</returns>
    public static int FindInsertionIndex(IReadOnlyList<FlybyCameraInstance> cameras, float timeSeconds, FlybySequenceTiming timing)
    {
        if (cameras.Count == 0 || !float.IsFinite(timeSeconds))
            return cameras.Count;

        for (int i = 0; i < cameras.Count - 1; i++)
        {
            float startTime = timing.GetCameraTime(i);
            float endTime = timing.GetCameraTime(i + 1);

            if (timeSeconds >= startTime && timeSeconds < endTime)
                return i + 1;
        }

        return cameras.Count;
    }

    /// <summary>
    /// Formats seconds as an MM:SS.CC timecode string.
    /// </summary>
    /// <param name="seconds">The time to format, in seconds.</param>
    /// <returns>The formatted timecode string. Non-finite values are formatted as <c>00:00.00</c>.</returns>
    public static string FormatTimecode(float seconds)
        => FormatCentisecondTime(seconds, true, false);

    /// <summary>
    /// Formats seconds for timeline ruler labels.
    /// </summary>
    /// <param name="seconds">The time to format, in seconds.</param>
    /// <returns>The formatted ruler label string. Non-finite values are formatted as <c>0.00</c>.</returns>
    public static string FormatRulerLabel(float seconds)
        => FormatCentisecondTime(seconds, false, true);

    /// <summary>
    /// Returns whether the provided bit index is valid for a <see langword="ushort"/> value.
    /// </summary>
    /// <param name="bit">Zero-based bit index to validate.</param>
    /// <returns><see langword="true"/> when the bit index is within the valid <see langword="ushort"/> range; otherwise <see langword="false"/>.</returns>
    public static bool IsValidFlagBit(int bit)
        => bit >= 0 && bit < sizeof(ushort) * 8;

    /// <summary>
    /// Returns whether a bit is set in a <see langword="ushort"/> flag value.
    /// </summary>
    /// <param name="flags">Flag value to inspect.</param>
    /// <param name="bit">Zero-based bit index to test.</param>
    /// <returns><see langword="true"/> when the bit is set; otherwise <see langword="false"/>.</returns>
    public static bool GetFlagBit(ushort flags, int bit)
    {
        if (!IsValidFlagBit(bit))
            return false;

        return (flags & (1 << bit)) != 0;
    }

    /// <summary>
    /// Sets or clears a bit in a <see langword="ushort"/> flag value.
    /// </summary>
    /// <param name="flags">Flag value to update.</param>
    /// <param name="bit">Zero-based bit index to modify.</param>
    /// <param name="value"><see langword="true"/> to set the bit; otherwise <see langword="false"/>.</param>
    /// <returns>The updated flag value, or the original value when the bit index is invalid.</returns>
    public static ushort SetFlagBit(ushort flags, int bit, bool value)
    {
        if (!IsValidFlagBit(bit))
            return flags;

        return value
            ? (ushort)(flags | (1 << bit))
            : (ushort)(flags & ~(1 << bit));
    }

    /// <summary>
    /// Solves the segment speed needed to reach a target camera at the requested time.
    /// </summary>
    /// <param name="cameras">Ordered flyby cameras in the active sequence.</param>
    /// <param name="speedCameraIndex">Index of the camera whose outgoing speed should be adjusted.</param>
    /// <param name="targetCameraIndex">Index of the camera that should land on the requested time.</param>
    /// <param name="targetTimeSeconds">Desired timeline time for the target camera.</param>
    /// <param name="useSmoothPause">Whether TombEngine smooth-pause timing should be applied.</param>
    /// <returns>The solved speed value, clamped to the supported flyby speed range.</returns>
    public static float SolveSegmentSpeedForTargetTime(IReadOnlyList<FlybyCameraInstance> cameras,
        int speedCameraIndex, int targetCameraIndex, float targetTimeSeconds, bool useSmoothPause)
    {
        if (cameras.Count == 0)
            return FlybyConstants.MinSpeed;

        float fallbackSpeed = GetClampedSpeed(cameras[Math.Clamp(speedCameraIndex, 0, cameras.Count - 1)].Speed);

        if (!float.IsFinite(targetTimeSeconds))
            return fallbackSpeed;

        if (speedCameraIndex < 0 || speedCameraIndex >= cameras.Count || targetCameraIndex < 0 || targetCameraIndex >= cameras.Count)
            return fallbackSpeed;

        if (targetCameraIndex <= speedCameraIndex)
            return fallbackSpeed;

        float low = FlybyConstants.MinSpeed;
        float high = Math.Min(Math.Max(fallbackSpeed, 1.0f), FlybyConstants.MaxSpeed);

        float lowTime = FlybySequenceTiming.GetCameraTimeForSpeed(cameras,
            targetCameraIndex, useSmoothPause, speedCameraIndex, low);

        if (lowTime <= targetTimeSeconds)
            return low;

        float highTime = FlybySequenceTiming.GetCameraTimeForSpeed(cameras,
            targetCameraIndex, useSmoothPause, speedCameraIndex, high);

        while (highTime > targetTimeSeconds && high < FlybyConstants.MaxSpeed)
        {
            high = Math.Min(high * 2.0f, FlybyConstants.MaxSpeed);
            highTime = FlybySequenceTiming.GetCameraTimeForSpeed(cameras,
                targetCameraIndex, useSmoothPause, speedCameraIndex, high);
        }

        if (highTime > targetTimeSeconds)
            return FlybyConstants.MaxSpeed;

        const int iterations = 24;

        for (int i = 0; i < iterations; i++)
        {
            float mid = (low + high) * 0.5f;
            float midTime = FlybySequenceTiming.GetCameraTimeForSpeed(cameras,
                targetCameraIndex, useSmoothPause, speedCameraIndex, mid);

            if (midTime > targetTimeSeconds)
                low = mid;
            else
                high = mid;
        }

        return (low + high) * 0.5f;
    }

    /// <summary>
    /// Derives flyby RotationY, RotationX and FOV from the editor camera look direction.
    /// </summary>
    /// <param name="editorCamera">Viewport camera whose orientation should be copied.</param>
    /// <param name="flyby">Flyby camera receiving the derived rotation and field of view.</param>
    public static void ApplyEditorCameraRotation(Camera editorCamera, FlybyCameraInstance flyby)
    {
        var cameraPos = editorCamera.GetPosition();
        var targetPos = editorCamera.GetTarget();
        var lookDir = targetPos - cameraPos;
        float fieldOfView = editorCamera.FieldOfView;

        if (!lookDir.IsFinite())
            return;

        if (!float.IsFinite(fieldOfView))
            return;

        if (lookDir.LengthSquared() < FlybyConstants.LookDirectionLengthSquaredEpsilon)
            return;

        lookDir = Vector3.Normalize(lookDir);

        float yaw = MathF.Atan2(lookDir.X, lookDir.Z);
        float pitch = -MathF.Asin(Math.Clamp(lookDir.Y, -1.0f, 1.0f));

        flyby.RotationY = MathC.SignedRadToDeg(yaw);
        flyby.RotationX = -MathC.SignedRadToDeg(pitch);
        flyby.Fov = MathC.SignedRadToDeg(fieldOfView);
    }

    private static float GetClampedSpeed(float speed)
    {
        if (!float.IsFinite(speed))
            return FlybyConstants.MinSpeed;

        return Math.Clamp(speed, FlybyConstants.MinSpeed, FlybyConstants.MaxSpeed);
    }

    private static string FormatCentisecondTime(float seconds, bool alwaysShowMinutes, bool roundToNearestCentisecond)
    {
        if (!float.IsFinite(seconds))
            return alwaysShowMinutes ? "00:00.00" : "0.00";

        int totalCentiseconds = roundToNearestCentisecond
            ? Math.Max(0, (int)MathF.Round(seconds * 100.0f))
            : Math.Max(0, (int)(seconds * 100.0f));

        int minutes = totalCentiseconds / 6000;
        int secs = (totalCentiseconds % 6000) / 100;
        int cs = totalCentiseconds % 100;

        if (alwaysShowMinutes)
            return $"{minutes:D2}:{secs:D2}.{cs:D2}";

        if (minutes > 0)
            return $"{minutes}:{secs:D2}.{cs:D2}";

        return $"{secs}.{cs:D2}";
    }
}
