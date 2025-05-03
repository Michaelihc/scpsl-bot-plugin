using MapGeneration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using LabApi.Features.Wrappers;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial class NavigationMesh
    {
        public static Dictionary<string, NavigationMesh> MeshesByRoomForm { get; } = new();
        public static Dictionary<string, List<GameObject>> RoomsByForm { get; } = new();

        public static Dictionary<GameObject, NavigationMesh> LocalMeshesByRoom { get; } = new();

        public static Dictionary<TransformCell, List<TransformCell>> ForeignConnectedCells = new();
        public static Dictionary<TransformCell, Dictionary<TransformCell, TransformEdge>> ForeignConnectedCellEdges = new();

        public static NavigationMesh CreateMesh(string form)
        {
            var mesh = new NavigationMesh();

            MeshesByRoomForm.Add(form, mesh);

            if (RoomsByForm.TryGetValue(form, out var rooms))
            {
                foreach (var room in rooms)
                {
                    LocalMeshesByRoom[room] = mesh;
                }

                mesh.CellCreated += cell => AddForeignConnectedCellsList(cell, form);
                mesh.CellDeleted += cell => RemoveForeignConnectedCellsList(cell, form);
            }

            return mesh;
        }

        private static void AddForeignConnectedCellsList(Cell cell, string form)
        {
            foreach (var room in RoomsByForm[form])
            {
                ForeignConnectedCells.Add(new(cell, room.transform), new());
                ForeignConnectedCellEdges.Add(new(cell, room.transform), new());
            }
        }

        private static void RemoveForeignConnectedCellsList(Cell cell, string form)
        {
            foreach (var room in RoomsByForm[form])
            {
                ForeignConnectedCells.Remove(new(cell, room.transform));
                ForeignConnectedCellEdges.Remove(new(cell, room.transform));
            }
        }

        #region Mesh querying

        public static TransformCell? GetCellWithin(Vector3 position)
        {
            if (!RoomUtils.TryGetRoom(position, out var room))
            {
                return null;
            }

            var roomCell = GetRoomCellWithin(position, room);
            if (roomCell != null)
            {
                return new (roomCell, room.transform);
            }
            return null;
        }

        public static Cell GetRoomCellWithin(Vector3 position, RoomIdentifier room = null)
        {
            if (!room)
            {
                RoomUtils.TryGetRoom(position, out room);
            }
            if (!room || !LocalMeshesByRoom.TryGetValue(room.gameObject, out var roomMesh))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            return roomMesh.Cells.FirstOrDefault(a => IsLocalPointWithinCell(a, localPosition));
        }

        public static bool IsAtPositiveEdgeSide(Vector3 position, TransformEdge transformEdge)
        {
            var localPosition = transformEdge.Transform.InverseTransformPoint(position);

            return IsAtPositiveEdgeSide(localPosition, new Edge(transformEdge.From.Local, transformEdge.To.Local));
        }

        public static bool IsAtPositiveEdgeSide(Vector3 position, Edge edge)
        {
            return GetPointDistToEdgePlane(edge, position) > 0f;
        }

        public static TransformEdge? GetNearestEdge(Vector3 position, RoomIdentifier room = null)
        {
            var nearestEdgeNullable = GetNearestEdge(position, out _, room);
            if (!nearestEdgeNullable.HasValue)
            {
                return null;
            }

            var (from, to) = nearestEdgeNullable.Value;

            return new TransformEdge((from, to, room.transform));
        }

        public static (Vertex From, Vertex To)? GetNearestEdge(Vector3 position, out Vector3 closestPoint, RoomIdentifier room = null)
        {
            closestPoint = Vector3.zero;

            if (!room)
            {
                RoomUtils.TryGetRoom(position, out room);
            }

            if (!room || !LocalMeshesByRoom.TryGetValue(room.gameObject, out var roomMesh))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            var hit = roomMesh.Cells.SelectMany(a => a.Edges)
                .Select(roomEdge => (roomEdge, planeDist: GetPointDistToEdgePlane(roomEdge, localPosition, out var planeClosest), planeClosest))
                .Where(t => t.planeDist <= 0f)

                .Select(t => (t.roomEdge, closest: ClampWithinEdgePoints(t.roomEdge, t.planeClosest)))
                .Select(t => (t.roomEdge, dist: -Vector3.SqrMagnitude(localPosition - t.closest), t.closest))

                .Where(t => IsEdgeCenterWithinVertically(t.roomEdge, localPosition))
                .OrderByDescending(t => t.dist)
                .Select(t => new (Edge, float, Vector3)?(t))
                .DefaultIfEmpty(null)
                .First();

            if (!hit.HasValue)
            {
                return null;
            }

            var (roomFormEdge, dist, closestLocalPoint) = hit.Value;

            closestPoint = room.transform.TransformPoint(closestLocalPoint);

            return (roomFormEdge.From, roomFormEdge.To);
        }

        public static void FindShortestPath(TransformCell startingCell, TransformCell endCell, List<TransformCell> results)
        {
            var cellsWithPriorityToEvaluate = new Dictionary<TransformCell, float>();
            var cameFromCells = new Dictionary<TransformCell, TransformCell>();
            var costsTill = new Dictionary<TransformCell, float>();

            var cost = 0f;
            var heuristic = Vector3.Magnitude(endCell.CenterPosition - startingCell.CenterPosition);

            cellsWithPriorityToEvaluate.Add(startingCell, cost + heuristic);
            costsTill.Add(startingCell, cost);

            var cell = startingCell;

            do
            {
                cell = cellsWithPriorityToEvaluate.Aggregate((a, c) => c.Value < a.Value ? c : a).Key;

                //var cellIdx = CellsByRoom[cell.Room].IndexOf(cell);
                //Log.Debug($"Evaluating connections for cell #{cellIdx} with priority value {cellsWithPriorityToEvaluate[cell]} {cell.FormCell.RoomForm}");

                cellsWithPriorityToEvaluate.Remove(cell);

                if (cell == endCell)
                {
                    break;
                }

                cost = costsTill[cell];

                //Log.Debug($"Cell evaluating connections #{cellIdx} cost so far {cost}");

                foreach (var connectedCell in cell.AdjacentCells.Concat(ForeignConnectedCells[cell]))
                {
                    var connectedCost = cost + Vector3.Magnitude(connectedCell.CenterPosition - cell.CenterPosition);

                    //var connCellIdx = CellsByRoom[connectedCell.Room].IndexOf(connectedCell);
                    //Log.Debug($"Connected cell #{connCellIdx} cost so far {connectedCost} {connectedCell.FormCell.RoomForm}");

                    if (!costsTill.ContainsKey(connectedCell) || connectedCost < costsTill[connectedCell])
                    {
                        costsTill[connectedCell] = connectedCost;
                        heuristic = Vector3.Magnitude(endCell.CenterPosition - connectedCell.CenterPosition);
                        cellsWithPriorityToEvaluate[connectedCell] = connectedCost + heuristic;
                        cameFromCells[connectedCell] = cell;

                        //Log.Debug($"Connected cell #{connCellIdx} adding for evaluation with heuristic {heuristic} {connectedCell.FormCell.RoomForm}");
                    }
                }
            }
            while (cellsWithPriorityToEvaluate.Any());

            results.Clear();
            var shortestPath = results;

            if (cameFromCells.ContainsKey(endCell))
            {
                cell = endCell;
                do
                {
                    shortestPath.Add(cell);
                }
                while (cameFromCells.TryGetValue(cell, out cell));

                shortestPath.Reverse();
            }
        }

        public static TransformVertex? GetVertexNearby(Vector3 position, float radius = 1f)
        {
            if (!RoomUtils.TryGetRoom(position, out var room) || !LocalMeshesByRoom.TryGetValue(room.gameObject, out var roomMesh))
            {
                return null;
            }

            var radiusSqr = Mathf.Pow(radius, 2);
            var localPosition = room.transform.InverseTransformPoint(position);

            var verticesWithinRadius = roomMesh.Vertices
                .Select(localVertex => (localVertex, distSqr: Vector3.SqrMagnitude(localVertex.Position - localPosition)))
                .Where(t => t.distSqr < radiusSqr);

            if (!verticesWithinRadius.Any())
            {
                return null;
            }

            return new (verticesWithinRadius
                .Aggregate((a, c) => c.distSqr < a.distSqr ? c : a)
                .localVertex, room.transform);
        }

        public static NavigationMesh GetMesh(string form)
        {
            MeshesByRoomForm.TryGetValue(form, out var mesh);
            return mesh;
        }

        public static Vector3Int GetDirectionToRoom(RoomIdentifier room, RoomIdentifier otherRoom)
        {
            return Vector3Int.RoundToInt(room.transform.InverseTransformDirection(otherRoom.MainCoords - room.MainCoords));
        }

        public static Vector3Int GetConnectorOrientation(RoomIdentifier room, Vector3 connectorTransformForward)
        {
            return Vector3Int.RoundToInt(room.transform.InverseTransformDirection(connectorTransformForward));
        }

        private static Vector3 ClampWithinEdgePoints(Edge edge, Vector3 planeClosestPoint)
        {
            var dir1To2 = edge.To.Position - edge.From.Position;
            var dir1ToPoint = planeClosestPoint - edge.From.Position;

            var dir2To1 = edge.From.Position - edge.To.Position;
            var dir2ToPoint = planeClosestPoint - edge.To.Position;

            if (Vector3.Dot(dir1ToPoint, dir1To2) < 0f)
            {
                return edge.From.Position;
            }
            if (Vector3.Dot(dir2ToPoint, dir2To1) < 0f)
            {
                return edge.To.Position;
            }

            return planeClosestPoint;
        }

        #endregion
        #region Mesh reading/writing

        public static void ReadMeshes(BinaryReader binaryReader)
        {
            var version = binaryReader.ReadByte();
            if (version < 3)
            {
                Debug.LogError($"Version in navmesh file is older than supported.");
                return;
            }

            ///
            /// Rooms reading
            ///

            var roomFormCount = binaryReader.ReadInt32();

            for (var i = 0; i < roomFormCount; i++)
            {
                var roomForm = binaryReader.ReadString();

                if (!MeshesByRoomForm.TryGetValue(roomForm, out var formMesh))
                {
                    formMesh = CreateMesh(roomForm);
                }

                formMesh.ReadMesh(binaryReader, version);
            }
        }

        public static void WriteMeshes(BinaryWriter binaryWriter)
        {
            byte version = 5;
            binaryWriter.Write(version);

            ///
            /// Rooms writing
            ///

            binaryWriter.Write(MeshesByRoomForm.Count);

            foreach (var (roomForm, mesh) in MeshesByRoomForm)
            {
                binaryWriter.Write(roomForm);

                mesh.WriteMesh(binaryWriter);
            }
        }

        #endregion
        #region Mesh initiation/resetting

        public static void InitMeshes()
        {
            foreach (var room in Room.List.Select(apiRoom => apiRoom.Base.gameObject))
            {
                var roomForm = GetForm(room);
                if (!RoomsByForm.TryGetValue(roomForm, out var rooms))
                {
                    rooms = new();
                    RoomsByForm.Add(roomForm, rooms);
                }
                rooms.Add(room);

            }

            foreach (var roomForm in RoomsByForm.Keys)
            {
                if (!MeshesByRoomForm.ContainsKey(roomForm))
                {
                    CreateMesh(roomForm);
                }
            }
        }

        public static void ResetMeshes()
        {
            RoomsByForm.Clear();
            MeshesByRoomForm.Clear();
            LocalMeshesByRoom.Clear();
            ForeignConnectedCells.Clear();
        }

        #endregion

        private static bool IsLocalPointWithinCell(Cell cell, Vector3 pointLocalPosition)
        {
            var cellLocalEdges = cell.Edges;

            var isAnyVertexWithinVerticalRange = false;
            foreach (var e in cellLocalEdges)
            {
                if (GetPointDistToEdgePlane(e, pointLocalPosition) <= 0f)
                {
                    return false;
                }

                if (!isAnyVertexWithinVerticalRange)
                {
                    isAnyVertexWithinVerticalRange =
                        e.From.Position.y > pointLocalPosition.y - 1f
                        && e.From.Position.y < pointLocalPosition.y + 1f;
                }
            }

            return isAnyVertexWithinVerticalRange;
        }

        private static float GetPointDistToEdgePlane(Edge edge, Vector3 point) => GetPointDistToEdgePlane(edge, point, out _);
        private static float GetPointDistToEdgePlane(Edge edge, Vector3 point, out Vector3 closestPoint)
        {
            var dirTo2 = edge.To.Position - edge.From.Position;
            var dirToPoint = point - edge.From.Position;

            var edgeNormal = Vector3.Cross(dirTo2.normalized, Vector3.down);

            var dist = Vector3.Dot(edgeNormal, dirToPoint);

            closestPoint = point - edgeNormal * dist;

            return dist;
        }

        private static bool IsEdgeCenterWithinVertically(Edge edge, Vector3 localPoint)
        {
            var localPointYLowest = localPoint.y - 1f;
            var localPointYHighest = localPoint.y + 1f;
            var edgeCenter = Vector3.Lerp(edge.From.Position, edge.To.Position, 0.5f);

            return edgeCenter.y > localPointYLowest
                && edgeCenter.y < localPointYHighest;
        }

        public static string GetForm(GameObject gameObject)
        {
            var gameObjectName = gameObject?.name;
            return (gameObjectName?.EndsWith("(Clone)") ?? false) ? gameObjectName.Remove(gameObjectName.LastIndexOf("(Clone)")) : gameObjectName;
        }

        public static bool StartsWithForm(GameObject gameObject, string comparingForm)
        {
            var gameObjectName = gameObject.name;
            return gameObjectName.Equals(gameObjectName.EndsWith("(Clone)") ? $"{comparingForm}(Clone)" : comparingForm);
        }
    }
}
