using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

using TombLib;
using TombLib.LevelData;

namespace TombEditor.Controls.ObjectBrush
{
    public static class ObjectBrushHelper
    {
        private static readonly Random _rng = new Random();

        #region Floor Height and Geometry Queries

        public static float? GetFloorHeightAtPoint(Room room, float localX, float localZ)
        {
            float sectorX = localX / Level.SectorSizeUnit;
            float sectorZ = localZ / Level.SectorSizeUnit;

            int ix = (int)sectorX;
            int iz = (int)sectorZ;

            var sector = room.GetSectorTry(new VectorInt2(ix, iz));
            if (sector == null || sector.IsAnyWall)
                return null;

            float fx = sectorX - ix;
            float fz = sectorZ - iz;

            float h00 = sector.Floor.XnZn;
            float h10 = sector.Floor.XpZn;
            float h01 = sector.Floor.XnZp;
            float h11 = sector.Floor.XpZp;

            float height = h00 * (1 - fx) * (1 - fz) +
                           h10 * fx * (1 - fz) +
                           h01 * (1 - fx) * fz +
                           h11 * fx * fz;

            return height;
        }

        public static bool IsValidFloorPosition(Room room, int sectorX, int sectorZ)
        {
            if (sectorX < 1 || sectorZ < 1 || sectorX >= room.NumXSectors - 1 || sectorZ >= room.NumZSectors - 1)
                return false;

            var sector = room.GetSectorTry(new VectorInt2(sectorX, sectorZ));
            return sector != null && !sector.IsAnyWall;
        }

        public static bool IsWithinConstraint(int sectorX, int sectorZ, RectangleInt2? constraint)
        {
            if (!constraint.HasValue)
                return true;

            var c = constraint.Value;
            return sectorX >= c.X0 && sectorX <= c.X1 && sectorZ >= c.Y0 && sectorZ <= c.Y1;
        }

        public static (Room Room, float LocalX, float LocalZ) ResolveFloorRoom(Room room, float localX, float localZ)
        {
            int sectorX = (int)(localX / Level.SectorSizeUnit);
            int sectorZ = (int)(localZ / Level.SectorSizeUnit);

            var sector = room.GetSectorTry(new VectorInt2(sectorX, sectorZ));

            if (sector?.FloorPortal != null)
            {
                // Only probe through solid collisional portals.
                var connInfo = room.GetFloorRoomConnectionInfo(new VectorInt2(sectorX, sectorZ), true);
                if (!sector.FloorPortal.IsTraversable || connInfo.TraversableType != Room.RoomConnectionType.FullPortal)
                    return (room, localX, localZ);

                var bottomRoom = sector.FloorPortal.AdjoiningRoom;

                if (bottomRoom != null)
                {
                    float bottomLocalX = localX + (room.WorldPos.X - bottomRoom.WorldPos.X);
                    float bottomLocalZ = localZ + (room.WorldPos.Z - bottomRoom.WorldPos.Z);

                    int bsx = (int)(bottomLocalX / Level.SectorSizeUnit);
                    int bsz = (int)(bottomLocalZ / Level.SectorSizeUnit);

                    if (IsValidFloorPosition(bottomRoom, bsx, bsz))
                        return (bottomRoom, bottomLocalX, bottomLocalZ);
                }
            }

            return (room, localX, localZ);
        }

        // Check if a position sits on a vertical non-solid non-collisional portal.
        public static bool IsOnNonSolidPortal(Room room, float localX, float localZ)
        {
            int sectorX = (int)(localX / Level.SectorSizeUnit);
            int sectorZ = (int)(localZ / Level.SectorSizeUnit);

            var sector = room.GetSectorTry(new VectorInt2(sectorX, sectorZ));
            if (sector?.FloorPortal == null)
                return false;

            var connInfo = room.GetFloorRoomConnectionInfo(new VectorInt2(sectorX, sectorZ), true);
            return connInfo.TraversableType != Room.RoomConnectionType.NoPortal &&
                   connInfo.TraversableType != Room.RoomConnectionType.FullPortal;
        }

        #endregion

        #region Bounding Box and Placement Safety

        public static BoundingBox? GetItemBoundingBox(Level level, ItemType itemType)
        {
            if (itemType.IsStatic)
            {
                var wadStatic = level.Settings?.WadTryGetStatic(itemType.StaticId);

                if (wadStatic?.Mesh?.BoundingBox.Size.Length() > 0.0f)
                    return wadStatic.CollisionBox;
            }
            else
            {
                var wadMoveable = level.Settings?.WadTryGetMoveable(itemType.MoveableId);

                if (wadMoveable?.Animations?.Count > 0 && wadMoveable.Animations[0].KeyFrames.Count > 0)
                    return wadMoveable.Animations[0].KeyFrames[0].BoundingBox;
            }

            return null;
        }

        /// <summary>
        /// Checks that all 4 bounding box bottom corners at a given position and rotation
        /// are above valid floor geometry. Returns the corrected Y position or null if any corner
        /// is over invalid geometry. The object is placed such that no bounding box bottom corner
        /// hangs in the air — it rests on the lowest floor point under any corner, with other corners
        /// potentially submerging into higher terrain.
        /// </summary>
        /// 
        public static float? GetSafePlacementHeight(Room room, Vector3 localPos, float rotationYDeg, float scale, BoundingBox bbox)
        {
            var scaledMin = new Vector2(bbox.Minimum.X * scale, bbox.Minimum.Z * scale);
            var scaledMax = new Vector2(bbox.Maximum.X * scale, bbox.Maximum.Z * scale);

            var corners = new Vector2[]
            {
                new Vector2(scaledMin.X, scaledMin.Y),
                new Vector2(scaledMax.X, scaledMin.Y),
                new Vector2(scaledMax.X, scaledMax.Y),
                new Vector2(scaledMin.X, scaledMax.Y)
            };

            float rotRad = rotationYDeg * (float)(Math.PI / 180.0);
            float cosR = (float)Math.Cos(rotRad);
            float sinR = (float)Math.Sin(rotRad);

            float minFloorHeight = float.MaxValue;

            foreach (var corner in corners)
            {
                float rx = corner.X * cosR + corner.Y * sinR;
                float rz = -corner.X * sinR + corner.Y * cosR;

                float? floorH = GetFloorHeightAtPoint(room, localPos.X + rx, localPos.Z + rz);
                if (!floorH.HasValue)
                    return null;

                minFloorHeight = Math.Min(minFloorHeight, floorH.Value);
            }

            // Place the object so the bottom of the bounding box rests on the lowest floor point.
            // In TR coordinate space, Y increases downward: bbox.Maximum.Y is the bottom.
            // objY + bbox.Maximum.Y * scale = minFloorHeight => objY = minFloorHeight - bbox.Maximum.Y * scale
            return minFloorHeight - bbox.Maximum.Y * scale;
        }

        #endregion

        #region Brush Area Queries

        public static List<Room> GetBrushRooms(Room currentRoom, bool includeAdjacent)
        {
            var rooms = new List<Room> { currentRoom };
            if (includeAdjacent)
            {
                foreach (var portal in currentRoom.Portals)
                {
                    if (portal.AdjoiningRoom != null && !rooms.Contains(portal.AdjoiningRoom))
                        rooms.Add(portal.AdjoiningRoom);
                }
            }
            return rooms;
        }

        public static bool IsInBrushArea(float x, float z, float centerX, float centerZ, float radius, ObjectBrushShape shape)
        {
            float dx = x - centerX;
            float dz = z - centerZ;

            if (shape == ObjectBrushShape.Circle)
                return (dx * dx + dz * dz) <= (radius * radius);
            else
                return Math.Abs(dx) <= radius && Math.Abs(dz) <= radius;
        }

        private static int CountMatchingObjectsInArea(List<Vector2> positions, float centerWorldX, float centerWorldZ, float radius, ObjectBrushShape shape)
        {
            int count = 0;

            foreach (var pos in positions)
            {
                if (IsInBrushArea(pos.X, pos.Y, centerWorldX, centerWorldZ, radius, shape))
                    count++;
            }

            return count;
        }

        public static int GetTargetObjectCount(float radius, float density, ObjectBrushShape shape)
        {
            float radiusSectors = radius / Level.SectorSizeUnit;
            float area = shape == ObjectBrushShape.Circle ? (float)(Math.PI * radiusSectors * radiusSectors) : (2 * radiusSectors) * (2 * radiusSectors);
            return Math.Max(1, (int)Math.Round(area * density));
        }

        #endregion

        #region Object Creation and Candidate Position Generation

        public static PositionBasedObjectInstance CreateObjectInstance(ItemType itemType)
        {
            if (itemType.IsStatic)
                return new StaticInstance { WadObjectId = itemType.StaticId };
            else
                return new MoveableInstance { WadObjectId = itemType.MoveableId };
        }

        public static List<Vector2> GenerateCandidatePositions(float centerWorldX, float centerWorldZ, float radius, float density, ObjectBrushShape shape)
        {
            var result = new List<Vector2>();

            if (density <= 0)
                return result;

            float cellSizeWorld = Level.SectorSizeUnit / (float)Math.Sqrt(density);

            int gridMinX = (int)Math.Floor((centerWorldX - radius) / cellSizeWorld);
            int gridMaxX = (int)Math.Ceiling((centerWorldX + radius) / cellSizeWorld);
            int gridMinZ = (int)Math.Floor((centerWorldZ - radius) / cellSizeWorld);
            int gridMaxZ = (int)Math.Ceiling((centerWorldZ + radius) / cellSizeWorld);

            for (int gx = gridMinX; gx <= gridMaxX; gx++)
            {
                for (int gz = gridMinZ; gz <= gridMaxZ; gz++)
                {
                    float px = (gx + (float)_rng.NextDouble()) * cellSizeWorld;
                    float pz = (gz + (float)_rng.NextDouble()) * cellSizeWorld;

                    if (IsInBrushArea(px, pz, centerWorldX, centerWorldZ, radius, shape))
                        result.Add(new Vector2(px, pz));
                }
            }

            return result;
        }

        #endregion

        #region Distance and Overlap Checks

        public static bool IsTooCloseToExisting(Room room, float worldX, float worldZ, float minDistWorld, IReadOnlyList<ItemType> chosenItems)
        {
            float minDistSq = minDistWorld * minDistWorld;

            foreach (var obj in room.Objects)
            {
                if (obj is ItemInstance item && chosenItems.Contains(item.ItemType))
                {
                    float dx = obj.Position.X - worldX;
                    float dz = obj.Position.Z - worldZ;

                    if (dx * dx + dz * dz < minDistSq)
                        return true;
                }
            }

            return false;
        }

        private static bool IsTooCloseInList(List<Vector2> positions, float x, float z, float minDistSq)
        {
            foreach (var pos in positions)
            {
                float dx = pos.X - x;
                float dz = pos.Y - z;
                if (dx * dx + dz * dz < minDistSq)
                    return true;
            }

            return false;
        }

        #endregion

        #region Main Brush Operations

        private struct PlacementContext
        {
            public Dictionary<ItemType, BoundingBox?> BboxCache;
            public Dictionary<Room, List<Vector2>> PosCache;
        }

        // Main object placement operation.

        public static List<PositionBasedObjectInstance> PlaceObjectsWithBrush(Editor editor, Room room, float x, float z,
            IReadOnlyList<ItemType> chosenItems, RectangleInt2? sectorConstraint = null)
        {
            var shape = editor.Configuration.ObjectBrush_Shape;
            float radius = editor.Configuration.ObjectBrush_Radius;
            float density = editor.Configuration.ObjectBrush_Density;

            var placedObjects = new List<PositionBasedObjectInstance>();

            // Min distance for spacing (derived from density, in world units).
            float cellSizeWorld = Level.SectorSizeUnit / (float)Math.Sqrt(Math.Max(0.01f, density));
            float minDistWorld = cellSizeWorld * 0.5f;
            float minDistSq = minDistWorld * minDistWorld;

            var rooms = GetBrushRooms(room, editor.Configuration.ObjectBrush_PlaceInAdjacentRooms);

            // Build per-call context: bbox cache (shared across rooms) + position cache (per room).
            // Pre-populate PosCache with existing matching objects so distance checks don't scan room.Objects.
            var ctx = new PlacementContext
            {
                BboxCache = new Dictionary<ItemType, BoundingBox?>(),
                PosCache  = new Dictionary<Room, List<Vector2>>()
            };

            foreach (var r in rooms)
            {
                var posList = new List<Vector2>();

                foreach (var obj in r.Objects)
                {
                    if (obj is ItemInstance item && chosenItems.Contains(item.ItemType))
                        posList.Add(new Vector2(item.Position.X, item.Position.Z));
                }

                ctx.PosCache[r] = posList;
            }

            foreach (var targetRoom in rooms)
            {
                var offset = room.WorldPos - targetRoom.WorldPos;
                float localCenterX = x + offset.X;
                float localCenterZ = z + offset.Z;

                int existingCount = CountMatchingObjectsInArea(ctx.PosCache[targetRoom], localCenterX, localCenterZ, radius, shape);
                int targetCount = GetTargetObjectCount(radius, density, shape);

                if (existingCount >= targetCount)
                    continue;

                int toPlace = targetCount - existingCount;
                var candidates = GenerateCandidatePositions(localCenterX, localCenterZ, radius, density, shape);
                ShuffleList(candidates);

                int placed = 0;
                foreach (var candidate in candidates)
                {
                    if (placed >= toPlace)
                        break;

                    // Pick random item type per placement
                    var chosenItem = chosenItems[_rng.Next(chosenItems.Count)];

                    if (!TryPlaceObject(editor, editor.Level, targetRoom, candidate, chosenItem,
                        minDistSq, ref ctx, sectorConstraint, out var instance))
                        continue;

                    placedObjects.Add(instance);
                    placed++;
                }
            }

            editor.ObjectChange(placedObjects, ObjectChangeType.Add);
            return placedObjects;
        }

        // Individual object placement attempt.

        private static bool TryPlaceObject(Editor editor, Level level, Room targetRoom,
            Vector2 candidate, ItemType chosenItem, float minDistSq, ref PlacementContext ctx, RectangleInt2? sectorConstraint,
            out PositionBasedObjectInstance instance)
        {
            instance = null;
            float worldX = candidate.X;
            float worldZ = candidate.Y;

            int sectorX = (int)(worldX / Level.SectorSizeUnit);
            int sectorZ = (int)(worldZ / Level.SectorSizeUnit);

            if (!IsValidFloorPosition(targetRoom, sectorX, sectorZ))
                return false;

            if (!IsWithinConstraint(sectorX, sectorZ, sectorConstraint))
                return false;

            var config = editor.Configuration;

            var placementRoom = targetRoom;
            float placeX = worldX;
            float placeZ = worldZ;

            // Probe through floor portals to find the actual room and position.
            if (config.ObjectBrush_PlaceInAdjacentRooms)
            {
                var resolved = ResolveFloorRoom(targetRoom, worldX, worldZ);
                placementRoom = resolved.Room;
                placeX = resolved.LocalX;
                placeZ = resolved.LocalZ;
            }
            else
            {
                // If not placing in adjacent rooms, skip positions on non-solid portals.
                if (IsOnNonSolidPortal(targetRoom, worldX, worldZ))
                    return false;
            }

            // Use cached position list instead of scanning room.Objects on every candidate
            if (!ctx.PosCache.TryGetValue(placementRoom, out var posList))
            {
                posList = new List<Vector2>();
                ctx.PosCache[placementRoom] = posList;
            }

            if (IsTooCloseInList(posList, placeX, placeZ, minDistSq))
                return false;

            bool randomRot = config.ObjectBrush_RandomizeRotation;
            bool randomScale = config.ObjectBrush_RandomizeScale;

            float rotY = randomRot ? (float)(_rng.NextDouble() * 360.0) : config.ObjectBrush_Rotation;
            if (config.ObjectBrush_Perpendicular)
                rotY = (rotY + 90.0f) % 360.0f;
            float scale = 1.0f;

            if (randomScale && chosenItem.IsStatic)
                scale = config.ObjectBrush_ScaleMin + (float)_rng.NextDouble() * (config.ObjectBrush_ScaleMax - config.ObjectBrush_ScaleMin);

            // Determine Y position in the placement room (use cached bounds to avoid repeated wad look-ups).
            if (!ctx.BboxCache.TryGetValue(chosenItem, out var bbox))
            {
                bbox = GetItemBoundingBox(level, chosenItem);
                ctx.BboxCache[chosenItem] = bbox;
            }

            float yPos;

            if (config.ObjectBrush_FitToGround && bbox.HasValue)
            {
                float? safeHeight = GetSafePlacementHeight(placementRoom, new Vector3(placeX, 0, placeZ), rotY, scale, bbox.Value);
                if (!safeHeight.HasValue) return false;
                yPos = safeHeight.Value;
            }
            else
            {
                float? floorH = GetFloorHeightAtPoint(placementRoom, placeX, placeZ);
                if (!floorH.HasValue) return false;
                yPos = floorH.Value;
            }

            instance = CreateObjectInstance(chosenItem);
            instance.Position = new Vector3(placeX, yPos, placeZ);

            if (instance is IRotateableY rotatable)
                rotatable.RotationY = rotY;

            if (instance is IScaleable scaleable && randomScale)
                scaleable.Scale = scale;

            placementRoom.AddObject(level, instance);
            EditorActions.RebuildLightsForObject(instance);

            // Record newly placed position in the cache for subsequent distance checks within this stroke.
            ctx.PosCache[placementRoom].Add(new Vector2(placeX, placeZ));

            return true;
        }

        // Main object erase operation.

        public static List<(PositionBasedObjectInstance Obj, Room Room)> EraseObjectsWithBrush(Editor editor, Room room,
            float centerWorldX, float centerWorldZ, IReadOnlyList<ItemType> chosenItems, RectangleInt2? sectorConstraint = null)
        {
            var shape     = editor.Configuration.ObjectBrush_Shape;
            float radius  = editor.Configuration.ObjectBrush_Radius;
            float density = editor.Configuration.ObjectBrush_Density;
            bool adjacent = editor.Configuration.ObjectBrush_PlaceInAdjacentRooms;

            int targetCount = GetTargetObjectCount(radius, density * 0.3f, shape);

            var removedObjects = new List<(PositionBasedObjectInstance, Room)>();
            var rooms = GetBrushRooms(room, adjacent);
            int totalRemoved = 0;

            foreach (var targetRoom in rooms)
            {
                if (totalRemoved >= targetCount)
                    break;

                var offset = room.WorldPos - targetRoom.WorldPos;
                float localCenterX = centerWorldX + offset.X;
                float localCenterZ = centerWorldZ + offset.Z;

                var toRemove = CollectObjectsInBrushArea(targetRoom, localCenterX, localCenterZ,
                    radius, shape, chosenItems, sectorConstraint);

                // Shuffle so removal is spatially random rather than biased by iteration order
                ShuffleList(toRemove);

                foreach (var obj in toRemove)
                {
                    if (totalRemoved >= targetCount)
                        break;

                    if (editor.SelectedObject == obj)
                        editor.SelectedObject = null;

                    // Capture room reference before removal (RemoveObject sets obj.Room to null).
                    targetRoom.RemoveObject(editor.Level, obj);
                    removedObjects.Add((obj, targetRoom));
                    totalRemoved++;
                }
            }

            foreach (var (obj, objRoom) in removedObjects)
                editor.ObjectChange(obj, ObjectChangeType.Remove, objRoom);

            return removedObjects;
        }

        private static List<PositionBasedObjectInstance> CollectObjectsInBrushArea(Room room, float centerWorldX, float centerWorldZ, float radius,
            ObjectBrushShape shape, IReadOnlyList<ItemType> chosenItems, RectangleInt2? sectorConstraint)
        {
            var result = new List<PositionBasedObjectInstance>();

            foreach (var obj in room.Objects)
            {
                if (!(obj is ItemInstance item) || !chosenItems.Contains(item.ItemType))
                    continue;

                if (sectorConstraint.HasValue)
                {
                    int sx = (int)(obj.Position.X / Level.SectorSizeUnit);
                    int sz = (int)(obj.Position.Z / Level.SectorSizeUnit);
                    if (!IsWithinConstraint(sx, sz, sectorConstraint))
                        continue;
                }

                if (IsInBrushArea(obj.Position.X, obj.Position.Z, centerWorldX, centerWorldZ, radius, shape))
                    result.Add(obj);
            }
            return result;
        }

        public static void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        #endregion

        #region Selection Tool

        // Selects or deselects objects within brush radius.
        // Ctrl deselects, Shift limits to current ChosenItems only.

        public static void SelectObjectsWithBrush(Editor editor, Room room, float centerWorldX, float centerWorldZ,
            IReadOnlyList<ItemType> chosenItems, RectangleInt2? sectorConstraint,
            HashSet<ObjectInstance> processedObjects = null)
        {
            var shape = editor.Configuration.ObjectBrush_Shape;
            float radius = editor.Configuration.ObjectBrush_Radius;
            bool adjacent = editor.Configuration.ObjectBrush_PlaceInAdjacentRooms;
            bool ctrlHeld = Control.ModifierKeys.HasFlag(Keys.Control);
            bool filterByChosen = Control.ModifierKeys.HasFlag(Keys.Shift);

            var rooms = GetBrushRooms(room, adjacent);
            var objectsInArea = new List<PositionBasedObjectInstance>();

            foreach (var targetRoom in rooms)
            {
                var offset = room.WorldPos - targetRoom.WorldPos;
                float localCenterX = centerWorldX + offset.X;
                float localCenterZ = centerWorldZ + offset.Z;

                foreach (var obj in targetRoom.Objects)
                {
                    if (!(obj is ItemInstance item))
                        continue;

                    // Skip objects already processed during this stroke.
                    if (processedObjects != null && processedObjects.Contains(obj))
                        continue;

                    if (filterByChosen && !chosenItems.Contains(item.ItemType))
                        continue;

                    if (sectorConstraint.HasValue)
                    {
                        int sx = (int)(obj.Position.X / Level.SectorSizeUnit);
                        int sz = (int)(obj.Position.Z / Level.SectorSizeUnit);
                        if (!IsWithinConstraint(sx, sz, sectorConstraint))
                            continue;
                    }

                    if (IsInBrushArea(obj.Position.X, obj.Position.Z, localCenterX, localCenterZ, radius, shape))
                        objectsInArea.Add(obj);
                }
            }

            if (objectsInArea.Count == 0)
                return;

            // Mark all newly found objects as processed for this stroke.
            if (processedObjects != null)
            {
                foreach (var obj in objectsInArea)
                    processedObjects.Add(obj);
            }

            if (ctrlHeld)
            {
                // Deselect all objects in the brush area from current selection.
                if (editor.SelectedObject is ObjectGroup group)
                {
                    var remaining = group.Where(o => !objectsInArea.Contains(o)).ToList();
                    if (remaining.Count == 0)
                        editor.SelectedObject = null;
                    else if (remaining.Count == 1)
                        editor.SelectedObject = remaining[0];
                    else
                        editor.SelectedObject = new ObjectGroup(remaining);
                }
                else if (objectsInArea.Any(o => o == editor.SelectedObject))
                {
                    editor.SelectedObject = null;
                }
            }
            else
            {
                // Only select objects not already in the current selection.
                foreach (var obj in objectsInArea)
                {
                    bool alreadySelected = editor.SelectedObject == obj ||
                        (editor.SelectedObject is ObjectGroup existing && existing.Contains(obj));
                    if (!alreadySelected)
                        EditorActions.MultiSelect(obj);
                }
            }
        }

        #endregion

        #region Pencil Tool

        // Place a single object per step. Cycles through ChosenItems sequentially.
        // ObjectBrush_Radius specifies fixed interval between placed objects.
        // If Ctrl is held, advance only in the rotation direction.

        public static List<PositionBasedObjectInstance> PlaceObjectWithPencil(Editor editor, Room room,
            float centerWorldX, float centerWorldZ, IReadOnlyList<ItemType> chosenItems, ref int itemIndex,
            RectangleInt2? sectorConstraint, bool skipOverlapCheck = false)
        {
            var config = editor.Configuration;
            float radius = config.ObjectBrush_Radius;
            bool adjacent = config.ObjectBrush_PlaceInAdjacentRooms;

            var placedObjects = new List<PositionBasedObjectInstance>();
            var level = editor.Level;

            // Build bbox cache.
            var bboxCache = new Dictionary<ItemType, BoundingBox?>();

            // Select the item to place.
            var chosenItem = chosenItems[itemIndex % chosenItems.Count];

            // Check if placement would overlap with any existing object of any chosen type.
            if (!bboxCache.TryGetValue(chosenItem, out var bbox))
            {
                bbox = GetItemBoundingBox(level, chosenItem);
                bboxCache[chosenItem] = bbox;
            }

            // Compute minimum distance from bounding box for seamless placement.
            // Use X extent if Perpendicular flag adds a 90-degree rotation to placed objects.
            float minDist = radius;
            if (bbox.HasValue)
            {
                float extent = config.ObjectBrush_Perpendicular
                    ? bbox.Value.Maximum.X - bbox.Value.Minimum.X
                    : bbox.Value.Maximum.Z - bbox.Value.Minimum.Z;
                if (extent > 0.0f)
                    minDist = Math.Max(extent, radius);
            }
            float minDistSq = minDist * minDist;

            var rooms = GetBrushRooms(room, adjacent);

            foreach (var targetRoom in rooms)
            {
                // Check for overlap with existing objects, unless the caller opted out (e.g. Ctrl+pencil line mode).
                bool tooClose = false;
                var offset = room.WorldPos - targetRoom.WorldPos;
                float localX = centerWorldX + offset.X;
                float localZ = centerWorldZ + offset.Z;

                if (!skipOverlapCheck)
                    foreach (var obj in targetRoom.Objects)
                    {
                        if (obj is ItemInstance item && chosenItems.Contains(item.ItemType))
                        {
                            float dx = obj.Position.X - localX;
                            float dz = obj.Position.Z - localZ;
                            if (dx * dx + dz * dz < minDistSq)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                    }

                if (tooClose)
                    continue;

                var ctx = new PlacementContext
                {
                    BboxCache = bboxCache,
                    PosCache = new Dictionary<Room, List<Vector2>>()
                };

                var candidate = new Vector2(localX, localZ);
                if (TryPlaceObject(editor, level, targetRoom, candidate, chosenItem, 0, ref ctx, sectorConstraint, out var instance))
                {
                    placedObjects.Add(instance);
                    itemIndex++;
                    break;
                }
            }

            editor.ObjectChange(placedObjects, ObjectChangeType.Add);
            return placedObjects;
        }

        #endregion

        #region Fill Tool

        // Fill a defined area with objects using density setting.

        public static List<PositionBasedObjectInstance> FillAreaWithObjects(Editor editor, Room room,
            IReadOnlyList<ItemType> chosenItems, RectangleInt2 area)
        {
            var config = editor.Configuration;
            float density = config.ObjectBrush_Density;
            bool adjacent = config.ObjectBrush_PlaceInAdjacentRooms;

            var placedObjects = new List<PositionBasedObjectInstance>();
            var level = editor.Level;

            // Calculate center and extent of area in world units.
            float areaMinX = area.X0 * Level.SectorSizeUnit;
            float areaMinZ = area.Y0 * Level.SectorSizeUnit;
            float areaMaxX = (area.X1 + 1) * Level.SectorSizeUnit;
            float areaMaxZ = (area.Y1 + 1) * Level.SectorSizeUnit;

            // Generate candidate positions using density.
            float cellSizeWorld = Level.SectorSizeUnit / (float)Math.Sqrt(Math.Max(0.01f, density));
            float minDistWorld = cellSizeWorld * 0.5f;
            float minDistSq = minDistWorld * minDistWorld;

            var ctx = new PlacementContext
            {
                BboxCache = new Dictionary<ItemType, BoundingBox?>(),
                PosCache = new Dictionary<Room, List<Vector2>>()
            };

            // Pre-populate position cache.
            var rooms = GetBrushRooms(room, adjacent);
            foreach (var r in rooms)
            {
                var posList = new List<Vector2>();
                foreach (var obj in r.Objects)
                {
                    if (obj is ItemInstance item && chosenItems.Contains(item.ItemType))
                        posList.Add(new Vector2(item.Position.X, item.Position.Z));
                }
                ctx.PosCache[r] = posList;
            }

            // Generate candidates within the area.
            int gridMinX = (int)Math.Floor(areaMinX / cellSizeWorld);
            int gridMaxX = (int)Math.Ceiling(areaMaxX / cellSizeWorld);
            int gridMinZ = (int)Math.Floor(areaMinZ / cellSizeWorld);
            int gridMaxZ = (int)Math.Ceiling(areaMaxZ / cellSizeWorld);

            var candidates = new List<Vector2>();
            for (int gx = gridMinX; gx <= gridMaxX; gx++)
            {
                for (int gz = gridMinZ; gz <= gridMaxZ; gz++)
                {
                    float px = (gx + (float)_rng.NextDouble()) * cellSizeWorld;
                    float pz = (gz + (float)_rng.NextDouble()) * cellSizeWorld;

                    if (px >= areaMinX && px < areaMaxX && pz >= areaMinZ && pz < areaMaxZ)
                        candidates.Add(new Vector2(px, pz));
                }
            }

            ShuffleList(candidates);

            foreach (var targetRoom in rooms)
            {
                var offset = room.WorldPos - targetRoom.WorldPos;

                foreach (var candidate in candidates)
                {
                    var localCandidate = new Vector2(candidate.X + offset.X, candidate.Y + offset.Z);
                    var chosenItem = chosenItems[_rng.Next(chosenItems.Count)];

                    if (TryPlaceObject(editor, level, targetRoom, localCandidate, chosenItem, minDistSq, ref ctx,
                        (RectangleInt2?)area, out var instance))
                        placedObjects.Add(instance);
                }
            }

            editor.ObjectChange(placedObjects, ObjectChangeType.Add);
            return placedObjects;
        }

        #endregion
    }
}
