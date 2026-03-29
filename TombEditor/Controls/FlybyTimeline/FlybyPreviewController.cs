#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using TombLib.Forms;
using TombLib.LevelData;

namespace TombEditor.Controls.FlybyTimeline;

/// <summary>
/// Manages flyby camera preview and playback lifecycle.
/// Scrubbing and playback are backed by a pre-calculated <see cref="FlybySequenceCache"/>;
/// all time-to-frame mapping is a simple array lookup with linear interpolation.
/// </summary>
public sealed class FlybyPreviewController(Editor editor) : IDisposable
{
    private readonly Editor _editor = editor;

    private FlybySequenceCache? _cache;
    private ushort? _cacheSequence;
    private FlybyCameraInstance[]? _cacheCameras;
    private FlybyPreview? _playbackPreview;
    private DispatcherTimer? _playbackTimer;
    private bool _isChangingPreview;

    private bool UseSmoothPause => _editor.Level?.Settings.GameVersion == TRVersion.Game.TombEngine;

    /// <summary>
    /// Gets whether the editor is currently in any camera preview mode.
    /// </summary>
    public bool IsPreviewActive => _editor.CameraPreviewMode != CameraPreviewType.None;

    /// <summary>
    /// Gets whether timed sequence playback is currently running.
    /// </summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Gets the current timeline playhead position in seconds.
    /// </summary>
    public float PlayheadSeconds { get; private set; } = -1.0f;

    /// <summary>
    /// Raised when preview or playback state changes.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// Raised when the playhead position changes.
    /// </summary>
    public event Action? PlayheadChanged;

    /// <summary>
    /// Enters static preview mode and optionally shows the given camera.
    /// </summary>
    /// <param name="camera">The flyby camera to show after entering preview mode, or <see langword="null"/> to keep the current preview frame.</param>
    public void EnterPreview(FlybyCameraInstance? camera)
    {
        var previousMode = _editor.CameraPreviewMode;

        if (!EnsureStaticPreviewActive())
            return;

        if (camera is not null)
            _editor.CameraPreviewUpdated(camera);

        if (previousMode != CameraPreviewType.Static)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// Exits preview mode and stops any active playback.
    /// </summary>
    public void ExitPreview()
    {
        if (!IsPreviewActive)
            return;

        StopPlaybackCore(false);
        SetPreviewActive(false);

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Toggles static preview mode for the given camera.
    /// </summary>
    /// <param name="camera">The flyby camera to show when preview is enabled, or <see langword="null"/> to leave the current preview frame unchanged.</param>
    public void TogglePreview(FlybyCameraInstance? camera)
    {
        if (IsPreviewActive)
            ExitPreview();
        else
            EnterPreview(camera);
    }

    /// <summary>
    /// Updates the shown camera while static preview is active.
    /// </summary>
    /// <param name="camera">The flyby camera to display.</param>
    public void ShowCamera(FlybyCameraInstance camera)
    {
        if (!IsPreviewActive || IsPlaying)
            return;

        if (!EnsureStaticPreviewActive())
            return;

        _editor.CameraPreviewUpdated(camera);
    }

    /// <summary>
    /// Starts playback for the provided sequence.
    /// </summary>
    /// <param name="cameras">The flyby cameras belonging to the sequence.</param>
    /// <param name="sequence">The sequence number to preview.</param>
    public void StartPlayback(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence)
    {
        if (cameras.Count < 2)
        {
            _editor.SendMessage("Flyby sequence needs at least 2 cameras to play.", PopupType.Info);
            return;
        }

        if (!EnsureStaticPreviewActive())
            return;

        var camera = _editor.GetViewportCamera?.Invoke();

        if (camera is null)
            return;

        if (!TryEnsureValidCache(cameras, sequence, out var cache))
            return;

        if (IsPlaying)
            StopPlaybackCore(false);

        _playbackPreview?.Dispose();
        _playbackPreview = new FlybyPreview(cache, camera);

        bool restartFromBeginning = PlayheadSeconds >= cache.TotalDuration - FlybyConstants.PreviewReplayEndTolerance;
        float startOffset = !restartFromBeginning && PlayheadSeconds > 0.0f
            ? cache.TimelineToPlaybackTime(PlayheadSeconds)
            : 0.0f;

        _playbackPreview.BeginExternalUpdate(startOffset);

        if (restartFromBeginning)
            SetPlayheadSeconds(0.0f);

        IsPlaying = true;

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FlybyConstants.PreviewTimerInterval)
        };

        _playbackTimer.Tick += OnPlaybackTimerTick;
        _playbackTimer.Start();

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Stops active playback if it is running.
    /// </summary>
    public void StopPlayback()
    {
        if (!IsPlaying)
            return;

        StopPlaybackCore(true);
    }

    /// <summary>
    /// Scrubs preview playback to a specific time in the sequence.
    /// </summary>
    /// <param name="cameras">The flyby cameras belonging to the sequence.</param>
    /// <param name="sequence">The sequence number to sample.</param>
    /// <param name="timeSeconds">The timeline time, in seconds, to preview.</param>
    public void ScrubToTime(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, float timeSeconds)
    {
        if (cameras.Count == 0)
            return;

        if (!float.IsFinite(timeSeconds))
            return;

        if (IsPlaying)
            StopPlayback();

        if (!EnsureStaticPreviewActive())
            return;

        if (TryEnsureValidCache(cameras, sequence, out var cache))
        {
            var frame = cache.SampleAtTime(timeSeconds);
            _editor.CameraPreviewScrub(frame);
        }
        else
        {
            var timing = FlybySequenceTiming.Build(cameras, UseSmoothPause);
            int nearestIndex = FlybySequenceHelper.FindCameraIndexAtTime(cameras, timeSeconds, timing);
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

        bool wasPreviewActive = IsPreviewActive;
        bool wasPlaying = IsPlaying;

        if (!wasPreviewActive && !wasPlaying && PlayheadSeconds < 0.0f)
            return;

        StopPlaybackCore(false);
        SetPlayheadSeconds(-1.0f);

        if (wasPreviewActive || wasPlaying)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// Returns the current sequence cache, building it if necessary.
    /// </summary>
    /// <param name="cameras">The flyby cameras belonging to the sequence.</param>
    /// <param name="sequence">The sequence number to resolve.</param>
    /// <returns>The matching sequence cache, or <see langword="null"/> when no valid cache can be produced.</returns>
    public FlybySequenceCache? GetOrBuildCache(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence)
        => TryEnsureValidCache(cameras, sequence, out var cache) ? cache : null;

    /// <summary>
    /// Invalidates the current cache (e.g. after camera edits) and forces a rebuild on next access.
    /// Does not raise any events or change preview state by itself.
    /// </summary>
    public void InvalidateCache()
    {
        _cache = null;
        _cacheSequence = null;
        _cacheCameras = null;
    }

    /// <summary>
    /// Returns the interpolated frame at the given time in seconds (for camera insertion).
    /// </summary>
    /// <param name="cameras">The flyby cameras belonging to the sequence.</param>
    /// <param name="sequence">The sequence number to sample.</param>
    /// <param name="timeSeconds">The timeline time, in seconds, to sample.</param>
    /// <returns>The interpolated frame, or <see langword="null"/> when no valid cache can be produced.</returns>
    public FlybyPreview.FrameState? GetInterpolatedFrameAtTime(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, float timeSeconds)
    {
        if (!float.IsFinite(timeSeconds))
            return null;

        if (!TryEnsureValidCache(cameras, sequence, out var cache))
            return null;

        return cache.SampleAtTime(timeSeconds);
    }

    /// <summary>
    /// Releases preview state and playback resources.
    /// </summary>
    public void Dispose()
    {
        StopPlaybackCore(false);
        InvalidateCache();

        if (IsPreviewActive)
            SetPreviewActive(false);
    }

    /// <summary>
    /// Ensures the editor is in static preview mode before preview updates are sent.
    /// </summary>
    private bool EnsureStaticPreviewActive()
    {
        if (_editor.FlyMode)
            return false;

        if (_editor.CameraPreviewMode == CameraPreviewType.Static)
            return true;

        if (IsPreviewActive)
            SetPreviewActive(false);

        SetPreviewActive(true);
        return _editor.CameraPreviewMode == CameraPreviewType.Static;
    }

    /// <summary>
    /// Toggles the editor camera preview state while suppressing feedback loops.
    /// </summary>
    private void SetPreviewActive(bool enabled)
    {
        _isChangingPreview = true;

        try
        {
            _editor.ToggleCameraPreview(enabled);
        }
        finally
        {
            _isChangingPreview = false;
        }
    }

    /// <summary>
    /// Advances playback and updates the editor preview frame.
    /// </summary>
    private void OnPlaybackTick()
    {
        if (_playbackPreview is null)
        {
            StopPlaybackCore(true);
            return;
        }

        if (_editor.CameraPreviewMode != CameraPreviewType.Static)
        {
            OnExternalPreviewExit();
            return;
        }

        var frame = _playbackPreview.Update();

        if (frame.Finished)
        {
            float totalDuration = _playbackPreview.Cache.TotalDuration; // Cache before stopping playback, as preview dispose will invalidate the cache reference.
            StopPlayback();

            SetPlayheadSeconds(totalDuration);
            return;
        }

        _editor.CameraPreviewScrub(frame);

        SetPlayheadSeconds(_playbackPreview.GetCurrentTimeSeconds());
    }

    /// <summary>
    /// Forwards timer ticks to the playback update routine.
    /// </summary>
    private void OnPlaybackTimerTick(object? sender, EventArgs e)
        => OnPlaybackTick();

    /// <summary>
    /// Updates the cached playhead position and notifies listeners.
    /// </summary>
    private void SetPlayheadSeconds(float timeSeconds)
    {
        if (!float.IsFinite(timeSeconds))
            timeSeconds = -1.0f;

        if (PlayheadSeconds == timeSeconds)
            return;

        PlayheadSeconds = timeSeconds;
        PlayheadChanged?.Invoke();
    }

    /// <summary>
    /// Stops timer-driven playback and optionally raises a state change event.
    /// </summary>
    private void StopPlaybackCore(bool raiseStateChanged)
    {
        if (_playbackTimer is not null)
        {
            _playbackTimer.Stop();
            _playbackTimer.Tick -= OnPlaybackTimerTick;
            _playbackTimer = null;
        }

        _playbackPreview?.Dispose();
        _playbackPreview = null;

        bool wasPlaying = IsPlaying;
        IsPlaying = false;

        if (raiseStateChanged && wasPlaying)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// Ensures the cache matches the current sequence and camera list.
    /// </summary>
    private bool TryEnsureValidCache(IReadOnlyList<FlybyCameraInstance> cameras, ushort sequence, [NotNullWhen(true)] out FlybySequenceCache? cache)
    {
        bool cacheMatches = _cache is not null && _cacheSequence == sequence && MatchesCachedCameras(cameras);

        if (!cacheMatches)
        {
            if (_editor.Level is null)
            {
                InvalidateCache();
            }
            else
            {
                _cache = new FlybySequenceCache(cameras, UseSmoothPause);
                _cacheSequence = sequence;
                _cacheCameras = [.. cameras];
            }
        }

        cache = _cache;
        return cache?.IsValid == true;
    }

    /// <summary>
    /// Checks whether the current camera list matches the cached camera references.
    /// </summary>
    private bool MatchesCachedCameras(IReadOnlyList<FlybyCameraInstance> cameras)
    {
        var cachedCameras = _cacheCameras;

        if (cachedCameras is null || cachedCameras.Length != cameras.Count)
            return false;

        for (int i = 0; i < cameras.Count; i++)
        {
            if (!ReferenceEquals(cachedCameras[i], cameras[i]))
                return false;
        }

        return true;
    }
}
