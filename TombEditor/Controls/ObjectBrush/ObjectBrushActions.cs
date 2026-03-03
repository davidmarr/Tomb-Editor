using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TombLib;
using TombLib.LevelData;
using TombLib.Utils;

namespace TombEditor.Controls.ObjectBrush
{
    public static class ObjectBrushActions
    {
        public struct BrushPaintResult
        {
            public List<UndoRedoInstance> UndoInstances;
            public Vector3 WorldPosition;
        }

        private static List<UndoRedoInstance> ExecuteBrushAction(Editor editor, Room selectedRoom, float centerWorldX, float centerWorldZ, RectangleInt2? sectorConstraint)
        {
            var undoInstances = new List<UndoRedoInstance>();

            if (editor.Tool.Tool == EditorToolType.ObjectBrush && editor.ChosenItems.Count > 0)
            {
                var placed = ObjectBrushHelper.PlaceObjectsWithBrush(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                undoInstances.AddRange(placed.Select(o => new AddRemoveObjectUndoInstance(editor.UndoManager, o, true)));
            }
            else if (editor.Tool.Tool == EditorToolType.ObjectEraser && editor.ChosenItems.Count > 0)
            {
                var removed = ObjectBrushHelper.EraseObjectsWithBrush(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                undoInstances.AddRange(removed.Select(r => new AddRemoveObjectUndoInstance(editor.UndoManager, r.Obj, false, r.Room)));
            }

            return undoInstances;
        }

        public static BrushPaintResult BeginBrushStroke(Editor editor, Room selectedRoom, VectorInt2 sectorPos)
        {
            float centerWorldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            float centerWorldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;

            var worldPosition = new Vector3(centerWorldX + selectedRoom.WorldPos.X, 0, centerWorldZ + selectedRoom.WorldPos.Z);
            var sectorConstraint = editor.SelectedSectors.Valid && !editor.SelectedSectors.Empty ? (RectangleInt2?)editor.SelectedSectors.Area : null;

            var undoInstances = ExecuteBrushAction(editor, selectedRoom, centerWorldX, centerWorldZ, sectorConstraint);

            return new BrushPaintResult { UndoInstances = undoInstances, WorldPosition = worldPosition };
        }

        public static BrushPaintResult? ContinueBrushStroke(Editor editor, Room pickedRoom, Room selectedRoom, VectorInt2 sectorPos, Vector3? lastWorldPosition, float quantizationDistance)
        {
            var room = pickedRoom == selectedRoom ? selectedRoom : pickedRoom;

            float worldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit + room.WorldPos.X;
            float worldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit + room.WorldPos.Z;
            var currentWorldPos = new Vector3(worldX, 0, worldZ);

            // Quantize movement — only paint if moved far enough.
            bool shouldPaint = !lastWorldPosition.HasValue;
            if (!shouldPaint && lastWorldPosition.HasValue)
            {
                float dx = currentWorldPos.X - lastWorldPosition.Value.X;
                float dz = currentWorldPos.Z - lastWorldPosition.Value.Z;
                shouldPaint = (dx * dx + dz * dz) >= (quantizationDistance * quantizationDistance);
            }

            if (!shouldPaint || editor.ChosenItems.Count == 0)
            {
                UpdateBrushCursor(editor, room, sectorPos);
                return null;
            }

            float centerWorldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            if (room != selectedRoom)
                centerWorldX += (room.WorldPos.X - selectedRoom.WorldPos.X);

            float centerWorldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;
            if (room != selectedRoom)
                centerWorldZ += (room.WorldPos.Z - selectedRoom.WorldPos.Z);

            var sectorConstraint = editor.SelectedSectors.Valid && !editor.SelectedSectors.Empty ? (RectangleInt2?)editor.SelectedSectors.Area : null;

            var undoInstances = ExecuteBrushAction(editor, selectedRoom, centerWorldX, centerWorldZ, sectorConstraint);

            UpdateBrushCursor(editor, room, sectorPos);

            return new BrushPaintResult { UndoInstances = undoInstances, WorldPosition = currentWorldPos };
        }

        public static void UpdateBrushCursor(Editor editor, Room room, VectorInt2 sectorPos)
        {
            float localX  = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            float localZ  = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;
            float worldX  = localX + room.WorldPos.X;
            float worldZ  = localZ + room.WorldPos.Z;
            float? floorH = ObjectBrushHelper.GetFloorHeightAtPoint(room, localX, localZ);
            float yPos    = floorH.HasValue ? (floorH.Value + room.WorldPos.Y) : room.WorldPos.Y;

            editor.UpdateObjectBrushCursor(new Vector3(worldX, yPos, worldZ), room);
        }

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
