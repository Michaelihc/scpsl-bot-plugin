using Interactables.Interobjects.DoorUtils;
using Interactables.Interobjects;
using MapGeneration;
using PluginAPI.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal partial class NavigationMesh
    {
        public static Dictionary<string, NavigationMesh> MeshesByRoomForm { get; } = new();

        public static Dictionary<Vertex, string> FormsByVertices { get; } = new();
        public static Dictionary<Area, string> FormsByAreas { get; } = new();


        public static Dictionary<string, List<GameObject>> RoomsByForm { get; } = new();

        public static Dictionary<GameObject, NavigationMesh> LocalMeshesByRoom { get; } = new();

        public static Dictionary<TransformArea, List<TransformArea>> ForeignConnectedAreas = new();

        public static NavigationMesh CreateRoom(string form)
        {
            var mesh = new NavigationMesh();

            MeshesByRoomForm.Add(form, mesh);

            if (!RoomsByForm.TryGetValue(form, out var rooms))
            {
                rooms = new();
                RoomsByForm.Add(form, rooms);
            }

            foreach (var room in rooms)
            {
                LocalMeshesByRoom[room] = mesh;
            }

            mesh.VertexCreated += vertex => FormsByVertices.Add(vertex, form);
            mesh.VertexDeleted += vertex => FormsByVertices.Remove(vertex);

            mesh.AreaCreated += area => FormsByAreas.Add(area, form);
            mesh.AreaDeleted += area => FormsByAreas.Remove(area);

            mesh.AreaCreated += area => AddForeignConnectedAreasList(area, form);
            mesh.AreaDeleted += area => RemoveForeignConnectedAreasList(area, form);

            return mesh;
        }

        private static void AddForeignConnectedAreasList(Area area, string form)
        {
            foreach (var room in RoomsByForm[form])
            {
                ForeignConnectedAreas.Add(new(area, room.transform), new());
            }
        }

        private static void RemoveForeignConnectedAreasList(Area area, string form)
        {
            foreach (var room in RoomsByForm[form])
            {
                ForeignConnectedAreas.Remove(new(area, room.transform));
            }
        }

        #region Mesh querying

        public static TransformArea? GetAreaWithin(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                return null;
            }

            var roomArea = GetRoomAreaWithin(position, room);
            if (roomArea != null)
            {
                return new (roomArea, room.transform);
            }
            return null;
        }

        public static Area GetRoomAreaWithin(Vector3 position, RoomIdentifier room = null)
        {
            room ??= RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room || !LocalMeshesByRoom.TryGetValue(room.gameObject, out var roomMesh))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            return roomMesh.Areas.FirstOrDefault(a => IsLocalPointWithinArea(a, localPosition));
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

        public static (Vertex From, Vertex To)? GetNearestEdge(Vector3 position, RoomIdentifier room = null) => GetNearestEdge(position, out _, room);
        public static (Vertex From, Vertex To)? GetNearestEdge(Vector3 position, out Vector3 closestPoint, RoomIdentifier room = null)
        {
            closestPoint = Vector3.zero;

            room ??= RoomIdUtils.RoomAtPositionRaycasts(position);

            if (!room || !LocalMeshesByRoom.TryGetValue(room.gameObject, out var roomMesh))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            var hit = roomMesh.Areas.SelectMany(a => a.Edges)
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

        public static void FindShortestPath(TransformArea startingArea, TransformArea endArea, List<TransformArea> results)
        {
            var areasWithPriorityToEvaluate = new Dictionary<TransformArea, float>();
            var cameFromAreas = new Dictionary<TransformArea, TransformArea>();
            var costsTill = new Dictionary<TransformArea, float>();

            var cost = 0f;
            var heuristic = Vector3.Magnitude(endArea.CenterPosition - startingArea.CenterPosition);

            areasWithPriorityToEvaluate.Add(startingArea, cost + heuristic);
            costsTill.Add(startingArea, cost);

            var area = startingArea;

            do
            {
                area = areasWithPriorityToEvaluate.Aggregate((a, c) => c.Value < a.Value ? c : a).Key;

                //var areaIdx = AreasByRoom[area.Room].IndexOf(area);
                //Log.Debug($"Evaluating connections for area #{areaIdx} with priority value {areasWithPriorityToEvaluate[area]} {area.FormArea.RoomForm}");

                areasWithPriorityToEvaluate.Remove(area);

                if (area == endArea)
                {
                    break;
                }

                cost = costsTill[area];

                //Log.Debug($"Area evaluating connections #{areaIdx} cost so far {cost}");

                foreach (var connectedArea in area.ConnectedAreas.Concat(ForeignConnectedAreas[area]))
                {
                    var connectedCost = cost + Vector3.Magnitude(connectedArea.CenterPosition - area.CenterPosition);

                    //var connAreaIdx = AreasByRoom[connectedArea.Room].IndexOf(connectedArea);
                    //Log.Debug($"Connected area #{connAreaIdx} cost so far {connectedCost} {connectedArea.FormArea.RoomForm}");

                    if (!costsTill.ContainsKey(connectedArea) || connectedCost < costsTill[connectedArea])
                    {
                        costsTill[connectedArea] = connectedCost;
                        heuristic = Vector3.Magnitude(endArea.CenterPosition - connectedArea.CenterPosition);
                        areasWithPriorityToEvaluate[connectedArea] = connectedCost + heuristic;
                        cameFromAreas[connectedArea] = area;

                        //Log.Debug($"Connected area #{connAreaIdx} adding for evaluation with heuristic {heuristic} {connectedArea.FormArea.RoomForm}");
                    }
                }
            }
            while (areasWithPriorityToEvaluate.Any());

            results.Clear();
            var shortestPath = results;

            if (cameFromAreas.ContainsKey(endArea))
            {
                area = endArea;
                do
                {
                    shortestPath.Add(area);
                }
                while (cameFromAreas.TryGetValue(area, out area));

                shortestPath.Reverse();
            }
        }

        public static Vertex GetVertexNearby(Vector3 position, float radius = 1f)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room || !LocalMeshesByRoom.TryGetValue(room.gameObject, out var roomMesh))
            {
                return null;
            }

            var radiusSqr = Mathf.Pow(radius, 2);

            var verticesWithinRadius = roomMesh.Vertices
                .Select(vertex => (vertex, distSqr: Vector3.SqrMagnitude(vertex.Position - position)))
                .Where(t => t.distSqr < radiusSqr);

            if (!verticesWithinRadius.Any())
            {
                return null;
            }

            return verticesWithinRadius
                .Aggregate((a, c) => c.distSqr < a.distSqr ? c : a)
                .vertex;
        }

        public static string GetForm(Vertex vertex)
        {
            return FormsByVertices[vertex];
        }

        public static string GetForm(Area area)
        {
            return FormsByAreas[area];
        }

        public static NavigationMesh GetMesh(string form)
        {
            MeshesByRoomForm.TryGetValue(form, out var mesh);
            return mesh;
        }

        public static Vector3Int GetDirectionToRoom(RoomIdentifier room, RoomIdentifier otherRoom)
        {
            return Vector3Int.RoundToInt(room.transform.InverseTransformDirection(otherRoom.OccupiedCoords[0] - room.OccupiedCoords[0]));
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
                Log.Error($"Version in navmesh file is older than supported.");
                return;
            }

            ///
            /// Rooms reading
            ///

            var roomFormCount = binaryReader.ReadInt32();

            for (var i = 0; i < roomFormCount; i++)
            {
                var roomForm = binaryReader.ReadString();

                var formMesh = CreateRoom(roomForm);

                formMesh.ReadMesh(binaryReader);
            }
        }

        public static void WriteMeshes(BinaryWriter binaryWriter)
        {
            byte version = 4;
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
            foreach (var room in Facility.Rooms.Select(apiRoom => apiRoom.Identifier.gameObject))
            {
                var roomForm = GetForm(room);
                if (!RoomsByForm.TryGetValue(roomForm, out var rooms))
                {
                    rooms = new();
                    RoomsByForm.Add(roomForm, rooms);
                }
                rooms.Add(room);

                LocalMeshesByRoom.Add(room.gameObject, new());
            }

            var allConnectors = RoomConnector.AllConnectors.Select(c => (c.gameObject, c.Rooms));
            var allDoors = DoorVariant.AllDoors.Where(d => d.Rooms.Length >= 2).Select(c => (c.gameObject, c.Rooms));
        }

        public static void ResetMeshes()
        {
            RoomsByForm.Clear();
            MeshesByRoomForm.Clear();
            LocalMeshesByRoom.Clear();
        }

        #endregion

        private static bool IsLocalPointWithinArea(Area area, Vector3 pointLocalPosition)
        {
            var areaLocalEdges = area.Edges;

            var isAnyVertexWithinVerticalRange = false;
            foreach (var e in areaLocalEdges)
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
