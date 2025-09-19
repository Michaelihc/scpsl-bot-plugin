using DrawableLine;
using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class Obstacle(TransformCell transformCell, Vector3 position, HashSet<Collider> colliders, SightSense sightSense, LayerMask layerMask) : IBelief
    {
        public event Action OnUpdate;

        public RaycastHit? HitResult;

        public void Update()
        {
            if (!sightSense.IsPositionWithinFov(position))
            {
                return;
            }

            if (!sightSense.IsPositionObstructed(position, out var outObstructionHit))
            {
                if (HitResult.HasValue)
                {
                    HitResult = null;
                    OnUpdate?.Invoke();
                }
                return;
            }

            if (!colliders.Contains(outObstructionHit.collider))
            {
                return;
            }

            if (!HitResult.HasValue)
            {
                HitResult = outObstructionHit;
                OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(Obstacle)}({transformCell}): {HitResult.HasValue}";
        }
    }
}
