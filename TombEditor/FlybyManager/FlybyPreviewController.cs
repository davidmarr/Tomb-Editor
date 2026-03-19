#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Threading;
using TombLib.Forms;
using TombLib.LevelData;

namespace TombEditor.FlybyManager;

/// <summary>
/// Manages flyby camera preview and playback lifecycle.
/// Communicates with Panel3D through Editor events.
/// </summary>
public class FlybyPreviewController : IDisposable
{
    private readonly Editor _editor;
    private readonly Dispatcher _dispatcher;

    private FlybyPreview? _scrubPreview;
    private DispatcherTimer? _playbackTimer;
    private DateTime _playbackStartTime;
    private float _playbackStartOffset;
    private bool _isChangingPreview;

    public bool IsPreviewActive { get; private set; }
    public bool IsPlaying { get; private set; }
    public float PlayheadSeconds { get; private set; } = -1.0f;

    /// <summary>
    /// Raised when preview or playback state changes.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// Raised when the playhead position changes.
    /// </summary>
    public event Action? PlayheadChanged;

    public FlybyPreviewController(Editor editor, Dispatcher dispatcher)
    {
        _editor = editor;
        _dispatcher = dispatcher;
    }

    public void EnterPreview(FlybyCameraInstance? camera)
    {
        if (_editor.FlyMode || IsPreviewActive)
            return;

        _isChangingPreview = true;
        _editor.ToggleCameraPreview(true);
        IsPreviewActive = true;
        _isChangingPreview = false;

        if (camera != null)
            _editor.CameraPreviewUpdated(camera);

        StateChanged?.Invoke();
    }

    public void ExitPreview()
    {
        if (!IsPreviewActive)
            return;

        StopPlayback();

        _isChangingPreview = true;
        _editor.ToggleCameraPreview(false);
        IsPreviewActive = false;
        _isChangingPreview = false;

        StateChanged?.Invoke();
    }

    public void TogglePreview(FlybyCameraInstance? camera)
    {
        if (IsPreviewActive)
            ExitPreview();
        else
            EnterPreview(camera);
    }

    public void ShowCamera(FlybyCameraInstance camera)
    {
        if (IsPreviewActive && !IsPlaying)
            _editor.CameraPreviewUpdated(camera);
    }

    public void StartPlayback(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence)
    {
        if (cameras.Count < 2)
        {
            _editor.SendMessage("Flyby sequence needs at least 2 cameras to play.", PopupType.Info);
            return;
        }

        if (!IsPreviewActive)
        {
            _isChangingPreview = true;
            _editor.ToggleCameraPreview(true);
            IsPreviewActive = true;
            _isChangingPreview = false;
        }

        EnsureScrubPreview(sequence);

        if (_scrubPreview == null)
            return;

        IsPlaying = true;
        _playbackStartOffset = PlayheadSeconds > 0 ? PlayheadSeconds : 0;
        _playbackStartTime = DateTime.UtcNow;

        // Capture cameras for the playback tick closure.
        var playbackCameras = cameras;

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _playbackTimer.Tick += (s, e) => OnPlaybackTick(playbackCameras);
        _playbackTimer.Start();

        StateChanged?.Invoke();
    }

    public void StopPlayback()
    {
        if (!IsPlaying)
            return;

        _playbackTimer?.Stop();
        _playbackTimer = null;
        IsPlaying = false;

        StateChanged?.Invoke();
    }

    public void ScrubToTime(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, float timeSeconds)
    {
        if (cameras.Count == 0)
            return;

        if (IsPlaying)
            StopPlayback();

        if (!IsPreviewActive)
            EnterPreview(null);

        EnsureScrubPreview(sequence);

        float totalDuration = FlybySequenceData.GetTotalDuration(cameras);

        if (_scrubPreview != null && totalDuration > 0 && cameras.Count >= 2)
        {
            float progress = Math.Clamp(FlybySequenceData.TimeToProgress(cameras, timeSeconds), 0, 1.0f);
            var frame = _scrubPreview.GetFrameAtProgress(progress);
            _editor.CameraPreviewScrub(frame);
        }
        else if (cameras.Count > 0)
        {
            int nearestIndex = FlybySequenceData.FindCameraIndexAtTime(cameras, timeSeconds);
            _editor.CameraPreviewUpdated(cameras[nearestIndex]);
        }

        PlayheadSeconds = timeSeconds;
        PlayheadChanged?.Invoke();
    }

    /// <summary>
    /// Called when an external preview exit is detected via editor event (e.g. ESC key).
    /// </summary>
    public void OnExternalPreviewExit()
    {
        if (_isChangingPreview)
            return;

        if (IsPlaying)
        {
            _playbackTimer?.Stop();
            _playbackTimer = null;
            IsPlaying = false;
        }

        PlayheadSeconds = -1.0f;
        IsPreviewActive = false;

        StateChanged?.Invoke();
        PlayheadChanged?.Invoke();
    }

    public void InvalidateScrubPreview()
    {
        _scrubPreview?.Dispose();
        _scrubPreview = null;
    }

    public FlybyPreview.FrameState? GetInterpolatedFrame(ushort sequence, float progress)
    {
        EnsureScrubPreview(sequence);
        return _scrubPreview?.GetFrameAtProgress(progress);
    }

    public void Dispose()
    {
        StopPlayback();
        InvalidateScrubPreview();

        if (IsPreviewActive)
        {
            _isChangingPreview = true;
            _editor.ToggleCameraPreview(false);
            IsPreviewActive = false;
            _isChangingPreview = false;
        }
    }

    private void OnPlaybackTick(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        if (_scrubPreview == null)
        {
            StopPlayback();
            return;
        }

        // Detect external preview exit.
        if (_editor.CameraPreviewMode == CameraPreviewType.None)
        {
            OnExternalPreviewExit();
            return;
        }

        float elapsed = _playbackStartOffset + (float)(DateTime.UtcNow - _playbackStartTime).TotalSeconds;
        float totalDuration = FlybySequenceData.GetTotalDuration(cameras);

        if (elapsed >= totalDuration)
        {
            StopPlayback();
            PlayheadSeconds = totalDuration;
            PlayheadChanged?.Invoke();
            return;
        }

        // Handle camera cut: if current camera has cut flag, jump to target camera.
        int currentIndex = FlybySequenceData.FindCameraIndexAtTime(cameras, elapsed);

        if (currentIndex >= 0 && currentIndex < cameras.Count &&
            (cameras[currentIndex].Flags & FlybySequenceData.FlagCameraCut) != 0)
        {
            int targetIndex = cameras[currentIndex].Timer;

            if (targetIndex >= 0 && targetIndex < cameras.Count && targetIndex != currentIndex)
            {
                float targetTime = FlybySequenceData.GetTimecodeForCamera(cameras, targetIndex);
                elapsed = targetTime;
                _playbackStartOffset = targetTime;
                _playbackStartTime = DateTime.UtcNow;
            }
        }

        float progress = FlybySequenceData.TimeToProgress(cameras, elapsed);
        var frame = _scrubPreview.GetFrameAtProgress(progress);
        _editor.CameraPreviewScrub(frame);

        PlayheadSeconds = elapsed;
        PlayheadChanged?.Invoke();
    }

    private void EnsureScrubPreview(ushort sequence)
    {
        if (_scrubPreview != null || _editor.Level == null)
            return;

        var camera = _editor.GetViewportCamera?.Invoke();

        if (camera == null)
            return;

        _scrubPreview = new FlybyPreview(_editor.Level, sequence, camera);

        if (_scrubPreview.IsFinished)
        {
            _scrubPreview.Dispose();
            _scrubPreview = null;
        }
    }
}
