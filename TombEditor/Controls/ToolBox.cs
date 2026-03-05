using System;
using System.ComponentModel;
using System.Windows.Forms;
using TombEditor.Controls.ContextMenus;
using TombLib.Utils;

namespace TombEditor.Controls
{
    public partial class ToolBox : UserControl
    {
        public ToolStripLayoutStyle LayoutStyle
        {
            get { return toolStrip.LayoutStyle; }
            set
            {
                if (value == toolStrip.LayoutStyle)
                    return;
                else
                {
                    toolStrip.LayoutStyle = value;
                    if (value == ToolStripLayoutStyle.Flow)
                        toolStrip.Dock = DockStyle.Fill;
                    else
                    {
                        toolStrip.Dock = DockStyle.None;
                        toolStrip.AutoSize = true;
                    }
                }
            }
        }

        private readonly Editor _editor;
        private Timer _contextMenuTimer;

        public ToolBox()
        {
            InitializeComponent();

            _contextMenuTimer = new Timer();
            _contextMenuTimer.Interval = 300;
            _contextMenuTimer.Tick += ContextMenuTimer_Tick;


            if (LicenseManager.UsageMode == LicenseUsageMode.Runtime)
            {
                _editor = Editor.Instance;
                _editor.EditorEventRaised += EditorEventRaised;
            }
        }

        private void EditorEventRaised(IEditorEvent obj)
        {
            if (obj is Editor.ToolChangedEvent || obj is Editor.InitEvent)
            {
                EditorTool currentTool = _editor.Tool;

                toolSelection.Checked = currentTool.Tool == EditorToolType.Selection;
                toolBrush.Checked = currentTool.Tool == EditorToolType.Brush;
                toolPencil.Checked = currentTool.Tool == EditorToolType.Pencil;
                toolLine.Checked = currentTool.Tool == EditorToolType.Line;
                toolObjectDeselect.Checked = currentTool.Tool == EditorToolType.Deselect;
                toolFill.Checked = currentTool.Tool == EditorToolType.Fill;
                toolGroup.Checked = currentTool.Tool == EditorToolType.Group;
                toolGridPaint.Checked = currentTool.Tool == EditorToolType.GridPaint;
                toolShovel.Checked = currentTool.Tool == EditorToolType.Shovel;
                toolFlatten.Checked = currentTool.Tool == EditorToolType.Flatten;
                toolSmooth.Checked = currentTool.Tool == EditorToolType.Smooth;
                toolDrag.Checked = currentTool.Tool == EditorToolType.Drag;
                toolRamp.Checked = currentTool.Tool == EditorToolType.Ramp;
                toolQuarterPipe.Checked = currentTool.Tool == EditorToolType.QuarterPipe;
                toolHalfPipe.Checked = currentTool.Tool == EditorToolType.HalfPipe;
                toolBowl.Checked = currentTool.Tool == EditorToolType.Bowl;
                toolPyramid.Checked = currentTool.Tool == EditorToolType.Pyramid;
                toolTerrain.Checked = currentTool.Tool == EditorToolType.Terrain;
                toolPortalDigger.Checked = currentTool.Tool == EditorToolType.PortalDigger;
                toolObjectEraser.Checked = currentTool.Tool == EditorToolType.Eraser;

                toolUVFixer.Checked = currentTool.TextureUVFixer;

                switch(currentTool.GridSize)
                {
                    case PaintGridSize.Grid2x2:
                        toolGridPaint.Image = Properties.Resources.toolbox_GridPaint2x2_16;
                        toolGridPaint.ToolTipText = "Grid Paint (2x2)";
                        break;
                    case PaintGridSize.Grid3x3:
                        toolGridPaint.Image = Properties.Resources.toolbox_GridPaint3x3_16;
                        toolGridPaint.ToolTipText = "Grid Paint (3x3)";
                        break;
                    case PaintGridSize.Grid4x4:
                        toolGridPaint.Image = Properties.Resources.toolbox_GridPaint4x4_16;
                        toolGridPaint.ToolTipText = "Grid Paint (4x4)";
                        break;
                }
            }

            if (obj is Editor.ObjectBrushSettingsChangedEvent || obj is Editor.ConfigurationChangedEvent || obj is Editor.InitEvent)
            {
                toolBrushShapeCircle.Checked = _editor.Configuration.ObjectBrush_Shape == ObjectBrushShape.Circle;
                toolBrushShapeSquare.Checked = _editor.Configuration.ObjectBrush_Shape == ObjectBrushShape.Square;
                toolShowTextures.Checked = _editor.Configuration.ObjectBrush_ShowTextures;
            }

            if (obj is Editor.SelectedTexturesChangedEvent || obj is Editor.InitEvent)
            {
                toolEraser.Checked = _editor.SelectedTexture.Texture == null;
                toolInvisibility.Checked = _editor.SelectedTexture.Texture is TextureInvisible;
            }

            if (obj is Editor.ModeChangedEvent || obj is Editor.InitEvent)
            {
                EditorMode mode = _editor.Mode;
                bool geometryMode = mode == EditorMode.Geometry;
                bool faceEditMode = mode == EditorMode.FaceEdit || mode == EditorMode.Lighting;
                bool objectPlacementMode = mode == EditorMode.ObjectPlacement;

                toolFill.Visible = faceEditMode || objectPlacementMode;
                toolGroup.Visible = faceEditMode;
                toolGridPaint.Visible = faceEditMode;
                toolEraser.Visible = faceEditMode;
                toolInvisibility.Visible = faceEditMode;
                toolUVFixer.Visible = faceEditMode;
                toolFlatten.Visible = geometryMode;
                toolShovel.Visible = geometryMode;
                toolSmooth.Visible = geometryMode;
                toolDrag.Visible = geometryMode;
                toolRamp.Visible = geometryMode;
                toolQuarterPipe.Visible = geometryMode;
                toolHalfPipe.Visible = geometryMode;
                toolBowl.Visible = geometryMode;
                toolPyramid.Visible = geometryMode;
                toolTerrain.Visible = geometryMode;
                toolPortalDigger.Visible = geometryMode;

                toolBrushShapeCircle.Visible = objectPlacementMode;
                toolBrushShapeSquare.Visible = objectPlacementMode;
                toolBrushShapeSeparator.Visible = objectPlacementMode;
                toolLine.Visible = objectPlacementMode;
                toolObjectDeselect.Visible = objectPlacementMode;
                toolObjectEraser.Visible = objectPlacementMode;
                toolSeparator2.Visible = !objectPlacementMode;
                toolShowTextures.Visible = objectPlacementMode;

                toolStrip.AutoSize = true;
                AutoSize = true;
                toolStrip.Visible = mode == EditorMode.FaceEdit || mode == EditorMode.Lighting ||
                    mode == EditorMode.Geometry || mode == EditorMode.ObjectPlacement;
            }
        }

        private void ContextMenuTimer_Tick(object sender, EventArgs e)
        {
            var _currentContextMenu = new GridPaintContextMenu(_editor, this);
            _currentContextMenu.Show(Cursor.Position);
            _contextMenuTimer.Stop();
        }

        private void SwitchTool(EditorToolType tool)
        {
            EditorTool currentTool = new EditorTool() { Tool = tool, TextureUVFixer = _editor.Tool.TextureUVFixer, GridSize = _editor.Tool.GridSize };
            _editor.Tool = currentTool;
        }

        private void toolSelection_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Selection);
        }

        private void toolBrush_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Brush);
        }

        private void toolPencil_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Pencil);
        }

        private void toolLine_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Line);
        }

        private void toolObjectDeselect_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Deselect);
        }

        private void toolBrushShapeCircle_Click(object sender, EventArgs e)
        {
            _editor.Configuration.ObjectBrush_Shape = ObjectBrushShape.Circle;
            _editor.ObjectBrushSettingsChange();
        }

        private void toolBrushShapeSquare_Click(object sender, EventArgs e)
        {
            _editor.Configuration.ObjectBrush_Shape = ObjectBrushShape.Square;
            _editor.ObjectBrushSettingsChange();
        }

        private void toolShowTextures_Click(object sender, EventArgs e)
        {
            _editor.Configuration.ObjectBrush_ShowTextures = !_editor.Configuration.ObjectBrush_ShowTextures;
            _editor.ObjectBrushSettingsChange();
        }

        private void toolShovel_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Shovel);
        }

        private void toolFlatten_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Flatten);
        }

        private void toolFill_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Fill);
        }

        private void toolSmooth_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Smooth);
        }

        private void toolGroup_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Group);
        }

        private void toolDrag_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Drag);
        }

        private void toolRamp_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Ramp);
        }

        private void toolQuarterPipe_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.QuarterPipe);
        }

        private void toolHalfPipe_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.HalfPipe);
        }

        private void toolBowl_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Bowl);
        }

        private void toolPyramid_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Pyramid);
        }

        private void toolTerrain_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Terrain);
        }

        private void tooPaint2x2_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.GridPaint);
        }
        
        private void toolPortalDigger_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.PortalDigger);
        }

        private void toolInvisibility_Click(object sender, EventArgs e)
        {
            _editor.SelectedTexture = TextureArea.Invisible;
        }

        private void toolEraser_Click(object sender, EventArgs e)
        {
            _editor.SelectedTexture = TextureArea.None;
        }

        private void toolUVFixer_Click(object sender, EventArgs e)
        {
            EditorTool currentTool = new EditorTool() { Tool = _editor.Tool.Tool, TextureUVFixer = !_editor.Tool.TextureUVFixer, GridSize = _editor.Tool.GridSize };
            _editor.Tool = currentTool;
        }

        private void toolGridPaint_MouseUp(object sender, MouseEventArgs e)
        {
            _contextMenuTimer.Stop();
        }

        private void toolGridPaint_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                _contextMenuTimer.Start();
            else
                ContextMenuTimer_Tick(sender, e);
        }

        private void toolObjectEraser_Click(object sender, EventArgs e)
        {
            SwitchTool(EditorToolType.Eraser);
        }
    }
}
