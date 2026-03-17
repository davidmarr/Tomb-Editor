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
    /// Handles flyby camera sequence preview by interpolating position, target, FOV, roll, and speed using a Catmull-Rom spline.
    /// </summary>
    public class FlybyPreview
    {
        /// <summary> State of a single interpolated frame. </summary>
        public struct FrameState
        {
            public Vector3 Position;
            public float RotationY; // Yaw in radians    (FreeCamera convention)
            public float RotationX; // Pitch in radians  (FreeCamera convention: positive = look down)
            public float Roll;      // Roll in radians
            public float Fov;       // FOV in radians
            public bool Finished;
        }

        /// <summary> TombEngine smooth pause phases for SCF_STOP_MOVEMENT. </summary>
        private enum PausePhase
        {
            None,
            EaseOut,
            Hold,
            EaseIn
		}

		/// <summary>
		/// The game logic runs at 30 ticks per second.
		/// </summary>
		private const float GameTickRate = 30.0f;

		/// <summary>
		/// Speed conversion matching the game engine.
		/// Editor Speed S is compiled as S * 655. The game advances CurrentSplinePosition (0–65536)
		/// by that amount per frame at 30fps. So segments/second = S * 655 * 30 / 65536.
		/// </summary>
		private const float SpeedScale = (float)(ushort.MaxValue / 100) * GameTickRate / (float)ushort.MaxValue;

        /// <summary>
        /// Distance from camera to target point, matching the level compiler's convention.
        /// Target = Position + Direction * TargetDistance.
        /// </summary>
        private const float TargetDistance = Level.SectorSizeUnit;

        // SCF flag bits.
        private const int FlagStopMovement = 1 << 8;
        private const int FlagCutToCam =     1 << 7;

        // TombEngine smooth pause constants.
        private const float EaseDistance = 0.15f;
        private const float MinSpeed = 0.001f;

        // Catmull-Rom knot arrays (padded via CatmullRomSpline.PadKnots)
        private readonly float[] _posX, _posY, _posZ;
        private readonly float[] _tgtX, _tgtY, _tgtZ;
        private readonly float[] _rollKnots, _fovKnots, _speedKnots;

        // Per-camera flags and timers
        private readonly List<ushort> _cameraFlags = new();
        private readonly List<short> _cameraTimers = new();
        private readonly int _numCameras;
        private readonly int _numSegments; // = _numCameras - 1

        private readonly float _speedMultiplier;
        private readonly bool _useSmoothPause;

        // Current spline parameter: t in [0, _numSegments].
        private float _currentT;

        // Freeze state (SCF_STOP_MOVEMENT) for legacy game versions.
        private float _freezeRemaining;
        private bool _frozenAtBoundary;
        private int _currentSegment;

        // TombEngine smooth pause state machine.
        private PausePhase _pausePhase;
        private float _pauseEaseStartAlpha;
        private float _pauseEaseProgress;
        private float _pauseEaseStep;
        private float _pauseSpeedFactor = 1.0f;
        private int _pauseHoldTimer;
        private bool _isPauseComplete;

        private readonly Stopwatch _stopwatch = new();
        private double _lastElapsed;

        private Camera _savedCamera;
        private FrameState? _staticFrame;
        private Timer _sequenceTimer;

        public bool IsFinished { get; private set; }
        public int SequenceId { get; }
        public FrameState LastFrame { get; private set; }
        public Camera SavedCamera => _savedCamera;
        public FrameState? StaticFrame => _staticFrame;

        public FlybyPreview(Level level, int sequence, Camera savedCamera, float speedMultiplier = 1.0f)
        {
            SequenceId = sequence;
            _savedCamera = savedCamera;
            _speedMultiplier = speedMultiplier;
            _useSmoothPause = level.Settings.GameVersion == TRVersion.Game.TombEngine;
            _sequenceTimer = new Timer { Interval = 16 };

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

                return;
            }

            _numCameras = flybyCameras.Count;
            _numSegments = _numCameras - 1;

            BuildKnotArrays(flybyCameras,
                out _posX, out _posY, out _posZ,
                out _tgtX, out _tgtY, out _tgtZ,
                out _rollKnots, out _fovKnots, out _speedKnots);

            for (int i = 0; i < _numCameras; i++)
            {
                _cameraFlags.Add(flybyCameras[i].Flags);
                _cameraTimers.Add(flybyCameras[i].Timer);
            }

            _currentT = 0;
            _currentSegment = 0;
            IsFinished = false;

            // Initialize LastFrame so rendering is consistent before the first Update().
            LastFrame = GetFrameAtT(0);
        }

        /// <summary>
        /// Creates a preview session for a static single camera without spline interpolation.
        /// </summary>
        public FlybyPreview(Camera savedCamera)
        {
            _savedCamera = savedCamera;
            IsFinished = true;
            _posX = _posY = _posZ = Array.Empty<float>();
            _tgtX = _tgtY = _tgtZ = Array.Empty<float>();
            _rollKnots = _fovKnots = _speedKnots = Array.Empty<float>();
        }

        public void Start()
        {
            _stopwatch.Restart();
            _lastElapsed = 0;
        }

        public void Stop()
        {
            _stopwatch.Stop();
            IsFinished = true;
        }

        public void BeginSequence(EventHandler timerTick)
        {
            Start();
            _sequenceTimer.Tick += timerTick;
            _sequenceTimer.Start();
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

            float yawRad = MathC.DegToRad(camera.RotationY);
            float pitchRad = MathC.DegToRad(camera.RotationX);

            return new FrameState
            {
                Position = worldPos,
                RotationY = yawRad,
                RotationX = -pitchRad,
                Roll = -MathC.DegToRad(camera.Roll),
                Fov = MathC.DegToRad(camera.Fov),
                Finished = false
            };
        }

        /// <summary>
        /// Applies a pre-computed frame state to the given camera.
        /// </summary>
        public void ApplyFrameToCamera(Camera camera, FrameState frame)
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
        /// Computes the frame for a flyby camera instance, stores it as the static frame, and applies it to the given camera.
        /// </summary>
        public void ApplyStaticCameraFrame(Camera camera, FlybyCameraInstance flybyCamera)
        {
            _staticFrame = GetFrameForCamera(flybyCamera);
            ApplyFrameToCamera(camera, _staticFrame.Value);
        }

        /// <summary>
        /// Builds a view-projection matrix with roll support for the current preview frame.
        /// </summary>
        public Matrix4x4 BuildViewProjection(float width, float height, float defaultFov)
        {
            var frame = _staticFrame ?? LastFrame;

            // Build rotation from raw frame angles to preserve spline angle unwrapping.
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

        /// <summary>
        /// Legacy pause: abrupt freeze at segment boundaries.
        /// </summary>
        private FrameState UpdateWithLegacyPause(float deltaTime)
        {
            if (TryHandleLegacyFreeze(deltaTime, out var frozenFrame))
                return frozenFrame;

            AdvancePosition(deltaTime, 1.0f);

            if (ProcessLegacyBoundaries(out var boundaryFrame))
                return boundaryFrame;

            return FinishOrEmit();
        }

        /// <summary>
        /// Counts down the legacy freeze timer. Returns <see langword="true"/> if still frozen.
        /// </summary>
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
                float boundaryT = nextCamera;

                if (_currentT < boundaryT)
                    break;

                ushort flags = _cameraFlags[nextCamera];
                short timer = _cameraTimers[nextCamera];

                // SCF_STOP_MOVEMENT: freeze the camera.
                if ((flags & FlagStopMovement) != 0 && timer > 0 && !_frozenAtBoundary)
                {
                    int gameFrames = Math.Max(0, (timer >> 3) - 1);

                    if (gameFrames > 0)
                    {
                        _freezeRemaining = gameFrames / GameTickRate;
                        _currentT = boundaryT;
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

        /// <summary>
        /// TombEngine smooth pause: quadratic ease-out, hold, ease-in around segment boundaries.
        /// </summary>
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

        /// <summary>
        /// Triggers the ease-out phase when the camera is within <see cref="EaseDistance"/> of the segment end.
        /// </summary>
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

        /// <summary>
        /// Holds the camera still. Returns <see langword="true"/> if still holding.
        /// </summary>
        private bool AdvanceSmoothHold(float deltaTime)
        {
            _pauseHoldTimer -= (int)Math.Ceiling(deltaTime * GameTickRate);

            if (_pauseHoldTimer > 0)
                return true;

            _pauseEaseProgress = 0.0f;
            _pauseSpeedFactor = 0.0f;
            _pausePhase = PausePhase.EaseIn;

            // Advance to next segment.
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

        /// <summary>
        /// Handles SCF_CUT_TO_CAM flag. Returns <see langword="true"/> if the sequence ended.
        /// </summary>
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
            var frame = GetFrameAtT(_currentT);
            LastFrame = frame;
            return frame;
        }

        #endregion Update helpers

        #region Frame interpolation

        private FrameState GetFrameAtT(float t)
        {
            t = Math.Clamp(t, 0, _numSegments);

            float px = CatmullRomSpline.Evaluate(t, _posX);
            float py = CatmullRomSpline.Evaluate(t, _posY);
            float pz = CatmullRomSpline.Evaluate(t, _posZ);
            float tx = CatmullRomSpline.Evaluate(t, _tgtX);
            float ty = CatmullRomSpline.Evaluate(t, _tgtY);
            float tz = CatmullRomSpline.Evaluate(t, _tgtZ);
            float roll = CatmullRomSpline.Evaluate(t, _rollKnots);
            float fov = CatmullRomSpline.Evaluate(t, _fovKnots);

            // Derive yaw and pitch from interpolated camera-to-target direction,
            // matching the game's LookAt() approach.
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

        /// <summary>
        /// Builds padded Catmull-Rom knot arrays from flyby cameras.
        /// </summary>
        private static void BuildKnotArrays(
            IList<FlybyCameraInstance> cameras,
            out float[] posX, out float[] posY, out float[] posZ,
            out float[] tgtX, out float[] tgtY, out float[] tgtZ,
            out float[] rollKnots, out float[] fovKnots, out float[] speedKnots)
        {
            int n = cameras.Count;

            var rawPosX = new float[n];
            var rawPosY = new float[n];
            var rawPosZ = new float[n];
            var rawTgtX = new float[n];
            var rawTgtY = new float[n];
            var rawTgtZ = new float[n];
            var rawRoll = new float[n];
            var rawFov = new float[n];
            var rawSpeed = new float[n];

            for (int i = 0; i < n; i++)
            {
                var cam = cameras[i];
                var worldPos = cam.Position + cam.Room.WorldPos;

                // Direction from rotation angles (same formula the level compiler uses)
                float yawRad = MathC.DegToRad(cam.RotationY);
                float pitchRad = MathC.DegToRad(cam.RotationX);
                float cosPitch = (float)Math.Cos(pitchRad);
                float dirX = cosPitch * (float)Math.Sin(yawRad);
                float dirY = (float)Math.Sin(pitchRad);
                float dirZ = cosPitch * (float)Math.Cos(yawRad);

                rawPosX[i] = worldPos.X;
                rawPosY[i] = worldPos.Y;
                rawPosZ[i] = worldPos.Z;
                rawTgtX[i] = worldPos.X + (TargetDistance * dirX);
                rawTgtY[i] = worldPos.Y + (TargetDistance * dirY);
                rawTgtZ[i] = worldPos.Z + (TargetDistance * dirZ);
                rawRoll[i] = cam.Roll;
                rawFov[i] = cam.Fov;
                rawSpeed[i] = cam.Speed;
            }

            // Unwrap roll so the spline takes the shortest angular path.
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
