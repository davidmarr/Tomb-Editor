using System;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using TombLib.Utils;

namespace TombEditor.Views
{
    public partial class ToolBoxView : UserControl
    {
        private Editor _editor;
        private DispatcherTimer _contextMenuTimer;

        // Parent WinForms control reference for context menu hosting.
        private System.Windows.Forms.Control _winFormsHost;

        // Fires when the preferred height of the visible content changes.
        public event Action<int> PreferredHeightChanged;

        public ToolBoxView()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                _editor = Editor.Instance;
                _editor.EditorEventRaised += EditorEventRaised;
            }

            _contextMenuTimer = new DispatcherTimer();
            _contextMenuTimer.Interval = TimeSpan.FromMilliseconds(300);
            _contextMenuTimer.Tick += OnContextMenuTimerTick;

            AssignIcons();
        }

        public void Cleanup()
        {
            if (_editor != null)
                _editor.EditorEventRaised -= EditorEventRaised;
        }

        // Sets the parent WinForms control for context menu hosting.

        public void SetWinFormsHost(System.Windows.Forms.Control host)
        {
            _winFormsHost = host;
        }

        public System.Windows.Controls.Orientation PanelOrientation
        {
            get => toolPanel.Orientation;
            set
            {
                if (toolPanel.Orientation == value)
                    return;

                toolPanel.Orientation = value;
                UpdateSeparatorOrientation();
            }
        }

        private void UpdateSeparatorOrientation()
        {
            bool vertical = toolPanel.Orientation == System.Windows.Controls.Orientation.Vertical;

            foreach (var child in LogicalTreeHelper.GetChildren(toolPanel))
            {
                if (child is Border border && border.Style == (Style)FindResource("ToolSeparator"))
                {
                    if (vertical)
                    {
                        border.Width = double.NaN;
                        border.Height = 1;
                    }
                    else
                    {
                        border.Width = 1;
                        border.Height = double.NaN;
                    }
                }
            }
        }

        #region Icon Assignment

        private void AssignIcons()
        {
            SetIcon(toolBrushShapeCircle, Properties.Resources.objects_volume_sphere_16);
            SetIcon(toolBrushShapeSquare, Properties.Resources.objects_volume_box_16);
            SetIcon(toolSelection, Properties.Resources.toolbox_Selection_16);
            SetIcon(toolObjectDeselect, Properties.Resources.toolbox_Deselection_16);
            SetIcon(toolBrush, Properties.Resources.toolbox_Paint_16);
            SetIcon(toolShovel, Properties.Resources.toolbox_Shovel_16);
            SetIcon(toolPencil, Properties.Resources.toolbox_Pencil_16);
            SetIcon(toolLine, Properties.Resources.toolbox_Ruler_16);
            SetIcon(toolFlatten, Properties.Resources.toolbox_Bulldozer_1_16);
            SetIcon(toolSmooth, Properties.Resources.toolbox_Smooth_16);
            SetIcon(toolFill, Properties.Resources.toolbox_Fill_16);
            SetIcon(toolGridPaint, Properties.Resources.toolbox_GridPaint2x2_16);
            SetIcon(toolGroup, Properties.Resources.toolbox_GroupTexture_16);
            SetIcon(toolObjectEraser, Properties.Resources.toolbox_Eraser_16);
            SetIcon(toolDrag, Properties.Resources.toolbox_Drag_16);
            SetIcon(toolRamp, Properties.Resources.toolbox_GroupRamp_16);
            SetIcon(toolQuarterPipe, Properties.Resources.toolbox_GroupQuaterPipe_16);
            SetIcon(toolHalfPipe, Properties.Resources.toolbox_GroupHalfPipe_16);
            SetIcon(toolBowl, Properties.Resources.toolbox_GroupBowl_16);
            SetIcon(toolPyramid, Properties.Resources.toolbox_GroupPyramid_16);
            SetIcon(toolTerrain, Properties.Resources.toolbox_GroupTerrain_16);
            SetIcon(toolEraser, Properties.Resources.toolbox_Eraser_16);
            SetIcon(toolInvisibility, Properties.Resources.toolbox_Invisible_16);
            SetIcon(toolPortalDigger, Properties.Resources.toolbox_PortalDigger_16);
            SetIcon(toolUVFixer, Properties.Resources.toolbox_UVFixer_16);
            SetIcon(toolShowTextures, Properties.Resources.actions_TextureMode_16);
        }

        private static void SetIcon(ToggleButton button, System.Drawing.Bitmap bitmap)
        {
            button.Content = new Image { Source = ConvertBitmap(bitmap) };
        }

        private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        #endregion

        #region Editor Events

        private void EditorEventRaised(IEditorEvent obj)
        {
            if (obj is Editor.ToolChangedEvent || obj is Editor.InitEvent)
                UpdateToolCheckedState();

            if (obj is Editor.ObjectBrushSettingsChangedEvent ||
                obj is Editor.ConfigurationChangedEvent ||
                obj is Editor.InitEvent)
                UpdateBrushSettings();

            if (obj is Editor.SelectedTexturesChangedEvent || obj is Editor.InitEvent)
                UpdateTextureState();

            if (obj is Editor.ModeChangedEvent || obj is Editor.InitEvent)
                UpdateModeVisibility();
        }

        private void UpdateToolCheckedState()
        {
            var currentTool = _editor.Tool;

            toolSelection.IsChecked = currentTool.Tool == EditorToolType.Selection;
            toolBrush.IsChecked = currentTool.Tool == EditorToolType.Brush;
            toolPencil.IsChecked = currentTool.Tool == EditorToolType.Pencil;
            toolLine.IsChecked = currentTool.Tool == EditorToolType.Line;
            toolObjectDeselect.IsChecked = currentTool.Tool == EditorToolType.Deselect;
            toolFill.IsChecked = currentTool.Tool == EditorToolType.Fill;
            toolGroup.IsChecked = currentTool.Tool == EditorToolType.Group;
            toolGridPaint.IsChecked = currentTool.Tool == EditorToolType.GridPaint;
            toolShovel.IsChecked = currentTool.Tool == EditorToolType.Shovel;
            toolFlatten.IsChecked = currentTool.Tool == EditorToolType.Flatten;
            toolSmooth.IsChecked = currentTool.Tool == EditorToolType.Smooth;
            toolDrag.IsChecked = currentTool.Tool == EditorToolType.Drag;
            toolRamp.IsChecked = currentTool.Tool == EditorToolType.Ramp;
            toolQuarterPipe.IsChecked = currentTool.Tool == EditorToolType.QuarterPipe;
            toolHalfPipe.IsChecked = currentTool.Tool == EditorToolType.HalfPipe;
            toolBowl.IsChecked = currentTool.Tool == EditorToolType.Bowl;
            toolPyramid.IsChecked = currentTool.Tool == EditorToolType.Pyramid;
            toolTerrain.IsChecked = currentTool.Tool == EditorToolType.Terrain;
            toolPortalDigger.IsChecked = currentTool.Tool == EditorToolType.PortalDigger;
            toolObjectEraser.IsChecked = currentTool.Tool == EditorToolType.Eraser;
            toolUVFixer.IsChecked = currentTool.TextureUVFixer;

            UpdateGridPaintIcon(currentTool.GridSize);
        }

        private void UpdateBrushSettings()
        {
            toolBrushShapeCircle.IsChecked = _editor.Configuration.ObjectBrush_Shape == ObjectBrushShape.Circle;
            toolBrushShapeSquare.IsChecked = _editor.Configuration.ObjectBrush_Shape == ObjectBrushShape.Square;
            toolShowTextures.IsChecked = _editor.Configuration.ObjectBrush_ShowTextures;
        }

        private void UpdateTextureState()
        {
            toolEraser.IsChecked = _editor.SelectedTexture.Texture == null;
            toolInvisibility.IsChecked = _editor.SelectedTexture.Texture is TextureInvisible;
        }

        private void UpdateModeVisibility()
        {
            var mode = _editor.Mode;
            bool geometryMode = mode == EditorMode.Geometry;
            bool faceEditMode = mode == EditorMode.FaceEdit || mode == EditorMode.Lighting;
            bool objectPlacementMode = mode == EditorMode.ObjectPlacement;

            SetVisible(toolFill, faceEditMode || objectPlacementMode);
            SetVisible(toolGroup, faceEditMode);
            SetVisible(toolGridPaint, faceEditMode);
            SetVisible(toolEraser, faceEditMode);
            SetVisible(toolInvisibility, faceEditMode);
            SetVisible(toolUVFixer, faceEditMode);

            SetVisible(toolFlatten, geometryMode);
            SetVisible(toolShovel, geometryMode);
            SetVisible(toolSmooth, geometryMode);
            SetVisible(toolDrag, geometryMode);
            SetVisible(toolRamp, geometryMode);
            SetVisible(toolQuarterPipe, geometryMode);
            SetVisible(toolHalfPipe, geometryMode);
            SetVisible(toolBowl, geometryMode);
            SetVisible(toolPyramid, geometryMode);
            SetVisible(toolTerrain, geometryMode);
            SetVisible(toolPortalDigger, geometryMode);

            SetVisible(toolBrushShapeCircle, objectPlacementMode);
            SetVisible(toolBrushShapeSquare, objectPlacementMode);
            SetVisible(toolBrushShapeSeparator, objectPlacementMode);
            SetVisible(toolLine, objectPlacementMode);
            SetVisible(toolObjectDeselect, objectPlacementMode);
            SetVisible(toolObjectEraser, objectPlacementMode);
            SetVisible(toolSeparator2, !objectPlacementMode);
            SetVisible(toolShowTextures, objectPlacementMode);

            bool showToolbox = mode == EditorMode.FaceEdit || mode == EditorMode.Lighting ||
                mode == EditorMode.Geometry || mode == EditorMode.ObjectPlacement;
            Visibility = showToolbox ? Visibility.Visible : Visibility.Collapsed;

            RequestHeightUpdate();
        }

        // Schedules a deferred preferred height recalculation.

        public void RequestHeightUpdate()
        {
            Dispatcher.BeginInvoke(new Action(NotifyPreferredHeightChanged), DispatcherPriority.Render);
        }

        private void NotifyPreferredHeightChanged()
        {
            if (Visibility != Visibility.Visible)
            {
                PreferredHeightChanged?.Invoke(0);
                return;
            }

            double availableWidth = ActualWidth > 0 ? ActualWidth : 9999;
            Measure(new Size(availableWidth, double.PositiveInfinity));

            double dpiScale = GetDpiScale();
            int height = Math.Max(1, (int)Math.Ceiling(DesiredSize.Height * dpiScale));

            PreferredHeightChanged?.Invoke(height);
        }

        private double GetDpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        }

        private static void SetVisible(UIElement element, bool visible)
        {
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Tool Switching

        private void SwitchTool(EditorToolType tool)
        {
            var currentTool = new EditorTool
            {
                Tool = tool,
                TextureUVFixer = _editor.Tool.TextureUVFixer,
                GridSize = _editor.Tool.GridSize
            };
            _editor.Tool = currentTool;
        }

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button && button.Tag is string toolName)
            {
                if (Enum.TryParse<EditorToolType>(toolName, out var toolType))
                    SwitchTool(toolType);
            }
        }

        private void OnBrushShapeCircleClick(object sender, RoutedEventArgs e)
        {
            _editor.Configuration.ObjectBrush_Shape = ObjectBrushShape.Circle;
            _editor.ObjectBrushSettingsChange();
        }

        private void OnBrushShapeSquareClick(object sender, RoutedEventArgs e)
        {
            _editor.Configuration.ObjectBrush_Shape = ObjectBrushShape.Square;
            _editor.ObjectBrushSettingsChange();
        }

        private void OnShowTexturesClick(object sender, RoutedEventArgs e)
        {
            _editor.Configuration.ObjectBrush_ShowTextures = !_editor.Configuration.ObjectBrush_ShowTextures;
            _editor.ObjectBrushSettingsChange();
        }

        private void OnTextureEraserClick(object sender, RoutedEventArgs e)
        {
            _editor.SelectedTexture = TextureArea.None;
        }

        private void OnInvisibilityClick(object sender, RoutedEventArgs e)
        {
            _editor.SelectedTexture = TextureArea.Invisible;
        }

        private void OnUVFixerClick(object sender, RoutedEventArgs e)
        {
            var currentTool = new EditorTool
            {
                Tool = _editor.Tool.Tool,
                TextureUVFixer = !_editor.Tool.TextureUVFixer,
                GridSize = _editor.Tool.GridSize
            };
            _editor.Tool = currentTool;
        }

        #endregion

        #region Grid Paint

        private void OnGridPaintClick(object sender, RoutedEventArgs e)
        {
            SwitchTool(EditorToolType.GridPaint);
        }

        private void OnGridPaintMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
                ShowGridPaintContextMenu();
            else
                _contextMenuTimer.Start();
        }

        private void OnGridPaintMouseUp(object sender, MouseButtonEventArgs e)
        {
            _contextMenuTimer.Stop();
        }

        private void OnContextMenuTimerTick(object sender, EventArgs e)
        {
            _contextMenuTimer.Stop();
            ShowGridPaintContextMenu();
        }

        private void ShowGridPaintContextMenu()
        {
            var owner = _winFormsHost as System.Windows.Forms.IWin32Window;
            var menu = new Controls.ContextMenus.GridPaintContextMenu(_editor, owner);
            menu.Show(System.Windows.Forms.Cursor.Position);
        }

        private void UpdateGridPaintIcon(PaintGridSize gridSize)
        {
            System.Drawing.Bitmap icon;
            string tooltip;

            switch (gridSize)
            {
                case PaintGridSize.Grid2x2:
                    icon = Properties.Resources.toolbox_GridPaint2x2_16;
                    tooltip = "Grid Paint (2x2)";
                    break;
                case PaintGridSize.Grid3x3:
                    icon = Properties.Resources.toolbox_GridPaint3x3_16;
                    tooltip = "Grid Paint (3x3)";
                    break;
                case PaintGridSize.Grid4x4:
                    icon = Properties.Resources.toolbox_GridPaint4x4_16;
                    tooltip = "Grid Paint (4x4)";
                    break;
                default:
                    return;
            }

            SetIcon(toolGridPaint, icon);
            toolGridPaint.ToolTip = tooltip;
        }

        #endregion
    }
}
