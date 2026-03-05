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
			((System.ComponentModel.ISupportInitialize)nudRadius).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudDensity).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMin).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMax).BeginInit();
			((System.ComponentModel.ISupportInitialize)nudRotation).BeginInit();
			SuspendLayout();
			// 
			// lblRadius
			// 
			lblRadius.AutoSize = true;
			lblRadius.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			lblRadius.Location = new System.Drawing.Point(18, 10);
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
			nudRadius.Location = new System.Drawing.Point(64, 9);
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
			lblDensity.Location = new System.Drawing.Point(126, 11);
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
			nudDensity.Location = new System.Drawing.Point(176, 9);
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
			chkAdjacentRooms.Location = new System.Drawing.Point(135, 64);
			chkAdjacentRooms.Name = "chkAdjacentRooms";
			chkAdjacentRooms.Size = new System.Drawing.Size(152, 19);
			chkAdjacentRooms.TabIndex = 4;
			chkAdjacentRooms.Text = "Place in adjacent rooms";
			// 
			// chkRandomRotation
			// 
			chkRandomRotation.AutoSize = true;
			chkRandomRotation.Location = new System.Drawing.Point(18, 38);
			chkRandomRotation.Name = "chkRandomRotation";
			chkRandomRotation.Size = new System.Drawing.Size(116, 19);
			chkRandomRotation.TabIndex = 5;
			chkRandomRotation.Text = "Random rotation";
			// 
			// chkPerpendicular
			// 
			chkPerpendicular.AutoSize = true;
			chkPerpendicular.Location = new System.Drawing.Point(356, 10);
			chkPerpendicular.Name = "chkPerpendicular";
			chkPerpendicular.Size = new System.Drawing.Size(45, 19);
			chkPerpendicular.TabIndex = 14;
			chkPerpendicular.Text = "Flip";
			// 
			// chkRandomScale
			// 
			chkRandomScale.AutoSize = true;
			chkRandomScale.Location = new System.Drawing.Point(135, 38);
			chkRandomScale.Name = "chkRandomScale";
			chkRandomScale.Size = new System.Drawing.Size(103, 19);
			chkRandomScale.TabIndex = 7;
			chkRandomScale.Text = "Random scale:";
			// 
			// chkFitToGround
			// 
			chkFitToGround.AutoSize = true;
			chkFitToGround.Location = new System.Drawing.Point(18, 64);
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
			nudScaleMin.Location = new System.Drawing.Point(233, 38);
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
			nudScaleMax.Location = new System.Drawing.Point(298, 38);
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
			lblDash.Location = new System.Drawing.Point(286, 42);
			lblDash.Name = "lblDash";
			lblDash.Size = new System.Drawing.Size(12, 15);
			lblDash.TabIndex = 10;
			lblDash.Text = "-";
			// 
			// lblRotation
			// 
			lblRotation.AutoSize = true;
			lblRotation.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
			lblRotation.Location = new System.Drawing.Point(241, 11);
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
			nudRotation.Location = new System.Drawing.Point(298, 9);
			nudRotation.LoopValues = true;
			nudRotation.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
			nudRotation.Name = "nudRotation";
			nudRotation.Size = new System.Drawing.Size(52, 23);
			nudRotation.TabIndex = 12;
			// 
			// ObjectBrushToolbox
			// 
			AutoAnchor = true;
			BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
			Controls.Add(lblDash);
			Controls.Add(chkPerpendicular);
			Controls.Add(nudRotation);
			Controls.Add(lblRotation);
			Controls.Add(nudScaleMin);
			Controls.Add(chkRandomScale);
			Controls.Add(nudDensity);
			Controls.Add(nudRadius);
			Controls.Add(chkFitToGround);
			Controls.Add(chkRandomRotation);
			Controls.Add(lblRadius);
			Controls.Add(lblDensity);
			Controls.Add(chkAdjacentRooms);
			Controls.Add(nudScaleMax);
			Name = "ObjectBrushToolbox";
			Size = new System.Drawing.Size(404, 94);
			VerticalGrip = false;
			((System.ComponentModel.ISupportInitialize)nudRadius).EndInit();
			((System.ComponentModel.ISupportInitialize)nudDensity).EndInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMin).EndInit();
			((System.ComponentModel.ISupportInitialize)nudScaleMax).EndInit();
			((System.ComponentModel.ISupportInitialize)nudRotation).EndInit();
			ResumeLayout(false);
			PerformLayout();
		}

		#endregion

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
		private DarkUI.Controls.DarkCheckBox chkPerpendicular;
	}
}
