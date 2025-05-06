using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.Components
{
    internal record struct ColliderData(int InstanceId, Vector3 Center);

    internal class ColliderDataComponent : MonoBehaviour
    {
        public Dictionary<Collider, ColliderData> ColliderDatas { get; } = new();

        private Collider[] colliders;

        public void Awake()
        {
            colliders = GetComponents<Collider>();
            foreach (var collider in colliders)
            {
                ColliderDatas[collider] = new ColliderData(collider.GetInstanceID(), collider.bounds.center);
            }
        }

        public void Update()
        {
            foreach (var collider in colliders)
            {
                ColliderDatas[collider] = new ColliderData(ColliderDatas[collider].InstanceId, collider.bounds.center);
            }
        }
    }
}
