namespace TombIDE.ProjectMaster
{
	partial class SectionLevelProperties
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Component Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            checkBox_ShowAllFiles = new DarkUI.Controls.DarkCheckBox();
            contextMenu = new DarkUI.Controls.DarkContextMenu();
            menuItem_Open = new System.Windows.Forms.ToolStripMenuItem();
            menuItem_OpenFolder = new System.Windows.Forms.ToolStripMenuItem();
            separator_01 = new System.Windows.Forms.ToolStripSeparator();
            label_Loading = new DarkUI.Controls.DarkLabel();
            radioButton_LatestFile = new DarkUI.Controls.DarkRadioButton();
            radioButton_SpecificFile = new DarkUI.Controls.DarkRadioButton();
            sectionPanel = new DarkUI.Controls.DarkSectionPanel();
            tabControl = new System.Windows.Forms.CustomTabControl();
            tabPage_LevelSettings = new System.Windows.Forms.TabPage();
            treeView_AllPrjFiles = new DarkUI.Controls.DarkTreeView();
            tabPage_Resources = new System.Windows.Forms.TabPage();
            treeView_Resources = new DarkUI.Controls.DarkTreeView();
            tabPage_ = new System.Windows.Forms.TabPage();
            Label_fogColor = new DarkUI.Controls.DarkLabel();
            Label_skyLayerColor2 = new DarkUI.Controls.DarkLabel();
            Label_skyLayerColor1 = new DarkUI.Controls.DarkLabel();
            darkDataGridView1 = new DarkUI.Controls.DarkDataGridView();
            darkLabel3 = new DarkUI.Controls.DarkLabel();
            darkCheckBox1 = new DarkUI.Controls.DarkCheckBox();
            darkNumericUpDown5 = new DarkUI.Controls.DarkNumericUpDown();
            darkNumericUpDown4 = new DarkUI.Controls.DarkNumericUpDown();
            darkNumericUpDown3 = new DarkUI.Controls.DarkNumericUpDown();
            darkLabel4 = new DarkUI.Controls.DarkLabel();
            darkLabel5 = new DarkUI.Controls.DarkLabel();
            darkNumericUpDown2 = new DarkUI.Controls.DarkNumericUpDown();
            darkNumericUpDown1 = new DarkUI.Controls.DarkNumericUpDown();
            darkLabel2 = new DarkUI.Controls.DarkLabel();
            darkLabel1 = new DarkUI.Controls.DarkLabel();
            ComboBox_WeatherType = new DarkUI.Controls.DarkComboBox();
            darkLabel_weather = new DarkUI.Controls.DarkLabel();
            ComboBox_LaraType = new DarkUI.Controls.DarkComboBox();
            Label_LaraType = new DarkUI.Controls.DarkLabel();
            NumericUpDown_farView = new DarkUI.Controls.DarkNumericUpDown();
            darkLabel_farView = new DarkUI.Controls.DarkLabel();
            NumericUpDown_secrets = new DarkUI.Controls.DarkNumericUpDown();
            darkLabel_secrets = new DarkUI.Controls.DarkLabel();
            CheckBox_rumble = new DarkUI.Controls.DarkCheckBox();
            CheckBox_storm = new DarkUI.Controls.DarkCheckBox();
            CheckBox_horizon = new DarkUI.Controls.DarkCheckBox();
            ComboBox_nameKey = new DarkUI.Controls.DarkComboBox();
            Label_nameKey = new DarkUI.Controls.DarkLabel();
            ComboBox_loadScreenFile = new DarkUI.Controls.DarkComboBox();
            Label_loadScreenFile = new DarkUI.Controls.DarkLabel();
            ComboBox_ambientTrack = new DarkUI.Controls.DarkComboBox();
            Label_ambientTrack = new DarkUI.Controls.DarkLabel();
            timer_ResourceRefreshDelay = new System.Windows.Forms.Timer(components);
            toolTip1 = new System.Windows.Forms.ToolTip(components);
            contextMenu.SuspendLayout();
            sectionPanel.SuspendLayout();
            tabControl.SuspendLayout();
            tabPage_LevelSettings.SuspendLayout();
            tabPage_Resources.SuspendLayout();
            tabPage_.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)darkDataGridView1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown5).BeginInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown4).BeginInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown3).BeginInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown2).BeginInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_farView).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_secrets).BeginInit();
            SuspendLayout();
            // 
            // checkBox_ShowAllFiles
            // 
            checkBox_ShowAllFiles.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            checkBox_ShowAllFiles.Location = new System.Drawing.Point(9, 338);
            checkBox_ShowAllFiles.Name = "checkBox_ShowAllFiles";
            checkBox_ShowAllFiles.Size = new System.Drawing.Size(290, 18);
            checkBox_ShowAllFiles.TabIndex = 3;
            checkBox_ShowAllFiles.Text = "Show all .prj2 files (includes backup files)";
            checkBox_ShowAllFiles.CheckedChanged += checkBox_ShowAllFiles_CheckedChanged;
            // 
            // contextMenu
            // 
            contextMenu.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            contextMenu.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            contextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { menuItem_Open, menuItem_OpenFolder, separator_01 });
            contextMenu.Name = "contextMenu";
            contextMenu.Size = new System.Drawing.Size(223, 59);
            // 
            // menuItem_Open
            // 
            menuItem_Open.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            menuItem_Open.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            menuItem_Open.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            menuItem_Open.Name = "menuItem_Open";
            menuItem_Open.Size = new System.Drawing.Size(222, 24);
            menuItem_Open.Text = "Open";
            menuItem_Open.Click += menuItem_Open_Click;
            // 
            // menuItem_OpenFolder
            // 
            menuItem_OpenFolder.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            menuItem_OpenFolder.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            menuItem_OpenFolder.Image = Properties.Resources.forward_arrow_16;
            menuItem_OpenFolder.Name = "menuItem_OpenFolder";
            menuItem_OpenFolder.Size = new System.Drawing.Size(222, 24);
            menuItem_OpenFolder.Text = "Open Folder in Explorer";
            menuItem_OpenFolder.Click += menuItem_OpenFolder_Click;
            // 
            // separator_01
            // 
            separator_01.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            separator_01.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            separator_01.Margin = new System.Windows.Forms.Padding(0, 0, 0, 1);
            separator_01.Name = "separator_01";
            separator_01.Size = new System.Drawing.Size(219, 6);
            // 
            // label_Loading
            // 
            label_Loading.BackColor = System.Drawing.Color.FromArgb(48, 48, 48);
            label_Loading.Dock = System.Windows.Forms.DockStyle.Fill;
            label_Loading.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            label_Loading.ForeColor = System.Drawing.Color.Gray;
            label_Loading.Location = new System.Drawing.Point(0, 0);
            label_Loading.Name = "label_Loading";
            label_Loading.Size = new System.Drawing.Size(308, 562);
            label_Loading.TabIndex = 1;
            label_Loading.Text = "Loading resources. Please wait...";
            label_Loading.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            label_Loading.Visible = false;
            // 
            // radioButton_LatestFile
            // 
            radioButton_LatestFile.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            radioButton_LatestFile.Location = new System.Drawing.Point(9, 9);
            radioButton_LatestFile.Name = "radioButton_LatestFile";
            radioButton_LatestFile.Size = new System.Drawing.Size(290, 18);
            radioButton_LatestFile.TabIndex = 0;
            radioButton_LatestFile.TabStop = true;
            radioButton_LatestFile.Text = "Use the latest .prj2 file by date as the default level file";
            radioButton_LatestFile.CheckedChanged += radioButton_LatestFile_CheckedChanged;
            // 
            // radioButton_SpecificFile
            // 
            radioButton_SpecificFile.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            radioButton_SpecificFile.Location = new System.Drawing.Point(9, 33);
            radioButton_SpecificFile.Name = "radioButton_SpecificFile";
            radioButton_SpecificFile.Size = new System.Drawing.Size(290, 18);
            radioButton_SpecificFile.TabIndex = 1;
            radioButton_SpecificFile.TabStop = true;
            radioButton_SpecificFile.Text = "Use a specific .prj2 file from the folder:";
            radioButton_SpecificFile.CheckedChanged += radioButton_SpecificFile_CheckedChanged;
            // 
            // sectionPanel
            // 
            sectionPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            sectionPanel.Controls.Add(tabControl);
            sectionPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            sectionPanel.Location = new System.Drawing.Point(0, 0);
            sectionPanel.Name = "sectionPanel";
            sectionPanel.SectionHeader = "Selected Level Properties";
            sectionPanel.Size = new System.Drawing.Size(320, 620);
            sectionPanel.TabIndex = 0;
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabPage_LevelSettings);
            tabControl.Controls.Add(tabPage_Resources);
            tabControl.Controls.Add(tabPage_);
            tabControl.DisplayStyle = System.Windows.Forms.TabStyle.Dark;
            tabControl.DisplayStyleProvider.BorderColor = System.Drawing.Color.FromArgb(96, 96, 96);
            tabControl.DisplayStyleProvider.BorderColorHot = System.Drawing.Color.FromArgb(96, 96, 96);
            tabControl.DisplayStyleProvider.BorderColorSelected = System.Drawing.Color.FromArgb(96, 96, 96);
            tabControl.DisplayStyleProvider.CloserColor = System.Drawing.Color.White;
            tabControl.DisplayStyleProvider.CloserColorActive = System.Drawing.Color.FromArgb(152, 196, 232);
            tabControl.DisplayStyleProvider.FocusTrack = false;
            tabControl.DisplayStyleProvider.HotTrack = false;
            tabControl.DisplayStyleProvider.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            tabControl.DisplayStyleProvider.Opacity = 1F;
            tabControl.DisplayStyleProvider.Overlap = 0;
            tabControl.DisplayStyleProvider.Padding = new System.Drawing.Point(6, 3);
            tabControl.DisplayStyleProvider.Radius = 10;
            tabControl.DisplayStyleProvider.ShowTabCloser = false;
            tabControl.DisplayStyleProvider.TextColor = System.Drawing.Color.FromArgb(153, 153, 153);
            tabControl.DisplayStyleProvider.TextColorDisabled = System.Drawing.Color.FromArgb(96, 96, 96);
            tabControl.DisplayStyleProvider.TextColorSelected = System.Drawing.Color.FromArgb(152, 196, 232);
            tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            tabControl.Enabled = false;
            tabControl.Location = new System.Drawing.Point(1, 25);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new System.Drawing.Size(316, 592);
            tabControl.TabIndex = 0;
            tabControl.SelectedIndexChanged += tabControl_SelectedIndexChanged;
            // 
            // tabPage_LevelSettings
            // 
            tabPage_LevelSettings.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabPage_LevelSettings.Controls.Add(checkBox_ShowAllFiles);
            tabPage_LevelSettings.Controls.Add(treeView_AllPrjFiles);
            tabPage_LevelSettings.Controls.Add(radioButton_SpecificFile);
            tabPage_LevelSettings.Controls.Add(radioButton_LatestFile);
            tabPage_LevelSettings.Location = new System.Drawing.Point(4, 23);
            tabPage_LevelSettings.Name = "tabPage_LevelSettings";
            tabPage_LevelSettings.Padding = new System.Windows.Forms.Padding(6);
            tabPage_LevelSettings.Size = new System.Drawing.Size(308, 565);
            tabPage_LevelSettings.TabIndex = 0;
            tabPage_LevelSettings.Text = "Project file";
            // 
            // treeView_AllPrjFiles
            // 
            treeView_AllPrjFiles.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            treeView_AllPrjFiles.BackColor = System.Drawing.Color.FromArgb(48, 48, 48);
            treeView_AllPrjFiles.ExpandOnDoubleClick = false;
            treeView_AllPrjFiles.Location = new System.Drawing.Point(9, 57);
            treeView_AllPrjFiles.MaxDragChange = 20;
            treeView_AllPrjFiles.Name = "treeView_AllPrjFiles";
            treeView_AllPrjFiles.OverrideEvenColor = System.Drawing.Color.FromArgb(48, 48, 48);
            treeView_AllPrjFiles.OverrideOddColor = System.Drawing.Color.FromArgb(44, 44, 44);
            treeView_AllPrjFiles.Size = new System.Drawing.Size(290, 275);
            treeView_AllPrjFiles.TabIndex = 2;
            treeView_AllPrjFiles.SelectedNodesChanged += treeView_AllPrjFiles_SelectedNodesChanged;
            // 
            // tabPage_Resources
            // 
            tabPage_Resources.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabPage_Resources.Controls.Add(label_Loading);
            tabPage_Resources.Controls.Add(treeView_Resources);
            tabPage_Resources.Location = new System.Drawing.Point(4, 26);
            tabPage_Resources.Name = "tabPage_Resources";
            tabPage_Resources.Size = new System.Drawing.Size(308, 562);
            tabPage_Resources.TabIndex = 1;
            tabPage_Resources.Text = "Resources";
            // 
            // treeView_Resources
            // 
            treeView_Resources.BackColor = System.Drawing.Color.FromArgb(48, 48, 48);
            treeView_Resources.Dock = System.Windows.Forms.DockStyle.Fill;
            treeView_Resources.ExpandOnDoubleClick = false;
            treeView_Resources.ItemHeight = 30;
            treeView_Resources.Location = new System.Drawing.Point(0, 0);
            treeView_Resources.MaxDragChange = 30;
            treeView_Resources.Name = "treeView_Resources";
            treeView_Resources.OverrideEvenColor = System.Drawing.Color.FromArgb(48, 48, 48);
            treeView_Resources.OverrideOddColor = System.Drawing.Color.FromArgb(44, 44, 44);
            treeView_Resources.ShowIcons = true;
            treeView_Resources.Size = new System.Drawing.Size(308, 562);
            treeView_Resources.TabIndex = 0;
            treeView_Resources.MouseClick += treeView_Resources_MouseClick;
            treeView_Resources.MouseDoubleClick += treeView_Resources_MouseDoubleClick;
            // 
            // tabPage_
            // 
            tabPage_.AutoScroll = true;
            tabPage_.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabPage_.Controls.Add(Label_fogColor);
            tabPage_.Controls.Add(Label_skyLayerColor2);
            tabPage_.Controls.Add(Label_skyLayerColor1);
            tabPage_.Controls.Add(darkDataGridView1);
            tabPage_.Controls.Add(darkLabel3);
            tabPage_.Controls.Add(darkCheckBox1);
            tabPage_.Controls.Add(darkNumericUpDown5);
            tabPage_.Controls.Add(darkNumericUpDown4);
            tabPage_.Controls.Add(darkNumericUpDown3);
            tabPage_.Controls.Add(darkLabel4);
            tabPage_.Controls.Add(darkLabel5);
            tabPage_.Controls.Add(darkNumericUpDown2);
            tabPage_.Controls.Add(darkNumericUpDown1);
            tabPage_.Controls.Add(darkLabel2);
            tabPage_.Controls.Add(darkLabel1);
            tabPage_.Controls.Add(ComboBox_WeatherType);
            tabPage_.Controls.Add(darkLabel_weather);
            tabPage_.Controls.Add(ComboBox_LaraType);
            tabPage_.Controls.Add(Label_LaraType);
            tabPage_.Controls.Add(NumericUpDown_farView);
            tabPage_.Controls.Add(darkLabel_farView);
            tabPage_.Controls.Add(NumericUpDown_secrets);
            tabPage_.Controls.Add(darkLabel_secrets);
            tabPage_.Controls.Add(CheckBox_rumble);
            tabPage_.Controls.Add(CheckBox_storm);
            tabPage_.Controls.Add(CheckBox_horizon);
            tabPage_.Controls.Add(ComboBox_nameKey);
            tabPage_.Controls.Add(Label_nameKey);
            tabPage_.Controls.Add(ComboBox_loadScreenFile);
            tabPage_.Controls.Add(Label_loadScreenFile);
            tabPage_.Controls.Add(ComboBox_ambientTrack);
            tabPage_.Controls.Add(Label_ambientTrack);
            tabPage_.Location = new System.Drawing.Point(4, 23);
            tabPage_.Name = "tabPage_";
            tabPage_.Padding = new System.Windows.Forms.Padding(3);
            tabPage_.Size = new System.Drawing.Size(308, 565);
            tabPage_.TabIndex = 2;
            tabPage_.Text = "Settings";
            toolTip1.SetToolTip(tabPage_, "Color");
            // 
            // Label_fogColor
            // 
            Label_fogColor.BackColor = System.Drawing.Color.White;
            Label_fogColor.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            Label_fogColor.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_fogColor.Location = new System.Drawing.Point(139, 334);
            Label_fogColor.Name = "Label_fogColor";
            Label_fogColor.Size = new System.Drawing.Size(26, 26);
            Label_fogColor.TabIndex = 33;
            toolTip1.SetToolTip(Label_fogColor, "Color");
            Label_fogColor.Click += Label_fogColor_Click;
            // 
            // Label_skyLayerColor2
            // 
            Label_skyLayerColor2.BackColor = System.Drawing.Color.White;
            Label_skyLayerColor2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            Label_skyLayerColor2.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_skyLayerColor2.Location = new System.Drawing.Point(139, 301);
            Label_skyLayerColor2.Name = "Label_skyLayerColor2";
            Label_skyLayerColor2.Size = new System.Drawing.Size(26, 26);
            Label_skyLayerColor2.TabIndex = 32;
            toolTip1.SetToolTip(Label_skyLayerColor2, "Color");
            Label_skyLayerColor2.Click += Label_skyLayer2_Click;
            // 
            // Label_skyLayerColor1
            // 
            Label_skyLayerColor1.BackColor = System.Drawing.Color.White;
            Label_skyLayerColor1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            Label_skyLayerColor1.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_skyLayerColor1.Location = new System.Drawing.Point(139, 267);
            Label_skyLayerColor1.Name = "Label_skyLayerColor1";
            Label_skyLayerColor1.Size = new System.Drawing.Size(26, 26);
            Label_skyLayerColor1.TabIndex = 31;
            toolTip1.SetToolTip(Label_skyLayerColor1, "Color");
            Label_skyLayerColor1.Click += Label_skyLayer1_Click;
            // 
            // darkDataGridView1
            // 
            darkDataGridView1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            darkDataGridView1.ColumnHeadersHeight = 4;
            darkDataGridView1.ForegroundColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkDataGridView1.Location = new System.Drawing.Point(6, 479);
            darkDataGridView1.Name = "darkDataGridView1";
            darkDataGridView1.RowHeadersWidth = 41;
            darkDataGridView1.RowTemplate.Height = 28;
            darkDataGridView1.Size = new System.Drawing.Size(296, 81);
            darkDataGridView1.TabIndex = 29;
            // 
            // darkLabel3
            // 
            darkLabel3.AutoSize = true;
            darkLabel3.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel3.Location = new System.Drawing.Point(6, 457);
            darkLabel3.Name = "darkLabel3";
            darkLabel3.Size = new System.Drawing.Size(55, 19);
            darkLabel3.TabIndex = 28;
            darkLabel3.Text = "Objects";
            // 
            // darkCheckBox1
            // 
            darkCheckBox1.AutoSize = true;
            darkCheckBox1.Location = new System.Drawing.Point(6, 432);
            darkCheckBox1.Name = "darkCheckBox1";
            darkCheckBox1.Size = new System.Drawing.Size(104, 17);
            darkCheckBox1.TabIndex = 27;
            darkCheckBox1.Text = "Reset hub data";
            // 
            // darkNumericUpDown5
            // 
            darkNumericUpDown5.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            darkNumericUpDown5.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            darkNumericUpDown5.Location = new System.Drawing.Point(242, 333);
            darkNumericUpDown5.LoopValues = false;
            darkNumericUpDown5.Maximum = new decimal(new int[] { 32767, 0, 0, 0 });
            darkNumericUpDown5.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            darkNumericUpDown5.Name = "darkNumericUpDown5";
            darkNumericUpDown5.Size = new System.Drawing.Size(60, 26);
            darkNumericUpDown5.TabIndex = 26;
            toolTip1.SetToolTip(darkNumericUpDown5, "Max distance");
            // 
            // darkNumericUpDown4
            // 
            darkNumericUpDown4.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            darkNumericUpDown4.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            darkNumericUpDown4.Location = new System.Drawing.Point(171, 333);
            darkNumericUpDown4.LoopValues = false;
            darkNumericUpDown4.Maximum = new decimal(new int[] { 32767, 0, 0, 0 });
            darkNumericUpDown4.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            darkNumericUpDown4.Name = "darkNumericUpDown4";
            darkNumericUpDown4.Size = new System.Drawing.Size(60, 26);
            darkNumericUpDown4.TabIndex = 25;
            toolTip1.SetToolTip(darkNumericUpDown4, "Min distance");
            // 
            // darkNumericUpDown3
            // 
            darkNumericUpDown3.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            darkNumericUpDown3.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            darkNumericUpDown3.Location = new System.Drawing.Point(171, 301);
            darkNumericUpDown3.LoopValues = false;
            darkNumericUpDown3.Maximum = new decimal(new int[] { 32767, 0, 0, 0 });
            darkNumericUpDown3.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            darkNumericUpDown3.Name = "darkNumericUpDown3";
            darkNumericUpDown3.Size = new System.Drawing.Size(131, 26);
            darkNumericUpDown3.TabIndex = 22;
            toolTip1.SetToolTip(darkNumericUpDown3, "Speed");
            // 
            // darkLabel4
            // 
            darkLabel4.AutoSize = true;
            darkLabel4.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel4.Location = new System.Drawing.Point(6, 336);
            darkLabel4.Name = "darkLabel4";
            darkLabel4.Size = new System.Drawing.Size(32, 19);
            darkLabel4.TabIndex = 21;
            darkLabel4.Text = "Fog";
            // 
            // darkLabel5
            // 
            darkLabel5.AutoSize = true;
            darkLabel5.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel5.Location = new System.Drawing.Point(6, 303);
            darkLabel5.Name = "darkLabel5";
            darkLabel5.Size = new System.Drawing.Size(129, 19);
            darkLabel5.TabIndex = 20;
            darkLabel5.Text = "Secondary sky layer";
            // 
            // darkNumericUpDown2
            // 
            darkNumericUpDown2.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            darkNumericUpDown2.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            darkNumericUpDown2.Location = new System.Drawing.Point(171, 267);
            darkNumericUpDown2.LoopValues = false;
            darkNumericUpDown2.Maximum = new decimal(new int[] { 32767, 0, 0, 0 });
            darkNumericUpDown2.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            darkNumericUpDown2.Name = "darkNumericUpDown2";
            darkNumericUpDown2.Size = new System.Drawing.Size(131, 26);
            darkNumericUpDown2.TabIndex = 11;
            toolTip1.SetToolTip(darkNumericUpDown2, "Speed");
            // 
            // darkNumericUpDown1
            // 
            darkNumericUpDown1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            darkNumericUpDown1.DecimalPlaces = 1;
            darkNumericUpDown1.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            darkNumericUpDown1.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            darkNumericUpDown1.Location = new System.Drawing.Point(139, 235);
            darkNumericUpDown1.LoopValues = false;
            darkNumericUpDown1.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            darkNumericUpDown1.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            darkNumericUpDown1.Name = "darkNumericUpDown1";
            darkNumericUpDown1.Size = new System.Drawing.Size(163, 26);
            darkNumericUpDown1.TabIndex = 18;
            darkNumericUpDown1.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // darkLabel2
            // 
            darkLabel2.AutoSize = true;
            darkLabel2.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel2.Location = new System.Drawing.Point(6, 270);
            darkLabel2.Name = "darkLabel2";
            darkLabel2.Size = new System.Drawing.Size(113, 19);
            darkLabel2.TabIndex = 5;
            darkLabel2.Text = "Primary sky layer";
            // 
            // darkLabel1
            // 
            darkLabel1.AutoSize = true;
            darkLabel1.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel1.Location = new System.Drawing.Point(6, 237);
            darkLabel1.Name = "darkLabel1";
            darkLabel1.Size = new System.Drawing.Size(116, 19);
            darkLabel1.TabIndex = 17;
            darkLabel1.Text = "Weather strength";
            // 
            // ComboBox_WeatherType
            // 
            ComboBox_WeatherType.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ComboBox_WeatherType.FormattingEnabled = true;
            ComboBox_WeatherType.Items.AddRange(new object[] { "None", "Rain", "Snow" });
            ComboBox_WeatherType.Location = new System.Drawing.Point(139, 202);
            ComboBox_WeatherType.Name = "ComboBox_WeatherType";
            ComboBox_WeatherType.Size = new System.Drawing.Size(163, 27);
            ComboBox_WeatherType.TabIndex = 16;
            // 
            // darkLabel_weather
            // 
            darkLabel_weather.AutoSize = true;
            darkLabel_weather.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel_weather.Location = new System.Drawing.Point(6, 205);
            darkLabel_weather.Name = "darkLabel_weather";
            darkLabel_weather.Size = new System.Drawing.Size(97, 19);
            darkLabel_weather.TabIndex = 15;
            darkLabel_weather.Text = "Weather effect";
            // 
            // ComboBox_LaraType
            // 
            ComboBox_LaraType.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ComboBox_LaraType.FormattingEnabled = true;
            ComboBox_LaraType.Items.AddRange(new object[] { "Normal", "Young", "Bunhead", "Catsuit", "Divesuit", "Invisible" });
            ComboBox_LaraType.Location = new System.Drawing.Point(139, 169);
            ComboBox_LaraType.Name = "ComboBox_LaraType";
            ComboBox_LaraType.Size = new System.Drawing.Size(163, 27);
            ComboBox_LaraType.TabIndex = 14;
            // 
            // Label_LaraType
            // 
            Label_LaraType.AutoSize = true;
            Label_LaraType.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_LaraType.Location = new System.Drawing.Point(6, 172);
            Label_LaraType.Name = "Label_LaraType";
            Label_LaraType.Size = new System.Drawing.Size(67, 19);
            Label_LaraType.TabIndex = 13;
            Label_LaraType.Text = "Lara Type";
            // 
            // NumericUpDown_farView
            // 
            NumericUpDown_farView.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            NumericUpDown_farView.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            NumericUpDown_farView.Location = new System.Drawing.Point(139, 137);
            NumericUpDown_farView.LoopValues = false;
            NumericUpDown_farView.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            NumericUpDown_farView.Name = "NumericUpDown_farView";
            NumericUpDown_farView.Size = new System.Drawing.Size(163, 26);
            NumericUpDown_farView.TabIndex = 12;
            NumericUpDown_farView.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // darkLabel_farView
            // 
            darkLabel_farView.AutoSize = true;
            darkLabel_farView.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel_farView.Location = new System.Drawing.Point(6, 139);
            darkLabel_farView.Name = "darkLabel_farView";
            darkLabel_farView.Size = new System.Drawing.Size(158, 19);
            darkLabel_farView.TabIndex = 11;
            darkLabel_farView.Text = "Maximum draw distance";
            // 
            // NumericUpDown_secrets
            // 
            NumericUpDown_secrets.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            NumericUpDown_secrets.IncrementAlternate = new decimal(new int[] { 10, 0, 0, 65536 });
            NumericUpDown_secrets.Location = new System.Drawing.Point(139, 105);
            NumericUpDown_secrets.LoopValues = false;
            NumericUpDown_secrets.Name = "NumericUpDown_secrets";
            NumericUpDown_secrets.Size = new System.Drawing.Size(163, 26);
            NumericUpDown_secrets.TabIndex = 10;
            // 
            // darkLabel_secrets
            // 
            darkLabel_secrets.AutoSize = true;
            darkLabel_secrets.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel_secrets.Location = new System.Drawing.Point(6, 107);
            darkLabel_secrets.Name = "darkLabel_secrets";
            darkLabel_secrets.Size = new System.Drawing.Size(121, 19);
            darkLabel_secrets.TabIndex = 9;
            darkLabel_secrets.Text = "Number of secrets";
            // 
            // CheckBox_rumble
            // 
            CheckBox_rumble.AutoSize = true;
            CheckBox_rumble.Location = new System.Drawing.Point(6, 409);
            CheckBox_rumble.Name = "CheckBox_rumble";
            CheckBox_rumble.Size = new System.Drawing.Size(219, 17);
            CheckBox_rumble.TabIndex = 8;
            CheckBox_rumble.Text = "Enable occasional screen shake effect";
            // 
            // CheckBox_storm
            // 
            CheckBox_storm.AutoSize = true;
            CheckBox_storm.Location = new System.Drawing.Point(6, 386);
            CheckBox_storm.Name = "CheckBox_storm";
            CheckBox_storm.Size = new System.Drawing.Size(215, 17);
            CheckBox_storm.TabIndex = 7;
            CheckBox_storm.Text = "Enable flickering lightning in the sky";
            // 
            // CheckBox_horizon
            // 
            CheckBox_horizon.AutoSize = true;
            CheckBox_horizon.Location = new System.Drawing.Point(6, 363);
            CheckBox_horizon.Name = "CheckBox_horizon";
            CheckBox_horizon.Size = new System.Drawing.Size(99, 17);
            CheckBox_horizon.TabIndex = 6;
            CheckBox_horizon.Text = "Draw sky layer";
            // 
            // ComboBox_nameKey
            // 
            ComboBox_nameKey.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ComboBox_nameKey.FormattingEnabled = true;
            ComboBox_nameKey.Location = new System.Drawing.Point(139, 6);
            ComboBox_nameKey.Name = "ComboBox_nameKey";
            ComboBox_nameKey.Size = new System.Drawing.Size(163, 27);
            ComboBox_nameKey.TabIndex = 5;
            // 
            // Label_nameKey
            // 
            Label_nameKey.AutoSize = true;
            Label_nameKey.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_nameKey.Location = new System.Drawing.Point(6, 14);
            Label_nameKey.Name = "Label_nameKey";
            Label_nameKey.Size = new System.Drawing.Size(71, 19);
            Label_nameKey.TabIndex = 4;
            Label_nameKey.Text = "Name Key";
            // 
            // ComboBox_loadScreenFile
            // 
            ComboBox_loadScreenFile.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ComboBox_loadScreenFile.FormattingEnabled = true;
            ComboBox_loadScreenFile.Location = new System.Drawing.Point(139, 72);
            ComboBox_loadScreenFile.Name = "ComboBox_loadScreenFile";
            ComboBox_loadScreenFile.Size = new System.Drawing.Size(163, 27);
            ComboBox_loadScreenFile.TabIndex = 3;
            // 
            // Label_loadScreenFile
            // 
            Label_loadScreenFile.AutoSize = true;
            Label_loadScreenFile.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_loadScreenFile.Location = new System.Drawing.Point(6, 79);
            Label_loadScreenFile.Name = "Label_loadScreenFile";
            Label_loadScreenFile.Size = new System.Drawing.Size(123, 19);
            Label_loadScreenFile.TabIndex = 2;
            Label_loadScreenFile.Text = "Load screen image";
            // 
            // ComboBox_ambientTrack
            // 
            ComboBox_ambientTrack.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ComboBox_ambientTrack.FormattingEnabled = true;
            ComboBox_ambientTrack.Location = new System.Drawing.Point(139, 39);
            ComboBox_ambientTrack.Name = "ComboBox_ambientTrack";
            ComboBox_ambientTrack.Size = new System.Drawing.Size(163, 27);
            ComboBox_ambientTrack.TabIndex = 1;
            // 
            // Label_ambientTrack
            // 
            Label_ambientTrack.AutoSize = true;
            Label_ambientTrack.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            Label_ambientTrack.Location = new System.Drawing.Point(6, 47);
            Label_ambientTrack.Name = "Label_ambientTrack";
            Label_ambientTrack.Size = new System.Drawing.Size(137, 19);
            Label_ambientTrack.TabIndex = 0;
            Label_ambientTrack.Text = "Ambient sound track";
            // 
            // timer_ResourceRefreshDelay
            // 
            timer_ResourceRefreshDelay.Interval = 1;
            timer_ResourceRefreshDelay.Tick += timer_ResourceRefreshDelay_Tick;
            // 
            // SectionLevelProperties
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            Controls.Add(sectionPanel);
            Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            Name = "SectionLevelProperties";
            Size = new System.Drawing.Size(320, 620);
            contextMenu.ResumeLayout(false);
            sectionPanel.ResumeLayout(false);
            tabControl.ResumeLayout(false);
            tabPage_LevelSettings.ResumeLayout(false);
            tabPage_Resources.ResumeLayout(false);
            tabPage_.ResumeLayout(false);
            tabPage_.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)darkDataGridView1).EndInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown5).EndInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown4).EndInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown3).EndInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown2).EndInit();
            ((System.ComponentModel.ISupportInitialize)darkNumericUpDown1).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_farView).EndInit();
            ((System.ComponentModel.ISupportInitialize)NumericUpDown_secrets).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private DarkUI.Controls.DarkCheckBox checkBox_ShowAllFiles;
		private DarkUI.Controls.DarkContextMenu contextMenu;
		private DarkUI.Controls.DarkLabel label_Loading;
		private DarkUI.Controls.DarkRadioButton radioButton_LatestFile;
		private DarkUI.Controls.DarkRadioButton radioButton_SpecificFile;
		private DarkUI.Controls.DarkSectionPanel sectionPanel;
		private DarkUI.Controls.DarkTreeView treeView_AllPrjFiles;
		private DarkUI.Controls.DarkTreeView treeView_Resources;
		private System.Windows.Forms.CustomTabControl tabControl;
		private System.Windows.Forms.TabPage tabPage_LevelSettings;
		private System.Windows.Forms.TabPage tabPage_Resources;
		private System.Windows.Forms.Timer timer_ResourceRefreshDelay;
		private System.Windows.Forms.ToolStripMenuItem menuItem_Open;
		private System.Windows.Forms.ToolStripMenuItem menuItem_OpenFolder;
		private System.Windows.Forms.ToolStripSeparator separator_01;
        private System.Windows.Forms.TabPage tabPage_;
        private DarkUI.Controls.DarkComboBox ComboBox_ambientTrack;
        private DarkUI.Controls.DarkLabel Label_ambientTrack;
        private DarkUI.Controls.DarkLabel Label_loadScreenFile;
        private DarkUI.Controls.DarkComboBox ComboBox_loadScreenFile;
        private DarkUI.Controls.DarkComboBox ComboBox_nameKey;
        private DarkUI.Controls.DarkLabel Label_nameKey;
        private DarkUI.Controls.DarkCheckBox CheckBox_storm;
        private DarkUI.Controls.DarkCheckBox CheckBox_horizon;
        private DarkUI.Controls.DarkCheckBox CheckBox_rumble;
        private DarkUI.Controls.DarkLabel darkLabel_secrets;
        private DarkUI.Controls.DarkNumericUpDown NumericUpDown_farView;
        private DarkUI.Controls.DarkLabel darkLabel_farView;
        private DarkUI.Controls.DarkNumericUpDown NumericUpDown_secrets;
        private DarkUI.Controls.DarkComboBox ComboBox_LaraType;
        private DarkUI.Controls.DarkLabel Label_LaraType;
        private DarkUI.Controls.DarkComboBox ComboBox_WeatherType;
        private DarkUI.Controls.DarkLabel darkLabel_weather;
        private DarkUI.Controls.DarkNumericUpDown darkNumericUpDown1;
        private DarkUI.Controls.DarkLabel darkLabel1;
        private DarkUI.Controls.DarkNumericUpDown darkNumericUpDown2;
        private DarkUI.Controls.DarkLabel darkLabel2;
        private DarkUI.Controls.DarkNumericUpDown darkNumericUpDown3;
        private DarkUI.Controls.DarkLabel darkLabel4;
        private DarkUI.Controls.DarkLabel darkLabel5;
        private System.Windows.Forms.ToolTip toolTip1;
        private DarkUI.Controls.DarkNumericUpDown darkNumericUpDown5;
        private DarkUI.Controls.DarkNumericUpDown darkNumericUpDown4;
        private DarkUI.Controls.DarkLabel darkLabel3;
        private DarkUI.Controls.DarkCheckBox darkCheckBox1;
        private DarkUI.Controls.DarkDataGridView darkDataGridView1;
        private DarkUI.Controls.DarkLabel Label_skyLayerColor1;
        private DarkUI.Controls.DarkLabel Label_skyLayerColor2;
        private DarkUI.Controls.DarkLabel Label_fogColor;
    }
}
