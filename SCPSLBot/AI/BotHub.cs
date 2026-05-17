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

        public IEnumerator<JobHandle> Update()
        {
            Profiler.BeginSample($"{nameof(BotHub)}.{nameof(Update)}");

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

        private void HandleUpdateException(Exception ex)
        {
            Debug.LogException(ex);
            CurrentBotPlayer = null;
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
