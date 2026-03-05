using DarkUI.Controls;
using System;
using System.ComponentModel;
using TombLib.LevelData;

namespace TombEditor.Controls.ObjectBrush
{
	public partial class ObjectBrushToolbox : DarkFloatingToolbox
	{
		private readonly Editor _editor;
		private bool _suppressEvents;

		public ObjectBrushToolbox()
		{
			VerticalGrip = true;
			GripSize = 12;
			AutoAnchor = false;
			SnapToBorders = true;
			DragAnyPoint = true;

			InitializeComponent();
			WireEvents();

			if (LicenseManager.UsageMode == LicenseUsageMode.Runtime)
			{
				_editor = Editor.Instance;
				_editor.EditorEventRaised += EditorEventRaised;
			}

			LoadSettings();
		}

		private void WireEvents()
		{
			nudRadius.ValueChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			nudDensity.ValueChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			nudRotation.ValueChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			chkPerpendicular.CheckedChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			chkAdjacentRooms.CheckedChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			chkRandomRotation.CheckedChanged += (s, e) =>
			{
				if (!_suppressEvents)
				{
					SaveSettings();
					UpdateRotationFieldVisibility();
				}
			};
			chkFitToGround.CheckedChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			chkRandomScale.CheckedChanged += (s, e) =>
			{
				if (!_suppressEvents)
				{
					SaveSettings();
					UpdateScaleFieldsVisibility();
				}
			};
			nudScaleMin.ValueChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
			nudScaleMax.ValueChanged += (s, e) => { if (!_suppressEvents) SaveSettings(); };
		}

		private void UpdateScaleFieldsVisibility()
		{
			bool show = chkRandomScale.Checked;
			nudScaleMin.Enabled = show;
			nudScaleMax.Enabled = show;
		}

		private void UpdateRotationFieldVisibility()
		{
			nudRotation.Enabled = !chkRandomRotation.Checked;
		}

		private void UpdateControlsForTool()
		{
			if (_editor == null || _editor.Mode != EditorMode.ObjectPlacement)
				return;

			var tool = _editor.Tool.Tool;
			bool isBrush    = tool == EditorToolType.Brush;
			bool isPencil   = tool == EditorToolType.Pencil;
			bool isLine     = tool == EditorToolType.Line;
			bool isEraser   = tool == EditorToolType.Eraser;
			bool isFill     = tool == EditorToolType.Fill;

			nudRadius.Enabled = true;

			// Density: brush and eraser only.
			nudDensity.Enabled = isBrush || isEraser || isFill;

			// Adjacent rooms: brush, eraser, pencil, line.
			chkAdjacentRooms.Enabled = isBrush || isEraser || isPencil || isLine;

			// Rotation and perpendicular: brush, pencil, line.
			bool allowRotation = isBrush || isPencil || isLine;
			chkPerpendicular.Enabled  = allowRotation;
			chkRandomRotation.Enabled = allowRotation;
			nudRotation.Enabled       = allowRotation && !chkRandomRotation.Checked;

			// Scale and fit to ground: brush, pencil, line.
			bool allowScale = isBrush || isPencil || isLine;
			chkFitToGround.Enabled = allowScale;
			chkRandomScale.Enabled = allowScale;
			nudScaleMin.Enabled    = allowScale && chkRandomScale.Checked;
			nudScaleMax.Enabled    = allowScale && chkRandomScale.Checked;
		}

		private void LoadSettings()
		{
			if (_editor == null)
				return;

			_suppressEvents = true;

			var config = _editor.Configuration;

			// Radius is stored in world units, displayed in blocks (1 block = Level.SectorSizeUnit)
			nudRadius.Value = ClampDecimal(config.ObjectBrush_Radius / (float)Level.SectorSizeUnit, nudRadius.Minimum, nudRadius.Maximum);
			nudDensity.Value = ClampDecimal(config.ObjectBrush_Density, nudDensity.Minimum, nudDensity.Maximum);

			chkAdjacentRooms.Checked = config.ObjectBrush_PlaceInAdjacentRooms;
			chkRandomRotation.Checked = config.ObjectBrush_RandomizeRotation;
			nudRotation.Value = ClampDecimal(config.ObjectBrush_Rotation, nudRotation.Minimum, nudRotation.Maximum);
			chkPerpendicular.Checked = config.ObjectBrush_Perpendicular;
			chkFitToGround.Checked = config.ObjectBrush_FitToGround;
			chkRandomScale.Checked = config.ObjectBrush_RandomizeScale;
			nudScaleMin.Value = ClampDecimal(config.ObjectBrush_ScaleMin, nudScaleMin.Minimum, nudScaleMin.Maximum);
			nudScaleMax.Value = ClampDecimal(config.ObjectBrush_ScaleMax, nudScaleMax.Minimum, nudScaleMax.Maximum);

			_suppressEvents = false;

			UpdateScaleFieldsVisibility();
			UpdateRotationFieldVisibility();
			UpdateControlsForTool();
		}

		private void SaveSettings()
		{
			if (_editor == null) return;

			var config = _editor.Configuration;

			// Radius displayed in blocks, stored in world units.
			config.ObjectBrush_Radius = (float)nudRadius.Value * Level.SectorSizeUnit;
			config.ObjectBrush_Density = (float)nudDensity.Value;
			config.ObjectBrush_PlaceInAdjacentRooms = chkAdjacentRooms.Checked;
			config.ObjectBrush_RandomizeRotation = chkRandomRotation.Checked;
			config.ObjectBrush_Rotation = (float)nudRotation.Value;
			config.ObjectBrush_Perpendicular = chkPerpendicular.Checked;
			config.ObjectBrush_FitToGround = chkFitToGround.Checked;
			config.ObjectBrush_RandomizeScale = chkRandomScale.Checked;
			config.ObjectBrush_ScaleMin = (float)nudScaleMin.Value;
			config.ObjectBrush_ScaleMax = (float)nudScaleMax.Value;

			_editor.ObjectBrushSettingsChange();
		}

		private void EditorEventRaised(IEditorEvent obj)
		{
			if (obj is Editor.ConfigurationChangedEvent)
				LoadSettings();

			// Reload when brush settings change externally (e.g. Alt+Mousewheel rotation).
			if (obj is Editor.ObjectBrushSettingsChangedEvent)
				LoadSettings();

			if (obj is Editor.ModeChangedEvent || obj is Editor.ToolChangedEvent)
				UpdateControlsForTool();
		}

		private static decimal ClampDecimal(float value, decimal min, decimal max)
		{
			return (decimal)Math.Max((float)min, Math.Min((float)max, value));
		}
	}
}
