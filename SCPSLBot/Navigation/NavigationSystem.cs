using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using MEC;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SCPSLBot.Navigation
{
    internal class NavigationSystem
    {
        public static NavigationSystem Instance { get; } = new NavigationSystem();

        public string BaseDir { get; set; }
        public string MeshFileName { get; } = "navmesh.slnmf";

        public bool Initialized { get; private set; } = false;

        public void Init()
        {
            EnsureDefaultMeshFile();

            ServerEvents.MapGenerated += OnMapGenerated;
            ServerEvents.RoundRestarted += OnRoundRestarted;

            if (SeedSynchronizer.MapGenerated)
            {
                LoadConnectMeshes();
            }

            Initialized = true;
        }

        public void Terminate()
        {
            ServerEvents.MapGenerated -= OnMapGenerated;
            ServerEvents.RoundRestarted -= OnRoundRestarted;

            Initialized = false;

            NavigationMesh.ResetMeshes();
        }

        private void OnMapGenerated(MapGeneratedEventArgs args)
        {
            Timing.RunCoroutine(LoadConnectMeshesAsync());
        }

        private void OnRoundRestarted()
        {
            NavigationMesh.ResetMeshes();
        }

        private IEnumerator<float> LoadConnectMeshesAsync()
        {
            yield return Timing.WaitUntilTrue(() => SeedSynchronizer.MapGenerated);

            LoadConnectMeshes();
        }

        public void LoadConnectMeshes()
        {
            Debug.Log($"Initializing meshes.");
            NavigationMesh.InitMeshes();

            Debug.Log($"Loading meshes.");
            LoadMeshes(MeshFileName);

            Debug.Log($"Connecting cells between rooms.");
            foreach (var door in DoorVariant.AllDoors)
            {
                if (door.Rooms.Length == 2)
                {
                    var doorCenterPosition = door.transform.position + Vector3.up;  // assuming pivot point is located at the bottom of all doors
                    LinkRoomCellsAtPoint(doorCenterPosition, door.Rooms[0], door.Rooms[1]);
                }
            }

            Debug.Log($"Connecting cells across door-less connectors (open hallways / clutter).");
            ConnectDoorlessConnectors();

            Debug.Log($"Connecting cells between elevator destinations.");
            var elevatorGroups = Enum.GetValues(typeof(ElevatorGroup));
            foreach (ElevatorGroup group in elevatorGroups)
            {
                var elevatorDoors = ElevatorDoor.GetDoorsForGroup(group);
                if (elevatorDoors.Count != 2)
                {
                    Debug.LogWarning($"Irregular elevator level count ({elevatorDoors.Count}) of group {group}");
                    continue;
                }

                var cellAt0InShaft = ResolveElevatorShaftCell(elevatorDoors[0]);
                var cellAt1InShaft = ResolveElevatorShaftCell(elevatorDoors[1]);

                if (cellAt0InShaft != null && cellAt1InShaft != null)
                {
                    // Connect
                    ConnectForeignCells(cellAt0InShaft.Value, cellAt1InShaft.Value);
                    ConnectForeignCells(cellAt1InShaft.Value, cellAt0InShaft.Value);
                }
            }
            Debug.Log($"Connecting cells finished.");
        }

        // Links navmesh cells across room connectors that are NOT doors (open hallways, bulk-door
        // openings, clutter passages). Doors are already handled via DoorVariant.AllDoors; elevators
        // separately. This is what lets bots traverse the native map when the connector->standard-door
        // rewrite is disabled. Each connector sits on the boundary between two rooms; we resolve the
        // room on each side from its position and link the nearest boundary cells (same scheme as
        // doors). Runs once per map load. When ForceStandardDoorConnectors is enabled every connector
        // is a door, so this finds nothing to link and is a no-op.
        private static void ConnectDoorlessConnectors()
        {
            var connectors = UnityEngine.Object.FindObjectsByType<global::MapGeneration.RoomConnectors.SpawnableRoomConnector>(FindObjectsSortMode.None);
            foreach (var connector in connectors)
            {
                if (!connector)
                {
                    continue;
                }

                // Doors / elevator doors are connected through their own passes.
                if (connector.GetComponentInChildren<DoorVariant>() != null)
                {
                    continue;
                }

                var transform = connector.transform;
                var center = transform.position + Vector3.up;
                var forward = transform.forward;

                if (!TryGetConnectorSideRoom(center, forward, out var roomA)
                    || !TryGetConnectorSideRoom(center, -forward, out var roomB)
                    || roomA == roomB)
                {
                    continue;
                }

                LinkRoomCellsAtPoint(center, roomA, roomB);
            }
        }

        private static bool TryGetConnectorSideRoom(Vector3 center, Vector3 direction, out RoomIdentifier room)
        {
            for (var distance = 1.5f; distance <= 4.5f; distance += 1.5f)
            {
                if (RoomUtils.TryGetRoom(center + direction * distance, out room)
                    && room != null
                    && NavigationMesh.LocalMeshesByRoom.ContainsKey(room.gameObject))
                {
                    return true;
                }
            }

            room = null;
            return false;
        }

        // Connects the nearest boundary cells of two rooms at a shared passage point, in both
        // directions. Safe against missing meshes / no matching cell / duplicate links.
        private static void LinkRoomCellsAtPoint(Vector3 point, RoomIdentifier roomA, RoomIdentifier roomB)
        {
            if (roomA == null || roomB == null || roomA == roomB)
            {
                return;
            }

            if (!NavigationMesh.LocalMeshesByRoom.TryGetValue(roomA.gameObject, out var meshA)
                || !NavigationMesh.LocalMeshesByRoom.TryGetValue(roomB.gameObject, out var meshB))
            {
                return;
            }

            var edgeA = NavigationMesh.GetNearestEdge(point, roomA);
            var edgeB = NavigationMesh.GetNearestEdge(point, roomB);
            if (!edgeA.HasValue || !edgeB.HasValue)
            {
                return;
            }

            var cellA = meshA.Cells
                .Where(lc => lc.Edges.Any(e => e == edgeA.Value.Local))
                .Select(lc => (TransformCell?)new TransformCell(lc, roomA.transform))
                .FirstOrDefault();
            var cellB = meshB.Cells
                .Where(lc => lc.Edges.Any(e => e == edgeB.Value.Local))
                .Select(lc => (TransformCell?)new TransformCell(lc, roomB.transform))
                .FirstOrDefault();
            if (!cellA.HasValue || !cellB.HasValue)
            {
                return;
            }

            ConnectForeignCells(cellA.Value, cellB.Value);
            ConnectForeignCellEdge(cellA.Value, cellB.Value, edgeB.Value);

            ConnectForeignCells(cellB.Value, cellA.Value);
            ConnectForeignCellEdge(cellB.Value, cellA.Value, edgeA.Value);
        }

        private static void ConnectForeignCellEdge(TransformCell from, TransformCell to, TransformEdge edge)
        {
            if (!NavigationMesh.ForeignConnectedCellEdges.TryGetValue(from, out var edges))
            {
                edges = new Dictionary<TransformCell, TransformEdge>();
                NavigationMesh.ForeignConnectedCellEdges[from] = edges;
            }

            edges[to] = edge;
        }

        // Resolves the navmesh cell at an elevator landing WITHOUT mutating native
        // RoomIdentifier.RoomsByCoords. The previous probe-loop registered a fake coord->room
        // entry there, which threw on a second nav load (duplicate key) and left a destroyed-room
        // reference in native state across rounds. This is behavior-equivalent: if the shaft-side
        // position maps to a room, resolve normally; otherwise resolve the landing cell against the
        // door's far-side room mesh directly.
        private static TransformCell? ResolveElevatorShaftCell(ElevatorDoor door)
        {
            if (door == null)
            {
                return null;
            }

            var doorTransform = door.transform;
            var doorPosition = doorTransform.position + Vector3.up;
            var doorForward = doorTransform.forward;
            var probePosition = doorPosition - doorForward;

            if (RoomUtils.TryGetRoom(probePosition, out _))
            {
                return NavigationMesh.GetCellWithin(probePosition);
            }

            if (!RoomUtils.TryGetRoom(doorPosition + doorForward, out var fallbackRoom) || fallbackRoom == null)
            {
                return null;
            }

            return NavigationMesh.GetRoomCellWithin(probePosition, fallbackRoom);
        }

        private static void ConnectForeignCells(TransformCell from, TransformCell to)
        {
            if (!NavigationMesh.ForeignConnectedCells.TryGetValue(from, out var connected))
            {
                connected = new List<TransformCell>();
                NavigationMesh.ForeignConnectedCells[from] = connected;
            }

            if (!connected.Contains(to))
            {
                connected.Add(to);
            }
        }

        public void LoadMeshes(string fileName)
        {
            var path = Path.Combine(BaseDir, fileName);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"Navigation mesh file not found at {path}.");
                return;
            }

            using var fileStream = File.OpenRead(path);
            using var binaryReader = new BinaryReader(fileStream);

            NavigationMesh.ReadMeshes(binaryReader);
        }

        public void SaveMeshes(string fileName)
        {
            var path = Path.Combine(BaseDir, fileName);

            using var fileStream = File.Open(path, FileMode.Create, FileAccess.Write);
            using var binaryWriter = new BinaryWriter(fileStream);

            NavigationMesh.WriteMeshes(binaryWriter);
        }

        private void EnsureDefaultMeshFile()
        {
            if (string.IsNullOrWhiteSpace(BaseDir))
            {
                return;
            }

            var path = Path.Combine(BaseDir, MeshFileName);
            if (File.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(BaseDir);

            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = assembly.GetManifestResourceStream("SCPSLBot.Assets.navmesh.slnmf");
            if (resourceStream == null)
            {
                Debug.LogWarning("Embedded default navigation mesh was not found.");
                return;
            }

            using var fileStream = File.Create(path);
            resourceStream.CopyTo(fileStream);
            Debug.Log($"Installed default navigation mesh to {path}.");
        }

        #region Private constructor
        private NavigationSystem()
        { }
        #endregion
    }
}
