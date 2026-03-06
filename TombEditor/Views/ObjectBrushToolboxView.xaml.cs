using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using TombLib.LevelData;

namespace TombEditor.Views
{
    public partial class ObjectBrushToolboxView : UserControl
    {
        private Editor _editor;
        private bool _suppressEvents;

        public ObjectBrushToolboxView()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                _editor = Editor.Instance;
                _editor.EditorEventRaised += EditorEventRaised;
            }

            WireEvents();
            LoadSettings();
        }

        public void Cleanup()
        {
            if (_editor != null)
                _editor.EditorEventRaised -= EditorEventRaised;
        }

        private void WireEvents()
        {
            nudRadius.ValueChanged += OnSettingChanged;
            nudDensity.ValueChanged += OnSettingChanged;
            nudRotation.ValueChanged += OnSettingChanged;
            nudScaleMin.ValueChanged += OnSettingChanged;
            nudScaleMax.ValueChanged += OnSettingChanged;

            chkPerpendicular.Checked += OnSettingChanged;
            chkPerpendicular.Unchecked += OnSettingChanged;
            chkAdjacentRooms.Checked += OnSettingChanged;
            chkAdjacentRooms.Unchecked += OnSettingChanged;
            chkFitToGround.Checked += OnSettingChanged;
            chkFitToGround.Unchecked += OnSettingChanged;

            chkRandomRotation.Checked += OnRandomRotationChanged;
            chkRandomRotation.Unchecked += OnRandomRotationChanged;

            chkFollowMouseDirection.Checked += OnFollowMouseDirectionChanged;
            chkFollowMouseDirection.Unchecked += OnFollowMouseDirectionChanged;

            chkRandomScale.Checked += OnRandomScaleChanged;
            chkRandomScale.Unchecked += OnRandomScaleChanged;
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            if (!_suppressEvents)
                SaveSettings();
        }

        private void OnRandomRotationChanged(object sender, RoutedEventArgs e)
        {
            if (!_suppressEvents)
            {
                SaveSettings();
                UpdateRotationFieldVisibility();
            }
        }

        private void OnFollowMouseDirectionChanged(object sender, RoutedEventArgs e)
        {
            if (!_suppressEvents)
            {
                SaveSettings();
                UpdateRotationFieldVisibility();
            }
        }

        private void OnRandomScaleChanged(object sender, RoutedEventArgs e)
        {
            if (!_suppressEvents)
            {
                SaveSettings();
                UpdateScaleFieldsVisibility();
            }
        }

        private void UpdateScaleFieldsVisibility()
        {
            bool show = chkRandomScale.IsChecked == true;
            nudScaleMin.IsEnabled = show;
            nudScaleMax.IsEnabled = show;
        }

        private void UpdateRotationFieldVisibility()
        {
            bool followDir = chkFollowMouseDirection.IsChecked == true;
            nudRotation.IsEnabled = chkRandomRotation.IsChecked != true && !followDir;
        }

        private void UpdateControlsForTool()
        {
            if (_editor == null || _editor.Mode != EditorMode.ObjectPlacement)
                return;

            var tool = _editor.Tool.Tool;
            bool isBrush = tool == EditorToolType.Brush;
            bool isPencil = tool == EditorToolType.Pencil;
            bool isLine = tool == EditorToolType.Line;
            bool isEraser = tool == EditorToolType.Eraser;
            bool isFill = tool == EditorToolType.Fill;

            nudRadius.IsEnabled = true;
            nudDensity.IsEnabled = isBrush || isEraser || isFill;
            chkAdjacentRooms.IsEnabled = isBrush || isEraser || isPencil || isLine;

            bool allowRotation = isBrush || isPencil || isLine;

            chkPerpendicular.IsEnabled = allowRotation;
            chkRandomRotation.IsEnabled = isBrush || isPencil;
            chkFollowMouseDirection.IsEnabled = isBrush || isPencil;
            nudRotation.IsEnabled = isLine || (allowRotation && chkRandomRotation.IsChecked != true && chkFollowMouseDirection.IsChecked != true);

            bool allowScale = isBrush || isPencil || isLine;

            chkFitToGround.IsEnabled = allowScale;
            chkRandomScale.IsEnabled = allowScale;
            nudScaleMin.IsEnabled = allowScale && chkRandomScale.IsChecked == true;
            nudScaleMax.IsEnabled = allowScale && chkRandomScale.IsChecked == true;
        }

        private void LoadSettings()
        {
            if (_editor == null)
                return;

            _suppressEvents = true;

            var config = _editor.Configuration;

            nudRadius.Value = Clamp(config.ObjectBrush_Radius / Level.SectorSizeUnit, nudRadius.Minimum, nudRadius.Maximum);
            nudDensity.Value = Clamp(config.ObjectBrush_Density, nudDensity.Minimum, nudDensity.Maximum);
            nudRotation.Value = Clamp(config.ObjectBrush_Rotation, nudRotation.Minimum, nudRotation.Maximum);

            chkAdjacentRooms.IsChecked = config.ObjectBrush_PlaceInAdjacentRooms;
            chkRandomRotation.IsChecked = config.ObjectBrush_RandomizeRotation;
            chkFollowMouseDirection.IsChecked = config.ObjectBrush_FollowMouseDirection;
            chkPerpendicular.IsChecked = config.ObjectBrush_Perpendicular;
            chkFitToGround.IsChecked = config.ObjectBrush_FitToGround;
            chkRandomScale.IsChecked = config.ObjectBrush_RandomizeScale;

            nudScaleMin.Value = Clamp(config.ObjectBrush_ScaleMin, nudScaleMin.Minimum, nudScaleMin.Maximum);
            nudScaleMax.Value = Clamp(config.ObjectBrush_ScaleMax, nudScaleMax.Minimum, nudScaleMax.Maximum);

            _suppressEvents = false;

            UpdateScaleFieldsVisibility();
            UpdateRotationFieldVisibility();
            UpdateControlsForTool();
        }

        private void SaveSettings()
        {
            if (_editor == null)
                return;

            var config = _editor.Configuration;

            config.ObjectBrush_Radius = (float)nudRadius.Value * Level.SectorSizeUnit;
            config.ObjectBrush_Density = (float)nudDensity.Value;
            config.ObjectBrush_Rotation = (float)nudRotation.Value;
            config.ObjectBrush_PlaceInAdjacentRooms = chkAdjacentRooms.IsChecked == true;
            config.ObjectBrush_RandomizeRotation = chkRandomRotation.IsChecked == true;
            config.ObjectBrush_FollowMouseDirection = chkFollowMouseDirection.IsChecked == true;
            config.ObjectBrush_Perpendicular = chkPerpendicular.IsChecked == true;
            config.ObjectBrush_FitToGround = chkFitToGround.IsChecked == true;
            config.ObjectBrush_RandomizeScale = chkRandomScale.IsChecked == true;
            config.ObjectBrush_ScaleMin = (float)nudScaleMin.Value;
            config.ObjectBrush_ScaleMax = (float)nudScaleMax.Value;

            _editor.ObjectBrushSettingsChange();
        }

        private void EditorEventRaised(IEditorEvent obj)
        {
            if (obj is Editor.ConfigurationChangedEvent ||
                obj is Editor.ObjectBrushSettingsChangedEvent)
                LoadSettings();

            if (obj is Editor.ModeChangedEvent ||
                obj is Editor.ToolChangedEvent)
                UpdateControlsForTool();
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
