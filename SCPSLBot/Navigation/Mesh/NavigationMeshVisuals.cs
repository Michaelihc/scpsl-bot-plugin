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

        public TransformVertex? NearestVertex { get; set; }
        public TransformVertex? FacingVertex { get; set; }

        public List<Vertex> SelectedLocalVertices { get; set; }

        public TransformCell? NearestCell { get; set; }
        public TransformCell? FacingCell { get; set; }
        public TransformCell? CachedCell { get; set; }

        public List<TransformCell> Path { get; } = new ();

        private Dictionary<Vertex, PrimitiveObjectToy> VertexVisuals { get; } = new();
        private Dictionary<Edge, (PrimitiveObjectToy Visual, Cell Cell)> EdgeVisuals { get; } = new();
        private Dictionary<(Cell From, Cell To), PrimitiveObjectToy> ConnectionVisuals { get; } = new();

        private Dictionary<Cell, PrimitiveObjectToy> CellVisuals { get; } = new ();

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
                UpdateCellInfo();

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
                    var form = NavigationMesh.GetForm(NearestVertex?.Transform.gameObject);
                    var mesh = NavigationMesh.GetMesh(form);
                    var nearestVertexId = mesh.Vertices.IndexOf(NearestVertex?.Local);
                    VisualsMessages[0] = $"Vertex #{nearestVertexId} in {form}";

                    var selectedIdx = SelectedLocalVertices.IndexOf(NearestVertex?.Local);
                    if (selectedIdx >= 0)
                    {
                        VisualsMessages[0] += $" <color=green>(selected #{selectedIdx})</color>";
                    }
                }

                if (FacingVertex != null)
                {
                    var form = NavigationMesh.GetForm(FacingVertex?.Transform.gameObject);
                    var mesh = NavigationMesh.GetMesh(form);
                    var facingVertexId = mesh.Vertices.IndexOf(FacingVertex?.Local);
                    VisualsMessages[1] = $"Facing vertex #{facingVertexId} in {form}";

                    var selectedIdx = SelectedLocalVertices.IndexOf(FacingVertex?.Local);
                    if (selectedIdx >= 0)
                    {
                        VisualsMessages[1] += $" <color=green>(selected #{selectedIdx})</color>";
                    }
                }
            }
        }

        public void UpdateCellInfo()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                if (NearestCell != null)
                {
                    var nearestCell = NearestCell.Value;

                    //var connectedIdsStr = string.Join(", ", NearestCell.ConnectedCells.Select(c => $"#{c.Id}"));
                    var form = NavigationMesh.GetForm(nearestCell.Transform.gameObject);
                    var mesh = NavigationMesh.GetMesh(form);
                    var NearestCellId = mesh.Cells.IndexOf(nearestCell.Local);
                    VisualsMessages[0] = $"Cell #{NearestCellId} in {form}";
                }

                if (CachedCell != null)
                {
                    var cachedCell = CachedCell.Value;

                    var form = NavigationMesh.GetForm(cachedCell.Transform.gameObject);
                    var mesh = NavigationMesh.GetMesh(form);
                    var cachedCellId = mesh.Cells.IndexOf(cachedCell.Local);
                    VisualsMessages[1] = $"Cached cell #{cachedCellId} in {form}";

                    if (NearestCell != null)
                    {
                        var nearestCell = NearestCell.Value;

                        if (nearestCell.Local.ConnectedCells.Contains(cachedCell.Local) && cachedCell.Local.ConnectedCells.Contains(nearestCell.Local))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (nearestCell.Local.ConnectedCells.Contains(cachedCell.Local))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (cachedCell.Local.ConnectedCells.Contains(nearestCell.Local))
                        {
                            VisualsMessages[1] += $" <color=green>(connected from)";
                        }
                    }
                }

                if (FacingCell != null)
                {
                    var facingCell = FacingCell.Value;

                    var form = NavigationMesh.GetForm(facingCell.Transform.gameObject);
                    var mesh = NavigationMesh.GetMesh(form);
                    var facingCellId = mesh.Cells.IndexOf(facingCell.Local);
                    VisualsMessages[1] = $"Facing cell #{facingCellId} in {form}";

                    if (NearestCell != null)
                    {
                        var nearestCell = NearestCell.Value;

                        if (nearestCell.Local.ConnectedCells.Contains(facingCell.Local) && facingCell.Local.ConnectedCells.Contains(nearestCell.Local))
                        {
                            VisualsMessages[1] += $" <color=green>(bi-connected)";
                        }
                        else if (nearestCell.Local.ConnectedCells.Contains(facingCell.Local))
                        {
                            VisualsMessages[1] += $" <color=green>(connected to)";
                        }
                        else if (facingCell.Local.ConnectedCells.Contains(nearestCell.Local))
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

                    if (!NavigationMesh.LocalMeshesByRoom.Values.Distinct().Any(m => m.Vertices.Contains(vertexVisual.Key)) || vertexPosChanged)
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
                        if (NearestCell?.Local.Vertices.Contains(localVertex) ?? false)
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

        public void UpdateCellVisuals()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                foreach (var cellVisual in CellVisuals.Where(p => p.Value.gameObject.activeInHierarchy).ToArray())
                {
                    if (!NavigationMesh.LocalMeshesByRoom.Values.Any(m => m.Cells.Contains(cellVisual.Key)))
                    {
                        NetworkServer.Destroy(cellVisual.Value.gameObject);
                        CellVisuals.Remove(cellVisual.Key);
                    }
                }

                foreach (var (room, localCell) in NavigationMesh.LocalMeshesByRoom.SelectMany(p => p.Value.Cells.Select(a => ((p.Key, a)))))
                {
                    if (!CellVisuals.TryGetValue(localCell, out var visual))
                    {
                        visual = UnityEngine.Object.Instantiate(this.primPrefab);
                        visual.gameObject.SetActive(false);

                        visual.NetworkPrimitiveType = PrimitiveType.Quad;

                        visual.transform.RotateAround(visual.transform.position, visual.transform.right, 90f);
                        visual.transform.localScale = Vector3.one * .25f;
                        visual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                        // NetworkServer.Spawn(visual.gameObject);

                        CellVisuals.Add(localCell, visual);
                    }

                    visual.transform.position = room.transform.TransformPoint(localCell.CenterPosition);

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
                        if (NearestCell?.Local == localCell)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else if (NearestCell?.Local.ConnectedCells.Contains(localCell) ?? false)
                        {
                            visual.NetworkMaterialColor = Color.yellow;
                        }
                        else
                        {
                            visual.NetworkMaterialColor = Color.white;
                        }
                    }
                }

                foreach (var cell in Path)
                {
                    var cellVisual = CellVisuals[cell.Local];
                    cellVisual.NetworkMaterialColor = Color.blue;
                }
            }
            else
            {
                foreach (var cellVisual in CellVisuals.Values)
                {
                    NetworkServer.Destroy(cellVisual.gameObject);
                }
                CellVisuals.Clear();
            }
        }

        public void UpdateEdgeVisuals()
        {
            if (PlayerEnabledVisualsFor != null)
            {
                var enabledEdgeVisuals = EdgeVisuals.Where(p => p.Value.Visual.gameObject.activeInHierarchy);
                foreach (var (edge, (visual, cell)) in enabledEdgeVisuals.Select(p => (p.Key, p.Value)).ToArray())
                {
                    var isCellRemoved = !NavigationMesh.LocalMeshesByRoom.Values.Distinct().Any(m => m.Cells.Contains(cell));

                    Vector3 currentEdgeCenter() => Vector3.Lerp(edge.From.Position, edge.To.Position, 0.5f);
                    bool isEdgeCenterChanged() => currentEdgeCenter() != visual.transform.position;

                    if (isCellRemoved || !cell.ContainsEdge(new(edge.From, edge.To)) || isEdgeCenterChanged())
                    {
                        NetworkServer.Destroy(visual.gameObject);
                        EdgeVisuals.Remove(edge);
                    }
                }

                foreach (var (room, localCell) in NavigationMesh.LocalMeshesByRoom.SelectMany(p => p.Value.Cells.Select(a => ((p.Key, a)))))
                {
                    foreach (var localEdge in localCell.Edges)
                    {
                        if (!EdgeVisuals.TryGetValue(localEdge, out var edgeVisualCell))
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

                            edgeVisualCell = (newEdgeVisual, localCell);
                            EdgeVisuals.Add(localEdge, edgeVisualCell);
                        }

                        var (edgeVisual, _) = edgeVisualCell;

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
                            edgeVisual.NetworkMaterialColor = (NearestCell?.Local.Edges.Contains(localEdge) ?? false) ? Color.yellow : Color.white;
                        }
                    }
                }

                if (Path.Count >= 2)
                {
                    var pathEnumerator = Path.GetEnumerator();

                    pathEnumerator.MoveNext();
                    var nextCell = pathEnumerator.Current;

                    while (pathEnumerator.MoveNext())
                    {
                        var cell = nextCell;
                        nextCell = pathEnumerator.Current;

                        if (!cell.ConnectedCellEdges.TryGetValue(nextCell, out var connectedEdge))
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
                foreach (var (edgeVisual, cell) in EdgeVisuals.Values)
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
                foreach (var ((cellFrom, cellTo), visual) in ConnectionVisuals.Select(p => (p.Key, p.Value)).ToArray())
                {
                    var isCellFromRemoved = !NavigationMesh.LocalMeshesByRoom.Values.Distinct().Any(m => m.Cells.Contains(cellFrom));

                    //var containsForeignCell = cellFrom switch
                    //{
                    //    Cell roomCell => roomCell.ForeignConnectedCells.Contains(cellTo),
                    //    _ => throw new NotImplementedException()
                    //};

                    //if (isCellFromRemoved || (!containsForeignCell))
                    if (isCellFromRemoved)
                    {
                        NetworkServer.Destroy(visual.gameObject);
                        ConnectionVisuals.Remove((cellFrom, cellTo));
                    }
                }

                //foreach (var (room, cellFrom) in NavigationMesh.CellsByRoom.SelectMany(p => p.Value.Select(a => ((p.Key, a)))))
                //{
                //    var roomFrom = room;
                //    if (!roomFrom)
                //    {
                //        continue;
                //    }

                //    foreach (var (cellTo, i) in cellFrom.ForeignConnectedCells.Select((a, i) => (a, i)))
                //    {
                //        var roomTo = cellTo.Transform.GetComponent<RoomIdentifier>();
                //        if (!roomTo)
                //        {
                //            continue;
                //        }

                //        if (!ConnectionVisuals.TryGetValue((cellFrom, cellTo), out var connectionVisual))
                //        {
                //            var newConnectionVisual = UnityEngine.Object.Instantiate(this.primPrefab);
                //            newConnectionVisual.NetworkPrimitiveFlags &= ~PrimitiveFlags.Collidable;

                //            if (cellTo.ConnectedCellEdges.TryGetValue(cellFrom, out var fromCellEdge)
                //                && cellFrom.ConnectedCellEdges.TryGetValue(cellTo, out var toCellEdge))
                //            {
                //                // Adjacent rooms connection
                //                var fromCellEdgePos = Vector3.Lerp(fromCellEdge.From.Position, fromCellEdge.To.Position, .5f);
                //                var toCellEdgePos = Vector3.Lerp(toCellEdge.From.Position, toCellEdge.To.Position, .5f);

                //                newConnectionVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                //                newConnectionVisual.transform.position = Vector3.Lerp(fromCellEdgePos, toCellEdgePos, 0.5f);
                //                newConnectionVisual.transform.LookAt(toCellEdgePos);
                //                newConnectionVisual.transform.RotateAround(newConnectionVisual.transform.position, newConnectionVisual.transform.right, 90f);
                //                newConnectionVisual.transform.localScale = Vector3.forward * 0.01f + Vector3.right * 0.01f;
                //                newConnectionVisual.transform.localScale += Vector3.up * Vector3.Distance(fromCellEdgePos, toCellEdgePos) * 0.5f;
                //            }
                //            else
                //            {
                //                // Elevator/warping connection
                //                var fromCellCenterPosition = cellFrom.CenterPosition;

                //                newConnectionVisual.NetworkPrimitiveType = PrimitiveType.Cylinder;
                //                newConnectionVisual.transform.position = fromCellCenterPosition;
                //                newConnectionVisual.transform.localScale *= 0.01f;
                //            }

                //            NetworkServer.Spawn(newConnectionVisual.gameObject);

                //            connectionVisual = newConnectionVisual;
                //            ConnectionVisuals.Add((cellFrom, cellTo), connectionVisual);
                //        }

                //        //connectionVisual.NetworkMaterialColor = (NearestCell?.Edges.Contains(edge) ?? false) ? Color.yellow : Color.white;

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
