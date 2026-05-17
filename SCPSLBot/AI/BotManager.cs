using LabApi.Events.Handlers;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using SCPSLBot.AI.FirstPersonControl;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Components;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;

namespace SCPSLBot.AI
{
    internal class BotManager
    {
        public static BotManager Instance { get; } = new BotManager();

        public Dictionary<ReferenceHub, BotHub> BotPlayers { get; } = new Dictionary<ReferenceHub, BotHub>();

        private CoroutineHandle handle;
        private ReferenceHub pathTarget;
        private Vector3 pathTargetPosition;
        private float nextPathTargetUpdateTime;

        public void Init()
        {
            handle = Timing.RunCoroutine(RunPlayerUpdates());

            PlayerRoleManager.OnRoleChanged += OnRoleChanged;
            ReferenceHub.OnPlayerRemoved += RemovePlayerIfBot;
            ServerEvents.RoundRestarted += OnRoundRestarted;

            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(31, i, true);
            }

            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("Door"), false);
            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("InteractableNoPlayerCollision"), false);
            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("Glass"), false);
            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("Hitbox"), false);
        }

        public void Terminate()
        {
            Timing.KillCoroutines(handle);

            PlayerRoleManager.OnRoleChanged -= OnRoleChanged;
            ReferenceHub.OnPlayerRemoved -= RemovePlayerIfBot;
            ServerEvents.RoundRestarted -= OnRoundRestarted;

            foreach (var (referenceHub, _) in BotPlayers.ToArray())
            {
                NetworkServer.Destroy(referenceHub.gameObject);
                RemovePlayerIfBot(referenceHub);
            }
        }

        private void OnRoundRestarted()
        {
            foreach (var (referenceHub, _) in BotPlayers.ToArray())
            {
                RemovePlayerIfBot(referenceHub);
            }
        }

        public void AddBotPlayer()
        {
            var referenceHub = DummyUtils.SpawnDummy("SCPSL Bot");
            if (referenceHub == null)
            {
                Debug.LogError("Failed to spawn RA dummy bot.");
                return;
            }

            var player = referenceHub.gameObject;
            player.name = $"{NetworkManager.singleton.playerPrefab.name} [bot dummy]";

            BotPlayers.Add(referenceHub, new BotHub(referenceHub));

            Debug.Log($"Spawned RA dummy bot: {referenceHub}");

            // add perception
            var sensing = new GameObject("Bot Sensing");
            sensing.layer = 31;
            sensing.transform.parent = player.transform;

            var perceptionComponent = sensing.AddComponent<PerceptionComponent>();
            BotPlayers[referenceHub].FpcPlayer.Perception.AddTriggerHandlers(perceptionComponent);

            var sensingTrigger = sensing.AddComponent<SphereCollider>();
            sensingTrigger.isTrigger = true;
            sensingTrigger.radius = 32f;

            var sensingRigid = sensing.AddComponent<Rigidbody>();
            sensingRigid.isKinematic = true;

            if (referenceHub.roleManager.CurrentRole.RoleTypeId == RoleTypeId.None
                || referenceHub.roleManager.CurrentRole.RoleTypeId == RoleTypeId.Spectator)
            {
                referenceHub.roleManager.ServerSetRole(RoleTypeId.ClassD, RoleChangeReason.RemoteAdmin);
            }
        }

        public IEnumerator<float> RunPlayerUpdates()
        {
            var playersUpdates = new List<IEnumerator<JobHandle>>();

            while (true)
            {
                playersUpdates.Clear();

                var playersCount = BotPlayers.Values.Count;
                foreach (var botHub in BotPlayers.Values)
                {
                    playersUpdates.Add(botHub.Update());
                }

                var jobHandlesBuffer = new NativeArray<JobHandle>(playersCount, Allocator.Temp);
                int jobHandlesCount;

                var completedCount = 0;
                while (completedCount < playersCount)
                {
                    completedCount = 0;
                    jobHandlesCount = 0;
                    foreach (var playerUpdate in playersUpdates)
                    {
                        if (playerUpdate.MoveNext())
                        {
                            jobHandlesBuffer[jobHandlesCount] = playerUpdate.Current;
                            jobHandlesCount++;
                        }
                        else
                        {
                            completedCount++;
                        }
                    }

                    var jobHandles = jobHandlesBuffer.GetSubArray(0, jobHandlesCount);
                    JobHandle.CompleteAll(jobHandles);
                }

                yield return Timing.WaitForOneFrame;
            }
        }

        public void OnRoleChanged(ReferenceHub userHub, PlayerRoleBase prevRole, PlayerRoleBase newRole)
        {
            if (BotPlayers.TryGetValue(userHub, out var botPlayer))
            {
                botPlayer.OnRoleChanged(prevRole, newRole);
            }
        }

        public void RemovePlayerIfBot(ReferenceHub userHub)
        {
            if (BotPlayers.Remove(userHub))
            {
                Debug.Log($"Bot player removed: {userHub}");
            }
        }

        public bool TogglePathToTarget(ReferenceHub target)
        {
            if (pathTarget == target)
            {
                pathTarget = null;
                return false;
            }

            pathTarget = target;
            pathTargetPosition = target.transform.position;
            nextPathTargetUpdateTime = 0f;
            return true;
        }

        public bool TryGetPathTargetPosition(out Vector3 position)
        {
            if (pathTarget == null)
            {
                position = default;
                return false;
            }

            if (Time.time >= nextPathTargetUpdateTime)
            {
                pathTargetPosition = pathTarget.transform.position;
                nextPathTargetUpdateTime = Time.time + 1f;
            }

            position = pathTargetPosition;
            return true;
        }

        private BotManager()
        { }
    }
}
