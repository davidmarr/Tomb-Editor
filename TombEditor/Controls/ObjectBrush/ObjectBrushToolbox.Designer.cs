namespace TombEditor.Controls.ObjectBrush
{
    partial class ObjectBrushToolbox
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_editor != null)
                    _editor.EditorEventRaised -= EditorEventRaised;
                if (components != null)
                    components.Dispose();
            }
            base.Dispose(disposing);
        }

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			btnShapeCircle = new DarkUI.Controls.DarkButton();
			btnShapeSquare = new DarkUI.Controls.DarkButton();
			lblRadius = new DarkUI.Controls.DarkLabel();
			nudRadius = new DarkUI.Controls.DarkNumericUpDown();
			lblDensity = new DarkUI.Controls.DarkLabel();
			nudDensity = new DarkUI.Controls.DarkNumericUpDown();
			chkAdjacentRooms = new DarkUI.Controls.DarkCheckBox();
			chkRandomRotation = new DarkUI.Controls.DarkCheckBox();
			chkPerpendicular = new DarkUI.Controls.DarkCheckBox();
			chkRandomScale = new DarkUI.Controls.DarkCheckBox();
			chkFitToGround = new DarkUI.Controls.DarkCheckBox();
			nudScaleMin = new DarkUI.Controls.DarkNumericUpDown();
			nudScaleMax = new DarkUI.Controls.DarkNumericUpDown();
			lblDash = new DarkUI.Controls.DarkLabel();
			lblRotation = new DarkUI.Controls.DarkLabel();
			nudRotation = new DarkUI.Controls.DarkNumericUpDown();
			chkShowTextures = new DarkUI.Controls.DarkCheckBox();
			darkLabel1 = new DarkUI.Controls.DarkLabel();
			((System.ComponentModel.ISupportInitialize)nudRadius).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudDensity).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMin).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMax).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudRotation).BeginInit();
			SuspendLayout();
			// 
			// btnShapeCircle
			// 
			btnShapeCircle.Checked = true;
			btnShapeCircle.Image = Properties.Resources.objects_volume_sphere_16;
			btnShapeCircle.Location = new System.Drawing.Point(4, 24);
			btnShapeCircle.Name = "btnShapeCircle";
			btnShapeCircle.Size = new System.Drawing.Size(24, 24);
			btnShapeCircle.TabIndex = 0;
			// 
			// btnShapeSquare
			// 
			btnShapeSquare.Checked = false;
			btnShapeSquare.Image = Properties.Resources.objects_volume_box_16;
			btnShapeSquare.Location = new System.Drawing.Point(34, 24);
			btnShapeSquare.Name = "btnShapeSquare";
			btnShapeSquare.Size = new System.Drawing.Size(24, 24);
			btnShapeSquare.TabIndex = 1;
			// 
			// lblRadius
			// 
			lblRadius.AutoSize = true;
			lblRadius.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			lblRadius.Location = new System.Drawing.Point(66, 27);
			lblRadius.Name = "lblRadius";
			lblRadius.Size = new System.Drawing.Size(45, 15);
			lblRadius.TabIndex = 2;
			lblRadius.Text = "Radius:";
			// 
			// nudRadius
			// 
			nudRadius.DecimalPlaces = 1;
			nudRadius.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
			nudRadius.IncrementAlternate = new decimal(new int[] { 1, 0, 0, 0 });
			nudRadius.Location = new System.Drawing.Point(117, 25);
			nudRadius.LoopValues = false;
			nudRadius.Maximum = new decimal(new int[] { 25, 0, 0, 0 });
			nudRadius.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
			nudRadius.Name = "nudRadius";
			nudRadius.Size = new System.Drawing.Size(52, 23);
			nudRadius.TabIndex = 2;
			nudRadius.Value = new decimal(new int[] { 5, 0, 0, 65536 });
			// 
			// lblDensity
			// 
			lblDensity.AutoSize = true;
			lblDensity.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			lblDensity.Location = new System.Drawing.Point(175, 29);
			lblDensity.Name = "lblDensity";
			lblDensity.Size = new System.Drawing.Size(49, 15);
			lblDensity.TabIndex = 3;
			lblDensity.Text = "Density:";
			// 
			// nudDensity
			// 
			nudDensity.DecimalPlaces = 2;
			nudDensity.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
			nudDensity.IncrementAlternate = new decimal(new int[] { 1, 0, 0, 65536 });
			nudDensity.Location = new System.Drawing.Point(230, 27);
			nudDensity.LoopValues = false;
			nudDensity.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
			nudDensity.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
			nudDensity.Name = "nudDensity";
			nudDensity.Size = new System.Drawing.Size(52, 23);
			nudDensity.TabIndex = 3;
			nudDensity.Value = new decimal(new int[] { 10, 0, 0, 65536 });
			// 
			// chkAdjacentRooms
			// 
			chkAdjacentRooms.AutoSize = true;
			chkAdjacentRooms.Location = new System.Drawing.Point(90, 107);
			chkAdjacentRooms.Name = "chkAdjacentRooms";
			chkAdjacentRooms.Size = new System.Drawing.Size(152, 19);
			chkAdjacentRooms.TabIndex = 4;
			chkAdjacentRooms.Text = "Place in adjacent rooms";
			// 
			// chkRandomRotation
			// 
			chkRandomRotation.AutoSize = true;
			chkRandomRotation.Location = new System.Drawing.Point(62, 82);
			chkRandomRotation.Name = "chkRandomRotation";
			chkRandomRotation.Size = new System.Drawing.Size(71, 19);
			chkRandomRotation.TabIndex = 5;
			chkRandomRotation.Text = "Rotation";
			// 
			// chkPerpendicular
			// 
			chkPerpendicular.AutoSize = true;
			chkPerpendicular.Location = new System.Drawing.Point(175, 53);
			chkPerpendicular.Name = "chkPerpendicular";
			chkPerpendicular.Size = new System.Drawing.Size(99, 19);
			chkPerpendicular.TabIndex = 14;
			chkPerpendicular.Text = "Perpendicular";
			// 
			// chkRandomScale
			// 
			chkRandomScale.AutoSize = true;
			chkRandomScale.Location = new System.Drawing.Point(141, 82);
			chkRandomScale.Name = "chkRandomScale";
			chkRandomScale.Size = new System.Drawing.Size(56, 19);
			chkRandomScale.TabIndex = 7;
			chkRandomScale.Text = "Scale:";
			// 
			// chkFitToGround
			// 
			chkFitToGround.AutoSize = true;
			chkFitToGround.Location = new System.Drawing.Point(6, 107);
			chkFitToGround.Name = "chkFitToGround";
			chkFitToGround.Size = new System.Drawing.Size(81, 19);
			chkFitToGround.TabIndex = 6;
			chkFitToGround.Text = "Fit to floor";
			// 
			// nudScaleMin
			// 
			nudScaleMin.DecimalPlaces = 2;
			nudScaleMin.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
			nudScaleMin.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
			nudScaleMin.Location = new System.Drawing.Point(193, 81);
			nudScaleMin.LoopValues = false;
			nudScaleMin.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			nudScaleMin.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
			nudScaleMin.Name = "nudScaleMin";
			nudScaleMin.Size = new System.Drawing.Size(52, 23);
			nudScaleMin.TabIndex = 8;
			nudScaleMin.Value = new decimal(new int[] { 80, 0, 0, 131072 });
			// 
			// nudScaleMax
			// 
			nudScaleMax.DecimalPlaces = 2;
			nudScaleMax.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
			nudScaleMax.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
			nudScaleMax.Location = new System.Drawing.Point(264, 81);
			nudScaleMax.LoopValues = false;
			nudScaleMax.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
			nudScaleMax.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
			nudScaleMax.Name = "nudScaleMax";
			nudScaleMax.Size = new System.Drawing.Size(52, 23);
			nudScaleMax.TabIndex = 9;
			nudScaleMax.Value = new decimal(new int[] { 120, 0, 0, 131072 });
			// 
			// lblDash
			// 
			lblDash.AutoSize = true;
			lblDash.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			lblDash.Location = new System.Drawing.Point(246, 85);
			lblDash.Name = "lblDash";
			lblDash.Size = new System.Drawing.Size(13, 15);
			lblDash.TabIndex = 10;
			lblDash.Text = "–";
			// 
			// lblRotation
			// 
			lblRotation.AutoSize = true;
			lblRotation.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			lblRotation.Location = new System.Drawing.Point(59, 55);
			lblRotation.Name = "lblRotation";
			lblRotation.Size = new System.Drawing.Size(55, 15);
			lblRotation.TabIndex = 11;
			lblRotation.Text = "Rotation:";
			// 
			// nudRotation
			// 
			nudRotation.DecimalPlaces = 1;
			nudRotation.Increment = new decimal(new int[] { 5, 0, 0, 0 });
			nudRotation.IncrementAlternate = new decimal(new int[] { 1, 0, 0, 0 });
			nudRotation.Location = new System.Drawing.Point(117, 52);
			nudRotation.LoopValues = true;
			nudRotation.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
			nudRotation.Name = "nudRotation";
			nudRotation.Size = new System.Drawing.Size(52, 23);
			nudRotation.TabIndex = 12;
			// 
			// chkShowTextures
			// 
			chkShowTextures.AutoSize = true;
			chkShowTextures.Location = new System.Drawing.Point(243, 107);
			chkShowTextures.Name = "chkShowTextures";
			chkShowTextures.Size = new System.Drawing.Size(100, 19);
			chkShowTextures.TabIndex = 13;
			chkShowTextures.Text = "Show textures";
			// 
			// darkLabel1
			// 
			darkLabel1.AutoSize = true;
			darkLabel1.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			darkLabel1.Location = new System.Drawing.Point(4, 83);
			darkLabel1.Name = "darkLabel1";
			darkLabel1.Size = new System.Drawing.Size(55, 15);
			darkLabel1.TabIndex = 15;
			darkLabel1.Text = "Random:";
			// 
			// ObjectBrushToolbox
			// 
			AutoAnchor = true;
			BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
			Controls.Add(lblDash);
			Controls.Add(darkLabel1);
			Controls.Add(chkShowTextures);
			Controls.Add(chkPerpendicular);
			Controls.Add(nudRotation);
			Controls.Add(lblRotation);
			Controls.Add(nudScaleMin);
			Controls.Add(chkRandomScale);
			Controls.Add(nudDensity);
			Controls.Add(nudRadius);
			Controls.Add(chkFitToGround);
			Controls.Add(chkRandomRotation);
			Controls.Add(btnShapeCircle);
			Controls.Add(btnShapeSquare);
			Controls.Add(lblRadius);
			Controls.Add(lblDensity);
			Controls.Add(chkAdjacentRooms);
			Controls.Add(nudScaleMax);
			Name = "ObjectBrushToolbox";
			Size = new System.Drawing.Size(344, 131);
			((System.ComponentModel.ISupportInitialize)nudRadius).EndInit();
			((System.ComponentModel.ISupportInitialize)nudDensity).EndInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMin).EndInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMax).EndInit();
			((System.ComponentModel.ISupportInitialize)nudRotation).EndInit();
			ResumeLayout(false);
			PerformLayout();
		}

		#endregion

		private DarkUI.Controls.DarkButton btnShapeCircle;
        private DarkUI.Controls.DarkButton btnShapeSquare;
        private DarkUI.Controls.DarkLabel lblRadius;
        private DarkUI.Controls.DarkNumericUpDown nudRadius;
        private DarkUI.Controls.DarkLabel lblDensity;
        private DarkUI.Controls.DarkNumericUpDown nudDensity;
        private DarkUI.Controls.DarkCheckBox chkAdjacentRooms;
        private DarkUI.Controls.DarkCheckBox chkRandomRotation;
        private DarkUI.Controls.DarkCheckBox chkFitToGround;
        private DarkUI.Controls.DarkCheckBox chkRandomScale;
        private DarkUI.Controls.DarkNumericUpDown nudScaleMin;
        private DarkUI.Controls.DarkNumericUpDown nudScaleMax;
		private DarkUI.Controls.DarkLabel lblDash;
		private DarkUI.Controls.DarkLabel lblRotation;
		private DarkUI.Controls.DarkNumericUpDown nudRotation;
		private DarkUI.Controls.DarkCheckBox chkShowTextures;
		private DarkUI.Controls.DarkCheckBox chkPerpendicular;
		private DarkUI.Controls.DarkLabel darkLabel1;
	}
}
