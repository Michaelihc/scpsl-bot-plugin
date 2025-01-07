using MapGeneration;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class NavigationMesh
    {
        public static Dictionary<string, NavigationMesh> MeshesByRoomForm { get; } = new();
        public static Dictionary<string, NavigationMesh> MeshesByConnectorForm { get; } = new();


        public List<FormVertex> FormVertices { get; } = new();
        public event Action<FormVertex> FormVertexCreated;
        public event Action<FormVertex> FormVertexDeleted;

        public static Dictionary<GameObject, Dictionary<FormVertex, Vertex>> VerticesByRoomOrConnector { get; } = new();


        public List<FormArea> FormAreas { get; } = new();
        public event Action<FormArea> FormAreaCreated;
        public event Action<FormArea> FormAreaDeleted;

        public static Dictionary<GameObject, List<Area>> AreasByRoomOrConnector { get; } = new();


        public static Dictionary<GameObject, List<Transform>> ConnectorsByRoom { get; } = new();


        public NavigationMesh()
        {
            FormVertexCreated += AddVerticesToRoomsOrConnectors;
            
            FormVertexDeleted += RemoveVertexFromAreas;
            FormVertexDeleted += RemoveVerticesFromRoomsOrConnectors;

            FormAreaCreated += AddAreas;
            FormAreaDeleted += RemoveAreas;
        }

        #region Mesh querying

        public static Area GetAreaWithin(Vector3 position)
        {
            var roomArea = GetRoomAreaWithin(position);
            if (roomArea != null)
            {
                return roomArea;
            }

            return null;    // TODO: GetConnectorAreaWithin
        }

        public static Area GetRoomAreaWithin(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);

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

            var hit = roomAreas.SelectMany(a => a.FormArea.Edges)
                .Select(edge => (edge, planeDist: GetPointDistToEdgePlane(edge, localPosition, out var planeClosest), planeClosest))
                .Where(t => t.planeDist <= 0f)

                .Select(t => (t.edge, closest: ClampWithinEdgePoints(t.edge, t.planeClosest)))
                .Select(t => (t.edge, dist: -Vector3.SqrMagnitude(localPosition - t.closest), t.closest))

                .Where(t => IsEdgeCenterWithinVertically(t.edge, localPosition))
                .OrderByDescending(t => t.dist)
                .Select(t => new (FormEdge, float, Vector3)?(t))
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
            var localPosition = room.transform.InverseTransformPoint(position);

            var verticesWithinRadius = roomVertexs.Values.Select(vertex => (vertex, distSqr: Vector3.SqrMagnitude(vertex.FormVertex.LocalPosition - localPosition)))
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

        private static Vector3 ClampWithinEdgePoints(FormEdge edge, Vector3 planeClosestPoint)
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
        #region Mesh manipulation

        public FormVertex AddVertex(Vector3 localPosition, string form)
        {
            var newFormVertex = CreateFormVertex(localPosition, form);
            FormVertexCreated?.Invoke(newFormVertex);

            return newFormVertex;
        }

        private FormVertex CreateFormVertex(Vector3 localPosition, string form)
        {
            var newFormVertex = new FormVertex(localPosition, form);
            FormVertices.Add(newFormVertex);

            return newFormVertex;
        }

        private static void AddVerticesToRoomsOrConnectors(FormVertex formVertex)
        {
            foreach (var verticesPair in VerticesByRoomOrConnector.Where(p => StartsWithForm(p.Key, formVertex.Form)))
            {
                verticesPair.Value.Add(formVertex, new Vertex(formVertex, verticesPair.Key.transform));
            }
        }

        public bool DeleteVertex(FormVertex formVertex)
        {
            var form = formVertex.Form;

            if (!DeleteFormVertex(formVertex))
            {
                Log.Warning($"No vertices at {form} to remove vertex from.");
                return false;
            }

            FormVertexDeleted?.Invoke(formVertex);

            return true;
        }

        private bool DeleteFormVertex(FormVertex formVertex)
        {
            FormVertices.Remove(formVertex);

            return true;
        }

        private void RemoveVertexFromAreas(FormVertex formVertex)
        {
            foreach (var area in FormAreas)
            {
                area.RemoveVertex(formVertex);
            }
        }

        private static void RemoveVerticesFromRoomsOrConnectors(FormVertex formVertex)
        {
            foreach (var (_, vertices) in VerticesByRoomOrConnector.Where(p => StartsWithForm(p.Key, formVertex.Form)))
            {
                vertices.Remove(formVertex);
            }
        }

        public bool MoveVertex(FormVertex formVertex, Vector3 newLocalPosition)
        {
            formVertex.LocalPosition = newLocalPosition;

            return true;
        }

        public FormArea MakeArea(IEnumerable<FormVertex> formVertices, string form)
        {
            var newFormArea = new FormArea(formVertices, form);
            FormAreas.Add(newFormArea);

            FormAreaCreated?.Invoke(newFormArea);

            return newFormArea;
        }


        private void AddAreas(FormArea formArea)
        {
            foreach (var (roomOrConnector, areas) in AreasByRoomOrConnector.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var newArea = new Area(formArea, roomOrConnector.transform, formArea => areas.Find(a => a.FormArea == formArea));
                areas.Add(newArea);
            }
        }

        public bool RemoveArea(FormArea formArea)
        {
            var formAreas = FormAreas;
            if (!formAreas.Remove(formArea))
            {
                Log.Warning($"No areas at {formArea.Form} to remove area from.");
                return false;
            }

            RemoveConnectionsToArea(formArea);

            FormAreaDeleted?.Invoke(formArea);

            return true;
        }

        private void RemoveConnectionsToArea(FormArea formArea)
        {
            foreach (var otherFormArea in FormAreas)
            {
                otherFormArea.RemoveConnection(formArea);
            }
        }

        private void RemoveAreas(FormArea formArea)
        {
            foreach (var (_, areas) in AreasByRoomOrConnector.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var areaToRemove = areas.Find(n => n.FormArea == formArea);
                areas.Remove(areaToRemove);
            }
        }

        public void CreateAreaConnection(FormArea fromFormArea, FormArea toFormArea)
        {
            fromFormArea.AddConnection(toFormArea);
        }

        public void DeleteAreaConnection(FormArea fromFormArea, FormArea toFormArea)
        {
            fromFormArea.RemoveConnection(toFormArea);;
        }

        public void AddVertexToArea(FormArea area, FormVertex vertex, FormVertex beforeVertex)
        {
            area.AddVertex(vertex, beforeVertex);
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
                    formMesh = new NavigationMesh();
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
                        formMesh = new NavigationMesh();
                        MeshesByConnectorForm.Add(connectorForm, formMesh);
                    }

                    formMesh.ReadMesh(binaryReader, connectorForm);
                }
            }
        }

        public void ReadMesh(BinaryReader binaryReader, string form)
        {
            ///
            /// Vertices reading
            /// 

            var vertexCount = binaryReader.ReadInt32();

            for (var j = 0; j < vertexCount; j++)
            {
                var vertexLocalPosition = new Vector3()
                {
                    x = binaryReader.ReadSingle(),
                    y = binaryReader.ReadSingle(),
                    z = binaryReader.ReadSingle()
                };

                var newRoomFormVertex = AddVertex(vertexLocalPosition, form);
            }

            ///
            /// Areas reading
            ///

            var areasCount = binaryReader.ReadInt32();

            var areasVertices = new int[areasCount][];
            var areasConnections = new int[areasCount][];

            for (var j = 0; j < areasCount; j++)
            {
                var newRoomFormArea = MakeArea(Enumerable.Empty<FormVertex>(), form);

                var areaVerticesCount = binaryReader.ReadInt32();
                var areaVertices = new int[areaVerticesCount];
                for (var k = 0; k < areaVerticesCount; k++)
                {
                    areaVertices[k] = binaryReader.ReadInt32();
                }
                areasVertices[j] = areaVertices;

                var connectedAreasCount = binaryReader.ReadInt32();
                var connectedAreas = new int[connectedAreasCount];
                for (var k = 0; k < connectedAreasCount; k++)
                {
                    connectedAreas[k] = binaryReader.ReadInt32();
                }
                areasConnections[j] = connectedAreas;
            }

            foreach (var (area, vertices) in areasVertices.Select((vertices, areaIndex) => (FormAreas[areaIndex], vertices)))
            {
                foreach (var areaVertex in vertices.Select(vertexIdx => FormVertices[vertexIdx]))
                {
                    area.AddVertex(areaVertex);
                }
            }

            foreach (var (area, conns) in areasConnections.Select((conns, areaIndex) => (FormAreas[areaIndex], conns)))
            {
                foreach (var connectingArea in conns.Select(connectedIndex => FormAreas[connectedIndex]))
                {
                    area.AddConnection(connectingArea);
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

        public void WriteMesh(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(FormVertices.Count);
            foreach (var vertex in FormVertices)
            {
                binaryWriter.Write(vertex.LocalPosition.x);
                binaryWriter.Write(vertex.LocalPosition.y);
                binaryWriter.Write(vertex.LocalPosition.z);
            }

            binaryWriter.Write(FormAreas.Count);
            foreach (var area in FormAreas)
            {
                binaryWriter.Write(area.Vertices.Count);
                foreach (var vertexIdx in area.Vertices.Select(areaVertex => FormVertices.IndexOf(areaVertex)))
                {
                    binaryWriter.Write(vertexIdx);
                }

                binaryWriter.Write(area.ConnectedFormAreas.Count);
                foreach (var connIdx in area.ConnectedFormAreas.Select(connArea => FormAreas.IndexOf(connArea)))
                {
                    binaryWriter.Write(connIdx);
                }
            }
        }

        #endregion
        #region Mesh initiation/resetting

        public void Init()
        { }

        public static void InitVertices()
        {
            foreach (var room in Facility.Rooms)
            {
                var vertices = new Dictionary<FormVertex, Vertex>();
                VerticesByRoomOrConnector.Add(room.GameObject, vertices);
            }
        }

        public static void ResetVertices()
        {
            VerticesByRoomOrConnector.Clear();
            foreach (var (_, mesh) in MeshesByRoomForm)
            {
                mesh.FormVertices.Clear();
            }
        }

        public static void InitAreas()
        {
            foreach (var room in Facility.Rooms.Select(apiRoom => apiRoom.Identifier))
            {
                var areas = new List<Area>();
                AreasByRoomOrConnector.Add(room.gameObject, areas);
            }
        }

        public static void ResetAreas()
        {
            AreasByRoomOrConnector.Clear();
            foreach (var (_, mesh) in MeshesByRoomForm)
            {
                mesh.FormAreas.Clear();
            }
        }

        #endregion

        private static bool IsLocalPointWithinArea(Area area, Vector3 pointLocalPosition)
        {
            var areaRoomFormEdges = area.FormArea.Edges;

            var isAnyVertexWithinVerticalRange = false;
            foreach (var e in areaRoomFormEdges)
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

        private static float GetPointDistToEdgePlane(FormEdge roomEdge, Vector3 localPoint) => GetPointDistToEdgePlane(roomEdge, localPoint, out _);
        private static float GetPointDistToEdgePlane(FormEdge roomEdge, Vector3 localPoint, out Vector3 closestLocalPoint)
        {
            var dirTo2 = roomEdge.To.LocalPosition - roomEdge.From.LocalPosition;
            var dirToPoint = localPoint - roomEdge.From.LocalPosition;

            var edgeNormal = Vector3.Cross(dirTo2.normalized, Vector3.down);

            var dist = Vector3.Dot(edgeNormal, dirToPoint);

            closestLocalPoint = localPoint - edgeNormal * dist;

            return dist;
        }

        private static bool IsEdgeCenterWithinVertically(FormEdge edge, Vector3 localPoint)
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
            var gameObjectName = roomConnector?.gameObject.name;
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
