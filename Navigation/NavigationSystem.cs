using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using MEC;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Events;
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

        public void Init()
        {
            EventManager.RegisterEvents(this);
        }

        [PluginEvent(PluginAPI.Enums.ServerEventType.MapGenerated)]
        public void OnMapGenerated()
        {
            Timing.RunCoroutine(ConnectForeignAreasAsync());
        }

        private IEnumerator<float> ConnectForeignAreasAsync()
        {
            yield return Timing.WaitUntilTrue(() => SeedSynchronizer.MapGenerated);

            ConnectForeignAreas();
        }

        private void ConnectForeignAreas()
        {
            Log.Info($"Initializing meshes.");
            NavigationMesh.InitMeshes();

            Log.Info($"Loading meshes.");
            LoadMeshes();

            Log.Info($"Connecting areas between elevator destinations.");
            var elevatorGroups = Enum.GetValues(typeof(ElevatorGroup));
            foreach (ElevatorGroup group in elevatorGroups)
            {
                var elevatorDoors = ElevatorDoor.GetDoorsForGroup(group);
                if (elevatorDoors.Count != 2)
                {
                    Log.Warning($"Irregular elevator level count ({elevatorDoors.Count}) of group {group}");
                    continue;
                }

                var doorTransform = elevatorDoors[0].transform;
                var doorPosition = doorTransform.position;
                var doorForward = doorTransform.forward;
                var areaAt0InShaft = NavigationMesh.GetAreaWithin(doorPosition - doorForward + Vector3.up);

                doorTransform = elevatorDoors[1].transform;
                doorPosition = doorTransform.position;
                doorForward = doorTransform.forward;
                var areaAt1InShaft = NavigationMesh.GetAreaWithin(doorPosition - doorForward + Vector3.up);

                if (areaAt0InShaft != null && areaAt1InShaft != null)
                {
                    // Connect
                    areaAt0InShaft.AddConnection(areaAt1InShaft);
                    areaAt1InShaft.AddConnection(areaAt0InShaft);
                }
            }
            Log.Info($"Connecting areas finished.");
        }

        public void LoadMeshes()
        {
            var fileName = "navmesh.slnmf";
            var path = Path.Combine(BaseDir, fileName);

            if (!File.Exists(path))
            {
                return;
            }

            using var fileStream = File.OpenRead(path);
            using var binaryReader = new BinaryReader(fileStream);

            NavigationMesh.ReadMeshes(binaryReader);
        }

        public void SaveMeshes()
        {
            var fileName = "navmesh.slnmf";
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
