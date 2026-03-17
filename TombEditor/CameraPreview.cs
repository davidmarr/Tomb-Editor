using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using TombLib;
using TombLib.Graphics;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor
{
    /// <summary>
    /// Handles camera preview by interpolating position, target, FOV, roll, and speed
    /// using a Catmull-Rom spline for sequence playback, or by providing a static frame
    /// for single-camera preview.
    /// </summary>
    public class CameraPreview
    {
        public struct FrameState
        {
            public Vector3 Position;
            public float RotationY;
            public float RotationX;
            public float Roll;
            public float Fov;
            public bool Finished;
        }

        private enum PausePhase
        { 
            None, 
            EaseOut, 
            Hold, 
            EaseIn
        }

        // The game logic runs at 30 ticks per second.
        private const float GameTickRate = 30.0f;

        // Speed conversion: editor speed S is compiled as S * 655, game advances
        // CurrentSplinePosition (0-65536) by that per frame at 30 fps.
        private const float SpeedScale = (float)(ushort.MaxValue / 100) * GameTickRate / (float)ushort.MaxValue;

        // Distance from camera to target point, matching the level compiler.
        private const float TargetDistance = Level.SectorSizeUnit;

        // SCF flag bits.
        private const int FlagStopMovement = 1 << 8;
        private const int FlagCutToCam     = 1 << 7;

        // TombEngine smooth pause constants.
        private const float EaseDistance = 0.15f;
        private const float MinSpeed = 0.001f;

        // Catmull-Rom knot arrays (padded via CatmullRomSpline.PadKnots).
        private readonly float[] _posX, _posY, _posZ;
        private readonly float[] _tgtX, _tgtY, _tgtZ;
        private readonly float[] _rollKnots, _fovKnots, _speedKnots;

        // Per-camera flags and timers.
        private readonly ushort[] _cameraFlags;
        private readonly short[] _cameraTimers;
        private readonly int _numCameras;
        private readonly int _numSegments;
        private readonly float _speedMultiplier;
        private readonly bool _useSmoothPause;

        // Spline parameter: t in [0, _numSegments].
        private float _currentT;
        private int _currentSegment;

        // Legacy freeze state (SCF_STOP_MOVEMENT).
        private float _freezeRemaining;
        private bool _frozenAtBoundary;

        // TombEngine smooth pause state machine.
        private PausePhase _pausePhase;
        private float _pauseEaseStartAlpha;
        private float _pauseEaseProgress;
        private float _pauseEaseStep;
        private float _pauseSpeedFactor = 1.0f;
        private int _pauseHoldTimer;
        private bool _isPauseComplete;

        private readonly Stopwatch _stopwatch = new();
        private Timer _sequenceTimer;
        private double _lastElapsed;

        public bool IsFinished { get; private set; }
        public FrameState LastFrame { get; private set; }
        public Camera SavedCamera { get; }
        public FrameState? StaticFrame { get; private set; }

        /// <summary>
        /// Creates a sequence preview for the given flyby sequence.
        /// </summary>
        public CameraPreview(Level level, int sequence, Camera savedCamera, float speedMultiplier = 1.0f)
        {
            SavedCamera = savedCamera;
            _speedMultiplier = speedMultiplier;
            _useSmoothPause = level.Settings.GameVersion == TRVersion.Game.TombEngine;

            var flybyCameras = level.ExistingRooms
                .SelectMany(r => r.Objects.OfType<FlybyCameraInstance>())
                .Where(c => c.Sequence == sequence)
                .OrderBy(c => c.Number)
                .ToList();

            if (flybyCameras.Count < 2)
            {
                IsFinished = true;
                _posX = _posY = _posZ = Array.Empty<float>();
                _tgtX = _tgtY = _tgtZ = Array.Empty<float>();
                _rollKnots = _fovKnots = _speedKnots = Array.Empty<float>();
                _cameraFlags = Array.Empty<ushort>();
                _cameraTimers = Array.Empty<short>();

                return;
            }

            _numCameras = flybyCameras.Count;
            _numSegments = _numCameras - 1;

            BuildKnotArrays(flybyCameras,
                out _posX, out _posY, out _posZ,
                out _tgtX, out _tgtY, out _tgtZ,
                out _rollKnots, out _fovKnots, out _speedKnots);

            _cameraFlags = new ushort[_numCameras];
            _cameraTimers = new short[_numCameras];

            for (int i = 0; i < _numCameras; i++)
            {
                _cameraFlags[i] = flybyCameras[i].Flags;
                _cameraTimers[i] = flybyCameras[i].Timer;
            }

            LastFrame = GetFrameAtTimePoint(0);
        }

        /// <summary>
        /// Creates a static preview session without spline interpolation.
        /// </summary>
        public CameraPreview(Camera savedCamera)
        {
            SavedCamera = savedCamera;
            IsFinished = true;
            _posX = _posY = _posZ = Array.Empty<float>();
            _tgtX = _tgtY = _tgtZ = Array.Empty<float>();
            _rollKnots = _fovKnots = _speedKnots = Array.Empty<float>();
            _cameraFlags = Array.Empty<ushort>();
            _cameraTimers = Array.Empty<short>();
        }

        public void BeginSequence(EventHandler timerTick)
        {
            _stopwatch.Restart();
            _lastElapsed = 0;
            _sequenceTimer = new Timer { Interval = 16 };
            _sequenceTimer.Tick += timerTick;
            _sequenceTimer.Start();
        }

        public void Stop()
        {
            _stopwatch.Stop();
            _sequenceTimer?.Stop();
            IsFinished = true;
        }

        public void Dispose()
        {
            Stop();
            _sequenceTimer?.Dispose();
        }

        public FrameState Update()
        {
            if (IsFinished || _numCameras < 2)
                return new FrameState { Finished = true };

            float deltaTime = ComputeDeltaTime();

            if (_useSmoothPause)
                return UpdateWithSmoothPause(deltaTime);

            return UpdateWithLegacyPause(deltaTime);
        }

        /// <summary>
        /// Computes a single-camera <see cref="FrameState"/> from a flyby camera's current properties.
        /// </summary>
        public static FrameState GetFrameForCamera(FlybyCameraInstance camera)
        {
            var worldPos = camera.Position + camera.Room.WorldPos;

            return new FrameState
            {
                Position = worldPos,
                RotationY = MathC.DegToRad(camera.RotationY),
                RotationX = -MathC.DegToRad(camera.RotationX),
                Roll = -MathC.DegToRad(camera.Roll),
                Fov = MathC.DegToRad(camera.Fov)
            };
        }

        /// <summary>
        /// Applies a frame state to the given camera, updating position, rotation, FOV and target.
        /// </summary>
        public static void ApplyFrameToCamera(Camera camera, FrameState frame)
        {
            camera.Position = frame.Position;
            camera.RotationY = frame.RotationY;
            camera.RotationX = frame.RotationX;
            camera.FieldOfView = frame.Fov;

            var rotation = Matrix4x4.CreateFromYawPitchRoll(frame.RotationY, frame.RotationX, 0);
            var look = MathC.HomogenousTransform(Vector3.UnitZ, rotation);
            camera.Target = frame.Position + (Level.SectorSizeUnit * look);
        }

        /// <summary>
        /// Computes and stores the static frame for a flyby camera, then applies it to the given camera.
        /// </summary>
        public void ApplyStaticCameraFrame(Camera camera, FlybyCameraInstance flybyCamera)
        {
            StaticFrame = GetFrameForCamera(flybyCamera);
            ApplyFrameToCamera(camera, StaticFrame.Value);
        }

        /// <summary>
        /// Builds a view-projection matrix with roll support for the current preview frame.
        /// </summary>
        public Matrix4x4 BuildViewProjection(float width, float height, float defaultFov)
        {
            var frame = StaticFrame ?? LastFrame;

            var rotation = Matrix4x4.CreateFromYawPitchRoll(frame.RotationY, frame.RotationX, 0);
            var look = MathC.HomogenousTransform(Vector3.UnitZ, rotation);
            var right = MathC.HomogenousTransform(Vector3.UnitX, rotation);
            var up = Vector3.Cross(look, right);

            if (Math.Abs(frame.Roll) > 0.001f)
            {
                var rollMatrix = Matrix4x4.CreateFromAxisAngle(look, frame.Roll);
                up = Vector3.TransformNormal(up, rollMatrix);
            }

            var target = frame.Position + (Level.SectorSizeUnit * look);
            float fov = frame.Fov > 0.01f ? frame.Fov : defaultFov;

            var view = MathC.Matrix4x4CreateLookAtLH(frame.Position, target, up);
            float aspectRatio = height != 0.0f ? width / height : 1.0f;
            var projection = MathC.Matrix4x4CreatePerspectiveFieldOfViewLH(fov, aspectRatio, 20.0f, 1000000.0f);

            return view * projection;
        }

        #region Update helpers

        private float ComputeDeltaTime()
        {
            double currentElapsed = _stopwatch.Elapsed.TotalSeconds;
            float deltaTime = (float)(currentElapsed - _lastElapsed);
            _lastElapsed = currentElapsed;

            return deltaTime;
        }

        private FrameState UpdateWithLegacyPause(float deltaTime)
        {
            if (TryHandleLegacyFreeze(deltaTime, out var frozenFrame))
                return frozenFrame;

            AdvancePosition(deltaTime, 1.0f);

            if (ProcessLegacyBoundaries(out var boundaryFrame))
                return boundaryFrame;

            return FinishOrEmit();
        }

        private bool TryHandleLegacyFreeze(float deltaTime, out FrameState frame)
        {
            frame = default;

            if (_freezeRemaining <= 0)
                return false;

            _freezeRemaining -= deltaTime;

            if (_freezeRemaining > 0)
            {
                frame = EmitFrame();
                return true;
            }

            _freezeRemaining = 0;
            _frozenAtBoundary = true;

            return false;
        }

        private bool ProcessLegacyBoundaries(out FrameState frame)
        {
            frame = default;
            int maxSegment = _numSegments - 1;
            int loopGuard = _numCameras + 1;

            while (_currentSegment <= maxSegment && loopGuard-- > 0)
            {
                int nextCamera = _currentSegment + 1;

                if (_currentT < nextCamera)
                    break;

                ushort flags = _cameraFlags[nextCamera];
                short timer = _cameraTimers[nextCamera];

                if ((flags & FlagStopMovement) != 0 && timer > 0 && !_frozenAtBoundary)
                {
                    int gameFrames = Math.Max(0, (timer >> 3) - 1);

                    if (gameFrames > 0)
                    {
                        _freezeRemaining = gameFrames / GameTickRate;
                        _currentT = nextCamera;
                        frame = EmitFrame();
                        return true;
                    }

                    _frozenAtBoundary = true;
                }

                _frozenAtBoundary = false;

                if (ProcessCutToCam(flags, timer))
                    break;

                _currentSegment = nextCamera;
            }

            return false;
        }

        private FrameState UpdateWithSmoothPause(float deltaTime)
        {
            float currentSpeed = CatmullRomSpline.Evaluate(_currentT, _speedKnots);
            float speedPerSec = currentSpeed * _speedMultiplier * SpeedScale;
            float localAlpha = _currentT - _currentSegment;

            TriggerSmoothPauseIfNeeded(localAlpha, speedPerSec);

            switch (_pausePhase)
            {
                case PausePhase.EaseOut:
                    AdvanceSmoothEaseOut(deltaTime);
                    return EmitFrame();

                case PausePhase.Hold:
                    if (AdvanceSmoothHold(deltaTime))
                        return EmitFrame();
                    break;

                case PausePhase.EaseIn:
                    AdvanceSmoothEaseIn(deltaTime, speedPerSec);
                    return EmitFrame();

                default:
                    AdvancePosition(deltaTime, 1.0f);
                    break;
            }

            if (ProcessSmoothBoundaries())
                return EmitFrame();

            return FinishOrEmit();
        }

        private void TriggerSmoothPauseIfNeeded(float localAlpha, float speedPerSec)
        {
            if (_pausePhase != PausePhase.None)
                return;

            int nextCamera = _currentSegment + 1;

            if (nextCamera >= _numCameras)
                return;

            bool hasPause = _cameraTimers[nextCamera] > 0
                && (_cameraFlags[nextCamera] & FlagStopMovement) != 0
                && !_isPauseComplete;

            if (!hasPause || (1.0f - localAlpha) > EaseDistance)
                return;

            _pauseEaseStartAlpha = localAlpha;
            _pauseEaseProgress = 0.0f;
            _isPauseComplete = true;

            float remainingAlpha = Math.Max(1.0f - localAlpha, MinSpeed);
            float clampedSpeed = Math.Max(speedPerSec, MinSpeed);

            _pauseEaseStep = clampedSpeed / (2.0f * remainingAlpha);
            _pausePhase = PausePhase.EaseOut;
        }

        private void AdvanceSmoothEaseOut(float deltaTime)
        {
            _pauseEaseProgress = Math.Min(_pauseEaseProgress + _pauseEaseStep * deltaTime, 1.0f);

            float progress = _pauseEaseProgress;
            float localAlpha = _pauseEaseStartAlpha + (1.0f - _pauseEaseStartAlpha) * progress * (2.0f - progress);

            _currentT = _currentSegment + Math.Min(localAlpha, 1.0f);

            if (_pauseEaseProgress >= 1.0f)
            {
                _pauseSpeedFactor = 0.0f;
                _pauseHoldTimer = _cameraTimers[_currentSegment + 1] >> 3;
                _pausePhase = PausePhase.Hold;
            }
        }

        private bool AdvanceSmoothHold(float deltaTime)
        {
            _pauseHoldTimer -= (int)Math.Ceiling(deltaTime * GameTickRate);

            if (_pauseHoldTimer > 0)
                return true;

            _pauseEaseProgress = 0.0f;
            _pauseSpeedFactor = 0.0f;
            _pausePhase = PausePhase.EaseIn;

            _currentSegment++;
            _currentT = _currentSegment;
            _isPauseComplete = false;

            return false;
        }

        private void AdvanceSmoothEaseIn(float deltaTime, float speedPerSec)
        {
            _pauseEaseProgress = Math.Min(_pauseEaseProgress + _pauseEaseStep * deltaTime, 1.0f);
            _pauseSpeedFactor = _pauseEaseProgress * _pauseEaseProgress;

            if (_pauseEaseProgress >= 1.0f)
            {
                _pauseSpeedFactor = 1.0f;
                _pauseEaseProgress = 0.0f;
                _isPauseComplete = false;
                _pausePhase = PausePhase.None;
            }

            _currentT += speedPerSec * _pauseSpeedFactor * deltaTime;
        }

        private bool ProcessSmoothBoundaries()
        {
            if (_pausePhase != PausePhase.None)
                return false;

            int maxSegment = _numSegments - 1;
            int loopGuard = _numCameras + 1;

            while (_currentSegment <= maxSegment && loopGuard-- > 0)
            {
                int nextCamera = _currentSegment + 1;

                if (_currentT < nextCamera)
                    break;

                ushort flags = _cameraFlags[nextCamera];
                short timer = _cameraTimers[nextCamera];

                if (ProcessCutToCam(flags, timer))
                    return true;

                _currentSegment = nextCamera;
                _isPauseComplete = false;
            }

            return false;
        }

        private void AdvancePosition(float deltaTime, float speedFactor)
        {
            float currentSpeed = CatmullRomSpline.Evaluate(_currentT, _speedKnots);
            _currentT += currentSpeed * _speedMultiplier * deltaTime * SpeedScale * speedFactor;
        }

        private bool ProcessCutToCam(ushort flags, short timer)
        {
            if ((flags & FlagCutToCam) == 0)
                return false;

            int targetCam = Math.Clamp(timer, 0, _numCameras - 1);

            _currentT = targetCam;
            _currentSegment = targetCam;
            _frozenAtBoundary = false;

            if (targetCam >= _numCameras - 1)
            {
                _currentT = _numSegments;
                IsFinished = true;
            }

            return true;
        }

        private FrameState FinishOrEmit()
        {
            if (_currentT >= _numSegments)
            {
                _currentT = _numSegments;
                IsFinished = true;
            }

            return EmitFrame();
        }

        private FrameState EmitFrame()
        {
            var frame = GetFrameAtTimePoint(_currentT);
            LastFrame = frame;
            return frame;
        }

        #endregion Update helpers

        #region Frame interpolation

        private FrameState GetFrameAtTimePoint(float t)
        {
            t = Math.Clamp(t, 0, _numSegments);

            float px   = CatmullRomSpline.Evaluate(t, _posX);
            float py   = CatmullRomSpline.Evaluate(t, _posY);
            float pz   = CatmullRomSpline.Evaluate(t, _posZ);
            float tx   = CatmullRomSpline.Evaluate(t, _tgtX);
            float ty   = CatmullRomSpline.Evaluate(t, _tgtY);
            float tz   = CatmullRomSpline.Evaluate(t, _tgtZ);
            float roll = CatmullRomSpline.Evaluate(t, _rollKnots);
            float fov  = CatmullRomSpline.Evaluate(t, _fovKnots);

            // Derive yaw and pitch from interpolated camera-to-target direction.
            float dx = tx - px;
            float dy = ty - py;
            float dz = tz - pz;
            float horizontalDist = (float)Math.Sqrt((dx * dx) + (dz * dz));

            float yaw = 0, pitch = 0;

            if (horizontalDist > 0.001f || Math.Abs(dy) > 0.001f)
            {
                yaw = (float)Math.Atan2(dx, dz);
                pitch = (float)Math.Atan2(-dy, horizontalDist);
            }

            return new FrameState
            {
                Position = new Vector3(px, py, pz),
                RotationY = yaw,
                RotationX = pitch,
                Roll = -MathC.DegToRad(roll),
                Fov = MathC.DegToRad(fov),
                Finished = IsFinished
            };
        }

        #endregion Frame interpolation

        #region Keyframe building

        private static void BuildKnotArrays(
            IList<FlybyCameraInstance> cameras,
            out float[] posX, out float[] posY, out float[] posZ,
            out float[] tgtX, out float[] tgtY, out float[] tgtZ,
            out float[] rollKnots, out float[] fovKnots, out float[] speedKnots)
        {
            int n = cameras.Count;

            var rawPosX  = new float[n];
            var rawPosY  = new float[n];
            var rawPosZ  = new float[n];
            var rawTgtX  = new float[n];
            var rawTgtY  = new float[n];
            var rawTgtZ  = new float[n];
            var rawRoll  = new float[n];
            var rawFov   = new float[n];
            var rawSpeed = new float[n];

            for (int i = 0; i < n; i++)
            {
                var cam = cameras[i];
                var worldPos = cam.Position + cam.Room.WorldPos;

                float yawRad = MathC.DegToRad(cam.RotationY);
                float pitchRad = MathC.DegToRad(cam.RotationX);
                float cosPitch = (float)Math.Cos(pitchRad);

                rawPosX[i]  = worldPos.X;
                rawPosY[i]  = worldPos.Y;
                rawPosZ[i]  = worldPos.Z;
                rawTgtX[i]  = worldPos.X + (TargetDistance * cosPitch * (float)Math.Sin(yawRad));
                rawTgtY[i]  = worldPos.Y + (TargetDistance * (float)Math.Sin(pitchRad));
                rawTgtZ[i]  = worldPos.Z + (TargetDistance * cosPitch * (float)Math.Cos(yawRad));
                rawRoll[i]  = cam.Roll;
                rawFov[i]   = cam.Fov;
                rawSpeed[i] = cam.Speed;
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
            speedKnots = CatmullRomSpline.PadKnots(rawSpeed);
        }

        /// <summary>
        /// Adjusts consecutive angle values so that each delta is at most 180 degrees,
        /// preventing the spline from taking the long way around the 360-degree boundary.
        /// </summary>
        private static void UnwrapAngles(float[] angles)
        {
            for (int i = 1; i < angles.Length; i++)
            {
                float delta = angles[i] - angles[i - 1];
                angles[i] -= (float)Math.Round(delta / 360.0f) * 360.0f;
            }
        }

        #endregion Keyframe building
    }
}
