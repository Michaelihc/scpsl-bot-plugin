using AdminToys;
using Mirror;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Core.Zones;
using PluginAPI.Events;
using SCPSLBot.Navigation.Mesh.Room;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class NavigationMeshVisuals
    {
        public Player PlayerEnabledVisualsFor { get; set; }

        public RoomKindVertex NearestVertex { get; set; }
        public RoomKindVertex FacingVertex { get; set; }

        public List<RoomKindVertex> SelectedVertices { get; set; }

        public RoomKindArea NearestArea { get; set; }
        public RoomKindArea FacingArea { get; set; }
        public RoomKindArea CachedArea { get; set; }

        public List<Area> Path { get; } = new ();

        private Dictionary<RoomVertex, PrimitiveObjectToy> VertexVisuals { get; } = new();
        private Dictionary<(Vertex From, Vertex To), (PrimitiveObjectToy, Area)> EdgeVisuals { get; } = new();
        private Dictionary<(RoomArea From, RoomArea To), PrimitiveObjectToy> ConnectionVisuals { get; } = new();

        private Dictionary<Area, PrimitiveObjectToy> AreaVisuals { get; } = new ();

        private NavigationMesh NavigationMesh { get; } = NavigationMesh.Instance;

        private RoomKindVertex LastNearestVertex { get; set; }
        private RoomKindVertex LastFacingVertex { get; set; }

        private RoomKindArea LastNearestArea { get; set; }
        private RoomKindArea LastFacingArea { get; set; }

        private string[] VisualsMessages { get; } = new string[2];

        private string SentBroadcastMessage;

        private PrimitiveObjectToy primPrefab;

        public void Init()
        {
            EventManager.RegisterEvents(this);
        }

        [PluginEvent(PluginAPI.Enums.ServerEventType.WaitingForPlayers)]
        public void AssignPrimPrefab()
        {
            this.primPrefab = NetworkClient.prefabs.Values.Select(p => p.GetComponent<PrimitiveObjectToy>()).First(p => p);
        }

        public void UpdateBroadcastMessage()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                VisualsMessages[0] = null;
                VisualsMessages[1] = null;

                UpdateVertexInfo();
                UpdateAreaInfo();

                var messageLinesToSend = VisualsMessages.Where(m => m != null);
                if (messageLinesToSend.Any())
                {
                    var broadcastMessage = string.Join("\n", messageLinesToSend);
                    PlayerEnabledVisualsFor.SendBroadcast($"<size=30>{broadcastMessage}", 60, shouldClearPrevious: true);
                    SentBroadcastMessage = broadcastMessage;
                }
                else
                {
                    if (SentBroadcastMessage != null)
                    {
                        PlayerEnabledVisualsFor.ClearBroadcasts();
                        SentBroadcastMessage = null;
                    }
                }
            }
        }

        public void UpdateVertexInfo()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                if (NearestVertex != null)
                {
                    var nearestVertexId = NavigationMesh.VerticesByRoomKind[NearestVertex.RoomKind].IndexOf(NearestVertex);
                    VisualsMessages[0] = $"Vertex #{nearestVertexId} in {NearestVertex.RoomKind}";

                    var selectedIdx = SelectedVertices.IndexOf(NearestVertex);
                    if (selectedIdx >= 0)
                    {
                        VisualsMessages[0] += $" <color=green>(selected #{selectedIdx})</color>";
                    }
                }

                if (FacingVertex != null)
                {
                    var facingVertexId = NavigationMesh.VerticesByRoomKind[FacingVertex.RoomKind].IndexOf(FacingVertex);
                    VisualsMessages[1] = $"Facing vertex #{facingVertexId} in {FacingVertex.RoomKind}";

                    var selectedIdx = SelectedVertices.IndexOf(FacingVertex);
                    if (selectedIdx >= 0)
                    {
                        VisualsMessages[1] += $" <color=green>(selected #{selectedIdx})</color>";
                    }
                }
            }
        }

        public void UpdateAreaInfo()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                if (NearestArea != null)
                {
                    //var connectedIdsStr = string.Join(", ", NearestArea.ConnectedAreas.Select(c => $"#{c.Id}"));
                    var NearestAreaId = NavigationMesh.AreasByRoomKind[NearestArea.RoomKind].IndexOf(NearestArea);
                    VisualsMessages[0] = $"Area #{NearestAreaId} in {NearestArea.RoomKind}";
                }

                if (CachedArea != null)
                {
                    var cachedAreaId = NavigationMesh.AreasByRoomKind[CachedArea.RoomKind].IndexOf(CachedArea);
                    VisualsMessages[1] = $"Cached area #{cachedAreaId} in {CachedArea.RoomKind}";

                    if (NearestArea != null)
                    {
                        if (NearestArea.ConnectedRoomKindAreas.Contains(CachedArea) && CachedArea.ConnectedRoomKindAreas.Contains(NearestArea))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (NearestArea.ConnectedRoomKindAreas.Contains(CachedArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (CachedArea.ConnectedRoomKindAreas.Contains(NearestArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected from)";
                        }
                    }
                }

                if (FacingArea != null)
                {
                    var facingAreaId = NavigationMesh.AreasByRoomKind[FacingArea.RoomKind].IndexOf(FacingArea);
                    VisualsMessages[1] = $"Facing area #{facingAreaId} in {FacingArea.RoomKind}";

                    if (NearestArea != null)
                    {
                        if (NearestArea.ConnectedRoomKindAreas.Contains(FacingArea) && FacingArea.ConnectedRoomKindAreas.Contains(NearestArea))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (NearestArea.ConnectedRoomKindAreas.Contains(FacingArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (FacingArea.ConnectedRoomKindAreas.Contains(NearestArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected from)";
                        }
                    }
                }
            }
        }

        public void UpdateVertexVisuals()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                foreach (var vertexVisual in VertexVisuals.Where(p => p.Value.gameObject.activeInHierarchy).ToArray())
                {
                    var vertexPosChanged = vertexVisual.Value.transform.position != vertexVisual.Key.Position;

                    if (!NavigationMesh.VerticesByRoom.Values.Any(l => l.Values.Contains(vertexVisual.Key)) || vertexPosChanged)
                    {
                        NetworkServer.Destroy(vertexVisual.Value.gameObject);
                        VertexVisuals.Remove(vertexVisual.Key);
                    }
                }

                foreach (var vertex in NavigationMesh.VerticesByRoom.Values.SelectMany(l => l.Values))
                {
                    var room = vertex.Room.Identifier;

                    if (!VertexVisuals.TryGetValue(vertex, out var visual))
                    {
                        visual = UnityEngine.Object.Instantiate(this.primPrefab);
                        visual.gameObject.SetActive(false);

                        // NetworkServer.Spawn(visual.gameObject);

                        visual.transform.position = vertex.Position;
                        visual.transform.localScale = Vector3.one * 0.125f;
                        visual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                        VertexVisuals.Add(vertex, visual);
                    }

                    var isWithinRange = Vector3.SqrMagnitude(PlayerEnabledVisualsFor.Position - visual.transform.position) < Mathf.Pow(20f, 2);
                    if (isWithinRange && !visual.gameObject.activeInHierarchy)
                    {
                        visual.gameObject.SetActive(true);
                        NetworkServer.Spawn(visual.gameObject);
                    }

                    if (!isWithinRange && visual.gameObject.activeInHierarchy)
                    {
                        visual.gameObject.SetActive(false);
                        NetworkServer.UnSpawn(visual.gameObject);
                    }

                    if (visual.gameObject.activeSelf)
                    {
                        if (NearestArea?.Vertices.Contains(vertex.RoomKindVertex) ?? false)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (SelectedVertices.Contains(vertex.RoomKindVertex))
                        {
                            visual.NetworkMaterialColor = Color.green;
                        }
                        else
                        {
                            visual.NetworkMaterialColor = Color.white;
                        }
                    }
                }
            }
            else
            {
                foreach (var vertexVisual in VertexVisuals.Values)
                {
                    NetworkServer.Destroy(vertexVisual.gameObject);
                }
                VertexVisuals.Clear();
            }
        }

        public void UpdateAreaVisuals()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                foreach (var areaVisual in AreaVisuals.Where(p => p.Value.gameObject.activeInHierarchy).ToArray())
                {
                    if (!NavigationMesh.AreasByRoom.Values.Any(l => l.Contains(areaVisual.Key)))
                    {
                        NetworkServer.Destroy(areaVisual.Value.gameObject);
                        AreaVisuals.Remove(areaVisual.Key);
                    }
                }

                foreach (var area in NavigationMesh.AreasByRoom.Values.SelectMany(l => l))
                {
                    var room = area.Room.Identifier;

                    if (!AreaVisuals.TryGetValue(area, out var visual))
                    {
                        visual = UnityEngine.Object.Instantiate(this.primPrefab);
                        visual.gameObject.SetActive(false);

                        visual.NetworkPrimitiveType = PrimitiveType.Quad;

                        visual.transform.RotateAround(visual.transform.position, visual.transform.right, 90f);
                        visual.transform.localScale = Vector3.one * .25f;
                        visual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                        // NetworkServer.Spawn(visual.gameObject);

                        AreaVisuals.Add(area, visual);
                    }

                    visual.transform.position = room.transform.TransformPoint(area.LocalCenterPosition);

                    var isWithinRange = Vector3.SqrMagnitude(PlayerEnabledVisualsFor.Position - visual.transform.position) < Mathf.Pow(20f, 2);
                    if (isWithinRange && !visual.gameObject.activeInHierarchy)
                    {
                        visual.gameObject.SetActive(true);
                        NetworkServer.Spawn(visual.gameObject);
                    }
                    
                    if (!isWithinRange && visual.gameObject.activeInHierarchy)
                    {
                        visual.gameObject.SetActive(false);
                        NetworkServer.UnSpawn(visual.gameObject);
                    }

                    if (visual.gameObject.activeSelf)
                    {
                        if (NearestArea == area.RoomKindArea)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (NearestArea?.ConnectedRoomKindAreas.Contains(area.RoomKindArea) ?? false)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else
                        {
                            visual.NetworkMaterialColor = Color.white;
                        }
                    }
                }

                foreach (var area in Path)
                {
                    var areaVisual = AreaVisuals[area];
                    areaVisual.NetworkMaterialColor = Color.blue;
                }
            }
            else
            {
                foreach (var areaVisual in AreaVisuals.Values)
                {
                    NetworkServer.Destroy(areaVisual.gameObject);
                }
                AreaVisuals.Clear();
            }
        }

        public void UpdateEdgeVisuals()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                var enabledEdgeVisuals = EdgeVisuals.Where(p => p.Value.Item1.gameObject.activeInHierarchy);
                foreach (var (edge, (visual, area)) in enabledEdgeVisuals.Select(p => (p.Key, p.Value)).ToArray())
                {
                    var isAreaRemoved = area switch
                    {
                        RoomArea roomArea => !NavigationMesh.AreasByRoom[roomArea.Room].Contains(roomArea),
                        _ => throw new NotImplementedException()
                    };

                    Vector3 currentEdgeCenter() => Vector3.Lerp(edge.From.Position, edge.To.Position, 0.5f);
                    bool isEdgeCenterChanged() => currentEdgeCenter() != visual.transform.position;

                    if (isAreaRemoved || !area.ContainsEdge(new(edge.From, edge.To)) || isEdgeCenterChanged())
                    {
                        NetworkServer.Destroy(visual.gameObject);
                        EdgeVisuals.Remove(edge);
                    }
                }

                foreach (var area in NavigationMesh.AreasByRoom.Values.SelectMany(l => l))
                {
                    var room = area.Room;

                    foreach (var roomKindEdge in area.RoomKindArea.Edges)
                    {
                        var edge = (
                            From: NavigationMesh.VerticesByRoom[room][roomKindEdge.From],
                            To: NavigationMesh.VerticesByRoom[room][roomKindEdge.To]
                        );

                        if (!EdgeVisuals.TryGetValue(edge, out var edgeVisualArea))
                        {
                            var newEdgeVisual = UnityEngine.Object.Instantiate(this.primPrefab);
                            newEdgeVisual.gameObject.SetActive(false);

                            newEdgeVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                            newEdgeVisual.transform.position = Vector3.Lerp(room.Transform.TransformPoint(edge.From.LocalPosition), room.Transform.TransformPoint(edge.To.LocalPosition), 0.5f);
                            newEdgeVisual.transform.LookAt(room.Transform.TransformPoint(edge.To.LocalPosition));
                            newEdgeVisual.transform.RotateAround(newEdgeVisual.transform.position, newEdgeVisual.transform.right, 90f);
                            newEdgeVisual.transform.localScale = Vector3.forward * 0.01f + Vector3.right * 0.01f;
                            newEdgeVisual.transform.localScale += Vector3.up * Vector3.Distance(room.Transform.TransformPoint(edge.From.LocalPosition), room.Transform.TransformPoint(edge.To.LocalPosition)) * 0.5f;
                            newEdgeVisual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                            // NetworkServer.Spawn(newEdgeVisual.gameObject);

                            edgeVisualArea = (newEdgeVisual, area);
                            EdgeVisuals.Add(edge, edgeVisualArea);
                        }

                        var (edgeVisual, _) = edgeVisualArea;

                        var isWithinRange = Vector3.SqrMagnitude(PlayerEnabledVisualsFor.Position - edgeVisual.transform.position) < Mathf.Pow(20f, 2);
                        if (isWithinRange && !edgeVisual.gameObject.activeInHierarchy)
                        {
                            edgeVisual.gameObject.SetActive(true);
                            NetworkServer.Spawn(edgeVisual.gameObject);
                        }

                        if (!isWithinRange && edgeVisual.gameObject.activeInHierarchy)
                        {
                            edgeVisual.gameObject.SetActive(false);
                            NetworkServer.UnSpawn(edgeVisual.gameObject);
                        }

                        if (edgeVisual.gameObject.activeSelf)
                        {
                            edgeVisual.NetworkMaterialColor = (NearestArea?.Edges.Contains(roomKindEdge) ?? false) ? Color.yellow : Color.white;
                        }
                    }
                }

                if (Path.Count >= 2)
                {
                    var pathEnumerator = Path.GetEnumerator();

                    pathEnumerator.MoveNext();
                    var nextArea = pathEnumerator.Current;

                    while (pathEnumerator.MoveNext())
                    {
                        var area = nextArea;
                        nextArea = pathEnumerator.Current;

                        if (!area.ConnectedAreaEdges.TryGetValue(nextArea, out var connectedEdge))
                        {
                            continue;
                        }

                        var roomEdge = (connectedEdge.From, connectedEdge.To);
                        var (edgeVisual, _) = EdgeVisuals[roomEdge];
                        edgeVisual.NetworkMaterialColor = Color.blue;
                    }
                }
            }
            else
            {
                foreach (var (edgeVisual, area) in EdgeVisuals.Values)
                {
                    NetworkServer.Destroy(edgeVisual.gameObject);
                }
                EdgeVisuals.Clear();
            }
        }

        public void UpdateConnectionVisuals()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                foreach (var ((areaFrom, areaTo), visual) in ConnectionVisuals.Select(p => (p.Key, p.Value)).ToArray())
                {
                    var isAreaFromRemoved = !NavigationMesh.AreasByRoom[areaFrom.Room].Contains(areaFrom);
                    
                    if (isAreaFromRemoved || (!areaFrom.ForeignConnectedAreas.Contains(areaTo)))
                    {
                        NetworkServer.Destroy(visual.gameObject);
                        ConnectionVisuals.Remove((areaFrom, areaTo));
                    }
                }

                foreach (var areaFrom in NavigationMesh.AreasByRoom.Values.SelectMany(l => l))
                {
                    var roomFrom = areaFrom.Room;

                    foreach (var (areaTo, i) in areaFrom.ForeignConnectedAreas.Select((a, i) => (a, i)))
                    {
                        var roomTo = areaTo.Room;

                        if (!ConnectionVisuals.TryGetValue((areaFrom, areaTo), out var connectionVisual))
                        {
                            var newConnectionVisual = UnityEngine.Object.Instantiate(this.primPrefab);
                            newConnectionVisual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                            if (areaTo.ConnectedAreaEdges.TryGetValue(areaFrom, out var fromAreaEdge)
                                && areaFrom.ConnectedAreaEdges.TryGetValue(areaTo, out var toAreaEdge))
                            {
                                // Adjacent rooms connection
                                var fromAreaEdgePos = Vector3.Lerp(fromAreaEdge.From.Position, fromAreaEdge.To.Position, .5f);
                                var toAreaEdgePos = Vector3.Lerp(toAreaEdge.From.Position, toAreaEdge.To.Position, .5f);

                                newConnectionVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                                newConnectionVisual.transform.position = Vector3.Lerp(fromAreaEdgePos, toAreaEdgePos, 0.5f);
                                newConnectionVisual.transform.LookAt(toAreaEdgePos);
                                newConnectionVisual.transform.RotateAround(newConnectionVisual.transform.position, newConnectionVisual.transform.right, 90f);
                                newConnectionVisual.transform.localScale = Vector3.forward * 0.01f + Vector3.right * 0.01f;
                                newConnectionVisual.transform.localScale += Vector3.up * Vector3.Distance(fromAreaEdgePos, toAreaEdgePos) * 0.5f;
                            }
                            else
                            {
                                // Elevator/warping connection
                                var fromAreaCenterPosition = areaFrom.CenterPosition;

                                newConnectionVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                                newConnectionVisual.transform.position = fromAreaCenterPosition;
                                newConnectionVisual.transform.localScale *= 0.01f;
                            }

                            NetworkServer.Spawn(newConnectionVisual.gameObject);

                            connectionVisual = newConnectionVisual;
                            ConnectionVisuals.Add((areaFrom, areaTo), connectionVisual);
                        }

                        //connectionVisual.NetworkMaterialColor = (NearestArea?.Edges.Contains(edge) ?? false) ? Color.yellow : Color.white;

                    }
                }
            }
            else
            {
                foreach (var connectionVisual in ConnectionVisuals.Values)
                {
                    NetworkServer.Destroy(connectionVisual.gameObject);
                }
                ConnectionVisuals.Clear();
            }
        }
    }
}
