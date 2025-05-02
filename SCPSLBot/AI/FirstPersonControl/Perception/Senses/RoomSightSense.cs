using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Perception.Senses
{
    internal class RoomSightSense : SightSense, ISense
    {
        public List<TransformCell> ForeignRoomsCells { get; } = new();
        public IEnumerable<RoomIdentifier> ForeignRooms { get; }
        public RoomIdentifier RoomWithin { get; private set; }

        public event Action<TransformCell> OnSensedForeignRoomCell;
        public event Action OnAfterSensedForeignRooms;

        public event Action<RoomIdentifier> OnSensedRoomWithin;

        private readonly FpcBotPlayer _fpcBotPlayer;

        public RoomSightSense(FpcBotPlayer botPlayer) : base(botPlayer)
        {
            _fpcBotPlayer = botPlayer;

            ForeignRooms = ForeignRoomsCells.Select(fa => fa.Transform.GetComponent<RoomIdentifier>()).Distinct();
        }

        public override void ProcessSightSensedItems()
        {
            UpdateRoomWithin();
            UpdateForeignRoomsCells();

            foreach (var sensedForeignRoomCell in ForeignRoomsCells)
            {
                OnSensedForeignRoomCell?.Invoke(sensedForeignRoomCell);
            }
            OnAfterSensedForeignRooms?.Invoke();
        }

        private void UpdateRoomWithin()
        {
            var playerPosition = _fpcBotPlayer.PlayerPosition;

            if (!RoomUtils.TryGetRoom(playerPosition, out var newRoomWithin))
            {
                Debug.LogWarning($"Could not determine room bot currently in");
                return;
            }

            OnSensedRoomWithin?.Invoke(newRoomWithin);
            RoomWithin = newRoomWithin;
        }

        private void UpdateForeignRoomsCells()
        {
            if (RoomWithin)
            {
                ForeignRoomsCells.Clear();

                foreach (var localCell in NavigationMesh.LocalMeshesByRoom[RoomWithin.gameObject].Cells)
                {
                    var transformCell = new TransformCell(localCell, RoomWithin.transform);
                    foreach (var fa in NavigationMesh.ForeignConnectedCells[transformCell].Where(fa => fa.Transform.GetComponent<RoomIdentifier>()))
                    {
                        var faa = fa.AdjacentCells.First();
                        ForeignRoomsCells.Add(faa);
                    }
                }
            }
        }

        public void ProcessEnter(Collider other)
        {
        }

        public void ProcessExit(Collider other)
        {
        }

        public IEnumerator<JobHandle> ProcessSensibility()
        {
            yield break;
        }
    }
}
