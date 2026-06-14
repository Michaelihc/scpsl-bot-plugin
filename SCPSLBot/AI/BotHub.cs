using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using SCPSLBot.AI.FirstPersonControl;
using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace SCPSLBot.AI
{
    internal class BotHub
    {
        public readonly FpcBotPlayer FpcPlayer;

        public IBotPlayer CurrentBotPlayer { get; private set; }
        public ReferenceHub PlayerHub { get; }

        public BotHub(ReferenceHub hub)
        {
            PlayerHub = hub;

            FpcPlayer = new FpcBotPlayer(this);
        }

        private float lastExceptionLogTime = float.NegativeInfinity;
        private float exceptionWindowStart;
        private int recentExceptionCount;
        private float parkedUntilTime;

        public IEnumerator<JobHandle> Update()
        {
            Profiler.BeginSample($"{nameof(BotHub)}.{nameof(Update)}");

            // Auto-rearm a bot that was briefly parked after repeated faults, once its cooldown elapses.
            if (CurrentBotPlayer == null
                && parkedUntilTime > 0f
                && Time.time >= parkedUntilTime
                && PlayerHub != null
                && PlayerHub.roleManager?.CurrentRole is FpcStandardRoleBase fpcRole)
            {
                parkedUntilTime = 0f;
                FpcPlayer.FpcRole = fpcRole;
                CurrentBotPlayer = FpcPlayer;
                FpcPlayer.OnRoleChanged();
            }

            var botPlayerUpdate = CurrentBotPlayer?.Update();
            if (botPlayerUpdate != null)
            {
                while (botPlayerUpdate.TryCatchMoveNext(HandleUpdateException))
                {
                    yield return botPlayerUpdate.Current;
                }
            }

            Profiler.EndSample();
        }

        // A single per-tick fault must not permanently disable the bot for the whole round.
        // Abort only the current tick; rate-limit logging; only park (then auto-rearm) if a
        // bot is faulting every tick, so we stop spamming without killing it forever.
        private void HandleUpdateException(Exception ex)
        {
            if (Time.realtimeSinceStartup - lastExceptionLogTime > 5f)
            {
                Debug.LogException(ex);
                lastExceptionLogTime = Time.realtimeSinceStartup;
            }

            try
            {
                FpcPlayer.Move.DesiredLocalDirection = Vector3.zero;
            }
            catch
            {
                // best-effort; ignore secondary faults while recovering
            }

            var now = Time.time;
            if (now - exceptionWindowStart > 5f)
            {
                exceptionWindowStart = now;
                recentExceptionCount = 0;
            }

            if (++recentExceptionCount >= 30)
            {
                CurrentBotPlayer = null;
                parkedUntilTime = now + 5f;
                recentExceptionCount = 0;
            }
        }

        public void OnRoleChanged(PlayerRoleBase prevRole, PlayerRoleBase newRole)
        {
            if (newRole is FpcStandardRoleBase fpcRole)
            {
                FpcPlayer.FpcRole = fpcRole;
                CurrentBotPlayer = FpcPlayer;
            }
            else
            {
                CurrentBotPlayer = null;
            }

            CurrentBotPlayer?.OnRoleChanged();

            Debug.Log($"Bot got new role assigned. Role Id: {newRole.RoleTypeId}");
            Debug.Log($"Type of role: {newRole.GetType()}");
        }

        public void NotifyHurt(ReferenceHub attacker)
        {
            FpcPlayer.Combat.NotifyDamagedBy(attacker);
        }

        public override string ToString()
        {
            return $"{nameof(BotHub)}: {PlayerHub}";
        }
    }

    internal static class IEnumeratorExtensions
    { 
        public static bool TryCatchMoveNext<T>(this IEnumerator<T> enumerator, Action<Exception> handleException)
        {
            try
            {
                return enumerator.MoveNext();
            }
            catch (Exception ex)
            {
                handleException(ex);
                return false;
            }
        }
    }
}
