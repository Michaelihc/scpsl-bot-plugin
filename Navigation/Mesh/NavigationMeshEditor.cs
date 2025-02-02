using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using MEC;
using PluginAPI.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.Navigation.Mesh
{
    internal class NavigationMeshEditor
    {
        public static NavigationMeshEditor Instance { get; } = new();

        public bool IsEditing { get; set; }
        public Player PlayerEditing { get; set; }

        private NavigationMeshVisuals Visuals { get; } = new();

        private Player LastPlayerEditing { get; set; }

        private Area CachedArea { get; set; }

        private List<Vertex> SelectedVertices { get; } = new();
        private bool AutoSelectModeEnabled = false;

        public void Init()
        {
            Visuals.SelectedVertices = SelectedVertices;
            Visuals.Init();

            Timing.RunCoroutine(RunEachFrame(UpdateEditing));
            Timing.RunCoroutine(RunEachFrame(UpdateMeshEventLogging));

            Timing.RunCoroutine(RunEachFrame(UpdateNearestVertex));
            Timing.RunCoroutine(RunEachFrame(UpdateFacingVertex));
            Timing.RunCoroutine(RunEachFrame(UpdateVertexAutoSelect));

            Timing.RunCoroutine(RunEachFrame(UpdateNearestArea));
            Timing.RunCoroutine(RunEachFrame(UpdateCachedArea));
            Timing.RunCoroutine(RunEachFrame(UpdateFacingArea));

            Timing.RunCoroutine(RunEachFrame(Visuals.UpdateBroadcastMessage));

            Timing.RunCoroutine(RunEachFrame(Visuals.UpdateVertexVisuals));
            Timing.RunCoroutine(RunEachFrame(Visuals.UpdateAreaVisuals));
            Timing.RunCoroutine(RunEachFrame(Visuals.UpdateEdgeVisuals));
            Timing.RunCoroutine(RunEachFrame(Visuals.UpdateConnectionVisuals));

        }

        public Vertex FindClosestVertexFacingAt(GameObject roomOrConnector, Vector3 localPosition, Vector3 localDirection)
        {
            var vertices = NavigationMesh.VerticesByRoomOrConnector[roomOrConnector].Values;

            var targetVertex = vertices
                .Select(v => (n: v, d: Vector3.SqrMagnitude(v.LocalVertex.LocalPosition - localPosition)))
                .Where(t => t.d < 50f && t.d > 1f)
                .OrderBy(t => t.d)
                .Select(t => t.n)
                .FirstOrDefault(a => Vector3.Dot(Vector3.Normalize(a.LocalVertex.LocalPosition - localPosition), localDirection) > 0.999848f);

            return targetVertex;
        }

        public LocalArea FindClosestRoomAreaByCenter(Vector3 position, float radius = 1f)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                return null;
            }

            var roomForm = NavigationMesh.GetRoomForm(room.gameObject.name);
            if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out var mesh))
            {
                return null;
            }

            var radiusSqr = Mathf.Pow(radius, 2);
            var localPosition = room.transform.InverseTransformPoint(position);

            var areasWithinRadius = mesh.LocalAreas.Select(area => (area, distSqr: Vector3.SqrMagnitude(area.LocalCenterPosition - localPosition)))
                .Where(t => t.distSqr < radiusSqr);

            if (!areasWithinRadius.Any())
            {
                return null;
            }

            return areasWithinRadius
                .Aggregate((a, c) => c.distSqr < a.distSqr ? c : a)
                .area;
        }

        public Area FindClosestAreaFacingAt(GameObject roomOrConnector, Vector3 localPosition, Vector3 localDirection)
        {
            var areas = NavigationMesh.AreasByRoomOrConnector[roomOrConnector];

            var targetArea = areas
                .Select(a => (n: a, d: Vector3.SqrMagnitude(a.LocalArea.LocalCenterPosition - localPosition)))
                .Where(t => t.d < 50f && t.d > 1f)
                .OrderBy(t => t.d)
                .Select(t => t.n)
                .FirstOrDefault(a => Vector3.Dot(Vector3.Normalize(a.LocalArea.LocalCenterPosition - localPosition), localDirection) > 0.999848f);

            return targetArea;
        }

        public LocalVertex CreateVertex(Vector3 position, bool createConnector = false)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                return null;
            }

            NavigationMesh mesh;
            Transform transform;
            if (createConnector)
            {
                var connector = GetClosestConnector(position, out _, out _, room);
                if (!connector)
                {
                    return null;
                }

                var connectorForm = NavigationMesh.GetForm(connector);

                if (!NavigationMesh.MeshesByConnectorForm.TryGetValue(connectorForm, out mesh))
                {
                    mesh = NavigationMesh.Create(connectorForm);
                    NavigationMesh.MeshesByConnectorForm.Add(connectorForm, mesh);

                    AddLoggingHandlers(mesh, connectorForm);
                }

                transform = connector.transform;
            }
            else
            {
                var roomForm = NavigationMesh.GetRoomForm(room.gameObject.name);

                if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out mesh))
                {
                    mesh = NavigationMesh.Create(roomForm);
                    NavigationMesh.MeshesByRoomForm.Add(roomForm, mesh);

                    AddLoggingHandlers(mesh, roomForm);
                }

                transform = room.transform;
            }

            var localPosition = transform.InverseTransformPoint(position);
            if (SelectedVertices.Count == 2)
            {
                localPosition = GetProjectedPosition(localPosition);
            }

            var newFormVertex = mesh.AddVertex(localPosition);
            return newFormVertex;
        }

        public bool DeleteNearestVertex()
        {
            var vertex = Visuals.NearestVertex;
            if (vertex == null)
            {
                Log.Warning($"No vertex found nearby to remove.");

                return false;
            }

            SelectedVertices.Remove(vertex);

            var formVertex = vertex.LocalVertex;
            var form = NavigationMesh.GetForm(formVertex);
            var mesh = NavigationMesh.GetMesh(form);
            if (!mesh.DeleteVertex(formVertex))
            {
                Log.Warning($"No vertices at {form} to remove vertex from.");
                return false;
            }

            foreach (var area in mesh.LocalAreas.ToArray())
            {
                if (area.Vertices.Count < 3)
                {
                    mesh.RemoveArea(area);

                    Log.Warning($"Area at local center position {area.LocalCenterPosition} dissolved under {form}.");
                }
            }

            return true;
        }

        public bool MoveNearestVertex(Vector3 newPosition)
        {
            var vertex = Visuals.NearestVertex;
            if (vertex == null)
            {
                Log.Info($"No vertex found nearby to move.");
                return false;
            }

            var newLocalPosition = vertex.Transform.InverseTransformPoint(newPosition);

            if (SelectedVertices.Count == 2)
            {
                newLocalPosition = GetProjectedPosition(newLocalPosition);
            }

            var formVertex = vertex.LocalVertex;
            var form = NavigationMesh.GetForm(formVertex);
            var mesh = NavigationMesh.GetMesh(form);
            if (!mesh.MoveVertex(formVertex, newLocalPosition))
            {
                return false;
            }

            Log.Info($"Vertex #{mesh.LocalVertices.IndexOf(formVertex)} of {form} moved to new local position {formVertex.LocalPosition}.");

            return true;
        }

        public bool AddNearestVertexToSelection()
        {
            var vertex = Visuals.NearestVertex;
            if (vertex == null)
            {
                Log.Warning($"No vertex found nearby for selection.");
                return false;
            }

            if (SelectedVertices.Any() && SelectedVertices.First().Transform != vertex.Transform)
            {
                Log.Warning($"Form of the vertex for selection is different than of first selected vertex.");
                return false;
            }

            SelectedVertices.Add(vertex);

            var formVertex = vertex.LocalVertex;
            var form = NavigationMesh.GetForm(formVertex);

            Log.Info($"Vertex at local position {formVertex.LocalPosition} added to selection under {form}.");

            return true;
        }

        public bool RemoveNearestVertexFromSelection()
        {
            var vertex = Visuals.NearestVertex;
            if (vertex == null)
            {
                Log.Warning($"No vertex found nearby to remove from selection.");
                return false;
            }

            SelectedVertices.Remove(vertex);

            var formVertex = vertex.LocalVertex;
            var form = NavigationMesh.GetForm(formVertex);

            Log.Info($"Vertex at local position {formVertex.LocalPosition} removed from selection under {form}.");

            return true;
        }

        public void ClearVertexSelection()
        {
            SelectedVertices.Clear();
        }

        public void ToggleAutoSelectingVertices(bool isEnabled)
        {
            AutoSelectModeEnabled = isEnabled;
        }

        public LocalArea MakeArea(Vector3 position, bool createConnector = false)
        {
            if (SelectedVertices.Count < 3)
            {
                Log.Warning($"Not enough vertices (min 3) selected.");
                return null;
            }

            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                return null;
            }

            NavigationMesh mesh;
            string form;
            if (createConnector)
            {
                var connector = GetClosestConnector(position, out _, out _, room);
                if (!connector)
                {
                    return null;
                }

                form = NavigationMesh.GetForm(connector);
                mesh = NavigationMesh.MeshesByConnectorForm[form];
            }
            else
            {
                form = NavigationMesh.GetForm(room.gameObject);
                mesh = NavigationMesh.MeshesByRoomForm[form];
            }
            var newArea = mesh.MakeArea(SelectedVertices.Select(v => v.LocalVertex), form);
            ConnectAdjacentAreas(newArea, form);

            Log.Info($"Area #{mesh.LocalAreas.IndexOf(newArea)} at local center position {newArea.LocalCenterPosition} added under {newArea.Form}.");

            SelectedVertices.Clear();
            AutoSelectModeEnabled = false;
            PlayerEditing.ReceiveHint($"<size=30>Vertex auto-selection is stopped on area creation.", 3f);

            return newArea;
        }

        public static GameObject GetClosestConnector(Vector3 position, out Vector3Int direction, out Vector3Int orientation, out RoomIdentifier outRoom)
        {
            outRoom = RoomIdUtils.RoomAtPositionRaycasts(position);

            return GetClosestConnector(position, out direction, out orientation, outRoom);
        }

        public static GameObject GetClosestConnector(Vector3 position, out Vector3Int direction, out Vector3Int orientation, RoomIdentifier room = null)
        {
            room ??= RoomIdUtils.RoomAtPositionRaycasts(position);

            var nearestRoom = room.ConnectedRooms.OrderBy(connectedRoom => Vector3.SqrMagnitude(connectedRoom.transform.position - position)).First();
            direction = NavigationMesh.GetDirectionToRoom(room, nearestRoom);

            var closestConnector = RoomConnector.AllConnectors.FirstOrDefault(c => c.Rooms.Contains(nearestRoom) && c.Rooms.Contains(room))?.gameObject
                ?? DoorVariant.AllDoors.FirstOrDefault(c => c.Rooms.Contains(nearestRoom) && c.Rooms.Contains(room))?.gameObject;

            orientation = NavigationMesh.GetConnectorOrientation(room, closestConnector?.transform.forward ?? default);
            return closestConnector;
        }

        public bool DissolveArea(Vector3 position)
        {
            var formArea = Visuals.NearestArea?.LocalArea;
            if (formArea == null)
            {
                Log.Warning($"No area found within to remove.");
                return false;
            }

            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                Log.Warning($"No room to dissolve area on.");
                return false;
            }

            var mesh = NavigationMesh.GetMesh(formArea.Form);

            if (!mesh.RemoveArea(formArea))
            {
                Log.Warning($"Area already does not exist in collection by {formArea.Form}.");
            }
            else
            {
                Log.Info($"Area at local center position {formArea.LocalCenterPosition} removed under {formArea.Form}.");
            }

            return true;
        }

        public bool CreateVertexOnClosestRoomEdge(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            var roomForm = NavigationMesh.GetRoomForm(room.gameObject.name);

            var localPosition = room.transform.InverseTransformPoint(position);

            if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out var mesh))
            {
                return false;
            }

            var (newVertexPos, area, edge) = mesh.LocalAreas
                .SelectMany(a => a.Edges.Select(e => (edge: (from: e.From, to: e.To), area: a)))
                .Select(t => (
                    t.edge,
                    dirTo2: (t.edge.to.LocalPosition - t.edge.from.LocalPosition),
                    dirToPoint: (localPosition - t.edge.from.LocalPosition),
                    t.area))
                .Select(t => (t.edge, t.dirTo2, dirToProj: (Vector3.Project(t.dirToPoint, t.dirTo2)), t.area))
                .Where(t => Vector3.Dot(t.dirToProj, t.dirTo2) > 0f && t.dirToProj.sqrMagnitude < t.dirTo2.sqrMagnitude)
                .Select(t => (projected: (t.dirToProj + t.edge.from.LocalPosition), t.area, t.edge))

                .OrderBy(t => Vector3.SqrMagnitude(t.projected - localPosition))
                .FirstOrDefault();

            if (area == null)
            {
                return false;
            }

            var vertex = mesh.AddVertex(newVertexPos);

            mesh.AddVertexToArea(area, vertex, edge.to);

            Log.Info($"Vertex #{mesh.LocalVertices.IndexOf(vertex)} created on edge of area #{mesh.LocalAreas.IndexOf(area)}");

            return true;
        }

        public bool SliceClosestRoomAreaEdge(Vector3 position, Vector3 direction)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            var roomForm = NavigationMesh.GetRoomForm(room.gameObject.name);

            var localPosition = room.transform.InverseTransformPoint(position);
            var localDirection = room.transform.InverseTransformDirection(direction);

            if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out var mesh))
            {
                return false;
            }

            var lookPlane = new Plane(Vector3.Cross(localDirection, Vector3.up), localPosition);

            var (newVertexPos, area, edge) = mesh.LocalAreas
                .SelectMany(a => a.Edges.Select(e => (edge: (from: e.From, to: e.To), area: a)))
                .Select(t => (
                    t.edge,
                    dirTo2: (t.edge.to.LocalPosition - t.edge.from.LocalPosition),
                    t.area))
                .Select(t => (
                    t.edge, 
                    t.dirTo2, 
                    rayTo2: new Ray(t.edge.from.LocalPosition, t.dirTo2), 
                    t.area))
                .Select(t => (
                    t.edge, 
                    t.dirTo2, 
                    t.rayTo2,
                    isHit: lookPlane.Raycast(t.rayTo2, out var distToHit),
                    distToHit,
                    t.area))
                .Where(t => t.isHit)
                .Select(t => (
                    t.edge, 
                    t.dirTo2,
                    hitPoint: t.rayTo2.GetPoint(t.distToHit),
                    t.area))
                .Select(t => (
                    t.edge, 
                    t.dirTo2,
                    t.hitPoint,
                    dirToHit: t.hitPoint - t.edge.from.LocalPosition,
                    t.area))
                .Where(t => Vector3.Dot(t.dirToHit, t.dirTo2) > 0f && t.dirToHit.sqrMagnitude < t.dirTo2.sqrMagnitude)

                .OrderBy(t => Vector3.SqrMagnitude(t.hitPoint - localPosition))
                .Select(t => (t.hitPoint, t.area, t.edge))
                .FirstOrDefault();

            if (area == null)
            {
                return false;
            }

            var vertex = mesh.AddVertex(newVertexPos);

            mesh.AddVertexToArea(area, vertex, edge.to);

            Log.Info($"Vertex #{mesh.LocalVertices.IndexOf(vertex)} created on edge of area #{mesh.LocalAreas.IndexOf(area)}");

            return true;
        }

        public bool CacheNearestArea()
        {
            CachedArea = Visuals.NearestArea;

            return CachedArea != null;
        }

        public bool TracePath(Vector3 position)
        {
            if (CachedArea == null)
            {
                return false;
            }

            var targetArea = Visuals.NearestArea;
            if (targetArea == null)
            {
                return false;
            }

            Visuals.Path.Clear();

            NavigationMesh.FindShortestPath(CachedArea, targetArea, Visuals.Path);
            if (Visuals.Path.Count == 0)
            {
                Log.Warning($"No path found.");
            }

            return true;
        }

        public bool CreateConnection()
        {
            if (CachedArea == null)
            {
                return false;
            }

            var targetArea = Visuals.NearestArea;
            if (targetArea == null)
            {
                return false;
            }

            var mesh = NavigationMesh.GetMesh(CachedArea.LocalArea.Form);

            mesh.CreateAreaConnection(CachedArea.LocalArea, targetArea.LocalArea);

            return true;
        }

        public bool DeleteConnection()
        {
            if (CachedArea == null)
            {
                return false;
            }

            var targetArea = Visuals.NearestArea;
            if (targetArea == null)
            {
                return false;
            }

            var mesh = NavigationMesh.GetMesh(CachedArea.LocalArea.Form);

            mesh.DeleteAreaConnection(CachedArea.LocalArea, targetArea.LocalArea);

            return true;
        }

        private Vector3 GetProjectedPosition(Vector3 position)
        {
            var lineSegment = (from: SelectedVertices.First(), to: SelectedVertices.Last());

            var dirTo2 = (lineSegment.to.LocalPosition - lineSegment.from.LocalPosition);
            var dirToPoint = (position - lineSegment.from.LocalPosition);
            var dirToProj = (Vector3.Project(dirToPoint, dirTo2));
            var projected = (dirToProj + lineSegment.from.LocalPosition);

            return projected;
        }

        private void ConnectAdjacentAreas(LocalArea formArea, string form)
        {
            var mesh = NavigationMesh.GetMesh(form);

            foreach (var edge in formArea.Edges)
            {
                var inversedEdge = new LocalEdge(edge.To, edge.From);

                var connectedArea = mesh.LocalAreas.Find(a => a != formArea && a.Edges.Contains(inversedEdge));
                if (connectedArea != null)
                {
                    formArea.AddConnection(connectedArea);
                    connectedArea.AddConnection(formArea);
                }
            }
        }

        private void UpdateEditing()
        {
            if (PlayerEditing != null && !PlayerEditing.ReferenceHub)
            {
                PlayerEditing = null;
            }

            if (PlayerEditing != LastPlayerEditing)
            {
                LastPlayerEditing = PlayerEditing;

                Visuals.PlayerEnabledVisualsFor = PlayerEditing;

                Log.Debug($"Visuals.PlayerEnabledVisualsFor.DisplayNickname = {Visuals.PlayerEnabledVisualsFor?.DisplayNickname}");
            }
        }

        private void UpdateMeshEventLogging()
        {
            if (PlayerEditing == LastPlayerEditing)
            {
                return;
            }

            var meshesByForm = NavigationMesh.MeshesByRoomForm.Concat(NavigationMesh.MeshesByConnectorForm);

            if (PlayerEditing != null)
            {
                foreach (var (form, mesh) in meshesByForm)
                {
                    AddLoggingHandlers(mesh, form);
                }
            }
            else
            {
                foreach (var (_, mesh) in meshesByForm)
                {
                    RemoveLoggingHandlers(mesh);
                }
            }
        }

        private readonly Dictionary<NavigationMesh, Action<LocalVertex>> vertexCreatedDelegatesByMesh = new();
        private readonly Dictionary<NavigationMesh, Action<LocalVertex>> vertexDeletedDelegatesByMesh = new();

        private void AddLoggingHandlers(NavigationMesh mesh, string form)
        {
            vertexCreatedDelegatesByMesh.Add(mesh, vertex => LogVertexCreated(vertex, form));
            mesh.LocalVertexCreated += vertexCreatedDelegatesByMesh[mesh];

            vertexDeletedDelegatesByMesh.Add(mesh, vertex => LogVertexDeleted(vertex, form));
            mesh.LocalVertexDeleted += vertexDeletedDelegatesByMesh[mesh];
        }

        private void RemoveLoggingHandlers(NavigationMesh mesh)
        {
            vertexCreatedDelegatesByMesh.Remove(mesh, out var vertexCreatedDelagate);
            mesh.LocalVertexCreated -= vertexCreatedDelagate;

            vertexDeletedDelegatesByMesh.Remove(mesh, out var vertexDeletedDelagate);
            mesh.LocalVertexDeleted -= vertexDeletedDelagate;
        }

        private void LogVertexCreated(LocalVertex formVertex, string form)
        {
            Log.Info($"Vertex #{NavigationMesh.GetMesh(form).LocalVertices.IndexOf(formVertex)} at local position {formVertex.LocalPosition} added under {form}.");
        }

        private void LogVertexDeleted(LocalVertex formVertex, string form)
        {
            Log.Info($"Vertex at local position {formVertex.LocalPosition} deleted under {form}.");
        }

        private void UpdateNearestVertex()
        {
            if (PlayerEditing != null)
            {
                Visuals.NearestVertex = NavigationMesh.GetVertexNearby(PlayerEditing.Position, .125f);
            }
        }

        private void UpdateFacingVertex()
        {
            if (PlayerEditing != null && PlayerEditing.Camera)
            {
                var room = RoomIdUtils.RoomAtPositionRaycasts(PlayerEditing.Position);
                var cameraPosition = PlayerEditing.Camera.position;
                var cameraForward = PlayerEditing.Camera.forward;

                Visuals.FacingVertex = NavigationMesh.VerticesByRoomDirectionConnectorOrientation[room.gameObject].Keys
                    .Select(t => t.Connector.transform).Prepend(room.transform)
                    .Select(transform => (
                        roomOrConnector: transform.gameObject,
                        localPosition: transform.InverseTransformPoint(cameraPosition),
                        localForward: transform.InverseTransformDirection(cameraForward)))
                    .Select(t => FindClosestVertexFacingAt(t.roomOrConnector, t.localPosition, t.localForward))
                    .FirstOrDefault();
            }
        }

        private void UpdateNearestArea()
        {
            if (PlayerEditing != null && PlayerEditing.Camera)
            {
                var playerPosition = PlayerEditing.Position;
                Visuals.NearestArea = NavigationMesh.GetAreaWithin(playerPosition);
            }
        }

        private void UpdateCachedArea()
        {
            if (PlayerEditing != null)
            {
                Visuals.CachedArea = CachedArea;
            }
        }

        private void UpdateFacingArea()
        {
            if (PlayerEditing != null)
            {
                var room = RoomIdUtils.RoomAtPositionRaycasts(PlayerEditing.Position);
                var cameraPosition = PlayerEditing.Camera.position;
                var cameraForward = PlayerEditing.Camera.forward;

                Visuals.FacingArea = NavigationMesh.AreasByRoomDirectionConnectorOrientation[room.gameObject].Keys
                    .Select(t => t.Connector.transform).Prepend(room.transform)
                    .Select(transform => (
                        roomOrConnector: transform.gameObject,
                        localPosition: transform.InverseTransformPoint(cameraPosition),
                        localForward: transform.InverseTransformDirection(cameraForward)))
                    .Select(t => FindClosestAreaFacingAt(t.roomOrConnector, t.localPosition, t.localForward))
                    .FirstOrDefault();
            }
        }

        private void UpdateVertexAutoSelect()
        {
            if (PlayerEditing != null && AutoSelectModeEnabled && Visuals.NearestVertex != null)
            {
                if (SelectedVertices.Any() && SelectedVertices.First().Transform != Visuals.NearestVertex.Transform)
                {
                    return;
                }

                if (!SelectedVertices.Contains(Visuals.NearestVertex))
                {
                    SelectedVertices.Add(Visuals.NearestVertex);
                }
                else if (SelectedVertices.Count > 1 && SelectedVertices.FirstOrDefault() == Visuals.NearestVertex)
                {
                    AutoSelectModeEnabled = false;
                    PlayerEditing.ReceiveHint($"<size=30>Vertex auto-selection is stopped on first vertex selected.", 3f);

                    Log.Info($"Vertex auto-selection stopped on first vertex selected.");
                }
            }
        }

        #region Private constructor
        private NavigationMeshEditor()
        { }
        #endregion

        private IEnumerator<float> RunEachFrame(Action action)
        {
            while (true)
            {
                action.Invoke();

                yield return Timing.WaitForOneFrame;
            }
        }

        private IEnumerator<float> RunOncePerSecond(Action action)
        {
            while (true)
            {
                action.Invoke();

                yield return Timing.WaitForSeconds(1f);
            }
        }
    }
}
