using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class ElevationObstacle : Belief<bool>
    {
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
            var edgelessSegment = navigator.CellPathSegments.FirstOrDefault(s => !s.Cell.ConnectedCellEdges.ContainsKey(s.NextCell));
            if (edgelessSegment.NextCell == null)
            {
                if (DestinationCell != null && DestinationCell == navigator.GetCellWithin())
                {
                    Update(null, null, null);
                }

                return;
            }

            // path has edgeless segment

            var lastPoint = edgelessSegment.Cell.CenterPosition;

            if (!sightSense.IsPositionWithinFov(lastPoint))
            {
                return;
            }

            if (sightSense.IsPositionObstructed(lastPoint))
            {
                return;
            }

            var goalPosition = navigator.GoalPosition;

            if (Physics.Raycast(lastPoint, Vector3.down, out var hit, 2f))
            {
                var elevator = hit.collider.GetComponentInParent<ElevatorChamber>();
                if (elevator)
                {
                    Update(elevator, goalPosition, edgelessSegment.NextCell);
                }
            }
        }

        public bool Has(Vector3 goalPos) => GoalPosition == goalPos;
        public ElevatorChamber Elevator { get; private set; }
        public Vector3? GoalPosition { get; private set; }
        public TransformCell? DestinationCell { get; private set; }

        private void Update(ElevatorChamber newChamberValue, Vector3? goalPos, TransformCell? destinationCell)
        {
            if (newChamberValue != Elevator) 
            { 
                Elevator = newChamberValue;
                GoalPosition = goalPos;
                DestinationCell = destinationCell;
                InvokeOnUpdate();
            }
        }

        public override string ToString()
        {
            return $"{nameof(ElevationObstacle)}: {Elevator?.GetType().Name}";
        }
    }
}
