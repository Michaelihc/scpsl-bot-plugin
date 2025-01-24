using AdminToys;
using MapGeneration;
using Mirror;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Core.Zones;
using PluginAPI.Events;
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

        public LocalVertex NearestFormVertex { get; set; }
        public LocalVertex FacingFormVertex { get; set; }

        public List<LocalVertex> SelectedFormVertices { get; set; }

        public LocalArea NearestFormArea { get; set; }
        public LocalArea FacingFormArea { get; set; }
        public LocalArea CachedFormArea { get; set; }

        public List<Area> Path { get; } = new ();

        private Dictionary<Vertex, PrimitiveObjectToy> VertexVisuals { get; } = new();
        private Dictionary<(Vertex From, Vertex To), (PrimitiveObjectToy Visual, Area Area)> EdgeVisuals { get; } = new();
        private Dictionary<(Area From, Area To), PrimitiveObjectToy> ConnectionVisuals { get; } = new();

        private Dictionary<Area, PrimitiveObjectToy> AreaVisuals { get; } = new ();

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
                if (NearestFormVertex != null)
                {
                    var mesh = NavigationMesh.GetMesh(NearestFormVertex.Form);
                    var nearestVertexId = mesh.FormVertices.IndexOf(NearestFormVertex);
                    VisualsMessages[0] = $"Vertex #{nearestVertexId} in {NearestFormVertex.Form}";

                    var selectedIdx = SelectedFormVertices.IndexOf(NearestFormVertex);
                    if (selectedIdx >= 0)
                    {
                        VisualsMessages[0] += $" <color=green>(selected #{selectedIdx})</color>";
                    }
                }

                if (FacingFormVertex != null)
                {
                    var mesh = NavigationMesh.GetMesh(FacingFormVertex.Form);
                    var facingVertexId = mesh.FormVertices.IndexOf(FacingFormVertex);
                    VisualsMessages[1] = $"Facing vertex #{facingVertexId} in {FacingFormVertex.Form}";

                    var selectedIdx = SelectedFormVertices.IndexOf(FacingFormVertex);
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
                if (NearestFormArea != null)
                {
                    //var connectedIdsStr = string.Join(", ", NearestArea.ConnectedAreas.Select(c => $"#{c.Id}"));
                    var mesh = NavigationMesh.GetMesh(NearestFormArea.Form);
                    var NearestAreaId = mesh.FormAreas.IndexOf(NearestFormArea);
                    VisualsMessages[0] = $"Area #{NearestAreaId} in {NearestFormArea.Form}";
                }

                if (CachedFormArea != null)
                {
                    var mesh = NavigationMesh.GetMesh(CachedFormArea.Form);
                    var cachedAreaId = mesh.FormAreas.IndexOf(CachedFormArea);
                    VisualsMessages[1] = $"Cached area #{cachedAreaId} in {CachedFormArea.Form}";

                    if (NearestFormArea != null)
                    {
                        if (NearestFormArea.ConnectedFormAreas.Contains(CachedFormArea) && CachedFormArea.ConnectedFormAreas.Contains(NearestFormArea))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (NearestFormArea.ConnectedFormAreas.Contains(CachedFormArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (CachedFormArea.ConnectedFormAreas.Contains(NearestFormArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected from)";
                        }
                    }
                }

                if (FacingFormArea != null)
                {
                    var mesh = NavigationMesh.GetMesh(FacingFormArea.Form);
                    var facingAreaId = mesh.FormAreas.IndexOf(FacingFormArea);
                    VisualsMessages[1] = $"Facing area #{facingAreaId} in {FacingFormArea.Form}";

                    if (NearestFormArea != null)
                    {
                        if (NearestFormArea.ConnectedFormAreas.Contains(FacingFormArea) && FacingFormArea.ConnectedFormAreas.Contains(NearestFormArea))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (NearestFormArea.ConnectedFormAreas.Contains(FacingFormArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (FacingFormArea.ConnectedFormAreas.Contains(NearestFormArea))
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

                    if (!NavigationMesh.VerticesByRoomOrConnector.Values.Any(l => l.Values.Contains(vertexVisual.Key)) || vertexPosChanged)
                    {
                        NetworkServer.Destroy(vertexVisual.Value.gameObject);
                        VertexVisuals.Remove(vertexVisual.Key);
                    }
                }

                foreach (var vertex in NavigationMesh.VerticesByRoomOrConnector.Values.SelectMany(l => l.Values))
                {
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
                        if (NearestFormArea?.Vertices.Contains(vertex.FormVertex) ?? false)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (SelectedFormVertices.Contains(vertex.FormVertex))
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
                    if (!NavigationMesh.AreasByRoomOrConnector.Values.Any(l => l.Contains(areaVisual.Key)))
                    {
                        NetworkServer.Destroy(areaVisual.Value.gameObject);
                        AreaVisuals.Remove(areaVisual.Key);
                    }
                }

                foreach (var area in NavigationMesh.AreasByRoomOrConnector.Values.SelectMany(l => l))
                {
                    var room = area.Transform;

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
                        if (NearestFormArea == area.FormArea)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (NearestFormArea?.ConnectedFormAreas.Contains(area.FormArea) ?? false)
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
                var enabledEdgeVisuals = EdgeVisuals.Where(p => p.Value.Visual.gameObject.activeInHierarchy);
                foreach (var (edge, (visual, area)) in enabledEdgeVisuals.Select(p => (p.Key, p.Value)).ToArray())
                {
                    var isAreaRemoved = area switch
                    {
                        Area roomArea => !NavigationMesh.AreasByRoomOrConnector[roomArea.Transform.gameObject].Contains(roomArea),
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

                foreach (var area in NavigationMesh.AreasByRoomOrConnector.Values.SelectMany(l => l))
                {
                    var room = area.Transform;

                    foreach (var roomFormEdge in area.FormArea.Edges)
                    {
                        var edge = (
                            From: NavigationMesh.VerticesByRoomOrConnector[room.gameObject][roomFormEdge.From],
                            To: NavigationMesh.VerticesByRoomOrConnector[room.gameObject][roomFormEdge.To]
                        );

                        if (!EdgeVisuals.TryGetValue(edge, out var edgeVisualArea))
                        {
                            var newEdgeVisual = UnityEngine.Object.Instantiate(this.primPrefab);
                            newEdgeVisual.gameObject.SetActive(false);

                            newEdgeVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                            newEdgeVisual.transform.position = Vector3.Lerp(room.transform.TransformPoint(edge.From.LocalPosition), room.transform.TransformPoint(edge.To.LocalPosition), 0.5f);
                            newEdgeVisual.transform.LookAt(room.transform.TransformPoint(edge.To.LocalPosition));
                            newEdgeVisual.transform.RotateAround(newEdgeVisual.transform.position, newEdgeVisual.transform.right, 90f);
                            newEdgeVisual.transform.localScale = Vector3.forward * 0.01f + Vector3.right * 0.01f;
                            newEdgeVisual.transform.localScale += Vector3.up * Vector3.Distance(room.transform.TransformPoint(edge.From.LocalPosition), room.transform.TransformPoint(edge.To.LocalPosition)) * 0.5f;
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
                            edgeVisual.NetworkMaterialColor = (NearestFormArea?.Edges.Contains(roomFormEdge) ?? false) ? Color.yellow : Color.white;
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
                    var isAreaFromRemoved = areaFrom switch
                    {
                        Area roomArea => !NavigationMesh.AreasByRoomOrConnector[roomArea.Transform.gameObject].Contains(roomArea),
                        _ => throw new NotImplementedException()
                    };

                    var containsForeignArea = areaFrom switch
                    {
                        Area roomArea => roomArea.ForeignConnectedAreas.Contains(areaTo),
                        _ => throw new NotImplementedException()
                    };

                    if (isAreaFromRemoved || (!containsForeignArea))
                    {
                        NetworkServer.Destroy(visual.gameObject);
                        ConnectionVisuals.Remove((areaFrom, areaTo));
                    }
                }

                foreach (var areaFrom in NavigationMesh.AreasByRoomOrConnector.Values.SelectMany(l => l))
                {
                    var roomFrom = areaFrom.Transform.GetComponent<RoomIdentifier>();
                    if (!roomFrom)
                    {
                        continue;
                    }

                    foreach (var (areaTo, i) in areaFrom.ForeignConnectedAreas.Select((a, i) => (a, i)))
                    {
                        var roomTo = areaTo.Transform.GetComponent<RoomIdentifier>();
                        if (!roomTo)
                        {
                            continue;
                        }

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
