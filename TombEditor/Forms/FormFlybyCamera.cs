using System;
using System.Windows.Forms;
using DarkUI.Forms;
using TombLib.LevelData;

namespace TombEditor.Forms
{
    public partial class FormFlybyCamera : DarkForm
    {
        public bool IsNew { get; set; }

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

            cbBit0.Checked = (_flyByCamera.Flags & (1 << 0)) != 0;
            cbBit1.Checked = (_flyByCamera.Flags & (1 << 1)) != 0;
            cbBit2.Checked = (_flyByCamera.Flags & (1 << 2)) != 0;
            cbBit3.Checked = (_flyByCamera.Flags & (1 << 3)) != 0;
            cbBit4.Checked = (_flyByCamera.Flags & (1 << 4)) != 0;
            cbBit5.Checked = (_flyByCamera.Flags & (1 << 5)) != 0;
            cbBit6.Checked = (_flyByCamera.Flags & (1 << 6)) != 0;
            cbBit7.Checked = (_flyByCamera.Flags & (1 << 7)) != 0;
            cbBit8.Checked = (_flyByCamera.Flags & (1 << 8)) != 0;
            cbBit9.Checked = (_flyByCamera.Flags & (1 << 9)) != 0;
            cbBit10.Checked = (_flyByCamera.Flags & (1 << 10)) != 0;
            cbBit11.Checked = (_flyByCamera.Flags & (1 << 11)) != 0;
            cbBit12.Checked = (_flyByCamera.Flags & (1 << 12)) != 0;
            cbBit13.Checked = (_flyByCamera.Flags & (1 << 13)) != 0;
            cbBit14.Checked = (_flyByCamera.Flags & (1 << 14)) != 0;
            cbBit15.Checked = (_flyByCamera.Flags & (1 << 15)) != 0;

            numSequence.Value = _flyByCamera.Sequence;
            numNumber.Value = _flyByCamera.Number;
            numTimer.Value = _flyByCamera.Timer;
            numSpeed.Value = (decimal)_flyByCamera.Speed;
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

        private ushort CollectFlags()
        {
            ushort flags = 0;
            flags |= (ushort)(cbBit0.Checked ? 1 << 0 : 0);
            flags |= (ushort)(cbBit1.Checked ? 1 << 1 : 0);
            flags |= (ushort)(cbBit2.Checked ? 1 << 2 : 0);
            flags |= (ushort)(cbBit3.Checked ? 1 << 3 : 0);
            flags |= (ushort)(cbBit4.Checked ? 1 << 4 : 0);
            flags |= (ushort)(cbBit5.Checked ? 1 << 5 : 0);
            flags |= (ushort)(cbBit6.Checked ? 1 << 6 : 0);
            flags |= (ushort)(cbBit7.Checked ? 1 << 7 : 0);
            flags |= (ushort)(cbBit8.Checked ? 1 << 8 : 0);
            flags |= (ushort)(cbBit9.Checked ? 1 << 9 : 0);
            flags |= (ushort)(cbBit10.Checked ? 1 << 10 : 0);
            flags |= (ushort)(cbBit11.Checked ? 1 << 11 : 0);
            flags |= (ushort)(cbBit12.Checked ? 1 << 12 : 0);
            flags |= (ushort)(cbBit13.Checked ? 1 << 13 : 0);
            flags |= (ushort)(cbBit14.Checked ? 1 << 14 : 0);
            flags |= (ushort)(cbBit15.Checked ? 1 << 15 : 0);
            return flags;
        }
    }
}
