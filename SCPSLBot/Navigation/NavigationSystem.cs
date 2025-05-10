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

                    var edgeInFront = NavigationMesh.GetNearestEdge(doorCenterPosition, door.Rooms[0]);
                    var edgeInBack = NavigationMesh.GetNearestEdge(doorCenterPosition, door.Rooms[1]);

                    if (edgeInFront != null && edgeInBack != null)
                    {
                        // Connect
                        var cellInFront = NavigationMesh.LocalMeshesByRoom[door.Rooms[0].gameObject].Cells
                            .Select(lc => new TransformCell(lc, door.Rooms[0].transform))
                            .First(c => c.Local.Edges.Any(e => e == edgeInFront.Value.Local));

                        var cellInBack = NavigationMesh.LocalMeshesByRoom[door.Rooms[1].gameObject].Cells
                            .Select(lc => new TransformCell(lc, door.Rooms[1].transform))
                            .First(c => c.Local.Edges.Any(e => e == edgeInBack.Value.Local));

                        NavigationMesh.ForeignConnectedCells[cellInFront].Add(cellInBack);
                        NavigationMesh.ForeignConnectedCellEdges[cellInFront].Add(cellInBack, edgeInBack.Value);

                        NavigationMesh.ForeignConnectedCells[cellInBack].Add(cellInFront);
                        NavigationMesh.ForeignConnectedCellEdges[cellInBack].Add(cellInFront, edgeInFront.Value);
                    }
                }
            }

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

                var doorTransform = elevatorDoors[0].transform;
                var doorPosition = doorTransform.position + Vector3.up;
                var doorForward = doorTransform.forward;

                if (!RoomUtils.TryGetRoom(doorPosition - doorForward, out var room))
                {
                    RoomUtils.TryGetRoom(doorPosition + doorForward, out room);
                    RoomIdentifier.RoomsByCoords.Add(RoomUtils.PositionToCoords(doorPosition - doorForward), room);
                }

                var cellAt0InShaft = NavigationMesh.GetCellWithin(doorPosition - doorForward);

                doorTransform = elevatorDoors[1].transform;
                doorPosition = doorTransform.position + Vector3.up;
                doorForward = doorTransform.forward;

                if (!RoomUtils.TryGetRoom(doorPosition - doorForward, out room))
                {
                    RoomUtils.TryGetRoom(doorPosition + doorForward, out room);
                    RoomIdentifier.RoomsByCoords.Add(RoomUtils.PositionToCoords(doorPosition - doorForward), room);
                }

                var cellAt1InShaft = NavigationMesh.GetCellWithin(doorPosition - doorForward);

                if (cellAt0InShaft != null && cellAt1InShaft != null)
                {
                    // Connect
                    NavigationMesh.ForeignConnectedCells[cellAt0InShaft.Value].Add(cellAt1InShaft.Value);
                    NavigationMesh.ForeignConnectedCells[cellAt1InShaft.Value].Add(cellAt0InShaft.Value);
                }
            }
            Debug.Log($"Connecting cells finished.");
        }

        public void LoadMeshes(string fileName)
        {
            var path = Path.Combine(BaseDir, fileName);

            if (!File.Exists(path))
            {
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

        #region Private constructor
        private NavigationSystem()
        { }
        #endregion
    }
}
