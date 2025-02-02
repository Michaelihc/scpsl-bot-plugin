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
        public static Dictionary<string, NavigationMesh> MeshesByConnectorForm { get; } = new();
        public static Dictionary<(
            string RoomForm,
                Vector3Int Direction,
                    string ConnectorForm,
                        Vector3Int Orientation), NavigationMesh> MeshesByRoomConnectorForm { get; } = new();

        public static Dictionary<LocalVertex, string> FormsByVertices { get; } = new();


        public static Dictionary<GameObject, Dictionary<LocalVertex, Vertex>> VerticesByRoomOrConnector { get; } = new();
        public static Dictionary<
            GameObject, Dictionary<(
                Vector3Int Direction,
                    Transform Connector,
                        Vector3Int Orientation), Dictionary<LocalVertex, Vertex>>> VerticesByRoomDirectionConnectorOrientation { get; } = new();

        public static Dictionary<GameObject, List<Area>> AreasByRoomOrConnector { get; } = new();
        public static Dictionary<
            GameObject, Dictionary<(
                Vector3Int Direction,
                    Transform Connector,
                        Vector3Int Orientation), List<Area>>> AreasByRoomDirectionConnectorOrientation { get; } = new();


        public static NavigationMesh Create(string form)
        {
            var mesh = new NavigationMesh();

            mesh.LocalVertexCreated += vertex => AddVerticesToRoomsOrConnectors(vertex, form);
            mesh.LocalVertexDeleted += vertex => RemoveVerticesFromRoomsOrConnectors(vertex, form);

            mesh.LocalAreaCreated += AddAreas;
            mesh.LocalAreaDeleted += RemoveAreas;

            return mesh;
        }

        #region Mesh querying

        public static Area GetAreaWithin(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                return null;
            }

            var roomArea = GetRoomAreaWithin(position, room);
            if (roomArea != null)
            {
                return roomArea;
            }

            var areasByDirectionConnectorOrientation = AreasByRoomDirectionConnectorOrientation[room.gameObject];

            var roomConnectorAreas = areasByDirectionConnectorOrientation.Values.SelectMany(l => l);
            var roomConnectorArea = roomConnectorAreas.FirstOrDefault(a => IsLocalPointWithinArea(a, a.Transform.InverseTransformPoint(position)));
            if (roomConnectorArea != null)
            {
                return roomConnectorArea;
            }

            var connectorAreas = areasByDirectionConnectorOrientation.Keys.SelectMany(c => AreasByRoomOrConnector[c.Connector.gameObject]);
            return connectorAreas.FirstOrDefault(a => IsLocalPointWithinArea(a, a.Transform.InverseTransformPoint(position)));
        }

        public static Area GetRoomAreaWithin(Vector3 position, RoomIdentifier room = null)
        {
            room ??= RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room || !AreasByRoomOrConnector.TryGetValue(room.gameObject, out var roomAreas))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            return roomAreas.Find(a => IsLocalPointWithinArea(a, localPosition));
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

            if (!room || !AreasByRoomOrConnector.TryGetValue(room.gameObject, out var roomAreas))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            var hit = roomAreas.SelectMany(a => a.LocalArea.Edges)
                .Select(edge => (edge, planeDist: GetPointDistToEdgePlane(edge, localPosition, out var planeClosest), planeClosest))
                .Where(t => t.planeDist <= 0f)

                .Select(t => (t.edge, closest: ClampWithinEdgePoints(t.edge, t.planeClosest)))
                .Select(t => (t.edge, dist: -Vector3.SqrMagnitude(localPosition - t.closest), t.closest))

                .Where(t => IsEdgeCenterWithinVertically(t.edge, localPosition))
                .OrderByDescending(t => t.dist)
                .Select(t => new (LocalEdge, float, Vector3)?(t))
                .DefaultIfEmpty(null)
                .First();

            if (!hit.HasValue)
            {
                return null;
            }

            var (roomFormEdge, dist, closestLocalPoint) = hit.Value;

            Vertex roomEdgeFrom = VerticesByRoomOrConnector[room.gameObject][roomFormEdge.From],
                   roomEdgeTo = VerticesByRoomOrConnector[room.gameObject][roomFormEdge.To];

            closestPoint = room.transform.TransformPoint(closestLocalPoint);

            return (roomEdgeFrom, roomEdgeTo);
        }

        public static void FindShortestPath(Area startingArea, Area endArea, List<Area> results)
        {
            var areasWithPriorityToEvaluate = new Dictionary<Area, float>();
            var cameFromAreas = new Dictionary<Area, Area>();
            var costsTill = new Dictionary<Area, float>();

            var cost = 0f;
            var heuristic = Vector3.Magnitude(endArea.CenterPosition - startingArea.CenterPosition);

            areasWithPriorityToEvaluate.Add(startingArea, cost + heuristic);
            cameFromAreas.Add(startingArea, null);
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

                foreach (var connectedArea in area.ConnectedAreas)
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
                while (area != null)
                {
                    shortestPath.Add(area);
                    cameFromAreas.TryGetValue(area, out area);
                }
                shortestPath.Reverse();
            }
        }

        public static Vertex GetVertexNearby(Vector3 position, float radius = 1f)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room || !VerticesByRoomOrConnector.TryGetValue(room.gameObject, out var roomVertexs))
            {
                return null;
            }

            var radiusSqr = Mathf.Pow(radius, 2);

            var verticesAtDirectionConnectorOrientation = VerticesByRoomDirectionConnectorOrientation[room.gameObject];
            var connectorVertices = verticesAtDirectionConnectorOrientation.Keys.SelectMany(c => VerticesByRoomOrConnector[c.Connector.gameObject].Values);
            var roomConnectorVertices = verticesAtDirectionConnectorOrientation.Values.SelectMany(l => l.Values);

            var verticesWithinRadius = roomVertexs.Values
                .Concat(roomConnectorVertices)
                .Concat(connectorVertices)
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

        public static bool IsPointWithinArea(Area area, Vector3 pointPosition)
        {
            var pointLocalPosition = area.Transform.InverseTransformPoint(pointPosition);

            return IsLocalPointWithinArea(area, pointLocalPosition);
        }

        public static string GetForm(LocalVertex vertex)
        {
            return FormsByVertices[vertex];
        }

        public static NavigationMesh GetMesh(string form)
        {
            if (!MeshesByRoomForm.TryGetValue(form, out var mesh))
            {
                MeshesByConnectorForm.TryGetValue(form, out mesh);
            }
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

        private static Vector3 ClampWithinEdgePoints(LocalEdge edge, Vector3 planeClosestPoint)
        {
            var dir1To2 = edge.To.LocalPosition - edge.From.LocalPosition;
            var dir1ToPoint = planeClosestPoint - edge.From.LocalPosition;

            var dir2To1 = edge.From.LocalPosition - edge.To.LocalPosition;
            var dir2ToPoint = planeClosestPoint - edge.To.LocalPosition;

            if (Vector3.Dot(dir1ToPoint, dir1To2) < 0f)
            {
                return edge.From.LocalPosition;
            }
            if (Vector3.Dot(dir2ToPoint, dir2To1) < 0f)
            {
                return edge.To.LocalPosition;
            }

            return planeClosestPoint;
        }

        #endregion
        #region Mesh manipulation handlers

        private static void AddVerticesToRoomsOrConnectors(LocalVertex localVertex, string form)
        {
            foreach (var verticesPair in VerticesByRoomOrConnector.Where(p => StartsWithForm(p.Key, form)))
            {
                verticesPair.Value.Add(localVertex, new Vertex(localVertex, verticesPair.Key.transform));
            }

            FormsByVertices.Add(localVertex, form);
        }

        private static void RemoveVerticesFromRoomsOrConnectors(LocalVertex localVertex, string form)
        {
            foreach (var (_, vertices) in VerticesByRoomOrConnector.Where(p => StartsWithForm(p.Key, form)))
            {
                vertices.Remove(localVertex);
            }

            FormsByVertices.Remove(localVertex);
        }

        private static void AddAreas(LocalArea localArea)
        {
            foreach (var (roomOrConnector, areas) in AreasByRoomOrConnector.Where(p => StartsWithForm(p.Key, localArea.Form)))
            {
                var newArea = new Area(localArea, roomOrConnector.transform, localArea => areas.Find(a => a.LocalArea == localArea));
                areas.Add(newArea);
            }
        }

        private static void RemoveAreas(LocalArea localArea)
        {
            foreach (var (_, areas) in AreasByRoomOrConnector.Where(p => StartsWithForm(p.Key, localArea.Form)))
            {
                var areaToRemove = areas.Find(n => n.LocalArea == localArea);
                areas.Remove(areaToRemove);
            }
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
                string roomForm = binaryReader.ReadString();

                if (!MeshesByRoomForm.TryGetValue(roomForm, out var formMesh))
                {
                    formMesh = Create(roomForm);
                    MeshesByRoomForm.Add(roomForm, formMesh);
                }

                formMesh.ReadMesh(binaryReader, roomForm);
            }

            if (version == 4)
            {
                ///
                /// Connectors reading
                ///

                var connectorFormCount = binaryReader.ReadInt32();

                for (var i = 0; i < connectorFormCount; i++)
                {
                    string connectorForm = binaryReader.ReadString();

                    if (!MeshesByConnectorForm.TryGetValue(connectorForm, out var formMesh))
                    {
                        formMesh = Create(connectorForm);
                        MeshesByConnectorForm.Add(connectorForm, formMesh);
                    }

                    formMesh.ReadMesh(binaryReader, connectorForm);
                }
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

            ///
            /// Connectors writing
            ///

            binaryWriter.Write(MeshesByConnectorForm.Count);

            foreach (var (connectorForm, mesh) in MeshesByConnectorForm)
            {
                binaryWriter.Write(connectorForm);

                mesh.WriteMesh(binaryWriter);
            }
        }

        #endregion
        #region Mesh initiation/resetting

        public static void InitMeshes()
        {
            foreach (var room in Facility.Rooms.Select(apiRoom => apiRoom.Identifier))
            {
                VerticesByRoomOrConnector.Add(room.gameObject, new());
                AreasByRoomOrConnector.Add(room.gameObject, new());

                VerticesByRoomDirectionConnectorOrientation.Add(room.gameObject, new());
                AreasByRoomDirectionConnectorOrientation.Add(room.gameObject, new());
            }

            foreach (var connector in RoomConnector.AllConnectors)
            {
                if (connector.Rooms.Length != 2)
                {
                    Log.Warning($"Abnormal number {connector.Rooms.Length} of connected rooms for connector {connector}");
                }

                VerticesByRoomOrConnector.Add(connector.gameObject, new());
                AreasByRoomOrConnector.Add(connector.gameObject, new());
                foreach (var connectedRoom in connector.Rooms)
                {
                    var otherRoom = connector.Rooms.First(r => r != connectedRoom);
                    var direction = GetDirectionToRoom(connectedRoom, otherRoom);
                    var orientation = GetConnectorOrientation(connectedRoom, connector.transform.forward);

                    VerticesByRoomDirectionConnectorOrientation[connectedRoom.gameObject].Add((direction, connector.transform, orientation), new());
                    AreasByRoomDirectionConnectorOrientation[connectedRoom.gameObject].Add((direction, connector.transform, orientation), new());
                }
            }

            foreach (var door in DoorVariant.AllDoors.Where(d => d.Rooms.Length >= 2))
            {
                if (door.Rooms.Length != 2)
                {
                    Log.Warning($"Abnormal number {door.Rooms.Length} of connected rooms for door {door}");
                }

                VerticesByRoomOrConnector.Add(door.gameObject, new());
                AreasByRoomOrConnector.Add(door.gameObject, new());
                foreach (var connectedRoom in door.Rooms)
                {
                    var otherRoom = door.Rooms.First(r => r != connectedRoom);
                    var direction = GetDirectionToRoom(connectedRoom, otherRoom);
                    var orientation = GetConnectorOrientation(connectedRoom, door.transform.forward);

                    VerticesByRoomDirectionConnectorOrientation[connectedRoom.gameObject].Add((direction, door.transform, orientation), new());
                    AreasByRoomDirectionConnectorOrientation[connectedRoom.gameObject].Add((direction, door.transform, orientation), new());
                }
            }

            //foreach (var (room, (dir, connectorTransform, orientation)) in VerticesByRoomDirectionConnectorOrientation.SelectMany(p => p.Value.Keys.Select(k => (p.Key, k))))
            //{
            //    Debug.Log($"{GetForm(room),-25}{dir,-12}{GetForm(connectorTransform.gameObject),-40}{orientation,-12}");
            //}
        }

        public static void ResetMeshes()
        {
            VerticesByRoomOrConnector.Clear();
            AreasByRoomOrConnector.Clear();

            var meshes = MeshesByRoomForm.Values
                .Concat(MeshesByConnectorForm.Values)
                .Concat(MeshesByRoomConnectorForm.Values);
            foreach (var mesh in meshes)
            {
                mesh.LocalVertices.Clear();
                mesh.LocalAreas.Clear();
            }

            VerticesByRoomDirectionConnectorOrientation.Clear();
            AreasByRoomDirectionConnectorOrientation.Clear();
        }

        #endregion

        private static bool IsLocalPointWithinArea(Area area, Vector3 pointLocalPosition)
        {
            var areaLocalEdges = area.LocalArea.Edges;

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
                        e.From.LocalPosition.y > pointLocalPosition.y - 1f
                        && e.From.LocalPosition.y < pointLocalPosition.y + 1f;
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

        private static float GetPointDistToEdgePlane(LocalEdge roomEdge, Vector3 localPoint) => GetPointDistToEdgePlane(roomEdge, localPoint, out _);
        private static float GetPointDistToEdgePlane(LocalEdge roomEdge, Vector3 localPoint, out Vector3 closestLocalPoint)
        {
            var dirTo2 = roomEdge.To.LocalPosition - roomEdge.From.LocalPosition;
            var dirToPoint = localPoint - roomEdge.From.LocalPosition;

            var edgeNormal = Vector3.Cross(dirTo2.normalized, Vector3.down);

            var dist = Vector3.Dot(edgeNormal, dirToPoint);

            closestLocalPoint = localPoint - edgeNormal * dist;

            return dist;
        }

        private static bool IsEdgeCenterWithinVertically(LocalEdge edge, Vector3 localPoint)
        {
            var localPointYLowest = localPoint.y - 1f;
            var localPointYHighest = localPoint.y + 1f;
            var edgeCenter = Vector3.Lerp(edge.From.LocalPosition, edge.To.LocalPosition, 0.5f);

            return edgeCenter.y > localPointYLowest
                && edgeCenter.y < localPointYHighest;
        }

        public static string GetRoomForm(string roomObjectName)
        {
            return roomObjectName.EndsWith("(Clone)") ? roomObjectName.Remove(roomObjectName.LastIndexOf("(Clone)")) : roomObjectName;
        }

        public static string GetForm<TBehaviour>(TBehaviour roomConnector)
            where TBehaviour : MonoBehaviour
        {
            return GetForm(roomConnector?.gameObject);
        }

        public static string GetForm(GameObject gameObject)
        {
            var gameObjectName = gameObject?.name;
            return (gameObjectName?.EndsWith("(Clone)") ?? false) ? gameObjectName.Remove(gameObjectName.LastIndexOf("(Clone)")) : gameObjectName;
        }

        public static bool StartsWithForm<TBehaviour>(TBehaviour behaviour, string comparingForm)
            where TBehaviour : MonoBehaviour
        {
            var gameObjectName = behaviour.gameObject.name;
            return gameObjectName.Equals(gameObjectName.EndsWith("(Clone)") ? $"{comparingForm}(Clone)" : comparingForm);
        }

        public static bool StartsWithForm(GameObject gameObject, string comparingForm)
        {
            var gameObjectName = gameObject.name;
            return gameObjectName.Equals(gameObjectName.EndsWith("(Clone)") ? $"{comparingForm}(Clone)" : comparingForm);
        }
    }
}
