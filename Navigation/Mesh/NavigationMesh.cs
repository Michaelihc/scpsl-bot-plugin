using Interactables.Interobjects;
using MapGeneration;
using PluginAPI.Core;
using SCPSLBot.Navigation.Mesh.Room;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class NavigationMesh
    {
        public static NavigationMesh Instance { get; } = new();


        public Dictionary<string, List<RoomFormVertex>> VerticesByRoomForm { get; } = new();
        public Dictionary<RoomFormVertex, Dictionary<Vector3Int, List<string>>> ConnectorFormsByDirectionByRoomFormVertex { get; } = new();
        public event Action<RoomFormVertex> RoomVertexCreated;
        public event Action<RoomFormVertex> RoomVertexDeleted;
        public event Action<RoomFormVertex, Vector3Int, string> RoomVertexAddedToConnector;
        public event Action<RoomFormVertex> RoomVertexRemovedFromConnectors;

        public Dictionary<RoomIdentifier, Dictionary<RoomFormVertex, RoomVertex>> VerticesByRoom { get; } = new();


        public Dictionary<string, List<RoomFormArea>> AreasByRoomForm { get; } = new();
        public Dictionary<string, Dictionary
            <Vector3Int, Dictionary
                <string, List<RoomFormArea>>>> AreasByConnectorByDirectionByRoomForm { get; } = new();

        public event Action<RoomFormArea> RoomAreaCreated;
        public event Action<RoomFormArea> RoomAreaDeleted;
        public event Action<RoomFormArea, Vector3Int, string> RoomConnectorAreaCreated;
        public event Action<RoomFormArea, Vector3Int, string> RoomConnectorAreaDeleted;

        public Dictionary<RoomIdentifier, List<RoomArea>> AreasByRoom { get; } = new();
        public Dictionary<RoomIdentifier, Dictionary<Vector3Int, string>> RoomConnectorsByRoom { get; } = new();


        private NavigationMesh()
        {
            RoomVertexCreated += AddVerticesToRooms;
            
            RoomVertexDeleted += RemoveVertexFromRoomConnectors;
            RoomVertexDeleted += RemoveVertexFromAreas;
            RoomVertexDeleted += RemoveVerticesFromRooms;

            RoomAreaCreated += (RoomFormArea formArea) => AddAreas(formArea, AreasByRoom, (formArea, room, roomConnectors, areaGetter) => new RoomArea(formArea, room, roomConnectors, areaGetter));
            RoomAreaDeleted += (RoomFormArea formArea) => RemoveAreas(formArea, AreasByRoom);

            RoomConnectorAreaCreated += AddConnectorAreas;
            RoomConnectorAreaDeleted += RemoveConnectorAreas;
        }

        #region Mesh querying

        public RoomArea GetAreaWithin(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);

            if (!room || !AreasByRoom.TryGetValue(room, out var roomAreas))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            return roomAreas.Find(a => IsLocalPointWithinArea(a, localPosition));
        }

        public bool IsAtPositiveEdgeSide(Vector3 position, Edge edge)
        {
            return GetPointDistToEdgePlane(edge, position) > 0f;
        }

        public (RoomVertex From, RoomVertex To)? GetNearestEdge(Vector3 position, RoomIdentifier room = null) => GetNearestEdge(position, out _, room);
        public (RoomVertex From, RoomVertex To)? GetNearestEdge(Vector3 position, out Vector3 closestPoint, RoomIdentifier room = null)
        {
            closestPoint = Vector3.zero;

            room ??= RoomIdUtils.RoomAtPositionRaycasts(position);

            if (!room || !AreasByRoom.TryGetValue(room, out var roomAreas))
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
                .Select(t => new (RoomFormEdge, float, Vector3)?(t))
                .DefaultIfEmpty(null)
                .First();

            if (!hit.HasValue)
            {
                return null;
            }

            var (roomFormEdge, dist, closestLocalPoint) = hit.Value;
                
            RoomVertex roomEdgeFrom = VerticesByRoom[room][roomFormEdge.From],
                       roomEdgeTo = VerticesByRoom[room][roomFormEdge.To];

            closestPoint = room.transform.TransformPoint(closestLocalPoint);

            return (roomEdgeFrom, roomEdgeTo);
        }

        public void FindShortestPath(Area startingArea, Area endArea, List<Area> results)
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

        public RoomVertex GetRoomVertexNearby(Vector3 position, float radius = 1f)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);

            if (!room || !VerticesByRoom.TryGetValue(room, out var roomVertexs))
            {
                return null;
            }

            var radiusSqr = Mathf.Pow(radius, 2);
            var localPosition = room.transform.InverseTransformPoint(position);

            var verticesWithinRadius = roomVertexs.Values.Select(vertex => (vertex, distSqr: Vector3.SqrMagnitude(vertex.RoomFormVertex.LocalPosition - localPosition)))
                .Where(t => t.distSqr < radiusSqr);

            if (!verticesWithinRadius.Any())
            {
                return null;
            }
            
            return verticesWithinRadius
                .Aggregate((a, c) => c.distSqr < a.distSqr ? c : a)
                .vertex;
        }

        public bool IsPointWithinArea(RoomArea area, Vector3 pointPosition)
        {
            var room = area.Room;
            var pointLocalPosition = room.transform.InverseTransformPoint(pointPosition);

            return IsLocalPointWithinArea(area, pointLocalPosition);
        }

        private Vector3 ClampWithinEdgePoints(RoomFormEdge edge, Vector3 planeClosestPoint)
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

        public RoomFormVertex AddRoomVertex(Vector3 localPosition, string roomForm)
        {
            var newFormVertex = CreateFormVertex(localPosition, roomForm, VerticesByRoomForm);
            RoomVertexCreated?.Invoke(newFormVertex);

            return newFormVertex;
        }

        public void AddVertexToRoomConnector(RoomFormVertex formVertex, Vector3Int direction, string connectorForm)
        {
            if (!ConnectorFormsByDirectionByRoomFormVertex.TryGetValue(formVertex, out var connectorFormsByDirection))
            {
                connectorFormsByDirection = new();
                ConnectorFormsByDirectionByRoomFormVertex.Add(formVertex, connectorFormsByDirection);
            }
            if (!connectorFormsByDirection.TryGetValue(direction, out var connectorForms))
            {
                connectorForms = new();
                connectorFormsByDirection.Add(direction, connectorForms);
            }

            connectorForms.Add(connectorForm);
            RoomVertexAddedToConnector?.Invoke(formVertex, direction, connectorForm);
        }

        private RoomFormVertex CreateFormVertex(Vector3 localPosition, string form, Dictionary<string, List<RoomFormVertex>> verticesByForm)
        {
            if (!verticesByForm.TryGetValue(form, out var formVertices))
            {
                formVertices = new List<RoomFormVertex>();
                verticesByForm.Add(form, formVertices);
            }

            var newFormVertex = new RoomFormVertex(localPosition, form);
            formVertices.Add(newFormVertex);

            return newFormVertex;
        }

        private void AddVerticesToRooms(RoomFormVertex formVertex)
        {
            foreach (var verticesPair in VerticesByRoom.Where(p => StartsWithForm(p.Key, formVertex.Form)))
            {
                verticesPair.Value.Add(formVertex, new RoomVertex(formVertex, verticesPair.Key));
            }
        }

        public bool DeleteRoomVertex(RoomFormVertex roomFormVertex)
        {
            var roomForm = roomFormVertex.Form;

            if (!DeleteFormVertex(roomFormVertex, VerticesByRoomForm))
            {
                Log.Warning($"No vertices at room {roomForm} to remove vertex from.");
                return false;
            }

            RoomVertexDeleted?.Invoke(roomFormVertex);

            return true;
        }

        private bool DeleteFormVertex(RoomFormVertex formVertex, Dictionary<string, List<RoomFormVertex>> verticesByForm)
        {
            if (!verticesByForm.TryGetValue(formVertex.Form, out var connectorFormVertices))
            {
                return false;
            }

            connectorFormVertices.Remove(formVertex);

            return true;
        }

        private void RemoveVertexFromAreas(RoomFormVertex formVertex)
        {
            foreach (var area in AreasByRoomForm[formVertex.Form])
            {
                area.RemoveVertex(formVertex);
            }
        }

        private void RemoveVertexFromRoomConnectors(RoomFormVertex formVertex)
        {
            ConnectorFormsByDirectionByRoomFormVertex.Remove(formVertex);
            RoomVertexRemovedFromConnectors?.Invoke(formVertex);
        }

        private void RemoveVerticesFromRooms(RoomFormVertex formVertex)
        {
            foreach (var connectorVerticesPair in VerticesByRoom.Where(p => StartsWithForm(p.Key, formVertex.Form)))
            {
                connectorVerticesPair.Value.Remove(formVertex);
            }
        }

        public bool MoveRoomVertex(RoomFormVertex roomFormVertex, Vector3 newLocalPosition)
        {
            roomFormVertex.LocalPosition = newLocalPosition;

            return true;
        }

        public RoomFormArea MakeRoomArea(IEnumerable<RoomFormVertex> roomFormVertices, string roomForm)
        {
            if (!AreasByRoomForm.TryGetValue(roomForm, out var formAreas))
            {
                formAreas = new List<RoomFormArea>();
                AreasByRoomForm.Add(roomForm, formAreas);
            }

            var newFormArea = new RoomFormArea(roomFormVertices, roomForm);
            formAreas.Add(newFormArea);

            RoomAreaCreated?.Invoke(newFormArea);

            return newFormArea;
        }

        public RoomFormArea MakeRoomArea(IEnumerable<RoomFormVertex> roomFormVertices, string roomForm, Vector3Int direction, string connectorForm)
        {
            if (!AreasByConnectorByDirectionByRoomForm.TryGetValue(roomForm, out var areasByConnectorByDirection))
            {
                areasByConnectorByDirection = new();
                AreasByConnectorByDirectionByRoomForm.Add(roomForm, areasByConnectorByDirection);
            }
            if (!areasByConnectorByDirection.TryGetValue(direction, out var areasByConnector))
            {
                areasByConnector = new();
                areasByConnectorByDirection.Add(direction, areasByConnector);
            }
            if (!areasByConnector.TryGetValue(connectorForm, out var formAreas))
            {
                formAreas = new();
                areasByConnector.Add(connectorForm, formAreas);
            }

            var newFormArea = new RoomFormArea(roomFormVertices, roomForm);
            formAreas.Add(newFormArea);

            RoomConnectorAreaCreated?.Invoke(newFormArea, direction, connectorForm);

            return newFormArea;
        }

        private void AddAreas<TArea>(
            RoomFormArea formArea,
            Dictionary<RoomIdentifier, List<TArea>> areasByRoom,
            Func<RoomFormArea, RoomIdentifier, Dictionary<Vector3Int, string>, Func<RoomFormArea, Area>, TArea> areaFactory)
            where TArea : Area
        {
            foreach (var (room, areas) in areasByRoom.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var roomConnectorsByDirection = RoomConnectorsByRoom[room];

                var newArea = areaFactory.Invoke(formArea, room, roomConnectorsByDirection, formArea => areas.Find(a => a.FormArea == formArea));
                areas.Add(newArea);
            }
        }

        private void AddConnectorAreas(RoomFormArea formArea, Vector3Int direction, string connectorForm)
        {
            foreach (var (room, areas) in AreasByRoom.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var roomConnectorsByDirection = RoomConnectorsByRoom[room];
                if (roomConnectorsByDirection[direction] != connectorForm)
                {
                    continue;
                }

                var newArea = new RoomArea(formArea, room, roomConnectorsByDirection, formArea => areas.Find(a => a.FormArea == formArea));
                areas.Add(newArea);
            }
        }

        public bool RemoveRoomArea(RoomFormArea roomFormArea)
        {
            var formAreas = AreasByRoomForm[roomFormArea.Form];
            if (!formAreas.Remove(roomFormArea))
            {
                Log.Warning($"No areas at room {roomFormArea.Form} to remove area from.");
                return false;
            }

            RemoveConnectionsToArea(roomFormArea, (otherFormArea, formArea) => otherFormArea.RemoveConnection(formArea));

            RoomAreaDeleted?.Invoke(roomFormArea);

            return true;
        }

        public bool RemoveRoomArea(RoomFormArea roomFormArea, Vector3Int direction, string connectorForm)
        {
            var formAreas = AreasByConnectorByDirectionByRoomForm[roomFormArea.Form][direction][connectorForm];
            if (!formAreas.Remove(roomFormArea))
            {
                Log.Warning($"No areas at {roomFormArea.Form} {direction} {connectorForm} to remove area from.");
                return false;
            }

            RemoveConnectionsToArea(roomFormArea, (otherFormArea, formArea) => otherFormArea.RemoveConnection(formArea, direction, connectorForm));

            RoomConnectorAreaDeleted?.Invoke(roomFormArea, direction, connectorForm);

            return true;
        }

        private void RemoveConnectionsToArea(RoomFormArea roomFormArea, Action<RoomFormArea, RoomFormArea> removeConnectionAction)
        {
            foreach (var otherFormArea in AreasByRoomForm[roomFormArea.Form]
                .Concat(AreasByConnectorByDirectionByRoomForm[roomFormArea.Form]
                    .SelectMany(p => p.Value)
                    .SelectMany(p => p.Value)))
            {
                removeConnectionAction.Invoke(otherFormArea, roomFormArea);
            }
        }

        private void RemoveAreas<TRoomInstance, TArea>(RoomFormArea formArea, Dictionary<TRoomInstance, List<TArea>> areasByFormInstance)
            where TRoomInstance : MonoBehaviour
            where TArea : Area
        {
            foreach (var (_, areasOfForm) in areasByFormInstance.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var areaToRemove = areasOfForm.Find(n => n.FormArea == formArea);
                areasOfForm.Remove(areaToRemove);
            }
        }

        private void RemoveConnectorAreas(RoomFormArea formArea, Vector3Int direction, string connectorForm)
        {
            foreach (var (room, areasOfRoom) in AreasByRoom.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                if (RoomConnectorsByRoom[room][direction] != connectorForm)
                {
                    continue;
                }

                var areaToRemove = areasOfRoom.Find(n => n.FormArea == formArea);
                areasOfRoom.Remove(areaToRemove);
            }
        }

        public void CreateRoomAreaConnection(RoomFormArea fromFormArea, RoomFormArea toFormArea)
        {
            fromFormArea.AddConnection(toFormArea);
        }

        public void DeleteRoomAreaConnection(RoomFormArea fromFormArea, RoomFormArea toFormArea)
        {
            fromFormArea.RemoveConnection(toFormArea);;
        }

        public void AddRoomVertexToArea(RoomFormArea area, RoomFormVertex vertex, RoomFormVertex beforeVertex)
        {
            area.AddVertex(vertex, beforeVertex);
        }

        #endregion
        #region Mesh reading/writing

        public void ReadMesh(BinaryReader binaryReader)
        {
            var version = binaryReader.ReadByte();
            if (version < 3)
            {
                Log.Error($"Version in navmesh file is newer or older than supported.");
                return;
            }

            ReadRooms(binaryReader, version);
        }

        private void ReadRooms(BinaryReader binaryReader, byte version)
        {
            var roomFormCount = binaryReader.ReadInt32();

            for (var i = 0; i < roomFormCount; i++)
            {
                string roomForm = binaryReader.ReadString();

                ///
                /// Vertices reading
                /// 

                if (!VerticesByRoomForm.TryGetValue(roomForm, out var roomFormVertices))
                {
                    roomFormVertices = new List<RoomFormVertex>();
                    VerticesByRoomForm.Add(roomForm, roomFormVertices);
                }
                else
                {
                    roomFormVertices.Clear();
                }

                var vertexCount = binaryReader.ReadInt32();

                for (var j = 0; j < vertexCount; j++)
                {
                    var vertexLocalPosition = new Vector3()
                    {
                        x = binaryReader.ReadSingle(),
                        y = binaryReader.ReadSingle(),
                        z = binaryReader.ReadSingle()
                    };

                    var newRoomFormVertex = AddRoomVertex(vertexLocalPosition, roomForm);
                }

                if (version > 3)
                {
                    var connectorVertexCount = binaryReader.ReadInt32();
                    for (var j = 0; j < connectorVertexCount; j++)
                    {
                        var idxConnectorVertex = binaryReader.ReadInt32();
                        var formVertex = VerticesByRoomForm[roomForm][idxConnectorVertex];

                        var directionCount = binaryReader.ReadInt32();
                        for (var k = 0; k < directionCount; k++)
                        {
                            var direction = new Vector3Int()
                            {
                                x = binaryReader.ReadInt32(),
                                y = binaryReader.ReadInt32(),
                                z = binaryReader.ReadInt32()
                            };

                            var connectorCount = binaryReader.ReadInt32();
                            for (var l = 0; l < connectorCount; l++)
                            {
                                var connectorForm = binaryReader.ReadString();
                                AddVertexToRoomConnector(formVertex, direction, connectorForm);
                            }
                        }
                    }
                }

                ///
                /// Areas reading
                /// 

                if (!AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
                {
                    roomFormAreas = new List<RoomFormArea>();
                    AreasByRoomForm.Add(roomForm, roomFormAreas);
                }
                else
                {
                    roomFormAreas.Clear();
                }

                var areasCount = binaryReader.ReadInt32();

                var areasVertices = new int[areasCount][];
                var areasConnections = new int[areasCount][];

                for (var j = 0; j < areasCount; j++)
                {
                    var newRoomFormArea = MakeRoomArea(Enumerable.Empty<RoomFormVertex>(), roomForm);

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

                foreach (var (area, vertices) in areasVertices.Select((vertices, areaIndex) => (roomFormAreas[areaIndex], vertices)))
                {
                    foreach (var areaVertex in vertices.Select(vertexIdx => roomFormVertices[vertexIdx]))
                    {
                        area.AddVertex(areaVertex);
                    }
                }

                foreach (var (area, conns) in areasConnections.Select((conns, areaIndex) => (roomFormAreas[areaIndex], conns)))
                {
                    foreach (var connectingArea in conns.Select(connectedIndex => roomFormAreas[connectedIndex]))
                    {
                        area.AddConnection(connectingArea);
                    }
                }
            }
        }

        public void WriteMesh(BinaryWriter binaryWriter)
        {
            byte version = 4;
            binaryWriter.Write(version);
            
            ///
            /// Rooms writing
            ///

            binaryWriter.Write(VerticesByRoomForm.Count);

            foreach (var (roomForm, vertices) in VerticesByRoomForm)
            {
                binaryWriter.Write(roomForm);

                binaryWriter.Write(vertices.Count);
                foreach (var vertex in vertices)
                {
                    binaryWriter.Write(vertex.LocalPosition.x);
                    binaryWriter.Write(vertex.LocalPosition.y);
                    binaryWriter.Write(vertex.LocalPosition.z);
                }

                binaryWriter.Write(ConnectorFormsByDirectionByRoomFormVertex.Count);
                foreach (var (vertex, connectorFormsByDirection) in ConnectorFormsByDirectionByRoomFormVertex)
                {
                    var vertexIdx = vertices.IndexOf(vertex);
                    binaryWriter.Write(vertexIdx);

                    binaryWriter.Write(connectorFormsByDirection.Count);
                    foreach (var (direction, connectorForms) in connectorFormsByDirection)
                    {
                        binaryWriter.Write(direction.x);
                        binaryWriter.Write(direction.y);
                        binaryWriter.Write(direction.z);

                        binaryWriter.Write(connectorForms.Count);
                        foreach (var connectorForm in connectorForms)
                        {
                            binaryWriter.Write(connectorForm);
                        }
                    }
                }

                if (!AreasByRoomForm.TryGetValue(roomForm, out var areas))
                {
                    areas = new();
                }

                binaryWriter.Write(areas.Count);
                foreach (var area in areas)
                {
                    binaryWriter.Write(area.Vertices.Count);
                    foreach (var vertexIdx in area.Vertices.Select(areaVertex => VerticesByRoomForm[roomForm].IndexOf(areaVertex)))
                    {
                        binaryWriter.Write(vertexIdx);
                    }

                    binaryWriter.Write(area.ConnectedFormAreas.Count);
                    foreach (var connIdx in area.ConnectedFormAreas.Select(connArea => AreasByRoomForm[roomForm].IndexOf(connArea)))
                    {
                        binaryWriter.Write(connIdx);
                    }
                }
            }
        }

        #endregion
        #region Mesh initiation/resetting

        public void Init()
        { }

        public void InitRoomVertices()
        {
            foreach (var room in Facility.Rooms)
            {
                var vertices = new Dictionary<RoomFormVertex, RoomVertex>();
                VerticesByRoom.Add(room.Identifier, vertices);
            }
        }

        public void ResetVertices()
        {
            VerticesByRoom.Clear();
        }

        public void InitRoomAreas()
        {
            foreach (var room in Facility.Rooms.Select(apiRoom => apiRoom.Identifier))
            {
                var areas = new List<RoomArea>();
                AreasByRoom.Add(room, areas);

                var roomConnectorsByDirection = room.ConnectedRooms
                    .ToDictionary(
                        connectedRoom => connectedRoom.OccupiedCoords[0] - room.OccupiedCoords[0],
                        connectedRoom => GetForm(RoomConnector.AllConnectors.SingleOrDefault(c => c.Rooms.Contains(connectedRoom) && c.Rooms.Contains(room))) ?? string.Empty
                    );
                RoomConnectorsByRoom.Add(room, roomConnectorsByDirection);
            }
        }

        public void ResetAreas()
        {
            AreasByRoom.Clear();
            RoomConnectorsByRoom.Clear();
        }

        #endregion

        private bool IsLocalPointWithinArea(RoomArea area, Vector3 pointLocalPosition)
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

        private float GetPointDistToEdgePlane(Edge edge, Vector3 point) => GetPointDistToEdgePlane(edge, point, out _);
        private float GetPointDistToEdgePlane(Edge edge, Vector3 point, out Vector3 closestPoint)
        {
            var dirTo2 = edge.To.Position - edge.From.Position;
            var dirToPoint = point - edge.From.Position;

            var edgeNormal = Vector3.Cross(dirTo2.normalized, Vector3.down);

            var dist = Vector3.Dot(edgeNormal, dirToPoint);

            closestPoint = point - edgeNormal * dist;

            return dist;
        }

        private float GetPointDistToEdgePlane(RoomFormEdge roomEdge, Vector3 localPoint) => GetPointDistToEdgePlane(roomEdge, localPoint, out _);
        private float GetPointDistToEdgePlane(RoomFormEdge roomEdge, Vector3 localPoint, out Vector3 closestLocalPoint)
        {
            var dirTo2 = roomEdge.To.LocalPosition - roomEdge.From.LocalPosition;
            var dirToPoint = localPoint - roomEdge.From.LocalPosition;

            var edgeNormal = Vector3.Cross(dirTo2.normalized, Vector3.down);

            var dist = Vector3.Dot(edgeNormal, dirToPoint);

            closestLocalPoint = localPoint - edgeNormal * dist;

            return dist;
        }

        private bool IsEdgeCenterWithinVertically(RoomFormEdge edge, Vector3 localPoint)
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
    }
}
