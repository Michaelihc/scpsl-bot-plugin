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
            var colliderDataComponent = triggeringCollider.GetComponent<ColliderDataComponent>();
            if (!colliderDataComponent)
            {
                colliderDataComponent = triggeringCollider.gameObject.AddComponent<ColliderDataComponent>();
            }

            var colliderData = colliderDataComponent.ColliderDatas[triggeringCollider];
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

                var colliderDataComponent = collider.GetComponent<ColliderDataComponent>();

                var prevData = collidersDatas[collider];
                var data = colliderDataComponent.ColliderDatas[collider];

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
