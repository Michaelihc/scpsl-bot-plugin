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
        private Area TracingEndingArea { get; set; }

        private List<FormVertex> SeletedFormVertices { get; } = new();
        private bool AutoSelectModeEnabled = false;

        public void Init()
        {
            Visuals.SelectedRoomVertices = SeletedFormVertices;
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

        public FormVertex FindClosestVertexFacingAt(string roomForm, Vector3 localPosition, Vector3 localDirection)
        {
            if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out var mesh))
            {
                return null;
            }

            var targetVertex = mesh.FormVertices
                .Select(a => (n: a, d: Vector3.SqrMagnitude(a.LocalPosition - localPosition)))
                .Where(t => t.d < 50f && t.d > 1f)
                .OrderBy(t => t.d)
                .Select(t => t.n)
                .FirstOrDefault(a => Vector3.Dot(Vector3.Normalize(a.LocalPosition - localPosition), localDirection) > 0.999848f);

            return targetVertex;
        }

        public FormArea FindClosestAreaByCenter(Vector3 position, float radius = 1f)
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

            var areasWithinRadius = mesh.FormAreas.Select(area => (area, distSqr: Vector3.SqrMagnitude(area.LocalCenterPosition - localPosition)))
                .Where(t => t.distSqr < radiusSqr);

            if (!areasWithinRadius.Any())
            {
                return null;
            }

            return areasWithinRadius
                .Aggregate((a, c) => c.distSqr < a.distSqr ? c : a)
                .area;
        }

        public FormArea FindClosestAreaFacingAt(string roomForm, Vector3 localPosition, Vector3 localDirection)
        {
            if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out var mesh))
            {
                return null;
            }

            var targetArea = mesh.FormAreas
                .Select(a => (n: a, d: Vector3.SqrMagnitude(a.LocalCenterPosition - localPosition)))
                .Where(t => t.d < 50f && t.d > 1f)
                .OrderBy(t => t.d)
                .Select(t => t.n)
                .FirstOrDefault(a => Vector3.Dot(Vector3.Normalize(a.LocalCenterPosition - localPosition), localDirection) > 0.999848f);

            return targetArea;
        }

        public FormVertex CreateVertex(Vector3 position, bool createConnector = false)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            if (!room)
            {
                return null;
            }

            NavigationMesh mesh;
            string form;
            Transform transform;
            if (createConnector)
            {
                var connector = GetClosestConnector(position, out _, room);
                if (!connector)
                {
                    return null;
                }

                var connectorForm = NavigationMesh.GetForm(connector);

                if (!NavigationMesh.MeshesByConnectorForm.TryGetValue(connectorForm, out mesh))
                {
                    mesh = new NavigationMesh();
                    NavigationMesh.MeshesByConnectorForm.Add(connectorForm, mesh);

                    AddLoggingHandlers(mesh);
                }

                form = connectorForm;
                transform = connector.transform;
            }
            else
            {
                var roomForm = NavigationMesh.GetRoomForm(room.gameObject.name);

                if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out mesh))
                {
                    mesh = new NavigationMesh();
                    NavigationMesh.MeshesByRoomForm.Add(roomForm, mesh);

                    AddLoggingHandlers(mesh);
                }

                form = roomForm;
                transform = room.transform;
            }

            var localPosition = transform.InverseTransformPoint(position);
            if (SeletedFormVertices.Count == 2)
            {
                localPosition = GetProjectedPosition(localPosition);
            }

            var newFormVertex = mesh.AddVertex(localPosition, form);
            return newFormVertex;
        }

        public bool DeleteVertex(Vector3 position)
        {
            var formVertex = NavigationMesh.GetVertexNearby(position)?.FormVertex;
            if (formVertex == null)
            {
                Log.Warning($"No vertex found nearby to remove.");

                return false;
            }

            SeletedFormVertices.Remove(formVertex);

            var mesh = NavigationMesh.GetMesh(formVertex.Form);
            if (!mesh.DeleteVertex(formVertex))
            {
                return false;
            }

            foreach (var area in mesh.FormAreas.ToArray())
            {
                if (area.Vertices.Count < 3)
                {
                    mesh.RemoveArea(area);

                    Log.Warning($"Area at local center position {area.LocalCenterPosition} dissolved under {formVertex.Form}.");
                }
            }

            return true;
        }

        public bool MoveVertex(Vector3 position)
        {
            var vertex = NavigationMesh.GetVertexNearby(position);
            if (vertex == null)
            {
                Log.Info($"No vertex found nearby to move.");
                return false;
            }

            var newLocalPosition = vertex.Transform.InverseTransformPoint(position);

            if (SeletedFormVertices.Count == 2)
            {
                newLocalPosition = GetProjectedPosition(newLocalPosition);
            }

            var formVertex = vertex.FormVertex;

            var mesh = NavigationMesh.GetMesh(formVertex.Form);
            if (!mesh.MoveVertex(formVertex, newLocalPosition))
            {
                return false;
            }

            Log.Info($"Vertex #{mesh.FormVertices.IndexOf(formVertex)} of {formVertex.Form} moved to new local position {formVertex.LocalPosition}.");

            return true;
        }

        public bool AddVertexToSelection(Vector3 position)
        {
            var vertex = NavigationMesh.GetVertexNearby(position);
            if (vertex == null)
            {
                Log.Warning($"No vertex found nearby for selection.");
                return false;
            }

            SeletedFormVertices.Add(vertex.FormVertex);

            Log.Info($"Vertex at local position {vertex.FormVertex.LocalPosition} added to selection under {vertex.FormVertex.Form}.");

            return true;
        }

        public bool RemoveVertexFromSelection(Vector3 position)
        {
            var vertex = NavigationMesh.GetVertexNearby(position);
            if (vertex == null)
            {
                Log.Warning($"No vertex found nearby to remove from selection.");
                return false;
            }

            SeletedFormVertices.Remove(vertex.FormVertex);

            Log.Info($"Vertex at local position {vertex.FormVertex.LocalPosition} removed from selection under {vertex.FormVertex.Form}.");

            return true;
        }

        public void ClearVertexSelection()
        {
            SeletedFormVertices.Clear();
        }

        public void ToggleAutoSelectingVertices(bool isEnabled)
        {
            AutoSelectModeEnabled = isEnabled;
        }

        public FormArea MakeArea(Vector3 position, bool createConnector = false)
        {
            if (SeletedFormVertices.Count < 3)
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
                var connector = GetClosestConnector(position, out _, room);
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
            var newArea = mesh.MakeArea(SeletedFormVertices, form);
            ConnectAdjacentAreas(newArea, form);

            Log.Info($"Area #{mesh.FormAreas.IndexOf(newArea)} at local center position {newArea.LocalCenterPosition} added under {newArea.Form}.");

            SeletedFormVertices.Clear();
            AutoSelectModeEnabled = false;
            PlayerEditing.ReceiveHint($"<size=30>Vertex auto-selection is stopped on area creation.", 3f);

            return newArea;
        }

        public static GameObject GetClosestConnector(Vector3 position, out Vector3Int direction, RoomIdentifier room = null)
        {
            room ??= RoomIdUtils.RoomAtPositionRaycasts(position);

            var nearestRoom = room.ConnectedRooms.OrderBy(connectedRoom => Vector3.SqrMagnitude(connectedRoom.transform.position - position)).First();
            direction = nearestRoom.OccupiedCoords[0] - room.OccupiedCoords[0];

            var closestConnector = RoomConnector.AllConnectors.FirstOrDefault(c => c.Rooms.Contains(nearestRoom) && c.Rooms.Contains(room));
            if (closestConnector != null)
            {
                return closestConnector.gameObject;
            }

            var closestDoorConnector = DoorVariant.AllDoors.FirstOrDefault(c => c.Rooms.Contains(nearestRoom) && c.Rooms.Contains(room));
            return closestDoorConnector?.gameObject;
        }

        public bool DissolveArea(Vector3 position)
        {
            var formArea = Visuals.NearestRoomArea;
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

        public bool CreateVertexOnClosestEdge(Vector3 position)
        {
            var room = RoomIdUtils.RoomAtPositionRaycasts(position);
            var roomForm = NavigationMesh.GetRoomForm(room.gameObject.name);

            var localPosition = room.transform.InverseTransformPoint(position);

            if (!NavigationMesh.MeshesByRoomForm.TryGetValue(roomForm, out var mesh))
            {
                return false;
            }

            var (newVertexPos, area, edge) = mesh.FormAreas
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

            var vertex = mesh.AddVertex(newVertexPos, roomForm);

            mesh.AddVertexToArea(area, vertex, edge.to);

            Log.Info($"Vertex #{mesh.FormVertices.IndexOf(vertex)} created on edge of area #{mesh.FormAreas.IndexOf(area)}");

            return true;
        }

        public bool SliceClosestAreaEdge(Vector3 position, Vector3 direction)
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

            var (newVertexPos, area, edge) = mesh.FormAreas
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

            var vertex = mesh.AddVertex(newVertexPos, roomForm);

            mesh.AddVertexToArea(area, vertex, edge.to);

            Log.Info($"Vertex #{mesh.FormVertices.IndexOf(vertex)} created on edge of area #{mesh.FormAreas.IndexOf(area)}");

            return true;
        }

        public bool CacheArea(Vector3 position)
        {
            CachedArea = NavigationMesh.GetAreaWithin(position);

            return CachedArea != null;
        }

        public bool TracePath(Vector3 position)
        {
            if (CachedArea == null)
            {
                return false;
            }

            var targetArea = NavigationMesh.GetAreaWithin(position);
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

        public bool CreateConnection(Vector3 position)
        {
            if (CachedArea == null)
            {
                return false;
            }

            var targetArea = NavigationMesh.GetAreaWithin(position);
            if (targetArea == null)
            {
                return false;
            }

            var mesh = NavigationMesh.GetMesh(CachedArea.FormArea.Form);

            mesh.CreateAreaConnection(CachedArea.FormArea, targetArea.FormArea);

            return true;
        }

        public bool DeleteConnection(Vector3 position)
        {
            if (CachedArea == null)
            {
                return false;
            }

            var targetArea = NavigationMesh.GetAreaWithin(position);
            if (targetArea == null)
            {
                return false;
            }

            var mesh = NavigationMesh.GetMesh(CachedArea.FormArea.Form);

            mesh.DeleteAreaConnection(CachedArea.FormArea, targetArea.FormArea);

            return true;
        }

        private Vector3 GetProjectedPosition(Vector3 position)
        {
            var lineSegment = (from: SeletedFormVertices.First(), to: SeletedFormVertices.Last());

            var dirTo2 = (lineSegment.to.LocalPosition - lineSegment.from.LocalPosition);
            var dirToPoint = (position - lineSegment.from.LocalPosition);
            var dirToProj = (Vector3.Project(dirToPoint, dirTo2));
            var projected = (dirToProj + lineSegment.from.LocalPosition);

            return projected;
        }

        private void ConnectAdjacentAreas(FormArea formArea, string form)
        {
            var mesh = NavigationMesh.GetMesh(form);

            foreach (var edge in formArea.Edges)
            {
                var inversedEdge = new FormEdge(edge.To, edge.From);

                var connectedArea = mesh.FormAreas.Find(a => a != formArea && a.Edges.Contains(inversedEdge));
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

            if (PlayerEditing != null)
            {
                foreach (var mesh in NavigationMesh.MeshesByRoomForm.Values)
                {
                    AddLoggingHandlers(mesh);
                }
            }
            else
            {
                foreach (var mesh in NavigationMesh.MeshesByRoomForm.Values)
                {
                    RemoveLoggingHandlers(mesh);
                }
            }
        }

        private void AddLoggingHandlers(NavigationMesh mesh)
        {
            mesh.FormVertexCreated += LogVertexCreated;
            mesh.FormVertexDeleted += LogVertexDeleted;
        }

        private void RemoveLoggingHandlers(NavigationMesh mesh)
        {
            mesh.FormVertexCreated -= LogVertexCreated;
            mesh.FormVertexDeleted -= LogVertexDeleted;
        }

        private void LogVertexCreated(FormVertex formVertex)
        {
            Log.Info($"Vertex #{NavigationMesh.MeshesByRoomForm[formVertex.Form].FormVertices.IndexOf(formVertex)} at local position {formVertex.LocalPosition} added under room {formVertex.Form}.");
        }

        private void LogVertexDeleted(FormVertex formVertex)
        {
            Log.Info($"Vertex at local position {formVertex.LocalPosition} deleted under room {formVertex.Form}.");
        }

        private void UpdateNearestVertex()
        {
            if (PlayerEditing != null)
            {
                Visuals.NearestRoomVertex = NavigationMesh.GetVertexNearby(PlayerEditing.Position, .125f)?.FormVertex;
            }
        }

        private void UpdateFacingVertex()
        {
            if (PlayerEditing != null && PlayerEditing.Camera)
            {
                var room = RoomIdUtils.RoomAtPositionRaycasts(PlayerEditing.Position);

                var localPosition = room.transform.InverseTransformPoint(PlayerEditing.Camera.position);
                var localForward = room.transform.InverseTransformDirection(PlayerEditing.Camera.forward);

                Visuals.FacingRoomVertex = FindClosestVertexFacingAt(NavigationMesh.GetRoomForm(room.gameObject.name), localPosition, localForward);
            }
        }

        private void UpdateNearestArea()
        {
            if (PlayerEditing != null && PlayerEditing.Camera)
            {
                var playerPosition = PlayerEditing.Position;
                Visuals.NearestRoomArea = NavigationMesh.GetAreaWithin(playerPosition)?.FormArea ?? FindClosestAreaByCenter(playerPosition, .25f);
            }
        }

        private void UpdateCachedArea()
        {
            if (PlayerEditing != null)
            {
                Visuals.CachedRoomArea = CachedArea?.FormArea;
            }
        }

        private void UpdateFacingArea()
        {
            if (PlayerEditing != null)
            {
                var room = RoomIdUtils.RoomAtPositionRaycasts(PlayerEditing.Position);

                var localPosition = room.transform.InverseTransformPoint(PlayerEditing.Camera.position);
                var localForward = room.transform.InverseTransformDirection(PlayerEditing.Camera.forward);

                Visuals.FacingRoomArea = FindClosestAreaFacingAt(NavigationMesh.GetRoomForm(room.gameObject.name), localPosition, localForward);
            }
        }

        private void UpdateVertexAutoSelect()
        {
            if (PlayerEditing != null && AutoSelectModeEnabled && Visuals.NearestRoomVertex != null)
            {
                if (!SeletedFormVertices.Contains(Visuals.NearestRoomVertex))
                {
                    SeletedFormVertices.Add(Visuals.NearestRoomVertex);
                }
                else if (SeletedFormVertices.Count > 1 && SeletedFormVertices.FirstOrDefault() == Visuals.NearestRoomVertex)
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
