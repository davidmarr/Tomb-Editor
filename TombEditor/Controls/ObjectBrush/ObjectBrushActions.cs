using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TombEditor;
using TombLib;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.ObjectBrush
{
    /// <summary>
    /// High-level brush/eraser actions called from the Panel3D mouse handlers.
    /// Keeps the Panel3D code focused on picking and state management,
    /// while all placement/erasure orchestration lives in the ObjectBrush folder.
    /// </summary>
    public static class ObjectBrushActions
    {
        /// <summary>
        /// Result of a brush paint step containing undo instances and a world position.
        /// </summary>
        public struct BrushPaintResult
        {
            public List<UndoRedoInstance> UndoInstances;
            public Vector3 WorldPosition;
        }

        /// <summary>
        /// Begins a brush stroke on mouse down — places or erases objects at the clicked sector.
        /// </summary>
        public static BrushPaintResult BeginBrushStroke(
            Editor editor,
            Room selectedRoom,
            VectorInt2 sectorPos)
        {
            float centerWorldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            float centerWorldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;

            var worldPosition = new Vector3(
                centerWorldX + selectedRoom.WorldPos.X,
                0,
                centerWorldZ + selectedRoom.WorldPos.Z);

            var sectorConstraint = editor.SelectedSectors.Valid && !editor.SelectedSectors.Empty
                ? (RectangleInt2?)editor.SelectedSectors.Area : null;

            var undoInstances = new List<UndoRedoInstance>();

            if (editor.Tool.Tool == EditorToolType.ObjectBrush && editor.ChosenItems.Count > 0)
            {
                var placed = ObjectBrushHelper.PlaceObjectsWithBrush(editor, selectedRoom,
                    centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                undoInstances.AddRange(placed.Select(o =>
                    new AddRemoveObjectUndoInstance(editor.UndoManager, o, true)));
            }
            else if (editor.Tool.Tool == EditorToolType.ObjectEraser && editor.ChosenItems.Count > 0)
            {
                var removed = ObjectBrushHelper.EraseObjectsWithBrush(editor, selectedRoom,
                    centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                undoInstances.AddRange(removed.Select(r =>
                    new AddRemoveObjectUndoInstance(editor.UndoManager, r.Obj, false, r.Room)));
            }

            return new BrushPaintResult { UndoInstances = undoInstances, WorldPosition = worldPosition };
        }

        /// <summary>
        /// Continues a brush stroke during mouse drag with quantized movement.
        /// Returns null if mouse movement was smaller than the quantization threshold.
        /// </summary>
        public static BrushPaintResult? ContinueBrushStroke(
            Editor editor,
            Room pickedRoom,
            Room selectedRoom,
            VectorInt2 sectorPos,
            Vector3? lastWorldPosition,
            float quantizationDistance)
        {
            var room = pickedRoom == selectedRoom ? selectedRoom : pickedRoom;

            float worldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit + room.WorldPos.X;
            float worldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit + room.WorldPos.Z;
            var currentWorldPos = new Vector3(worldX, 0, worldZ);

            // Quantize movement — only paint if moved far enough
            bool shouldPaint = !lastWorldPosition.HasValue;
            if (!shouldPaint && lastWorldPosition.HasValue)
            {
                float dx = currentWorldPos.X - lastWorldPosition.Value.X;
                float dz = currentWorldPos.Z - lastWorldPosition.Value.Z;
                shouldPaint = (dx * dx + dz * dz) >= (quantizationDistance * quantizationDistance);
            }

            if (!shouldPaint || editor.ChosenItems.Count == 0)
            {
                // Update cursor even when not painting
                UpdateBrushCursor(editor, room, sectorPos);
                return null;
            }

            // Convert to selected room's local world units
            float centerWorldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            if (room != selectedRoom)
                centerWorldX += (room.WorldPos.X - selectedRoom.WorldPos.X);
            float centerWorldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;
            if (room != selectedRoom)
                centerWorldZ += (room.WorldPos.Z - selectedRoom.WorldPos.Z);

            var sectorConstraint = editor.SelectedSectors.Valid && !editor.SelectedSectors.Empty
                ? (RectangleInt2?)editor.SelectedSectors.Area : null;

            var undoInstances = new List<UndoRedoInstance>();

            if (editor.Tool.Tool == EditorToolType.ObjectBrush)
            {
                var placed = ObjectBrushHelper.PlaceObjectsWithBrush(editor, selectedRoom,
                    centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                undoInstances.AddRange(placed.Select(o =>
                    new AddRemoveObjectUndoInstance(editor.UndoManager, o, true)));
            }
            else if (editor.Tool.Tool == EditorToolType.ObjectEraser)
            {
                var removed = ObjectBrushHelper.EraseObjectsWithBrush(editor, selectedRoom,
                    centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                undoInstances.AddRange(removed.Select(r =>
                    new AddRemoveObjectUndoInstance(editor.UndoManager, r.Obj, false, r.Room)));
            }

            // Update cursor
            UpdateBrushCursor(editor, room, sectorPos);

            return new BrushPaintResult { UndoInstances = undoInstances, WorldPosition = currentWorldPos };
        }

        /// <summary>
        /// Updates the brush cursor visualization at a given sector position.
        /// Called on mouse move regardless of whether the brush is actively painting.
        /// </summary>
        public static void UpdateBrushCursor(Editor editor, Room room, VectorInt2 sectorPos)
        {
            float localX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            float localZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;
            float? floorH = ObjectBrushHelper.GetFloorHeightAtPoint(room, localX, localZ);
            float yPos = floorH.HasValue ? (floorH.Value + room.WorldPos.Y) : room.WorldPos.Y;
            float worldX = localX + room.WorldPos.X;
            float worldZ = localZ + room.WorldPos.Z;
            editor.UpdateObjectBrushCursor(new Vector3(worldX, yPos, worldZ), room);
        }

        /// <summary>
        /// Finalizes a brush stroke by pushing all accumulated undo entries as a single batch.
        /// </summary>
        public static void EndBrushStroke(Editor editor, List<UndoRedoInstance> strokeUndoList)
        {
            if (strokeUndoList.Count > 0)
            {
                editor.UndoManager.Push(strokeUndoList.ToList());
                strokeUndoList.Clear();
            }
        }
    }
}
