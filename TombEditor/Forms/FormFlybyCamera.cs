using DarkUI.Forms;
using System;
using System.Windows.Forms;
using TombEditor.Controls.FlybyTimeline;
using TombLib.LevelData;

namespace TombEditor.Forms
{
    public partial class FormFlybyCamera : DarkForm
    {
        public bool IsNew { get; set; }
        public bool HasChanges { get; private set; }

        private readonly FlybyCameraInstance _flyByCamera;
        private readonly Editor _editor;

        // Original values for restoring on Cancel.
        private ushort _originalFlags;
        private ushort _originalSequence;
        private ushort _originalNumber;
        private short _originalTimer;
        private float _originalSpeed;
        private float _originalFov;
        private float _originalRoll;
        private float _originalRotationX;
        private float _originalRotationY;

        private bool _isLoading;
        private bool _ownedPreview;

        public FormFlybyCamera(FlybyCameraInstance flyByCamera)
        {
            _flyByCamera = flyByCamera;
            _editor = Editor.Instance;

            InitializeComponent();

            // Set window property handlers.
            Configuration.ConfigureWindow(this, _editor.Configuration);
        }

        private void butCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void FormFlybyCamera_Load(object sender, EventArgs e)
        {
            SaveOriginalValues();

            _isLoading = true;

            var flagCheckBoxes = GetFlagCheckBoxes();

            for (int i = 0; i < flagCheckBoxes.Length; i++)
                flagCheckBoxes[i].Checked = FlybySequenceHelper.GetFlagBit(_flyByCamera.Flags, i);

            numSequence.Value = _flyByCamera.Sequence;
            numNumber.Value = _flyByCamera.Number;
            numTimer.Value = _flyByCamera.Timer;
            numSpeed.Value = ClampNumericValue(_flyByCamera.Speed, numSpeed);
            numFOV.Value = (decimal)_flyByCamera.Fov;
            numRoll.Value = (decimal)_flyByCamera.Roll;
            numRotationX.Value = (decimal)_flyByCamera.RotationX;
            numRotationY.Value = (decimal)_flyByCamera.RotationY;

            if (_editor.Level.Settings.GameVersion is TRVersion.Game.TR5 or TRVersion.Game.TombEngine)
            {
                cbBit1.Text = "Vignette";
                cbBit4.Text = "Hide Lara";
                cbBit12.Text = "Make fade-in";
                cbBit13.Text = "Make fade-out";
            }
            else if (_editor.Level.Settings.GameVersion.Native() <= TRVersion.Game.TR3)
            {
                Size = new System.Drawing.Size(205, 319);
            }

            _isLoading = false;

            // Subscribe to value changes for live preview.
            numFOV.ValueChanged += PreviewParameter_Changed;
            numRoll.ValueChanged += PreviewParameter_Changed;
            numRotationX.ValueChanged += PreviewParameter_Changed;
            numRotationY.ValueChanged += PreviewParameter_Changed;

            // Start live camera preview. Only toggle if preview is not already active
            // (e.g. flyby timeline may have entered preview before this form was opened).
            if (!_editor.FlyMode)
            {
                if (_editor.CameraPreviewMode == CameraPreviewType.None)
                {
                    _editor.ToggleCameraPreview(true);
                    _ownedPreview = true;
                }

                _editor.CameraPreviewUpdated(_flyByCamera);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (DialogResult == DialogResult.Cancel)
                RestoreOriginalValues();

            // Only exit the preview mode if we were the one who entered it.
            if (_ownedPreview && _editor.CameraPreviewMode != CameraPreviewType.None)
                _editor.ToggleCameraPreview(false);

            base.OnFormClosed(e);
        }

        private void PreviewParameter_Changed(object sender, EventArgs e)
        {
            if (_isLoading)
                return;

            _flyByCamera.Fov = (float)numFOV.Value;
            _flyByCamera.Roll = (float)numRoll.Value;
            _flyByCamera.RotationX = (float)numRotationX.Value;
            _flyByCamera.RotationY = (float)numRotationY.Value;

            _editor.CameraPreviewUpdated(_flyByCamera);
        }

        private void butOK_Click(object sender, EventArgs e)
        {
            HasChanges = HasPendingChanges();
            _flyByCamera.Flags = CollectFlags();
            _flyByCamera.Sequence = (ushort)numSequence.Value;
            _flyByCamera.Number = (ushort)numNumber.Value;
            _flyByCamera.Timer = (short)numTimer.Value;
            _flyByCamera.Speed = (float)numSpeed.Value;
            _flyByCamera.Fov = (float)numFOV.Value;
            _flyByCamera.Roll = (float)numRoll.Value;
            _flyByCamera.RotationX = (float)numRotationX.Value;
            _flyByCamera.RotationY = (float)numRotationY.Value;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void SaveOriginalValues()
        {
            _originalFlags = _flyByCamera.Flags;
            _originalSequence = _flyByCamera.Sequence;
            _originalNumber = _flyByCamera.Number;
            _originalTimer = _flyByCamera.Timer;
            _originalSpeed = _flyByCamera.Speed;
            _originalFov = _flyByCamera.Fov;
            _originalRoll = _flyByCamera.Roll;
            _originalRotationX = _flyByCamera.RotationX;
            _originalRotationY = _flyByCamera.RotationY;
        }

        private void RestoreOriginalValues()
        {
            _flyByCamera.Flags = _originalFlags;
            _flyByCamera.Sequence = _originalSequence;
            _flyByCamera.Number = _originalNumber;
            _flyByCamera.Timer = _originalTimer;
            _flyByCamera.Speed = _originalSpeed;
            _flyByCamera.Fov = _originalFov;
            _flyByCamera.Roll = _originalRoll;
            _flyByCamera.RotationX = _originalRotationX;
            _flyByCamera.RotationY = _originalRotationY;
        }

        private bool HasPendingChanges()
        {
            return CollectFlags() != _originalFlags ||
                (ushort)numSequence.Value != _originalSequence ||
                (ushort)numNumber.Value != _originalNumber ||
                (short)numTimer.Value != _originalTimer ||
                (float)numSpeed.Value != _originalSpeed ||
                (float)numFOV.Value != _originalFov ||
                (float)numRoll.Value != _originalRoll ||
                (float)numRotationX.Value != _originalRotationX ||
                (float)numRotationY.Value != _originalRotationY;
        }

        private static decimal ClampNumericValue(float value, NumericUpDown numeric)
        {
            decimal fallbackValue = Math.Min(numeric.Maximum, Math.Max(numeric.Minimum, numeric.Value));

            if (!float.IsFinite(value))
                return fallbackValue;

            decimal decimalValue = (decimal)value;
            return Math.Min(numeric.Maximum, Math.Max(numeric.Minimum, decimalValue));
        }

        private ushort CollectFlags()
        {
            ushort flags = 0;

            var flagCheckBoxes = GetFlagCheckBoxes();

            for (int i = 0; i < flagCheckBoxes.Length; i++)
                flags = FlybySequenceHelper.SetFlagBit(flags, i, flagCheckBoxes[i].Checked);

            return flags;
        }

        private CheckBox[] GetFlagCheckBoxes() =>
        [
            cbBit0,
            cbBit1,
            cbBit2,
            cbBit3,
            cbBit4,
            cbBit5,
            cbBit6,
            cbBit7,
            cbBit8,
            cbBit9,
            cbBit10,
            cbBit11,
            cbBit12,
            cbBit13,
            cbBit14,
            cbBit15
        ];
    }
}
