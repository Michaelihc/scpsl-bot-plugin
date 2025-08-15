using DrawableLine;
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
        public RaycastHit? HitResult;

        public Vector3 ToPos => this.toPos;

        public event Action OnUpdate;

        public bool HasHit(Vector3 toPos, Vector3 fromPos)
        {
            if (this.toPos != toPos)
            {
                this.toPos = toPos;
                this.HitResult = null;
            }
            if (this.fromPos != fromPos)
            {
                this.fromPos = fromPos;
            }

            return HitResult.HasValue;
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

            DrawableLines.GenerateLine(fromPos, toPos);

            if (Physics.Linecast(fromPos, toPos, out var hit, layerMask))
            {
                if (!this.HitResult.HasValue)
                {
                    this.HitResult = hit;
                    OnUpdate?.Invoke();
                }
            }
            else
            {
                if (this.HitResult.HasValue)
                {
                    this.HitResult = null;
                    OnUpdate?.Invoke();
                }
            }
        }

        public override string ToString()
        {
            return $"{nameof(Obstacle)}({transformCell}): {HitResult.HasValue}";
        }
    }
}
