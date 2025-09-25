using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal enum ElevationObstacleMode
    { 
        NoElevator,
        IsElevatorNotAtOrigin,
        IsElevatorAtOrigin
    }

    [Obsolete]
    internal class ElevationObstacle : IBelief
    {
        public event Action OnUpdate;

        private readonly int doorLayer = LayerMask.NameToLayer("Door");

        private readonly FpcBotNavigator navigator;
        private readonly SightSense sightSense;

        public ElevationObstacle(SightSense sightSense, FpcBotNavigator botNavigator) 
        {
            this.navigator = botNavigator;
            this.sightSense = sightSense;

            sightSense.OnAfterSightSensing += OnAfterSightSensing;
        }

        private void OnAfterSightSensing()
        {
            var edgelessSegmentResult = navigator.CellPathSegments
                .Where(s => !s.Cell.AdjacentCellEdges.ContainsKey(s.NextCell)
                    && !NavigationMesh.ForeignConnectedCellEdges[s.Cell].ContainsKey(s.NextCell))
                .Select(s => new (TransformCell Cell, TransformCell NextCell)?(s))
                .FirstOrDefault();
            if (!edgelessSegmentResult.HasValue)
            {
                if (DestinationCell != null && DestinationCell == navigator.GetCellWithin())
                {
                    Update(null, null, null, null);
                }

                return;
            }
            var edgelessSegment = edgelessSegmentResult.Value;

            // path has edgeless segment

            var originPoint = edgelessSegment.Cell.MeanPosition;
            var goalPosition = navigator.GoalPosition;

            if (!sightSense.IsPositionWithinFov(originPoint))
            {
                return;
            }

            if (sightSense.IsPositionObstructed(originPoint, out var hit))
            {
                var elevatorDoor = hit.collider.GetComponentInParent<ElevatorDoor>();
                if (!elevatorDoor || hit.collider.gameObject.layer != doorLayer)
                {
                    return;
                }

                var elevator = elevatorDoor.Chamber;
                if (!elevator)
                {
                    Debug.LogWarning($"No elevator chamber assigned to obstructing elevator door {elevatorDoor}.");
                    return;
                }

                Update(elevator, goalPosition, edgelessSegment.NextCell, elevatorDoor.IsConsideredOpen() ? elevator : null);
                return;
            }

            if (Physics.Raycast(originPoint, Vector3.down, out hit, 2f))
            {
                var elevator = hit.collider.GetComponentInParent<ElevatorChamber>();
                if (elevator)
                {
                    Update(elevator, goalPosition, edgelessSegment.NextCell, elevator);
                    return;
                }
            }

            var destPoint = edgelessSegment.NextCell.MeanPosition;
            if (Physics.Raycast(destPoint, Vector3.down, out hit, 2f))
            {
                var elevator = hit.collider.GetComponentInParent<ElevatorChamber>();
                if (elevator)
                {
                    Update(elevator, goalPosition, edgelessSegment.NextCell, null);
                    return;
                }
            }

            Update(null, goalPosition, edgelessSegment.NextCell, null);
        }

        public ElevationObstacleMode Has(Vector3 goalPos) => GoalPosition == goalPos ? HasAtOrigin : ElevationObstacleMode.NoElevator;
        public ElevationObstacleMode HasAtOrigin => ElevatorAtOrigin ? ElevationObstacleMode.IsElevatorAtOrigin : ElevationObstacleMode.IsElevatorNotAtOrigin;

        public ElevatorChamber Elevator { get; private set; }
        public Vector3? GoalPosition { get; private set; }
        public TransformCell? DestinationCell { get; private set; }
        public ElevatorChamber ElevatorAtOrigin { get; private set; }

        private void Update(ElevatorChamber newElevatorValue, Vector3? goalPos, TransformCell? destinationCell, ElevatorChamber elevatorAtOrigin)
        {
            if (newElevatorValue != Elevator || elevatorAtOrigin != ElevatorAtOrigin) 
            { 
                Elevator = newElevatorValue;
                GoalPosition = goalPos;
                DestinationCell = destinationCell;
                ElevatorAtOrigin = elevatorAtOrigin;
                OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(ElevationObstacle)}: {Elevator?.GetType().Name ?? "ElevatorInTransit"}";
        }
    }
}
