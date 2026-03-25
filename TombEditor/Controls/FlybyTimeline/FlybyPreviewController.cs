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

        SetPreviewActive(true);

        if (camera != null)
            _editor.CameraPreviewUpdated(camera);

        StateChanged?.Invoke();
    }

    public void ExitPreview()
    {
        if (!IsPreviewActive)
            return;

        StopPlayback();

        SetPreviewActive(false);

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

        EnsurePreviewActive();

        var camera = _editor.GetViewportCamera?.Invoke();

        if (camera == null)
            return;

        if (!TryEnsureValidCache(cameras, sequence, out var cache))
            return;

        var validCache = cache!;

        _playbackPreview?.Dispose();
        _playbackPreview = new FlybyPreview(validCache, camera);

        float startOffset = PlayheadSeconds > 0 ? validCache.TimelineToPlaybackTime(PlayheadSeconds) : 0;
        _playbackPreview.BeginExternalUpdate(startOffset);

        IsPlaying = true;

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FlybyConstants.PreviewTimerInterval)
        };

        _playbackTimer.Tick += (s, e) => OnPlaybackTick();
        _playbackTimer.Start();

        StateChanged?.Invoke();
    }

    public void StopPlayback()
    {
        if (!IsPlaying)
            return;

        StopPlaybackCore(true);
    }

    public void ScrubToTime(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, float timeSeconds)
    {
        if (cameras.Count == 0)
            return;

        if (IsPlaying)
            StopPlayback();

        EnsurePreviewActive();

        if (TryEnsureValidCache(cameras, sequence, out var cache))
        {
            var frame = cache!.SampleAtTime(timeSeconds);
            _editor.CameraPreviewScrub(frame);
        }
        else
        {
            int nearestIndex = FlybySequenceHelper.FindCameraIndexAtTime(cameras, timeSeconds);
            _editor.CameraPreviewUpdated(cameras[nearestIndex]);
        }

        SetPlayheadSeconds(timeSeconds);
    }

    /// <summary>
    /// Called when an external preview exit is detected via editor event (e.g. ESC key).
    /// </summary>
    public void OnExternalPreviewExit()
    {
        if (_isChangingPreview)
            return;

        StopPlaybackCore(false);
        SetPlayheadSeconds(-1.0f);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Returns the current sequence cache, building it if necessary.
    /// </summary>
    public FlybySequenceCache? GetOrBuildCache(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence)
    {
        TryEnsureValidCache(cameras, sequence, out _);
        return _cache;
    }

    public void InvalidateCache()
    {
        _cache = null;
    }

    /// <summary>
    /// Returns the interpolated frame at the given time in seconds (for camera insertion).
    /// </summary>
    public FlybyPreview.FrameState? GetInterpolatedFrameAtTime(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, float timeSeconds)
    {
        TryEnsureValidCache(cameras, sequence, out _);
        return _cache?.SampleAtTime(timeSeconds);
    }

    public void Dispose()
    {
        StopPlayback();
        InvalidateCache();

        if (IsPreviewActive)
            SetPreviewActive(false);
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
            SetPlayheadSeconds(_cache?.TotalDuration ?? 0);
            return;
        }

        _editor.CameraPreviewScrub(frame);

        SetPlayheadSeconds(_playbackPreview.GetCurrentTimeSeconds());
    }

    private bool TryEnsureValidCache(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, out FlybySequenceCache? cache)
    {
        if (_cache == null && _editor.Level != null)
        {
            bool useSmoothPause = _editor.Level.Settings.GameVersion == TRVersion.Game.TombEngine;
            _cache = new FlybySequenceCache(cameras, useSmoothPause);
        }

        cache = _cache;
        return cache != null && cache.IsValid;
    }

    private void EnsurePreviewActive()
    {
        if (!IsPreviewActive)
            SetPreviewActive(true);
    }

    private void SetPreviewActive(bool enabled)
    {
        _isChangingPreview = true;
        _editor.ToggleCameraPreview(enabled);
        _isChangingPreview = false;
    }

    private void SetPlayheadSeconds(float timeSeconds)
    {
        PlayheadSeconds = timeSeconds;
        PlayheadChanged?.Invoke();
    }

    private void StopPlaybackCore(bool raiseStateChanged)
    {
        _playbackTimer?.Stop();
        _playbackTimer = null;

        _playbackPreview?.Dispose();
        _playbackPreview = null;

        bool wasPlaying = IsPlaying;
        IsPlaying = false;

        if (raiseStateChanged && wasPlaying)
            StateChanged?.Invoke();
    }
}
