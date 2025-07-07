using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(FpcBotPlayer botPlayer) : IAction
    {
        private NavigationCell enablingNavigationCell;
        private NavigationCell impactingNavigationCell;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            this.enablingNavigationCell = fpcMind.ActionEnabledBy<NavigationCell, TransformCell?>(this,
                matchGetter: (b, m) => m.Get<NavigationCell, TransformCell?>(impactingNavigationCell)!.Value.AdjacentCells.Select(c => new TransformCell?(c)).ToArray(),
                matchPredicate: c => c?.IsPositionWithin(botPlayer.PlayerPosition) ?? false);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.impactingNavigationCell = fpcMind.ActionImpacts<NavigationCell, TransformCell?>(this, c => c.HasValue);
        }

        public float Cost(FpcMatchProvider targetMatchProvider, FpcMatchProvider enablingMatchProvider)
        {
            var targetCell = targetMatchProvider.Get<NavigationCell, TransformCell?>(this.impactingNavigationCell);
            var enablingCell = enablingMatchProvider.Get<NavigationCell, TransformCell?>(this.enablingNavigationCell);

            return Vector3.Distance(targetCell!.Value.CenterPosition, enablingCell!.Value.CenterPosition);
        }

        public void Tick(FpcMatchProvider matchProvider)
        {
            throw new NotImplementedException();
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }
    }
}
