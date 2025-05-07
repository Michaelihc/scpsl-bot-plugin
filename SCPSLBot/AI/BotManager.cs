using CentralAuth;
using LabApi.Events.Handlers;
using MEC;
using Mirror;
using PlayerRoles;
using SCPSLBot.AI.FirstPersonControl;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Components;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;
using LocalConnectionToClient = SCPSLBot.LocalNetworking.LocalConnectionToClient;
using LocalConnectionToServer = SCPSLBot.LocalNetworking.LocalConnectionToServer;

namespace SCPSLBot.AI
{
    internal class BotManager
    {
        public static BotManager Instance { get; } = new BotManager();

        public Dictionary<ReferenceHub, BotHub> BotPlayers { get; } = new Dictionary<ReferenceHub, BotHub>();

        private CoroutineHandle handle;

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
                var player = referenceHub.gameObject;
                ServerConsole.Disconnect(player, "BotManager terminating");

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
            var player = Object.Instantiate(NetworkManager.singleton.playerPrefab);
            player.name = string.Format("{0} [bot]", NetworkManager.singleton.playerPrefab.name);

            var connectionToClient = new LocalConnectionToClient(--lastConnNum);
            var connectionToServer = new LocalConnectionToServer() { connectionToClient = connectionToClient };
            connectionToClient.connectionToServer = connectionToServer;

            Debug.Log($"connectionToClient = {connectionToClient}");
            //NetworkDiagnostics.InMessageEvent += LogInMessage;

            NetworkServer.AddConnection(connectionToClient);
            NetworkServer.AddPlayerForConnection(connectionToClient, player);
            NetworkServer.OnConnectedEvent?.Invoke(connectionToClient);

            var referenceHub = player.GetComponent<ReferenceHub>();
            Debug.Log($"referenceHub = {referenceHub}");

            BotPlayers.Add(referenceHub, new BotHub(connectionToClient, connectionToServer, referenceHub));

            Debug.Log($"connectionToClient.identity = {connectionToClient.identity}");

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

            Timing.RunCoroutine(AssignUserIdAsync(player));
        }

        private IEnumerator<float> AssignUserIdAsync(GameObject player)
        {
            yield return Timing.WaitForSeconds(1f);

            PlayerAuthenticationManager playerAuthManager = player.GetComponent<PlayerAuthenticationManager>();
            playerAuthManager.UserId = $"BotUserId{this.lastConnNum}";

            yield break;
        }

        public IEnumerator<float> RunPlayerUpdates()
        {
            var playersUpdates = new List<IEnumerator<JobHandle>>();

            while (true)
            {
                playersUpdates.Clear();

                var playersCount = BotPlayers.Values.Count;
                foreach (var player in BotPlayers.Values)
                {
                    playersUpdates.Add(player.Update());
                }

                var jobHandlesBuffer = new NativeArray<JobHandle>(playersCount, Allocator.Temp);
                var jobHandlesCount = 0;

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

        private BotManager()
        { }

        private int lastConnNum = 0;
    }
}
