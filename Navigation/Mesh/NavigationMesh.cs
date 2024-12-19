using CustomRendering;
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


        public Dictionary<string, List<FormVertex>> VerticesByRoomForm { get; } = new();
        public Dictionary<RoomIdentifier, Dictionary<FormVertex, RoomVertex>> VerticesByRoom { get; } = new();

        public Dictionary<string, List<FormArea>> AreasByRoomForm { get; } = new();
        public Dictionary<RoomIdentifier, List<RoomArea>> AreasByRoom { get; } = new();


        public Dictionary<string, List<FormVertex>> VerticesByConnectorForm { get; } = new();
        public Dictionary<RoomConnector, Dictionary<FormVertex, ConnectorVertex>> VerticesByConnector { get; } = new();
        public event Action<FormVertex> ConnectorVertexRemoved;

        public Dictionary<string, List<FormArea>> AreasByConnectorForm { get; } = new();
        public Dictionary<RoomConnector, List<ConnectorArea>> AreasByConnector { get; } = new();


        private NavigationMesh()
        {
            ConnectorVertexRemoved += RemoveConnectorVertexFromAreas;
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
                .Select(t => new (FormEdge, float, Vector3)?(t))
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

        private Vector3 ClampWithinEdgePoints(FormEdge edge, Vector3 planeClosestPoint)
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

        public FormVertex AddRoomVertex(Vector3 localPosition, string roomForm)
        {
            var newRoomFormVertex = CreateFormVertex(localPosition, roomForm, VerticesByRoomForm);
            AddVertices(newRoomFormVertex, VerticesByRoom, (formVertex, formInst) => new RoomVertex(formVertex, formInst));

            Log.Debug($"Room vertex added.");

            return newRoomFormVertex;
        }

        public FormVertex AddConnectorVertex(Vector3 localPosition, string connectorForm)
        {
            var newFormVertex = CreateFormVertex(localPosition, connectorForm, VerticesByConnectorForm);
            AddVertices(newFormVertex, VerticesByConnector, (formVertex, formInst) => new ConnectorVertex(formVertex, formInst));

            Log.Debug($"Connector vertex added.");

            return newFormVertex;
        }

        private FormVertex CreateFormVertex(Vector3 localPosition, string form, Dictionary<string, List<FormVertex>> verticesByForm)
        {
            if (!verticesByForm.TryGetValue(form, out var formVertices))
            {
                formVertices = new List<FormVertex>();
                verticesByForm.Add(form, formVertices);
            }

            var newFormVertex = new FormVertex(localPosition, form);
            formVertices.Add(newFormVertex);

            return newFormVertex;
        }

        private void AddVertices<TFormInstance, TVertex>(
            FormVertex formVertex, 
            Dictionary<TFormInstance, Dictionary<FormVertex, TVertex>> verticesByFormInstance, 
            Func<FormVertex, TFormInstance, TVertex> vertexFactory)
            where TFormInstance : MonoBehaviour
        {
            foreach (var verticesPair in verticesByFormInstance.Where(p => StartsWithForm(p.Key, formVertex.Form)))
            {
                verticesPair.Value.Add(formVertex, vertexFactory.Invoke(formVertex, verticesPair.Key));
            }
        }

        public bool DeleteRoomVertex(FormVertex roomFormVertex)
        {
            var roomForm = roomFormVertex.Form;

            if (!DeleteFormVertex(roomFormVertex, VerticesByRoomForm))
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

            RemoveVertices(roomFormVertex, VerticesByRoom);

            return true;
        }

        public bool DeleteConnectorVertex(FormVertex connectorFormVertex)
        {
            if (!DeleteFormVertex(connectorFormVertex, VerticesByConnectorForm))
            {
                Log.Warning($"No vertices at connector {connectorFormVertex.Form} to remove vertex from.");
                return false;
            }

            ConnectorVertexRemoved?.Invoke(connectorFormVertex);

            RemoveVertices(connectorFormVertex, VerticesByConnector);

            return true;
        }

        private bool DeleteFormVertex(FormVertex formVertex, Dictionary<string, List<FormVertex>> verticesByForm)
        {
            if (!verticesByForm.TryGetValue(formVertex.Form, out var connectorFormVertices))
            {
                return false;
            }

            connectorFormVertices.Remove(formVertex);

            return true;
        }

        private void RemoveVertices<TFormInstance, TVertex>(
            FormVertex formVertex,
            Dictionary<TFormInstance, Dictionary<FormVertex, TVertex>> verticesByFormInstance)
            where TFormInstance : MonoBehaviour
        {
            foreach (var connectorVerticesPair in verticesByFormInstance.Where(p => StartsWithForm(p.Key, formVertex.Form)))
            {
                Log.Debug($"Connector vertex removed.");
                connectorVerticesPair.Value.Remove(formVertex);
            }
        }

        public bool MoveRoomVertex(FormVertex roomFormVertex, Vector3 newLocalPosition)
        {
            roomFormVertex.LocalPosition = newLocalPosition;

            return true;
        }

        public bool MoveConnectorVertex(FormVertex connectorFormVertex, Vector3 newLocalPosition)
        {
            connectorFormVertex.LocalPosition = newLocalPosition;

            return true;
        }

        public FormArea MakeRoomArea(IEnumerable<FormVertex> roomFormVertices, string roomForm)
        {
            var newRoomFormArea = CreateFormArea(roomFormVertices, roomForm, AreasByRoomForm);
            AddRoomAreas(newRoomFormArea);

            return newRoomFormArea;
        }

        public FormArea MakeConnectorArea(IEnumerable<FormVertex> connectorFormVertices, string connectorForm)
        {
            var newFormArea = CreateFormArea(connectorFormVertices, connectorForm, AreasByConnectorForm);
            AddConnectorAreas(newFormArea);

            return newFormArea;
        }

        private FormArea CreateFormArea(IEnumerable<FormVertex> formVertices, string form, Dictionary<string, List<FormArea>> areasByForm)
        {
            if (!areasByForm.TryGetValue(form, out var formAreas))
            {
                formAreas = new List<FormArea>();
                areasByForm.Add(form, formAreas);
            }

            var newFormArea = new FormArea(formVertices, form);
            formAreas.Add(newFormArea);

            foreach (var edge in newFormArea.Edges)
            {
                var inversedEdge = new FormEdge(edge.To, edge.From);
                var connectedArea = formAreas.Find(a => a != newFormArea && a.Edges.Contains(inversedEdge));
                if (connectedArea != null)
                {
                    newFormArea.ConnectedFormAreas.Add(connectedArea);
                    connectedArea.ConnectedFormAreas.Add(newFormArea);
                }
            }

            return newFormArea;
        }

        public void RemoveRoomArea(FormArea roomFormArea)
        {
            if (!DeleteFormArea(roomFormArea, AreasByRoomForm))
            {
                Log.Warning($"No areas at room {roomFormArea.Form} to remove area from.");
                return;
            }

            RemoveAreas(roomFormArea, AreasByRoom);
        }

        public void RemoveConnectorArea(FormArea connectorFormArea)
        {
            if (!DeleteFormArea(connectorFormArea, AreasByConnectorForm))
            {
                Log.Warning($"No areas at connector {connectorFormArea.Form} to remove area from.");
                return;
            }

            RemoveAreas(connectorFormArea, AreasByConnector);
        }

        private bool DeleteFormArea(FormArea formArea, Dictionary<string, List<FormArea>> areasByForm)
        {
            if (!areasByForm.TryGetValue(formArea.Form, out var connectorFormAreas))
            {
                return false;
            }

            connectorFormAreas.Remove(formArea);

            foreach (var otherFormArea in connectorFormAreas)
            {
                otherFormArea.ConnectedFormAreas.Remove(formArea);
            }

            return true;
        }

        private void RemoveAreas<TFormInstance, TArea>(FormArea formArea, Dictionary<TFormInstance, List<TArea>> areasByFormInstance)
            where TFormInstance : MonoBehaviour
            where TArea : Area
        {
            foreach (var (_, areasOfForm) in areasByFormInstance.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var areaToRemove = areasOfForm.Find(n => n.FormArea == formArea);
                areasOfForm.Remove(areaToRemove);

                foreach (var area in areasOfForm)
                {
                    area.ConnectedAreasOfForm.Remove(formArea);
                }
            }
        }

        public void CreateRoomAreaConnection(FormArea fromFormArea, FormArea toFormArea)
        {
            CreateAreaConnections(fromFormArea, toFormArea, AreasByRoom);
        }

        public void CreateConnectorAreaConnection(FormArea fromFormArea, FormArea toFormArea)
        {
            CreateAreaConnections(fromFormArea, toFormArea, AreasByConnector);
        }

        private void CreateAreaConnections<TFormInstance, TArea>(FormArea fromFormArea, FormArea toFormArea, Dictionary<TFormInstance, List<TArea>> areasByFormInstance)
            where TFormInstance : MonoBehaviour
            where TArea : Area
        {
            fromFormArea.ConnectedFormAreas.Add(toFormArea);

            foreach (var (_, connectorAreas) in areasByFormInstance.Where(a => StartsWithForm(a.Key, fromFormArea.Form)))
            {
                var fromConnectorArea = connectorAreas.Find(a => a.FormArea == fromFormArea);
                var toConnectorArea = connectorAreas.Find(a => a.FormArea == toFormArea);
                fromConnectorArea.ConnectedAreasOfForm.Add(toFormArea, toConnectorArea);
            }
        }

        public void DeleteRoomAreaConnection(FormArea fromFormArea, FormArea toFormArea)
        {
            DeleteAreaConnections(fromFormArea, toFormArea, AreasByRoom);
        }

        public void DeleteConnectorAreaConnection(FormArea fromFormArea, FormArea toFormArea)
        {
            DeleteAreaConnections(fromFormArea, toFormArea, AreasByConnector);
        }

        private void DeleteAreaConnections<TFormInstance, TArea>(FormArea fromFormArea, FormArea toFormArea, Dictionary<TFormInstance, List<TArea>> areasByFormInstance)
            where TFormInstance : MonoBehaviour
            where TArea : Area
        {
            fromFormArea.ConnectedFormAreas.Remove(toFormArea);

            foreach (var (_, connectorAreas) in areasByFormInstance.Where(a => StartsWithForm(a.Key, fromFormArea.Form)))
            {
                var fromConnectorArea = connectorAreas.Find(a => a.FormArea == fromFormArea);
                fromConnectorArea.ConnectedAreasOfForm.Remove(toFormArea);
            }
        }

        public void AddRoomVertexToArea(FormArea area, FormVertex vertex, FormVertex beforeVertex)
        {
            AddVertexToArea(area, vertex, beforeVertex);
        }

        public void AddConnectorVertexToArea(FormArea area, FormVertex vertex, FormVertex beforeVertex)
        {
            AddVertexToArea(area, vertex, beforeVertex);
        }

        private void AddVertexToArea(FormArea area, FormVertex vertex, FormVertex beforeVertex)
        {
            var atIdx = area.Vertices.IndexOf(beforeVertex);
            area.Vertices.Insert(atIdx, vertex);
        }

        private void AddRoomAreas(FormArea newRoomFormArea)
        {
            AddAreas(newRoomFormArea, AreasByRoom, (formArea, room) => new RoomArea(formArea, room), AreasByRoomForm);
        }

        private void AddConnectorAreas(FormArea newFormArea)
        {
            AddAreas(newFormArea, AreasByConnector, (formArea, connector) => new ConnectorArea(formArea, connector), AreasByConnectorForm);
        }

        private void AddAreas<TFormInstance, TArea>(
            FormArea formArea, 
            Dictionary<TFormInstance, List<TArea>> areasByFormInstance, 
            Func<FormArea, TFormInstance, TArea> areaFactory,
            Dictionary<string, List<FormArea>> areasByForm)
            where TFormInstance : MonoBehaviour
            where TArea : Area
        {
            var connectingFromFormAreas = areasByForm[formArea.Form].Where(a => a.ConnectedFormAreas.Contains(formArea));

            foreach (var (formInstance, areas) in areasByFormInstance.Where(p => StartsWithForm(p.Key, formArea.Form)))
            {
                var newArea = areaFactory.Invoke(formArea, formInstance);
                areas.Add(newArea);

                foreach (var connectedToFormArea in formArea.ConnectedFormAreas)
                {
                    var areaOfConnectedToForm = areas.Find(a => a.FormArea == connectedToFormArea);
                    newArea.ConnectedAreasOfForm.Add(connectedToFormArea, areaOfConnectedToForm);
                }

                foreach (var connectingFromFormArea in connectingFromFormAreas)
                {
                    var areaOfConnectingFrom = areas.Find(a => a.FormArea == connectingFromFormArea);
                    areaOfConnectingFrom.ConnectedAreasOfForm.Add(formArea, newArea);
                }
            }
        }

        private void RemoveConnectorVertexFromAreas(FormVertex connectorFormVertex)
        {
            if (AreasByConnectorForm.TryGetValue(connectorFormVertex.Form, out var connectorFormAreas))
            {
                foreach (var area in connectorFormAreas.ToArray())
                {
                    area.Vertices.Remove(connectorFormVertex);
                    if (area.Vertices.Count < 3)
                    {
                        RemoveConnectorArea(area);

                        Log.Warning($"Area at local center position {area.LocalCenterPosition} removed under connector {connectorFormVertex.Form}.");
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
                    roomFormVertices = new List<FormVertex>();
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

                    var newRoomFormVertex = new FormVertex(vertexLocalPosition, roomForm);
                    roomFormVertices.Add(newRoomFormVertex);
                }

                ///
                /// Areas reading
                /// 

                if (!AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
                {
                    roomFormAreas = new List<FormArea>();
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
                    var newRoomFormArea = new FormArea(roomForm);
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
                    area.ConnectedFormAreas.AddRange(conns.Select(connectedIndex => roomFormAreas[connectedIndex]));
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
                    connectorFormVertices = new List<FormVertex>();
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

                    var newFormVertex = new FormVertex(vertexLocalPosition, connectorForm);
                    connectorFormVertices.Add(newFormVertex);
                }

                ///
                /// Areas reading
                /// 

                if (!AreasByConnectorForm.TryGetValue(connectorForm, out var connectorFormAreas))
                {
                    connectorFormAreas = new List<FormArea>();
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
                    var newFormArea = new FormArea(connectorForm);
                    connectorFormAreas.Add(newFormArea);

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
                    area.ConnectedFormAreas.AddRange(conns.Select(connectedIndex => connectorFormAreas[connectedIndex]));
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

                    binaryWriter.Write(area.ConnectedFormAreas.Count);
                    foreach (var connIdx in area.ConnectedFormAreas.Select(connArea => AreasByRoomForm[roomForm].IndexOf(connArea)))
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

                    binaryWriter.Write(area.ConnectedFormAreas.Count);
                    foreach (var connIdx in area.ConnectedFormAreas.Select(connArea => AreasByConnectorForm[connectorForm].IndexOf(connArea)))
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
                var vertices = new Dictionary<FormVertex, RoomVertex>();
                VerticesByRoom.Add(room.Identifier, vertices);

                var roomForm = GetRoomForm(room.Identifier.gameObject.name);

                if (!VerticesByRoomForm.TryGetValue(roomForm, out var roomFormVertices))
                {
                    continue;
                }

                foreach (var f in roomFormVertices)
                {
                    vertices.Add(f, new RoomVertex(f, room.Identifier));
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
                AreasByRoom.Add(room.Identifier, areas);

                var roomForm = GetRoomForm(room.Identifier.gameObject.name);

                if (!AreasByRoomForm.TryGetValue(roomForm, out var roomFormAreas))
                {
                    continue;
                }

                areas.AddRange(roomFormAreas.Select(k => new RoomArea(k, room.Identifier)));

                foreach (var roomArea in areas)
                {
                    var connectedAreas = roomArea.FormArea.ConnectedFormAreas.Select(c => AreasByRoom[room.Identifier].Find(a => a.FormArea == c));
                    //roomArea.ConnectedAreas.AddRange(connectedAreas);

                    var connectedEdges = roomArea.FormArea.ConnectedFormAreas
                        .Select(cka => (cka, cke: cka.Edges.First(cke => roomArea.FormArea.Edges.Any(e => cke == new FormEdge(e.To, e.From)))))
                        .Select(t => (
                            roomArea.ConnectedRoomAreas.First(ca => ca.FormArea == t.cka),
                            new Edge(VerticesByRoom[room.Identifier][t.cke.From], VerticesByRoom[room.Identifier][t.cke.To])
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

        private float GetPointDistToEdgePlane(FormEdge roomEdge, Vector3 localPoint) => GetPointDistToEdgePlane(roomEdge, localPoint, out _);
        private float GetPointDistToEdgePlane(FormEdge roomEdge, Vector3 localPoint, out Vector3 closestLocalPoint)
        {
            var dirTo2 = roomEdge.To.LocalPosition - roomEdge.From.LocalPosition;
            var dirToPoint = localPoint - roomEdge.From.LocalPosition;

            var edgeNormal = Vector3.Cross(dirTo2.normalized, Vector3.down);

            var dist = Vector3.Dot(edgeNormal, dirToPoint);

            closestLocalPoint = localPoint - edgeNormal * dist;

            return dist;
        }

        private bool IsEdgeCenterWithinVertically(FormEdge edge, Vector3 localPoint)
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

        public static bool StartsWithForm<TBehaviour>(TBehaviour behaviour, string comparingForm)
            where TBehaviour : MonoBehaviour
        {
            return behaviour.gameObject.name.StartsWith(comparingForm);
        }

        public static bool StartsWithForm(RoomIdentifier room, string comparingForm)
        {
            return room.gameObject.name.StartsWith(comparingForm);
        }

        public static bool StartsWithForm(RoomConnector roomConnector, string comparingForm)
        {
            return roomConnector.gameObject.name.StartsWith(comparingForm);
        }
    }
}
