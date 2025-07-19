using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class Obstacle(TransformCell transformCell, SightSense sightSense, LayerMask layerMask) : IBelief
    {
        private Vector3 toPos;
        private Vector3 fromPos;
        public RaycastHit? hit;

        public Vector3 ToPos => this.toPos;

        public event Action OnUpdate;

        public bool HasHit(Vector3 toPos, Vector3 fromPos)
        {
            if (this.toPos != toPos)
            {
                this.toPos = toPos;
                this.hit = null;
            }
            if (this.fromPos != fromPos)
            {
                this.fromPos = fromPos;
            }

            return hit.HasValue;
        }

        public void Update()
        {
            var toPos = this.toPos;
            var fromPos = this.fromPos;

            if (!sightSense.IsPositionWithinFov(toPos))
            {
                return;
            }

            if (sightSense.IsPositionObstructed(fromPos))
            {
                return;
            }

            if (Physics.Linecast(fromPos, toPos, out var hit, layerMask))
            {
                if (!this.hit.HasValue)
                {
                    this.hit = hit;
                    OnUpdate?.Invoke();
                }
            }
            else
            {
                if (this.hit.HasValue)
                {
                    this.hit = null;
                    OnUpdate?.Invoke();
                }
            }
        }
    }
}
