using PlayerRoles.FirstPersonControl;
using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Termination
{
    internal class TargetSightedLocation(PlayersWithinSightSense playersWithinSense) : Location
    {
        public void Update()
        {
            var enemyPlayers = playersWithinSense.EnemiesWithinSight;
            var enemyPositions = enemyPlayers.Select(h => (IFpcRole)h.roleManager.CurrentRole).Select(r => r.FpcModule.Position);

            SetPositions(enemyPositions);
            
            // Evaluate nearness to positions
            foreach (var position in Positions)
            {
                if (playersWithinSense.GetDistanceToPositionSqr(position) <= NearDistSqr)
                {
                    AddNearPosition(position);
                }
                else
                {
                    RemoveNearPosition(position);
                }
            }
        }

        private const float NearDistSqr = 1.75f * 1.75f;
    }
}
