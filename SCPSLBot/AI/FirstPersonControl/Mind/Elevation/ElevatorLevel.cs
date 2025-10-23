using DrawableLine;
using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class ElevatorLevel(TransformCell cell, Vector3 panelPosition, Vector3 panelUp, SightSense sightSense) : IBelief
    {
        public event Action OnUpdate;

        public readonly Vector3 PanelPosition = panelPosition;
        public readonly Vector3 PanelUp = panelUp;
        public ElevatorPanel HitPanel;

        public ElevatorChamber ChamberAtLevel;
        public bool IsElevatorAt => ChamberAtLevel is not null;

        public void Update()
        {
            if (sightSense.GetDistanceToPositionSqr(cell.MeanPosition) > 4f)
            {
                if (!sightSense.IsPositionWithinFov(PanelPosition))
                {
                    return;
                }

                if (!sightSense.IsPositionObstructed(PanelPosition, out var panelPosHit)
                    || panelPosHit.collider.GetComponent<ElevatorPanel>() is not ElevatorPanel panel)
                {
                    return;
                }

                HitPanel = panel;
            }            

            var levelPosition = cell.MeanPosition;
            if (!Physics.Raycast(levelPosition, Vector3.down, out var hit, 2f, ElevatorMask)
                || hit.collider.GetComponentInParent<ElevatorChamber>() is not ElevatorChamber chamber)
            {
                if (ChamberAtLevel is not null)
                {
                    ChamberAtLevel = null;
                    OnUpdate?.Invoke();
                }
                return;
            }

            if (ChamberAtLevel is null)
            {
                ChamberAtLevel = chamber;
                OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(ElevatorLevel)}({cell}): {IsElevatorAt}";
        }

        private readonly int ElevatorMask = ~LayerMask.GetMask("Player", "Hitbox");
    }
}
