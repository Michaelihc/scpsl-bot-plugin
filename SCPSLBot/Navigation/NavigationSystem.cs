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
using System.Text;
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

            Debug.Log($"Connecting cells between rooms and adding cells with door obstacles.");
            var stringBuilder = new StringBuilder();
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

                        // Add to cells with obstacle
                        Span<TransformCell> cells = [cellInFront, cellInBack];
                        foreach (var cell in cells)
                        {
                            NavigationMesh.CellsWithObstacles.Add(cell);
                        }
                    }
                }
                else
                {
                    const float PlayerRadius = .5f;
                    var cellResult = NavigationMesh.GetCellWithinOrClosest(door.transform.position + Vector3.up, PlayerRadius);
                    if (!cellResult.HasValue)
                    {
                        var doorRoomNames = stringBuilder.Clear().AppendJoin(", ", door.Rooms.Select(r => r.gameObject.name));
                        Debug.LogWarning($"Could not find nav cell for door {door} at ({doorRoomNames}) to create obstacle belief with");
                        continue;
                    }

                    var cell = cellResult.Value;
                    NavigationMesh.CellsWithObstacles.Add(cell);
                }
            }

            Debug.Log($"Connecting cells between elevator destinations and adding elevation and level cells.");
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

                for (int i = 1; i <= 3; i++)
                {
                    var doorForwardX = doorForward * i;

                    if (!RoomUtils.TryGetRoom(doorPosition - doorForwardX, out var room))
                    {
                        RoomUtils.TryGetRoom(doorPosition + doorForward, out room);
                        RoomIdentifier.RoomsByCoords.Add(RoomUtils.PositionToCoords(doorPosition - doorForwardX), room);
                        break;
                    }
                }

                var cellAt0InShaftResult = NavigationMesh.GetCellWithinOrClosest(doorPosition - doorForward);

                doorTransform = elevatorDoors[1].transform;
                doorPosition = doorTransform.position + Vector3.up;
                doorForward = doorTransform.forward;

                for (int i = 1; i <= 3; i++)
                {
                    var doorForwardX = doorForward * i;

                    if (!RoomUtils.TryGetRoom(doorPosition - doorForwardX, out var room))
                    {
                        RoomUtils.TryGetRoom(doorPosition + doorForward, out room);
                        RoomIdentifier.RoomsByCoords.Add(RoomUtils.PositionToCoords(doorPosition - doorForwardX), room);
                        break;
                    }
                }

                var cellAt1InShaftResult = NavigationMesh.GetCellWithinOrClosest(doorPosition - doorForward);

                if (cellAt0InShaftResult.HasValue && cellAt1InShaftResult.HasValue)
                {
                    var cellAt0InShaft = cellAt0InShaftResult.Value;
                    var cellAt1InShaft = cellAt1InShaftResult.Value;

                    // Connect
                    NavigationMesh.ForeignConnectedCells[cellAt0InShaft].Add(cellAt1InShaft);
                    NavigationMesh.ForeignConnectedCells[cellAt1InShaft].Add(cellAt0InShaft);

                    // Add elevation cells
                    NavigationMesh.ElevationCells.Add((cellAt0InShaft, cellAt1InShaft));

                    // Add level cells
                    int cell0Level = Mathf.FloorToInt(cellAt0InShaft.MeanPosition.y / NavigationMesh.LevelScale);
                    int cell1Level = Mathf.FloorToInt(cellAt1InShaft.MeanPosition.y / NavigationMesh.LevelScale);

                    if (Mathf.Abs(cell1Level - cell0Level) <= NavigationMesh.NumLevels)
                    {
                        continue;
                    }

                    var numLevelsBelow = NavigationMesh.NumLevelsBelow;
                    var numLevelsAbove = NavigationMesh.NumLevelsAbove;
                    var fromToCells = new[] {
                        (cell1Level, cell0Level, cellAt1InShaft, cellAt0InShaft),
                        (cell0Level, cell1Level, cellAt0InShaft, cellAt1InShaft)
                    };
                    foreach (var (fromLevel, toLevel, fromCell, toCell) in fromToCells)
                    {
                        if (!NavigationMesh.ExitEntryCellsByLevelFrom.TryGetValue(fromLevel, out var entryCellsTo))
                        {
                            entryCellsTo = [];
                            var lowestLevel = fromLevel - numLevelsBelow;
                            var highestLevel = fromLevel + numLevelsAbove;
                            for (var i = lowestLevel; i <= highestLevel; i++)
                            {
                                NavigationMesh.ExitEntryCellsByLevelFrom.Add(i, entryCellsTo);
                            }
                        }

                        if (!entryCellsTo.TryGetValue(toLevel, out var entryCells))
                        {
                            entryCells = [];
                            var lowestLevel = toLevel - numLevelsBelow;
                            var highestLevel = toLevel + numLevelsAbove;
                            for (var i = lowestLevel; i <= highestLevel; i++)
                            {
                                entryCellsTo.Add(i, entryCells);
                            }
                        }

                        entryCells.Add((fromCell, toCell));
                    }
                }
            }
            Debug.Log($"Connecting cells and adding cells with obstacles and level cells finished.");
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
