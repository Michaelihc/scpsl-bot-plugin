using AdminToys;
using Mirror;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
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

        public Vertex NearestLocalVertex { get; set; }
        public Vertex FacingLocalVertex { get; set; }

        public List<Vertex> SelectedLocalVertices { get; set; }

        public TransformArea? NearestArea { get; set; }
        public TransformArea? FacingArea { get; set; }
        public TransformArea? CachedArea { get; set; }

        public List<TransformArea> Path { get; } = new ();

        private Dictionary<Vertex, PrimitiveObjectToy> VertexVisuals { get; } = new();
        private Dictionary<Edge, (PrimitiveObjectToy Visual, Area Area)> EdgeVisuals { get; } = new();
        private Dictionary<(Area From, Area To), PrimitiveObjectToy> ConnectionVisuals { get; } = new();

        private Dictionary<Area, PrimitiveObjectToy> AreaVisuals { get; } = new ();

        private string[] VisualsMessages { get; } = new string[2];

        private string SentBroadcastMessage;

        private PrimitiveObjectToy primPrefab;

        public void Init()
        {
            EventManager.RegisterEvents(this);
        }

        public void Terminate()
        {
            EventManager.UnregisterEvents(this);
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
                if (NearestLocalVertex != null)
                {
                    var form = NavigationMesh.GetForm(NearestLocalVertex);
                    var mesh = NavigationMesh.GetMesh(form);
                    var nearestVertexId = mesh.Vertices.IndexOf(NearestLocalVertex);
                    VisualsMessages[0] = $"Vertex #{nearestVertexId} in {form}";

                    var selectedIdx = SelectedLocalVertices.IndexOf(NearestLocalVertex);
                    if (selectedIdx >= 0)
                    {
                        VisualsMessages[0] += $" <color=green>(selected #{selectedIdx})</color>";
                    }
                }

                if (FacingLocalVertex != null)
                {
                    var form = NavigationMesh.GetForm(FacingLocalVertex);
                    var mesh = NavigationMesh.GetMesh(form);
                    var facingVertexId = mesh.Vertices.IndexOf(FacingLocalVertex);
                    VisualsMessages[1] = $"Facing vertex #{facingVertexId} in {form}";

                    var selectedIdx = SelectedLocalVertices.IndexOf(FacingLocalVertex);
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
                var nearestLocalArea = NearestArea?.Local;
                var cachedLocalArea = CachedArea?.Local;
                var facingLocalArea = FacingArea?.Local;

                if (nearestLocalArea != null)
                {
                    //var connectedIdsStr = string.Join(", ", NearestArea.ConnectedAreas.Select(c => $"#{c.Id}"));
                    var form = NavigationMesh.GetForm(nearestLocalArea);
                    var mesh = NavigationMesh.GetMesh(form);
                    var NearestAreaId = mesh.Areas.IndexOf(nearestLocalArea);
                    VisualsMessages[0] = $"Area #{NearestAreaId} in {form}";
                }

                if (cachedLocalArea != null)
                {
                    var form = NavigationMesh.GetForm(cachedLocalArea);
                    var mesh = NavigationMesh.GetMesh(form);
                    var cachedAreaId = mesh.Areas.IndexOf(cachedLocalArea);
                    VisualsMessages[1] = $"Cached area #{cachedAreaId} in {form}";

                    if (nearestLocalArea != null)
                    {
                        if (nearestLocalArea.ConnectedAreas.Contains(cachedLocalArea) && cachedLocalArea.ConnectedAreas.Contains(nearestLocalArea))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (nearestLocalArea.ConnectedAreas.Contains(cachedLocalArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (cachedLocalArea.ConnectedAreas.Contains(nearestLocalArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected from)";
                        }
                    }
                }

                if (facingLocalArea != null)
                {
                    var form = NavigationMesh.GetForm(facingLocalArea);
                    var mesh = NavigationMesh.GetMesh(form);
                    var facingAreaId = mesh.Areas.IndexOf(facingLocalArea);
                    VisualsMessages[1] = $"Facing area #{facingAreaId} in {form}";

                    if (nearestLocalArea != null)
                    {
                        if (nearestLocalArea.ConnectedAreas.Contains(facingLocalArea) && facingLocalArea.ConnectedAreas.Contains(nearestLocalArea))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (nearestLocalArea.ConnectedAreas.Contains(facingLocalArea))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (facingLocalArea.ConnectedAreas.Contains(nearestLocalArea))
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

                    if (!NavigationMesh.LocalMeshesByRoom.Values.Any(m => m.Vertices.Contains(vertexVisual.Key)) || vertexPosChanged)
                    {
                        NetworkServer.Destroy(vertexVisual.Value.gameObject);
                        VertexVisuals.Remove(vertexVisual.Key);
                    }
                }

                foreach (var (room, localVertex) in NavigationMesh.LocalMeshesByRoom.SelectMany(p => p.Value.Vertices.Select(a => ((p.Key, a)))))
                {
                    if (!VertexVisuals.TryGetValue(localVertex, out var visual))
                    {
                        visual = UnityEngine.Object.Instantiate(this.primPrefab);
                        visual.gameObject.SetActive(false);

                        // NetworkServer.Spawn(visual.gameObject);

                        visual.transform.position = room.transform.TransformPoint(localVertex.Position);
                        visual.transform.localScale = Vector3.one * 0.125f;
                        visual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                        VertexVisuals.Add(localVertex, visual);
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
                        if (NearestArea?.Local.Vertices.Contains(localVertex) ?? false)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (SelectedLocalVertices.Contains(localVertex))
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
                    if (!NavigationMesh.LocalMeshesByRoom.Values.Any(m => m.Areas.Contains(areaVisual.Key)))
                    {
                        NetworkServer.Destroy(areaVisual.Value.gameObject);
                        AreaVisuals.Remove(areaVisual.Key);
                    }
                }

                foreach (var (room, localArea) in NavigationMesh.LocalMeshesByRoom.SelectMany(p => p.Value.Areas.Select(a => ((p.Key, a)))))
                {
                    if (!AreaVisuals.TryGetValue(localArea, out var visual))
                    {
                        visual = UnityEngine.Object.Instantiate(this.primPrefab);
                        visual.gameObject.SetActive(false);

                        visual.NetworkPrimitiveType = PrimitiveType.Quad;

                        visual.transform.RotateAround(visual.transform.position, visual.transform.right, 90f);
                        visual.transform.localScale = Vector3.one * .25f;
                        visual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                        // NetworkServer.Spawn(visual.gameObject);

                        AreaVisuals.Add(localArea, visual);
                    }

                    visual.transform.position = room.transform.TransformPoint(localArea.CenterPosition);

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
                        if (NearestArea?.Local == localArea)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (NearestArea?.Local.ConnectedAreas.Contains(localArea) ?? false)
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
                    var areaVisual = AreaVisuals[area.Local];
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
                        Area roomArea => !NavigationMesh.FormsByAreas.ContainsKey(roomArea),
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

                foreach (var (room, localArea) in NavigationMesh.LocalMeshesByRoom.SelectMany(p => p.Value.Areas.Select(a => ((p.Key, a)))))
                {
                    foreach (var localEdge in localArea.Edges)
                    {
                        if (!EdgeVisuals.TryGetValue(localEdge, out var edgeVisualArea))
                        {
                            var newEdgeVisual = UnityEngine.Object.Instantiate(this.primPrefab);
                            newEdgeVisual.gameObject.SetActive(false);

                            newEdgeVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                            newEdgeVisual.transform.position = Vector3.Lerp(room.transform.TransformPoint(localEdge.From.Position), room.transform.TransformPoint(localEdge.To.Position), 0.5f);
                            newEdgeVisual.transform.LookAt(room.transform.TransformPoint(localEdge.To.Position));
                            newEdgeVisual.transform.RotateAround(newEdgeVisual.transform.position, newEdgeVisual.transform.right, 90f);
                            newEdgeVisual.transform.localScale = Vector3.forward * 0.01f + Vector3.right * 0.01f;
                            newEdgeVisual.transform.localScale += Vector3.up * Vector3.Distance(room.transform.TransformPoint(localEdge.From.Position), room.transform.TransformPoint(localEdge.To.Position)) * 0.5f;
                            newEdgeVisual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                            // NetworkServer.Spawn(newEdgeVisual.gameObject);

                            edgeVisualArea = (newEdgeVisual, localArea);
                            EdgeVisuals.Add(localEdge, edgeVisualArea);
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
                            edgeVisual.NetworkMaterialColor = (NearestArea?.Local.Edges.Contains(localEdge) ?? false) ? Color.yellow : Color.white;
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

                        var (edgeVisual, _) = EdgeVisuals[new(connectedEdge.From.Local, connectedEdge.To.Local)];
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
                        Area roomArea => !NavigationMesh.FormsByAreas.ContainsKey(roomArea),
                        _ => throw new NotImplementedException()
                    };

                    //var containsForeignArea = areaFrom switch
                    //{
                    //    Area roomArea => roomArea.ForeignConnectedAreas.Contains(areaTo),
                    //    _ => throw new NotImplementedException()
                    //};

                    //if (isAreaFromRemoved || (!containsForeignArea))
                    if (isAreaFromRemoved)
                    {
                        NetworkServer.Destroy(visual.gameObject);
                        ConnectionVisuals.Remove((areaFrom, areaTo));
                    }
                }

                //foreach (var (room, areaFrom) in NavigationMesh.AreasByRoom.SelectMany(p => p.Value.Select(a => ((p.Key, a)))))
                //{
                //    var roomFrom = room;
                //    if (!roomFrom)
                //    {
                //        continue;
                //    }

                //    foreach (var (areaTo, i) in areaFrom.ForeignConnectedAreas.Select((a, i) => (a, i)))
                //    {
                //        var roomTo = areaTo.Transform.GetComponent<RoomIdentifier>();
                //        if (!roomTo)
                //        {
                //            continue;
                //        }

                //        if (!ConnectionVisuals.TryGetValue((areaFrom, areaTo), out var connectionVisual))
                //        {
                //            var newConnectionVisual = UnityEngine.Object.Instantiate(this.primPrefab);
                //            newConnectionVisual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                //            if (areaTo.ConnectedAreaEdges.TryGetValue(areaFrom, out var fromAreaEdge)
                //                && areaFrom.ConnectedAreaEdges.TryGetValue(areaTo, out var toAreaEdge))
                //            {
                //                // Adjacent rooms connection
                //                var fromAreaEdgePos = Vector3.Lerp(fromAreaEdge.From.Position, fromAreaEdge.To.Position, .5f);
                //                var toAreaEdgePos = Vector3.Lerp(toAreaEdge.From.Position, toAreaEdge.To.Position, .5f);

                //                newConnectionVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                //                newConnectionVisual.transform.position = Vector3.Lerp(fromAreaEdgePos, toAreaEdgePos, 0.5f);
                //                newConnectionVisual.transform.LookAt(toAreaEdgePos);
                //                newConnectionVisual.transform.RotateAround(newConnectionVisual.transform.position, newConnectionVisual.transform.right, 90f);
                //                newConnectionVisual.transform.localScale = Vector3.forward * 0.01f + Vector3.right * 0.01f;
                //                newConnectionVisual.transform.localScale += Vector3.up * Vector3.Distance(fromAreaEdgePos, toAreaEdgePos) * 0.5f;
                //            }
                //            else
                //            {
                //                // Elevator/warping connection
                //                var fromAreaCenterPosition = areaFrom.CenterPosition;

                //                newConnectionVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                //                newConnectionVisual.transform.position = fromAreaCenterPosition;
                //                newConnectionVisual.transform.localScale *= 0.01f;
                //            }

                //            NetworkServer.Spawn(newConnectionVisual.gameObject);

                //            connectionVisual = newConnectionVisual;
                //            ConnectionVisuals.Add((areaFrom, areaTo), connectionVisual);
                //        }

                //        //connectionVisual.NetworkMaterialColor = (NearestArea?.Edges.Contains(edge) ?? false) ? Color.yellow : Color.white;

                //    }
                //}
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
