#nullable enable

using System;
using System.Collections.Generic;
using System.Windows.Threading;
using TombLib.Forms;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Manages flyby camera preview and playback lifecycle.
/// Scrubbing and playback are backed by a pre-calculated <see cref="FlybySequenceCache"/>;
/// all time-to-frame mapping is a simple array lookup with linear interpolation.
/// </summary>
public class FlybyPreviewController : IDisposable
{
    private readonly Editor _editor;

    private FlybySequenceCache? _cache;
    private FlybyPreview? _playbackPreview;
    private DispatcherTimer? _playbackTimer;
    private bool _isChangingPreview;

    public bool IsPreviewActive => _editor.CameraPreviewMode != CameraPreviewType.None;
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

    public FlybyPreviewController(Editor editor)
    {
        _editor = editor;
    }

    public void EnterPreview(FlybyCameraInstance? camera)
    {
        if (_editor.FlyMode || IsPreviewActive)
            return;

        _isChangingPreview = true;
        _editor.ToggleCameraPreview(true);
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
            _isChangingPreview = false;
        }

        var camera = _editor.GetViewportCamera?.Invoke();

        if (camera == null)
            return;

        EnsureCache(cameras, sequence);

        if (_cache == null || !_cache.IsValid)
            return;

        _playbackPreview?.Dispose();
        _playbackPreview = new FlybyPreview(_cache, camera);

        float startOffset = PlayheadSeconds > 0 ? _cache.TimelineToPlaybackTime(PlayheadSeconds) : 0;
        _playbackPreview.BeginExternalUpdate(startOffset);

        IsPlaying = true;

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _playbackTimer.Tick += (s, e) => OnPlaybackTick();
        _playbackTimer.Start();

        StateChanged?.Invoke();
    }

    public void StopPlayback()
    {
        if (!IsPlaying)
            return;

        _playbackTimer?.Stop();
        _playbackTimer = null;

        _playbackPreview?.Dispose();
        _playbackPreview = null;

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

        EnsureCache(cameras, sequence);

        if (_cache != null && _cache.IsValid)
        {
            var frame = _cache.SampleAtTime(timeSeconds);
            _editor.CameraPreviewScrub(frame);
        }
        else if (cameras.Count > 0)
        {
            int nearestIndex = FlybySequenceHelper.FindCameraIndexAtTime(cameras, timeSeconds);
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
            _playbackPreview?.Dispose();
            _playbackPreview = null;
            IsPlaying = false;
        }

        PlayheadSeconds = -1.0f;

        StateChanged?.Invoke();
        PlayheadChanged?.Invoke();
    }

    /// <summary>
    /// Returns the current sequence cache, building it if necessary.
    /// </summary>
    public FlybySequenceCache? GetOrBuildCache(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence)
    {
        EnsureCache(cameras, sequence);
        return _cache;
    }

    public void InvalidateCache()
    {
        _cache = null;
    }

    /// <summary>
    /// Returns the interpolated frame at the given time in seconds (for camera insertion).
    /// </summary>
    public FlybyPreview.FrameState? GetInterpolatedFrameAtTime(
        IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, float timeSeconds)
    {
        EnsureCache(cameras, sequence);
        return _cache?.SampleAtTime(timeSeconds);
    }

    public void Dispose()
    {
        StopPlayback();
        InvalidateCache();

        if (IsPreviewActive)
        {
            _isChangingPreview = true;
            _editor.ToggleCameraPreview(false);
            _isChangingPreview = false;
        }
    }

    private void OnPlaybackTick()
    {
        if (_playbackPreview == null)
        {
            StopPlayback();
            return;
        }

        if (_editor.CameraPreviewMode == CameraPreviewType.None)
        {
            OnExternalPreviewExit();
            return;
        }

        var frame = _playbackPreview.Update();

        if (frame.Finished)
        {
            StopPlayback();
            PlayheadSeconds = _cache?.TotalDuration ?? 0;
            PlayheadChanged?.Invoke();
            return;
        }

        _editor.CameraPreviewScrub(frame);

        PlayheadSeconds = _playbackPreview.GetCurrentTimeSeconds();
        PlayheadChanged?.Invoke();
    }

    private void EnsureCache(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence)
    {
        if (_cache != null || _editor.Level == null)
            return;

        bool useSmoothPause = _editor.Level.Settings.GameVersion == TRVersion.Game.TombEngine;
        _cache = new FlybySequenceCache(cameras, useSmoothPause);
    }
}
