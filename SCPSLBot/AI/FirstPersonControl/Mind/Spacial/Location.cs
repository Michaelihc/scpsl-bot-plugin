using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Spacial
{
    internal class Location : IBelief
    {
        public event Action OnUpdate;
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> NearPositions = [];

        protected void AddPosition(Vector3 position)
        {
            if (!Positions.Contains(position))
            {
                Positions.Add(position);
                OnUpdate?.Invoke();
            }
        }

        protected void RemovePosition(Vector3 position)
        {
            if (Positions.Remove(position))
            {
                OnUpdate?.Invoke();
            }
        }

        protected void SetPositions(IEnumerable<Vector3> newPositions)
        {
            var changed = false;

            var posCount = Positions.Count;
            var newPosCount = 0;
            
            foreach (var pos in newPositions)
            {
                var i = newPosCount;
                if (posCount > i)
                {
                    if (Positions[i] != pos)
                    {
                        NearPositions.Remove(Positions[i]);
                        Positions[i] = pos;
                        changed = true;
                    }
                }
                else
                {
                    Positions.Add(pos);
                    changed = true;
                }

                newPosCount++;
            }

            if (posCount > newPosCount)
            {
                for (var i = newPosCount; i < posCount; i++)
                {
                    NearPositions.Remove(Positions[i]);
                }
                Positions.RemoveRange(newPosCount, posCount - newPosCount);
                changed = true;
            }

            if (changed)
            {
                OnUpdate?.Invoke();
            }
        }

        protected void RemoveAllPositions(Predicate<Vector3> predicate)
        {
            if (Positions.RemoveAll(predicate) > 0)
            {
                NearPositions.RemoveAll(predicate);
                OnUpdate?.Invoke(); 
            }
        }

        protected void AddNearPosition(Vector3 pos)
        {
            if (!NearPositions.Contains(pos))
            {
                NearPositions.Add(pos);
                OnUpdate?.Invoke();
            }
        }

        protected void RemoveNearPosition(Vector3 pos)
        {
            if (NearPositions.Remove(pos))
            {
                OnUpdate?.Invoke();
            }
        }
    }
}
