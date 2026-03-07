using System;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using TombLib;
using TombLib.LevelData;

namespace TombEditor.Controls.Panel3D
{
    public partial class Panel3D
    {
        // Object brush state
        private bool _objectBrushEngaged = false;
        private Vector3? _lastBrushWorldPosition;
        private float? _lastMouseDirectionAngle;

        private void HandleBrushMouseUp()
        {
            if (_objectBrushEngaged)
                ObjectBrush.ObjectBrushActions.EndBrushStroke(_editor);

            _objectBrushEngaged = false;
            _lastBrushWorldPosition = null;
            _lastMouseDirectionAngle = null;
            ObjectBrush.ObjectBrushHelper.SetMouseDirectionAngle(null);
        }

        // Returns true if the scroll event was consumed by the brush handler.
        private bool HandleBrushWheelScroll(int delta)
        {
            if (_editor.Mode != EditorMode.ObjectPlacement)
                return false;

            var modifiers = Control.ModifierKeys;

            // Alt + mousewheel adjusts brush rotation.
            if (modifiers.HasFlag(Keys.Alt))
            {
                float rotation = _editor.Configuration.ObjectBrush_Rotation + (delta > 0 ? 5.0f : -5.0f);
                rotation = ((rotation % 360.0f) + 360.0f) % 360.0f;
                _editor.Configuration.ObjectBrush_Rotation = rotation;
                _editor.ObjectBrushSettingsChange();
                Invalidate();
                return true;
            }

            // Ctrl + mousewheel adjusts brush radius.
            if (modifiers.HasFlag(Keys.Control))
            {
                float radius = _editor.Configuration.ObjectBrush_Radius + (delta > 0 ? 64.0f : -64.0f);
                _editor.Configuration.ObjectBrush_Radius = Math.Max(64.0f, radius);
                _editor.ObjectBrushSettingsChange();
                Invalidate();
                return true;
            }

            // Shift + mousewheel adjusts brush density.
            if (modifiers.HasFlag(Keys.Shift))
            {
                float density = _editor.Configuration.ObjectBrush_Density + (delta > 0 ? 0.1f : -0.1f);
                _editor.Configuration.ObjectBrush_Density = Math.Max(0.1f, (float)Math.Round(density, 1));
                _editor.ObjectBrushSettingsChange();
                Invalidate();
                return true;
            }

            return false;
        }

        private void HandleObjectPlacementMouseDown(Point location, VectorInt2 pos, PickingResultSector newSectorPicking)
        {
            if (!newSectorPicking.BelongsToFloor)
                return;

            if (_editor.Tool.Tool == EditorToolType.Fill)
            {
                // Fill executes immediately without brush engagement.
                ObjectBrush.ObjectBrushActions.ExecuteFill(_editor, _editor.SelectedRoom);
                Invalidate();
            }
            else
            {
                _objectBrushEngaged = true;

                // Compute actual cursor position from ray for sub-sector precision.
                var ray = GetRay(location.X, location.Y);
                var cursorWorldPos = new Vector3(
                    ray.Position.X + ray.Direction.X * newSectorPicking.Distance,
                    0,
                    ray.Position.Z + ray.Direction.Z * newSectorPicking.Distance);

                _lastBrushWorldPosition = ObjectBrush.ObjectBrushActions.BeginBrushStroke(_editor, _editor.SelectedRoom, pos, cursorWorldPos);
                Invalidate();
            }
        }

        // Returns true if the brush consumed the mouse move event (redraw needed).
        private bool HandleBrushMouseMove(Point location)
        {
            const float EraserQuantizationDistance = Level.SectorSizeUnit * 0.15f;

            if (!_objectBrushEngaged || _editor.Mode != EditorMode.ObjectPlacement)
                return false;

            var brushPicking = DoPicking(GetRay(location.X, location.Y), skipObjects: true) as PickingResultSector;
            if (brushPicking == null || !brushPicking.BelongsToFloor)
                return true;

            // Eraser fires on fixed step; other tools quantize to avoid over-painting.
            float quantizationDistance = _editor.Tool.Tool == EditorToolType.Eraser ? EraserQuantizationDistance : _editor.Configuration.ObjectBrush_Radius;

            // For Line tool, constrain movement to the rotation direction.
            // Use bounding box extent along the rotation axis for seamless spacing.
            if (_editor.Tool.Tool == EditorToolType.Line && _lastBrushWorldPosition.HasValue)
            {
                float rotRad = _editor.Configuration.ObjectBrush_Rotation * (float)(Math.PI / 180.0);
                var rotDir = new Vector3((float)Math.Sin(rotRad), 0, (float)Math.Cos(rotRad));

                // Use actual ray-intersection position, not sector-center-snapped position.
                var ray = GetRay(location.X, location.Y);
                var hitPos = new Vector3(
                    ray.Position.X + ray.Direction.X * brushPicking.Distance,
                    0,
                    ray.Position.Z + ray.Direction.Z * brushPicking.Distance);

                var delta = hitPos - _lastBrushWorldPosition.Value;
                delta.Y = 0;
                float proj = Vector3.Dot(delta, rotDir);

                // Compute seamless spacing from bounding box extent along local Z (rotation axis).
                float spacing = ObjectBrush.ObjectBrushHelper.ComputePencilSpacing(_editor);

                var room = brushPicking.Room == _editor.SelectedRoom ? _editor.SelectedRoom : brushPicking.Room;

                if (proj < spacing)
                {
                    // Not yet moved far enough; update cursor display only.
                    ObjectBrush.ObjectBrushActions.UpdateBrushCursor(_editor, room, brushPicking.Pos, hitPos.X, hitPos.Z);
                    return true;
                }

                // Snap to exact one-step advance from the last anchor for gapless tiling.
                // Pass null as lastWorldPosition to force paint regardless of distance, avoiding
                // the floating-point precision issue that caused the brush to get stuck.
                var snappedPos = _lastBrushWorldPosition.Value + rotDir * spacing;
                int sx = (int)((snappedPos.X - room.WorldPos.X) / Level.SectorSizeUnit);
                int sz = (int)((snappedPos.Z - room.WorldPos.Z) / Level.SectorSizeUnit);
                var constrainedPos = new VectorInt2(sx, sz);

                var result = ObjectBrush.ObjectBrushActions.ContinueBrushStroke(
                    _editor, room, _editor.SelectedRoom, constrainedPos,
                    null, spacing, snappedPos);

                if (result.HasValue)
                {
                    _lastBrushWorldPosition = snappedPos;
                    Invalidate();
                }
            }
            else
            {
                // Compute actual cursor position from ray intersection for sub-sector precision.
                var ray = GetRay(location.X, location.Y);
                var cursorWorldPos = new Vector3(
                    ray.Position.X + ray.Direction.X * brushPicking.Distance,
                    0,
                    ray.Position.Z + ray.Direction.Z * brushPicking.Distance);

                // Track mouse movement direction for the FollowMouseDirection option.
                if (_lastBrushWorldPosition.HasValue)
                {
                    float dx = cursorWorldPos.X - _lastBrushWorldPosition.Value.X;
                    float dz = cursorWorldPos.Z - _lastBrushWorldPosition.Value.Z;
                    if (dx * dx + dz * dz > 0.01f)
                    {
                        float angle = (float)(Math.Atan2(dx, dz) * (180.0f / Math.PI));
                        angle = ((angle % 360.0f) + 360.0f) % 360.0f;

                        if (_lastMouseDirectionAngle.HasValue)
                        {
                            float diff = ((angle - _lastMouseDirectionAngle.Value + 540.0f) % 360.0f) - 180.0f;
                            _lastMouseDirectionAngle = ((_lastMouseDirectionAngle.Value + diff * 0.35f) % 360.0f + 360.0f) % 360.0f;
                        }
                        else
                            _lastMouseDirectionAngle = angle;
                    }
                }
                ObjectBrush.ObjectBrushHelper.SetMouseDirectionAngle(_lastMouseDirectionAngle);

                var newPos = ObjectBrush.ObjectBrushActions.ContinueBrushStroke(
                    _editor,
                    brushPicking.Room,
                    _editor.SelectedRoom,
                    brushPicking.Pos,
                    _lastBrushWorldPosition,
                    quantizationDistance,
                    cursorWorldPos);

                if (newPos.HasValue)
                {
                    _lastBrushWorldPosition = newPos.Value;
                    Invalidate();
                }
            }

            return true;
        }
    }
}
