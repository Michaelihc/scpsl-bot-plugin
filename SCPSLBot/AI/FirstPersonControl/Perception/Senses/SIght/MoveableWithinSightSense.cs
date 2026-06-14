using InventorySystem.Items.Pickups;
using SCPSLBot.Components;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight
{
    internal abstract class MoveableWithinSightSense<TComponent> : SightSense<TComponent> where TComponent : Component
    {
        protected MoveableWithinSightSense(FpcBotPlayer botPlayer) : base(botPlayer)
        { }

        private readonly HashSet<Collider> colliders = new();
        private readonly Dictionary<Collider, ColliderData> collidersDatas = new();

        protected override void AddColliderDatas(Collider triggeringCollider, TComponent component, Dictionary<ColliderData, TComponent> data)
        {
            // Track the collider center directly. Previously this attached a ColliderDataComponent
            // MonoBehaviour to the (shared, world-owned) item GameObject that polled bounds.center
            // every frame forever and was never removed — a per-frame cost on every sensed item and
            // a cross-plugin leak. We instead refresh centers on demand in UpdateColliderData, only
            // for colliders a live bot is actually sensing.
            var colliderData = new ColliderData(triggeringCollider.GetInstanceID(), triggeringCollider.bounds.center);
            colliders.Add(triggeringCollider);
            collidersDatas[triggeringCollider] = colliderData;

            data[colliderData] = component;
        }

        protected override void RemoveColliderDatas(Collider triggeringCollider, TComponent component, Dictionary<ColliderData, TComponent> data)
        {
            colliders.Remove(triggeringCollider);
            collidersDatas.Remove(triggeringCollider, out var colliderData);

            data.Remove(colliderData);
        }

        protected override void UpdateColliderData(Dictionary<ColliderData, TComponent> validCollidersComponents)
        {
            foreach (var collider in colliders)
            {
                if (!collider)
                {
                    continue;
                }

                var prevData = collidersDatas[collider];
                var data = new ColliderData(prevData.InstanceId, collider.bounds.center);

                if (prevData.Center != data.Center)
                {
                    validCollidersComponents.Remove(prevData, out var value);
                    validCollidersComponents.Add(data, value);

                    collidersDatas[collider] = data;
                }
            }
        }
    }
}
