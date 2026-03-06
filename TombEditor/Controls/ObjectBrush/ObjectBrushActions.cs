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
        private static int _pencilItemIndex;

        // Stroke-session state: lives from BeginBrushStroke to EndBrushStroke.
        private static readonly List<UndoRedoInstance> _strokeUndoList = new List<UndoRedoInstance>();
        private static readonly List<PositionBasedObjectInstance> _strokePlacedObjects = new List<PositionBasedObjectInstance>();
        private static readonly HashSet<ObjectInstance> _strokeProcessedObjects = new HashSet<ObjectInstance>();

        private static void ExecuteBrushAction(Editor editor, Room selectedRoom, float centerWorldX, float centerWorldZ)
        {
            if (editor.ChosenItems.Count == 0)
                return;

            var sectorConstraint = editor.SelectedSectors.Valid && !editor.SelectedSectors.Empty
                ? (RectangleInt2?)editor.SelectedSectors.Area : null;

            switch (editor.Tool.Tool)
            {
                case EditorToolType.Brush:
                    var placed = ObjectBrushHelper.PlaceObjectsWithBrush(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                    _strokeUndoList.AddRange(placed.Select(o => new AddRemoveObjectUndoInstance(editor.UndoManager, o, true)));
                    _strokePlacedObjects.AddRange(placed);
                    break;

                case EditorToolType.Eraser:
                    var removed = ObjectBrushHelper.EraseObjectsWithBrush(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint);
                    _strokeUndoList.AddRange(removed.Select(r => new AddRemoveObjectUndoInstance(editor.UndoManager, r.Obj, false, r.Room)));
                    break;

                case EditorToolType.Selection:
                    ObjectBrushHelper.SelectObjectsWithBrush(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint, _strokeProcessedObjects);
                    break;

                case EditorToolType.Deselect:
                    ObjectBrushHelper.SelectObjectsWithBrush(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, sectorConstraint, _strokeProcessedObjects, deselect: true);
                    break;

                case EditorToolType.Pencil:
                case EditorToolType.Line:
                    var pencilPlaced = ObjectBrushHelper.PlaceObjectWithPencil(editor, selectedRoom, centerWorldX, centerWorldZ, editor.ChosenItems, ref _pencilItemIndex, sectorConstraint,
                        skipOverlapCheck: editor.Tool.Tool == EditorToolType.Line);

                    _strokeUndoList.AddRange(pencilPlaced.Select(o => new AddRemoveObjectUndoInstance(editor.UndoManager, o, true)));
                    _strokePlacedObjects.AddRange(pencilPlaced);
                    break;
            }
        }

        public static Vector3 BeginBrushStroke(Editor editor, Room selectedRoom, VectorInt2 sectorPos, Vector3? cursorWorldPos = null)
        {
            _pencilItemIndex = 0;
            _strokeUndoList.Clear();
            _strokePlacedObjects.Clear();
            _strokeProcessedObjects.Clear();

            float centerWorldX = cursorWorldPos.HasValue ? cursorWorldPos.Value.X - selectedRoom.WorldPos.X : (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            float centerWorldZ = cursorWorldPos.HasValue ? cursorWorldPos.Value.Z - selectedRoom.WorldPos.Z : (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;

            ExecuteBrushAction(editor, selectedRoom, centerWorldX, centerWorldZ);

            return cursorWorldPos ?? new Vector3(centerWorldX + selectedRoom.WorldPos.X, 0, centerWorldZ + selectedRoom.WorldPos.Z);
        }

        public static Vector3? ContinueBrushStroke(Editor editor, Room pickedRoom, Room selectedRoom,
            VectorInt2 sectorPos, Vector3? lastWorldPosition, float quantizationDistance, Vector3? cursorWorldPos = null)
        {
            var room = pickedRoom == selectedRoom ? selectedRoom : pickedRoom;

            float worldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit + room.WorldPos.X;
            float worldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit + room.WorldPos.Z;
            var trackingPos = cursorWorldPos ?? new Vector3(worldX, 0, worldZ);

            // Quantize movement - only paint if moved far enough.
            bool shouldPaint = !lastWorldPosition.HasValue;
            if (!shouldPaint)
            {
                float dx = trackingPos.X - lastWorldPosition.Value.X;
                float dz = trackingPos.Z - lastWorldPosition.Value.Z;
                shouldPaint = (dx * dx + dz * dz) >= (quantizationDistance * quantizationDistance);
            }

            if (!shouldPaint || editor.ChosenItems.Count == 0)
            {
                UpdateBrushCursor(editor, room, sectorPos,
                    cursorWorldPos.HasValue ? cursorWorldPos.Value.X : (float?)null,
                    cursorWorldPos.HasValue ? cursorWorldPos.Value.Z : (float?)null);
                return null;
            }

            float centerWorldX, centerWorldZ;
            if (cursorWorldPos.HasValue)
            {
                centerWorldX = cursorWorldPos.Value.X - selectedRoom.WorldPos.X;
                centerWorldZ = cursorWorldPos.Value.Z - selectedRoom.WorldPos.Z;
            }
            else
            {
                centerWorldX = (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
                centerWorldZ = (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;
                if (room != selectedRoom)
                {
                    centerWorldX += (room.WorldPos.X - selectedRoom.WorldPos.X);
                    centerWorldZ += (room.WorldPos.Z - selectedRoom.WorldPos.Z);
                }
            }

            ExecuteBrushAction(editor, selectedRoom, centerWorldX, centerWorldZ);

            UpdateBrushCursor(editor, room, sectorPos,
                cursorWorldPos.HasValue ? cursorWorldPos.Value.X : (float?)null,
                cursorWorldPos.HasValue ? cursorWorldPos.Value.Z : (float?)null);

            return trackingPos;
        }

        public static void ExecuteFill(Editor editor, Room selectedRoom)
        {
            if (editor.ChosenItems.Count == 0)
                return;

            var sectorConstraint = editor.SelectedSectors.Valid && !editor.SelectedSectors.Empty
                ? (RectangleInt2?)editor.SelectedSectors.Area
                : (RectangleInt2?)new RectangleInt2(1, 1, selectedRoom.NumXSectors - 2, selectedRoom.NumZSectors - 2);

            var placed = ObjectBrushHelper.FillAreaWithObjects(editor, selectedRoom, editor.ChosenItems, sectorConstraint.Value);
            if (placed.Count == 0)
                return;

            EditorActions.AllocateScriptIdsForObjects(placed);
            editor.UndoManager.Push(placed.Select(o => (UndoRedoInstance)new AddRemoveObjectUndoInstance(editor.UndoManager, o, true)).ToList());
        }

        public static void UpdateBrushCursor(Editor editor, Room room, VectorInt2 sectorPos,
            float? worldX = null, float? worldZ = null)
        {
            float localX = worldX.HasValue ? (worldX.Value - room.WorldPos.X) : (sectorPos.X + 0.5f) * Level.SectorSizeUnit;
            float localZ = worldZ.HasValue ? (worldZ.Value - room.WorldPos.Z) : (sectorPos.Y + 0.5f) * Level.SectorSizeUnit;
            float cursorX = worldX ?? (localX + room.WorldPos.X);
            float cursorZ = worldZ ?? (localZ + room.WorldPos.Z);
            float? floorH = ObjectBrushHelper.GetFloorHeightAtPoint(room, localX, localZ);
            float yPos = floorH.HasValue ? (floorH.Value + room.WorldPos.Y) : room.WorldPos.Y;

            editor.UpdateObjectBrushCursor(new Vector3(cursorX, yPos, cursorZ), room);
        }

        public static void EndBrushStroke(Editor editor)
        {
            EditorActions.AllocateScriptIdsForObjects(_strokePlacedObjects);

            if (_strokeUndoList.Count > 0)
                editor.UndoManager.Push(_strokeUndoList.ToList());

            _strokeUndoList.Clear();
            _strokePlacedObjects.Clear();
        }
    }
}
