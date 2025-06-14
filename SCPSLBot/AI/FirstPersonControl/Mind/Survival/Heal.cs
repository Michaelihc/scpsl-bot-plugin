using InventorySystem.Items.Usables;
using SCPSLBot.AI.FirstPersonControl.Mind.Item;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Beliefs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Survival
{
    internal class Heal : IAction
    {
        private ItemInInventory<ItemOfType> healingItemInInventory;
        private Health health;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            healingItemInInventory = fpcMind.ActionEnabledBy<ItemInInventory<ItemOfType>>(this, b => b.Criteria.Matches(ItemType.Medkit), b => b.Item);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            health = fpcMind.ActionImpacts<Health, float>(this, remAmount => remAmount >= 60f);
        }

        public float Cost => 5f * Mathf.Max(65f / (health.MaxAmount - health.Amount), 1f);

        public void Tick()
        {
            var healingItem = healingItemInInventory.Item;
            if (!healingItem.IsEquipped)
            {
                healingItem.Owner.inventory.ServerSelectItem(healingItem.ItemSerial);
            }

            UsableItemsController.ServerEmulateMessage(healingItem.ItemSerial, StatusMessage.StatusType.Start);
        }

        public void Reset()
        {

        }
    }
}
