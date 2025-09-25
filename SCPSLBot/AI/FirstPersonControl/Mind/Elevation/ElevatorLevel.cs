using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class ElevatorLevel(TransformCell cell, SightSense sightSense) : IBelief
    {
        public event Action OnUpdate;

        public ElevatorChamber HitChamber;
        public bool IsElevatorAt => HitChamber is not null;

        public void Update()
        {
            var levelPosition = cell.MeanPosition;

            if (!sightSense.IsPositionWithinFov(levelPosition))
            {
                return;
            }

            if (sightSense.IsPositionObstructed(levelPosition, out var hit))
            {
                if (hit.collider.gameObject.layer != doorLayer || hit.collider.GetComponentInParent<ElevatorDoor>() is not ElevatorDoor door)
                {
                    return;
                }

                if (!door.IsConsideredOpen())
                {
                    if (HitChamber is not null)
                    {
                        HitChamber = null;
                        OnUpdate?.Invoke();
                    }
                    return;
                }
            }

            if (!Physics.Raycast(levelPosition, Vector3.down, out hit, 2f)
                || hit.collider.GetComponentInParent<ElevatorChamber>() is not ElevatorChamber chamber)
            {
                if (HitChamber is not null)
                {
                    HitChamber = null;
                    OnUpdate?.Invoke();
                }
                return;
            }

            if (HitChamber is null)
            {
                HitChamber = chamber;
                OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(ElevatorLevel)}({cell}): {IsElevatorAt}";
        }

        private static readonly int doorLayer = LayerMask.NameToLayer("Door");
    }
}
