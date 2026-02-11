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
        }
    }
}
