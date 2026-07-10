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
        private const float RoomLookupFailureLogIntervalSeconds = 10f;

        public List<TransformCell> ForeignRoomsCells { get; } = new();
        public IEnumerable<RoomIdentifier> ForeignRooms { get; }
        public RoomIdentifier RoomWithin { get; private set; }

        public event Action<TransformCell> OnSensedForeignRoomCell;
        public event Action OnAfterSensedForeignRooms;

        public event Action<RoomIdentifier> OnSensedRoomWithin;

        private readonly FpcBotPlayer _fpcBotPlayer;
        private float _nextRoomLookupFailureLogTime;
        private int _suppressedRoomLookupFailureLogs;

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
                LogRoomLookupFailure();
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

        private void LogRoomLookupFailure()
        {
            if (Time.time < _nextRoomLookupFailureLogTime)
            {
                _suppressedRoomLookupFailureLogs++;
                return;
            }

            string suppressedSuffix = _suppressedRoomLookupFailureLogs > 0
                ? $" suppressed={_suppressedRoomLookupFailureLogs}"
                : string.Empty;
            _suppressedRoomLookupFailureLogs = 0;
            _nextRoomLookupFailureLogTime = Time.time + RoomLookupFailureLogIntervalSeconds;
            Debug.LogWarning($"Could not determine room bot currently in; retrying at reduced log rate.{suppressedSuffix}");
        }
    }
}
