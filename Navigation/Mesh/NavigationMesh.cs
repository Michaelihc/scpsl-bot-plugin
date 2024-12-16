using Interactables.Interobjects;
using MapGeneration;
using PluginAPI.Core;
using PluginAPI.Core.Zones;
using SCPSLBot.Navigation.Mesh.Connector;
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
        public Dictionary<FacilityRoom, Dictionary<RoomFormVertex, RoomVertex>> VerticesByRoom { get; } = new();

        public Dictionary<string, List<RoomFormArea>> AreasByRoomForm { get; } = new();
        public Dictionary<FacilityRoom, List<RoomArea>> AreasByRoom { get; } = new();


        public Dictionary<string, List<ConnectorFormVertex>> VerticesByConnectorForm { get; } = new();
        public Dictionary<RoomConnector, Dictionary<ConnectorFormVertex, ConnectorVertex>> VerticesByConnector { get; } = new();
        public event Action<ConnectorFormVertex> ConnectorVertexRemoved;

        public Dictionary<string, List<ConnectorFormArea>> AreasByConnectorForm { get; } = new();
        public Dictionary<RoomConnector, List<ConnectorArea>> AreasByConnector { get; } = new();


        private NavigationMesh()
        {
            ConnectorVertexRemoved += RemoveConnectorVertexFromAreas;
        }

        #region Mesh querying

        public RoomArea GetAreaWithin(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);

            if (!room || !AreasByRoom.TryGetValue(room.ApiRoom, out var roomAreas))
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

            if (!room || !AreasByRoom.TryGetValue(room.ApiRoom, out var roomAreas))
            {
                return null;
            }

            var localPosition = room.transform.InverseTransformPoint(position);

            var hit = roomAreas.SelectMany(a => a.RoomFormArea.Edges)
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
                
            RoomVertex roomEdgeFrom = VerticesByRoom[room.ApiRoom][roomFormEdge.From],
                       roomEdgeTo = VerticesByRoom[room.ApiRoom][roomFormEdge.To];

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
                //Log.Debug($"Evaluating connections for area #{areaIdx} with priority value {areasWithPriorityToEvaluate[area]} {area.RoomFormArea.RoomForm}");

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
                    //Log.Debug($"Connected area #{connAreaIdx} cost so far {connectedCost} {connectedArea.RoomFormArea.RoomForm}");

                    if (!costsTill.ContainsKey(connectedArea) || connectedCost < costsTill[connectedArea])
                    {
                        costsTill[connectedArea] = connectedCost;
                        heuristic = Vector3.Magnitude(endArea.CenterPosition - connectedArea.CenterPosition);
                        areasWithPriorityToEvaluate[connectedArea] = connectedCost + heuristic;
                        cameFromAreas[connectedArea] = area;

                        //Log.Debug($"Connected area #{connAreaIdx} adding for evaluation with heuristic {heuristic} {connectedArea.RoomFormArea.RoomForm}");
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

            if (!room || !VerticesByRoom.TryGetValue(room.ApiRoom, out var roomVertexs))
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
            var pointLocalPosition = room.Transform.InverseTransformPoint(pointPosition);

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
            if (!VerticesByRoomForm.TryGetValue(roomForm, out var roomFormVertices))
            {
                roomFormVertices = new List<RoomFormVertex>();
                VerticesByRoomForm.Add(roomForm, roomFormVertices);
            }

            var newRoomFormVertex = new RoomFormVertex(localPosition, roomForm);
            roomFormVertices.Add(newRoomFormVertex);

            foreach (var roomVerticesPair in VerticesByRoom.Where(r => GetRoomForm(r.Key.Identifier.gameObject.name) == roomForm))
            {
                Log.Debug($"Room vertex added.");
                roomVerticesPair.Value.Add(newRoomFormVertex, new RoomVertex(newRoomFormVertex, roomVerticesPair.Key));
            }

            return newRoomFormVertex;
        }

        public ConnectorFormVertex AddConnectorVertex(Vector3 localPosition, string connectorForm)
        {
            if (!VerticesByConnectorForm.TryGetValue(connectorForm, out var connectorFormVertices))
            {
                connectorFormVertices = new List<ConnectorFormVertex>();
                VerticesByConnectorForm.Add(connectorForm, connectorFormVertices);
            }

            var newConnectorFormVertex = new ConnectorFormVertex(localPosition, connectorForm);
            connectorFormVertices.Add(newConnectorFormVertex);

            foreach (var connectorVerticesPair in VerticesByConnector.Where(p => StartsWithForm(p.Key, connectorForm)))
            {
                Log.Debug($"Connector vertex added.");
                connectorVerticesPair.Value.Add(newConnectorFormVertex, new ConnectorVertex(newConnectorFormVertex, connectorVerticesPair.Key));
            }

            return newConnectorFormVertex;
        }

        public bool DeleteRoomVertex(RoomFormVertex roomFormVertex)
        {
            var roomForm = roomFormVertex.RoomForm;

            if (!VerticesByRoomForm.TryGetValue(roomForm, out var roomFormVertices))
            {
                Log.Warning($"No vertices at room {roomForm} to remove vertex from.");
                return false;
            }

            if (AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
            {
                foreach (var area in roomFormAreas.ToArray())
                {
                    area.Vertices.Remove(roomFormVertex);
                    if (area.Vertices.Count < 3)
                    {
                        RemoveRoomArea(area);

                        Log.Warning($"Area at local center position {area.LocalCenterPosition} removed under room {roomForm}.");
                    }
                }
            }

            roomFormVertices.Remove(roomFormVertex);

            foreach (var roomVerticesPair in VerticesByRoom.Where(r => GetRoomForm(r.Key.Identifier.gameObject.name) == roomForm))
            {
                Log.Debug($"Room vertex removed.");
                roomVerticesPair.Value.Remove(roomFormVertex);
            }

            return true;
        }

        public bool DeleteConnectorVertex(ConnectorFormVertex connectorFormVertex)
        {
            var connectorForm = connectorFormVertex.ConnectorForm;

            if (!VerticesByConnectorForm.TryGetValue(connectorForm, out var connectorFormVertices))
            {
                Log.Warning($"No vertices at connector {connectorForm} to remove vertex from.");
                return false;
            }

            connectorFormVertices.Remove(connectorFormVertex);

            ConnectorVertexRemoved?.Invoke(connectorFormVertex);

            foreach (var connectorVerticesPair in VerticesByConnector.Where(p => StartsWithForm(p.Key, connectorForm)))
            {
                Log.Debug($"Connector vertex removed.");
                connectorVerticesPair.Value.Remove(connectorFormVertex);
            }

            return true;
        }

        public bool MoveRoomVertex(RoomFormVertex roomFormVertex, Vector3 newLocalPosition)
        {
            roomFormVertex.LocalPosition = newLocalPosition;

            return true;
        }

        public bool MoveConnectorVertex(ConnectorFormVertex connectorFormVertex, Vector3 newLocalPosition)
        {
            connectorFormVertex.LocalPosition = newLocalPosition;

            return true;
        }

        public RoomFormArea MakeRoomArea(IEnumerable<RoomFormVertex> roomFormVertices, string roomForm)
        {
            if (!AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
            {
                roomFormAreas = new List<RoomFormArea>();
                AreasByRoomForm.Add(roomForm, roomFormAreas);
            }

            var newRoomFormArea = new RoomFormArea(roomFormVertices, roomForm);
            roomFormAreas.Add(newRoomFormArea);

            foreach (var edge in newRoomFormArea.Edges)
            {
                var inversedEdge = new RoomFormEdge(edge.To, edge.From);
                var connectedArea = roomFormAreas.Find(a => a != newRoomFormArea && a.Edges.Contains(inversedEdge));
                if (connectedArea != null)
                {
                    newRoomFormArea.ConnectedRoomFormAreas.Add(connectedArea);
                    connectedArea.ConnectedRoomFormAreas.Add(newRoomFormArea);
                }
            }

            AddRoomAreas(newRoomFormArea);

            return newRoomFormArea;
        }

        public ConnectorFormArea MakeConnectorArea(IEnumerable<ConnectorFormVertex> connectorFormVertices, string connectorForm)
        {
            if (!AreasByConnectorForm.TryGetValue(connectorForm, out var connectorFormAreas))
            {
                connectorFormAreas = new List<ConnectorFormArea>();
                AreasByConnectorForm.Add(connectorForm, connectorFormAreas);
            }

            var newConnectorFormArea = new ConnectorFormArea(connectorFormVertices, connectorForm);
            connectorFormAreas.Add(newConnectorFormArea);

            foreach (var edge in newConnectorFormArea.Edges)
            {
                var inversedEdge = new ConnectorFormEdge(edge.To, edge.From);
                var connectedArea = connectorFormAreas.Find(a => a != newConnectorFormArea && a.Edges.Contains(inversedEdge));
                if (connectedArea != null)
                {
                    newConnectorFormArea.ConnectedConnectorFormAreas.Add(connectedArea);
                    connectedArea.ConnectedConnectorFormAreas.Add(newConnectorFormArea);
                }
            }

            AddConnectorAreas(newConnectorFormArea);

            return newConnectorFormArea;
        }

        public void RemoveRoomArea(RoomFormArea roomFormArea)
        {
            var roomForm = roomFormArea.RoomForm;

            if (!AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
            {
                Log.Warning($"No areas at room {roomForm} to remove area from.");
                return;
            }

            foreach (var connectedToRemovingArea in roomFormArea.ConnectedRoomFormAreas)
            {
                connectedToRemovingArea.ConnectedRoomFormAreas.Remove(roomFormArea);
            }

            roomFormAreas.Remove(roomFormArea);

            foreach (var roomOfForm in AreasByRoom.Where(r => GetRoomForm(r.Key.Identifier.gameObject.name) == roomForm))
            {
                var area = roomOfForm.Value.Find(n => n.RoomFormArea == roomFormArea);
                roomOfForm.Value.Remove(area);
            }
        }
        public void RemoveConnectorArea(ConnectorFormArea connectorFormArea)
        {
            var connectorForm = connectorFormArea.ConnectorForm;

            if (!AreasByConnectorForm.TryGetValue(connectorForm, out var connectorFormAreas))
            {
                Log.Warning($"No areas at connector {connectorForm} to remove area from.");
                return;
            }

            foreach (var connectedToRemovingArea in connectorFormArea.ConnectedConnectorFormAreas)
            {
                connectedToRemovingArea.ConnectedConnectorFormAreas.Remove(connectorFormArea);
            }

            connectorFormAreas.Remove(connectorFormArea);

            foreach (var connectorOfForm in AreasByConnector.Where(p => StartsWithForm(p.Key, connectorForm)))
            {
                var area = connectorOfForm.Value.Find(n => n.ConnectorFormArea == connectorFormArea);
                connectorOfForm.Value.Remove(area);
            }
        }

        public void CreateRoomAreaConnection(RoomFormArea fromArea, RoomFormArea toArea)
        {
            fromArea.ConnectedRoomFormAreas.Add(toArea);
        }
        public void CreateConnectorAreaConnection(ConnectorFormArea fromArea, ConnectorFormArea toArea)
        {
            fromArea.ConnectedConnectorFormAreas.Add(toArea);
        }

        public void DeleteRoomAreaConnection(RoomFormArea fromArea, RoomFormArea toArea)
        {
            fromArea.ConnectedRoomFormAreas.Remove(toArea);
        }
        public void DeleteConnectorAreaConnection(ConnectorFormArea fromArea, ConnectorFormArea toArea)
        {
            fromArea.ConnectedConnectorFormAreas.Remove(toArea);
        }

        public void AddRoomVertexToArea(RoomFormArea area, RoomFormVertex vertex, RoomFormVertex beforeVertex)
        {
            var atIdx = area.Vertices.IndexOf(beforeVertex);

            area.Vertices.Insert(atIdx, vertex);
        }
        public void AddConnectorVertexToArea(ConnectorFormArea area, ConnectorFormVertex vertex, ConnectorFormVertex beforeVertex)
        {
            var atIdx = area.Vertices.IndexOf(beforeVertex);

            area.Vertices.Insert(atIdx, vertex);
        }

        private void AddRoomAreas(RoomFormArea roomFormArea)
        {
            var roomsAreasOfRoomForm = AreasByRoom.Select(r => (room: r.Key, areas: r.Value))
                .Where(t => GetRoomForm(t.room.Identifier.gameObject.name) == roomFormArea.RoomForm);

            foreach (var (room, areas) in roomsAreasOfRoomForm)
            {
                var newRoomArea = new RoomArea(roomFormArea, room);
                areas.Add(newRoomArea);
            }
        }
        private void AddConnectorAreas(ConnectorFormArea connectorFormArea)
        {
            var connectorsAreasOfConnectorForm = AreasByConnector.Where(p => StartsWithForm(p.Key, connectorFormArea.ConnectorForm));

            foreach (var (connector, areas) in connectorsAreasOfConnectorForm)
            {
                var newConnectorArea = new ConnectorArea(connectorFormArea, connector);
                areas.Add(newConnectorArea);
            }
        }

        private void RemoveConnectorVertexFromAreas(ConnectorFormVertex connectorFormVertex)
        {
            if (AreasByConnectorForm.TryGetValue(connectorFormVertex.ConnectorForm, out var connectorFormAreas))
            {
                foreach (var area in connectorFormAreas.ToArray())
                {
                    area.Vertices.Remove(connectorFormVertex);
                    if (area.Vertices.Count < 3)
                    {
                        RemoveConnectorArea(area);

                        Log.Warning($"Area at local center position {area.LocalCenterPosition} removed under connector {connectorFormVertex.ConnectorForm}.");
                    }
                }
            }
        }

        #endregion
        #region Mesh reading/writing

        public void ReadMesh(BinaryReader binaryReader)
        {
            var version = binaryReader.ReadByte();
            if (version < 2)
            {
                Log.Error($"Version in navmesh file is newer or older than supported.");
                return;
            }

            ReadRooms(binaryReader);

            if (version < 3)
            {
                return;
            }

            ReadConnectors(binaryReader);
        }

        private void ReadRooms(BinaryReader binaryReader)
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

                    var newRoomFormVertex = new RoomFormVertex(vertexLocalPosition, roomForm);
                    roomFormVertices.Add(newRoomFormVertex);
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
                    var newRoomFormArea = new RoomFormArea(roomForm);
                    roomFormAreas.Add(newRoomFormArea);

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
                    area.Vertices.AddRange(vertices.Select(vertexIdx => roomFormVertices![vertexIdx]));
                }

                foreach (var (area, conns) in areasConnections.Select((conns, areaIndex) => (roomFormAreas[areaIndex], conns)))
                {
                    area.ConnectedRoomFormAreas.AddRange(conns.Select(connectedIndex => roomFormAreas[connectedIndex]));
                }
            }
        }

        private void ReadConnectors(BinaryReader binaryReader)
        {
            var connectorFormCount = binaryReader.ReadInt32();

            for (var i = 0; i < connectorFormCount; i++)
            {
                string connectorForm = binaryReader.ReadString();

                ///
                /// Vertices reading
                /// 

                if (!VerticesByConnectorForm.TryGetValue(connectorForm, out var connectorFormVertices))
                {
                    connectorFormVertices = new List<ConnectorFormVertex>();
                    VerticesByConnectorForm.Add(connectorForm, connectorFormVertices);
                }
                else
                {
                    connectorFormVertices.Clear();
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

                    var newConnectorFormVertex = new ConnectorFormVertex(vertexLocalPosition, connectorForm);
                    connectorFormVertices.Add(newConnectorFormVertex);
                }

                ///
                /// Areas reading
                /// 

                if (!AreasByConnectorForm.TryGetValue(connectorForm, out var connectorFormAreas))
                {
                    connectorFormAreas = new List<ConnectorFormArea>();
                    AreasByConnectorForm.Add(connectorForm, connectorFormAreas);
                }
                else
                {
                    connectorFormAreas.Clear();
                }

                var areasCount = binaryReader.ReadInt32();

                var areasVertices = new int[areasCount][];
                var areasConnections = new int[areasCount][];

                for (var j = 0; j < areasCount; j++)
                {
                    var newConnectorFormArea = new ConnectorFormArea(connectorForm);
                    connectorFormAreas.Add(newConnectorFormArea);

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

                foreach (var (area, vertices) in areasVertices.Select((vertices, areaIndex) => (connectorFormAreas[areaIndex], vertices)))
                {
                    area.Vertices.AddRange(vertices.Select(vertexIdx => connectorFormVertices![vertexIdx]));
                }

                foreach (var (area, conns) in areasConnections.Select((conns, areaIndex) => (connectorFormAreas[areaIndex], conns)))
                {
                    area.ConnectedConnectorFormAreas.AddRange(conns.Select(connectedIndex => connectorFormAreas[connectedIndex]));
                }
            }
        }

        public void WriteMesh(BinaryWriter binaryWriter)
        {
            byte version = 3;
            binaryWriter.Write(version);
            
            ///
            /// Rooms writing
            ///

            binaryWriter.Write(VerticesByRoomForm.Count);

            foreach (var (roomForm, vertices) in VerticesByRoomForm.Select(p => (p.Key, p.Value)))
            {
                binaryWriter.Write(roomForm);

                binaryWriter.Write(vertices.Count);
                foreach (var vertex in vertices)
                {
                    binaryWriter.Write(vertex.LocalPosition.x);
                    binaryWriter.Write(vertex.LocalPosition.y);
                    binaryWriter.Write(vertex.LocalPosition.z);
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

                    binaryWriter.Write(area.ConnectedRoomFormAreas.Count);
                    foreach (var connIdx in area.ConnectedRoomFormAreas.Select(connArea => AreasByRoomForm[roomForm].IndexOf(connArea)))
                    {
                        binaryWriter.Write(connIdx);
                    }
                }
            }

            ///
            /// Connectors writing
            ///

            binaryWriter.Write(VerticesByConnectorForm.Count);

            foreach (var (connectorForm, vertices) in VerticesByConnectorForm.Select(p => (p.Key, p.Value)))
            {
                binaryWriter.Write(connectorForm);

                binaryWriter.Write(vertices.Count);
                foreach (var vertex in vertices)
                {
                    binaryWriter.Write(vertex.LocalPosition.x);
                    binaryWriter.Write(vertex.LocalPosition.y);
                    binaryWriter.Write(vertex.LocalPosition.z);
                }

                if (!AreasByConnectorForm.TryGetValue(connectorForm, out var areas))
                {
                    areas = new();
                }

                binaryWriter.Write(areas.Count);
                foreach (var area in areas)
                {
                    binaryWriter.Write(area.Vertices.Count);
                    foreach (var vertexIdx in area.Vertices.Select(areaVertex => VerticesByConnectorForm[connectorForm].IndexOf(areaVertex)))
                    {
                        binaryWriter.Write(vertexIdx);
                    }

                    binaryWriter.Write(area.ConnectedConnectorFormAreas.Count);
                    foreach (var connIdx in area.ConnectedConnectorFormAreas.Select(connArea => AreasByConnectorForm[connectorForm].IndexOf(connArea)))
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
                VerticesByRoom.Add(room, vertices);

                var roomForm = GetRoomForm(room.Identifier.gameObject.name);

                if (!VerticesByRoomForm.TryGetValue(roomForm, out var roomFormVertices))
                {
                    continue;
                }

                foreach (var f in roomFormVertices)
                {
                    vertices.Add(f, new RoomVertex(f, room));
                }
            }
        }

        public void ResetVertices()
        {
            VerticesByRoom.Clear();
        }

        public void InitRoomAreas()
        {
            foreach (var room in Facility.Rooms)
            {
                var areas = new List<RoomArea>();
                AreasByRoom.Add(room, areas);

                var roomForm = GetRoomForm(room.Identifier.gameObject.name);

                if (!AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
                {
                    continue;
                }

                areas.AddRange(roomFormAreas.Select(k => new RoomArea(k, room)));

                foreach (var roomArea in areas)
                {
                    var connectedAreas = roomArea.RoomFormArea.ConnectedRoomFormAreas.Select(c => AreasByRoom[room].Find(a => a.RoomFormArea == c));
                    //roomArea.ConnectedAreas.AddRange(connectedAreas);

                    var connectedEdges = roomArea.RoomFormArea.ConnectedRoomFormAreas
                        .Select(cka => (cka, cke: cka.Edges.First(cke => roomArea.RoomFormArea.Edges.Any(e => cke == new RoomFormEdge(e.To, e.From)))))
                        .Select(t => (
                            roomArea.ConnectedRoomAreas.First(ca => ca.RoomFormArea == t.cka),
                            new Edge(VerticesByRoom[room][t.cke.From], VerticesByRoom[room][t.cke.To])
                        ));

                    foreach (var (connectedArea, connectedEdge) in connectedEdges)
                    {
                        roomArea.ConnectedAreaEdges.Add(connectedArea, connectedEdge);
                    }
                }
            }
        }

        public void ResetAreas()
        {
            AreasByRoom.Clear();
        }

        #endregion

        private bool IsLocalPointWithinArea(RoomArea area, Vector3 pointLocalPosition)
        {
            var areaRoomFormEdges = area.RoomFormArea.Edges;

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

        public static string GetForm(RoomConnector roomConnector)
        {
            var gameObjectName = roomConnector.gameObject.name;
            return gameObjectName.EndsWith("(Clone)") ? gameObjectName.Remove(gameObjectName.LastIndexOf("(Clone)")) : gameObjectName;
        }

        public static bool StartsWithForm(RoomConnector roomConnector, string comparingForm)
        {
            return roomConnector.gameObject.name.StartsWith(comparingForm);
        }
    }
}
