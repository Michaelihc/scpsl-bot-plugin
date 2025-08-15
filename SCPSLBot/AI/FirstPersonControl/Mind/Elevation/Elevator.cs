using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class Elevator(TransformCell transformCellZero, TransformCell transformCellOne, SightSense sightSense) : IBelief
    {
        private static readonly int doorLayer = LayerMask.NameToLayer("Door");

        private TransformCell elevationCellAtOrigin;
        public ElevatorChamber ChamberAtOrigin;
        public bool notAtOrigin = false;

        public event Action OnUpdate;

        public bool IsAt(TransformCell elevationCell)
        {
            if (this.elevationCellAtOrigin != elevationCell)
            {
                this.elevationCellAtOrigin = elevationCell;
                this.ChamberAtOrigin = null;
                this.notAtOrigin = false;
            }

            return !this.notAtOrigin;
        }

        public void Update()
        {
            var originPoint = this.elevationCellAtOrigin.CenterPosition;

            if (!sightSense.IsPositionWithinFov(originPoint))
            {
                return;
            }

            if (sightSense.IsPositionObstructed(originPoint, out var hit))
            {
                if (hit.collider.gameObject.layer != doorLayer || hit.collider.GetComponentInParent<ElevatorDoor>() is not ElevatorDoor elevatorDoor)
                {
                    return;
                }

                var chamber = elevatorDoor.Chamber;
                if (!chamber)
                {
                    Debug.LogWarning($"No elevator chamber assigned to obstructing elevator door {elevatorDoor}.");
                    return;
                }

                if (elevatorDoor.IsConsideredOpen() && this.ChamberAtOrigin != chamber)
                {
                    this.ChamberAtOrigin = chamber;
                    this.notAtOrigin = false;
                    OnUpdate?.Invoke();
                }

                return;
            }

            if (Physics.Raycast(originPoint, Vector3.down, out hit, 2f))
            {
                var chamber = hit.collider.GetComponentInParent<ElevatorChamber>();
                if (chamber && this.ChamberAtOrigin != chamber)
                {
                    this.ChamberAtOrigin = chamber;
                    this.notAtOrigin = false;
                    OnUpdate?.Invoke();
                    return;
                }
            }

            //var destPoint = edgelessSegment.NextCell.CenterPosition;
            //if (Physics.Raycast(destPoint, Vector3.down, out hit, 2f))
            //{
            //    var elevator = hit.collider.GetComponentInParent<ElevatorChamber>();
            //    if (elevator)
            //    {
            //        Update(elevator, goalPosition, edgelessSegment.NextCell, null);
            //        return;
            //    }
            //}

            if (this.ChamberAtOrigin != null)
            {
                this.ChamberAtOrigin = null;
                this.notAtOrigin = true;
                OnUpdate?.Invoke();
            }
        }
    }
}
