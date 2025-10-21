using DrawableLine;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using MapGeneration.Distributors;
using SCPSLBot.AI.FirstPersonControl.Mind;
using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using SCPSLBot.AI.FirstPersonControl.Mind.Elevation;
using SCPSLBot.AI.FirstPersonControl.Mind.Escape;
using SCPSLBot.AI.FirstPersonControl.Mind.Goals;
using SCPSLBot.AI.FirstPersonControl.Mind.Item;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Actions;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Keycard;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.AI.FirstPersonControl.Mind.Room;
using SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Scp914;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl
{
    internal class FpcMindFactory(FpcBotPlayer botPlayer, FpcBotPerception perception)
    {
        public readonly HashSet<IBelief> Beliefs = [];

        public void BuildMindClassD(FpcMind mind)
        {
            AddNavigation(mind);
            AddItemsSearchingAndUsing(mind);

            AddScp914Searching(mind);
            AddScp914Operations(mind);

            AddFacilityEscaping(mind);
        }

        public void BuildMindScientist(FpcMind mind)
        {
            AddNavigation(mind);
            AddItemsSearchingAndUsing(mind);

            AddScp914Searching(mind);
            AddScp914Operations(mind);

            AddFacilityEscaping(mind);
        }

        private void AddFacilityEscaping(FpcMind mind)
        {
            mind.AddBelief(GetBelief(() => new FacilityEscapeLocation(perception.GetSense<RoomSightSense>())));
            mind.AddBelief(GetBelief(() => new PlayerEscaped()));
            mind.AddAction(GetAction(() => new GoToEscapeLocation(botPlayer)));

            mind.AddGoal(GetGoal(() => new EscapeTheFacility()));
        }

        private void AddScp914Operations(FpcMind mind)
        {
            mind.AddBelief(GetBelief(() => new Scp914Controls(perception.GetSense<InteractablesWithinSightSense>())));
            mind.AddBelief(GetBelief(() => new Scp914RunningOnSetting(Scp914.Scp914KnobSetting.Rough, perception.GetSense<RoomSightSense>()), Scp914.Scp914KnobSetting.Rough));
            mind.AddBelief(GetBelief(() => new Scp914RunningOnSetting(Scp914.Scp914KnobSetting.Coarse, perception.GetSense<RoomSightSense>()), Scp914.Scp914KnobSetting.Coarse));
            mind.AddBelief(GetBelief(() => new Scp914RunningOnSetting(Scp914.Scp914KnobSetting.OneToOne, perception.GetSense<RoomSightSense>()), Scp914.Scp914KnobSetting.OneToOne));
            mind.AddBelief(GetBelief(() => new Scp914RunningOnSetting(Scp914.Scp914KnobSetting.Fine, perception.GetSense<RoomSightSense>()), Scp914.Scp914KnobSetting.Fine));
            mind.AddBelief(GetBelief(() => new Scp914RunningOnSetting(Scp914.Scp914KnobSetting.VeryFine, perception.GetSense<RoomSightSense>()), Scp914.Scp914KnobSetting.VeryFine));

            mind.AddAction(GetAction(() => new GoToStartScp914OnSetting(Scp914.Scp914KnobSetting.Fine, botPlayer), Scp914.Scp914KnobSetting.Fine));
            mind.AddAction(GetAction(() => new GoToStartScp914OnSetting(Scp914.Scp914KnobSetting.OneToOne, botPlayer), Scp914.Scp914KnobSetting.OneToOne));

            var navigationBeliefs = mind.GetBelief<NavigationBeliefs>();
            foreach (var cell in navigationBeliefs.Obstacles.Keys)
            {
                mind.AddAction(GetAction(() => new WaitForChamberDoorOpening(cell, navigationBeliefs, botPlayer), cell));
            }

            (KeycardWithPermissions, bool)[] outputDoorPermissionFlags =
            [
                (new(DoorPermissionFlags.Checkpoints), true),
                (new(DoorPermissionFlags.ExitGates), true),
                (new(DoorPermissionFlags.Intercom), false),
                (new(DoorPermissionFlags.AlphaWarhead), false),
                (new(DoorPermissionFlags.ContainmentLevelOne), true),
                (new(DoorPermissionFlags.ContainmentLevelTwo), true),
                (new(DoorPermissionFlags.ContainmentLevelThree), false),
                (new(DoorPermissionFlags.ArmoryLevelOne), false),
                (new(DoorPermissionFlags.ArmoryLevelTwo), false),
                (new(DoorPermissionFlags.ArmoryLevelThree), false),
                (new(PermissionsCheckpointContainmentLevelOneTwo), false),
            ];
            foreach (var (withPermissions, addAction) in outputDoorPermissionFlags)
            {
                mind.AddBelief(GetBelief(() => new ItemsInOutakeChamber(withPermissions, perception.GetSense<ItemsWithinSightSense>()), withPermissions));

                if (addAction)
                {
                    mind.AddAction(GetAction(() => new GoToItemInOutakeChamber<KeycardWithPermissions>(withPermissions, botPlayer), withPermissions));
                }
            }


            IItemBeliefCriteria[] outputKeycardFacilityManagerCriterias =
            [
                new ItemOfType(ItemType.KeycardFacilityManager),
                new KeycardWithPermissions(DoorPermissionFlags.Checkpoints),
                new KeycardWithPermissions(DoorPermissionFlags.ExitGates),
                new KeycardWithPermissions(DoorPermissionFlags.Intercom),
                new KeycardWithPermissions(DoorPermissionFlags.AlphaWarhead),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelThree),
                new KeycardWithPermissions(PermissionsCheckpointContainmentLevelOneTwo),
            ];

            IItemBeliefCriteria[] outputKeycardResearchCoordinatorCriterias =
            [
                new ItemOfType(ItemType.KeycardResearchCoordinator),
                new KeycardWithPermissions(DoorPermissionFlags.Checkpoints),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo),
                new KeycardWithPermissions(PermissionsCheckpointContainmentLevelOneTwo),
            ];

            IItemBeliefCriteria[] outputKeycardScientistCriterias =
            [
                new ItemOfType(ItemType.KeycardScientist),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo),
            ];

            IItemBeliefCriteria[] outputKeycardZoneManagerCriterias =
            [
                new ItemOfType(ItemType.KeycardZoneManager),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne),
                new KeycardWithPermissions(DoorPermissionFlags.Checkpoints),
            ];

            IItemBeliefCriteria[] outputKeycardO5Criterias =
            [
                new ItemOfType(ItemType.KeycardO5),
                new KeycardWithPermissions(DoorPermissionFlags.Checkpoints),
                new KeycardWithPermissions(DoorPermissionFlags.ExitGates),
                new KeycardWithPermissions(DoorPermissionFlags.Intercom),
                new KeycardWithPermissions(DoorPermissionFlags.AlphaWarhead),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo),
                new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelThree),
                new KeycardWithPermissions(DoorPermissionFlags.ArmoryLevelOne),
                new KeycardWithPermissions(DoorPermissionFlags.ArmoryLevelTwo),
                new KeycardWithPermissions(DoorPermissionFlags.ArmoryLevelThree),
                new KeycardWithPermissions(PermissionsCheckpointContainmentLevelOneTwo),
            ];

            #region Scp914 KeycardScientist in intake chamber
            mind.AddBelief(GetBelief(() => new ItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardScientist)), new ItemOfType(ItemType.KeycardScientist)));
            mind.AddAction(GetAction(() => new GoToDropItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardScientist), botPlayer), new ItemOfType(ItemType.KeycardScientist)));
            #endregion

            #region Scp914 KeycardJanitor in intake chamber
            mind.AddBelief(GetBelief(() => new ItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardJanitor)), new ItemOfType(ItemType.KeycardJanitor)));
            mind.AddAction(GetAction(() => new GoToDropItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardJanitor), botPlayer), new ItemOfType(ItemType.KeycardJanitor)));
            #endregion

            #region Scp914 KeycardFacilityManager in outake chamber
            mind.AddBelief(GetBelief(() => new ItemsInOutakeChamber(new ItemOfType(ItemType.KeycardFacilityManager), perception.GetSense<ItemsWithinSightSense>()), new ItemOfType(ItemType.KeycardFacilityManager)));
            mind.AddAction(GetAction(() => new GoToItemInOutakeChamber<ItemOfType>(new(ItemType.KeycardFacilityManager), botPlayer), new ItemOfType(ItemType.KeycardFacilityManager)));
            #endregion

            #region Scp914 KeycardZoneManager in outake chamber
            mind.AddBelief(GetBelief(() => new ItemsInOutakeChamber(new ItemOfType(ItemType.KeycardZoneManager), perception.GetSense<ItemsWithinSightSense>()), new ItemOfType(ItemType.KeycardZoneManager)));
            mind.AddAction(GetAction(() => new GoToItemInOutakeChamber<ItemOfType>(new(ItemType.KeycardZoneManager), botPlayer), new ItemOfType(ItemType.KeycardZoneManager)));
            #endregion

            #region Scp914 KeycardScientist to KeycardResearchCoordinator on Fine
            mind.AddBelief(GetBelief(() => new ItemsInOutakeChamber(new ItemOfType(ItemType.KeycardResearchCoordinator), perception.GetSense<ItemsWithinSightSense>()), new ItemOfType(ItemType.KeycardResearchCoordinator)));
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardScientist, outputKeycardResearchCoordinatorCriterias, Scp914.Scp914KnobSetting.Fine), ItemType.KeycardScientist, ItemType.KeycardResearchCoordinator, Scp914.Scp914KnobSetting.Fine));
            mind.AddAction(GetAction(() => new GoToItemInOutakeChamber<ItemOfType>(new(ItemType.KeycardResearchCoordinator), botPlayer), new ItemOfType(ItemType.KeycardResearchCoordinator)));
            #endregion

            #region Scp914 KeycardZoneManager to KeycardFacilityManager on Fine
            mind.AddBelief(GetBelief(() => new ItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardZoneManager)), new ItemOfType(ItemType.KeycardZoneManager)));
            mind.AddAction(GetAction(() => new GoToDropItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardZoneManager), botPlayer), new ItemOfType(ItemType.KeycardZoneManager)));
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardZoneManager, outputKeycardFacilityManagerCriterias, Scp914.Scp914KnobSetting.Fine), ItemType.KeycardZoneManager, ItemType.KeycardFacilityManager, Scp914.Scp914KnobSetting.Fine));
            #endregion

            #region Scp914 KeycardResearchCoordinator to KeycardFacilityManager on Fine
            mind.AddBelief(GetBelief(() => new ItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardResearchCoordinator)), new ItemOfType(ItemType.KeycardResearchCoordinator)));
            mind.AddAction(GetAction(() => new GoToDropItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardResearchCoordinator), botPlayer), new ItemOfType(ItemType.KeycardResearchCoordinator)));
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardResearchCoordinator, outputKeycardFacilityManagerCriterias, Scp914.Scp914KnobSetting.Fine), ItemType.KeycardResearchCoordinator, ItemType.KeycardFacilityManager, Scp914.Scp914KnobSetting.Fine));
            #endregion

            #region Scp914 KeycardScientist to KeycardZoneManager on 1:1
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardScientist, outputKeycardZoneManagerCriterias, Scp914.Scp914KnobSetting.OneToOne), ItemType.KeycardScientist, ItemType.KeycardZoneManager, Scp914.Scp914KnobSetting.OneToOne));
            #endregion

            #region Scp914 KeycardJanitor to KeycardZoneManager on Fine            
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardJanitor, outputKeycardZoneManagerCriterias, Scp914.Scp914KnobSetting.OneToOne), ItemType.KeycardJanitor, ItemType.KeycardZoneManager, Scp914.Scp914KnobSetting.OneToOne));
            #endregion

            #region Scp914 KeycardJanitor to KeycardScientist on 1:1
            mind.AddBelief(GetBelief(() => new ItemsInOutakeChamber(new ItemOfType(ItemType.KeycardScientist), perception.GetSense<ItemsWithinSightSense>()), new ItemOfType(ItemType.KeycardScientist)));
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardJanitor, outputKeycardScientistCriterias, Scp914.Scp914KnobSetting.Fine), ItemType.KeycardJanitor, ItemType.KeycardScientist, Scp914.Scp914KnobSetting.Fine));
            mind.AddAction(GetAction(() => new GoToItemInOutakeChamber<ItemOfType>(new(ItemType.KeycardScientist), botPlayer), new ItemOfType(ItemType.KeycardScientist)));
            #endregion

            #region Scp914 KeycardFacilityManager to KeycardO5 on Fine
            mind.AddBelief(GetBelief(() => new ItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardFacilityManager)), new ItemOfType(ItemType.KeycardFacilityManager)));
            mind.AddAction(GetAction(() => new GoToDropItemInIntakeChamber<ItemOfType>(new(ItemType.KeycardFacilityManager), botPlayer), new ItemOfType(ItemType.KeycardFacilityManager)));

            mind.AddBelief(GetBelief(() => new ItemsInOutakeChamber(new ItemOfType(ItemType.KeycardO5), perception.GetSense<ItemsWithinSightSense>()), new ItemOfType(ItemType.KeycardO5)));
            mind.AddAction(GetAction(() => new WaitForItemUpgrading(ItemType.KeycardFacilityManager, outputKeycardO5Criterias, Scp914.Scp914KnobSetting.Fine), ItemType.KeycardFacilityManager, ItemType.KeycardO5, Scp914.Scp914KnobSetting.Fine));
            mind.AddAction(GetAction(() => new GoToItemInOutakeChamber<ItemOfType>(new(ItemType.KeycardO5), botPlayer), new ItemOfType(ItemType.KeycardO5)));
            #endregion
        }

        private void AddScp914Searching(FpcMind mind)
        {
            mind.AddBelief(GetBelief(() => new Scp914Location(perception.GetSense<RoomSightSense>())));
            mind.AddAction(GetAction(() => new GoToSearchRoomForScp914(botPlayer)));
        }

        private void AddItemsSearchingAndUsing(FpcMind mind)
        {
            #region Locker spawns
            mind.AddBelief(GetBelief(() => new LockerSpawnsLocation(StructureType.StandardLocker, perception.GetSense<RoomSightSense>()), StructureType.StandardLocker));
            #endregion

            #region KeycardJanitor picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardJanitor, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardJanitor));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardJanitor, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardJanitor));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardJanitor, botPlayer), ItemType.KeycardJanitor));

            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<ItemOfType>(ItemType.KeycardJanitor, [ItemType.KeycardJanitor], perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardJanitor));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<ItemOfType>(ItemType.KeycardJanitor, botPlayer), ItemType.KeycardJanitor));

            mind.AddBelief(GetBelief(() => new ItemSpawnsInSightedLocker<ItemOfType>(ItemType.KeycardJanitor, [ItemType.KeycardJanitor], perception.GetSense<LockersWithinSightSense>()), ItemType.KeycardJanitor));
            mind.AddAction(GetAction(() => new GoToLockerSpawnLocation<ItemOfType>(ItemType.KeycardJanitor, StructureType.StandardLocker, botPlayer), ItemType.KeycardJanitor));
            mind.AddAction(GetAction(() => new GoToItemSpawnInLocker<ItemOfType>(ItemType.KeycardJanitor, botPlayer), ItemType.KeycardJanitor));

            mind.AddAction(GetAction(() => new GoToSearchRoom<ItemOfType>(ItemType.KeycardJanitor, 0, botPlayer), ItemType.KeycardJanitor));
            #endregion

            #region KeycardZoneManager picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardZoneManager, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardZoneManager));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardZoneManager, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardZoneManager));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardZoneManager, botPlayer), ItemType.KeycardZoneManager));

            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<ItemOfType>(ItemType.KeycardZoneManager, [ItemType.KeycardZoneManager], perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardZoneManager));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<ItemOfType>(ItemType.KeycardZoneManager, botPlayer), ItemType.KeycardZoneManager));

            mind.AddBelief(GetBelief(() => new ItemSpawnsInSightedLocker<ItemOfType>(ItemType.KeycardZoneManager, [ItemType.KeycardZoneManager], perception.GetSense<LockersWithinSightSense>()), ItemType.KeycardZoneManager));
            mind.AddAction(GetAction(() => new GoToLockerSpawnLocation<ItemOfType>(ItemType.KeycardZoneManager, StructureType.StandardLocker, botPlayer), ItemType.KeycardZoneManager));
            mind.AddAction(GetAction(() => new GoToItemSpawnInLocker<ItemOfType>(ItemType.KeycardZoneManager, botPlayer), ItemType.KeycardZoneManager));

            mind.AddAction(GetAction(() => new GoToSearchRoom<ItemOfType>(ItemType.KeycardZoneManager, 0, botPlayer), ItemType.KeycardZoneManager));
            #endregion

            #region KeycardScientist picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardScientist, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardScientist));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardScientist, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardScientist));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardScientist, botPlayer), ItemType.KeycardScientist));

            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<ItemOfType>(ItemType.KeycardScientist, [ItemType.KeycardScientist], perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardScientist));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<ItemOfType>(ItemType.KeycardScientist, botPlayer), ItemType.KeycardScientist));

            mind.AddBelief(GetBelief(() => new ItemSpawnsInSightedLocker<ItemOfType>(ItemType.KeycardScientist, [ItemType.KeycardScientist], perception.GetSense<LockersWithinSightSense>()), ItemType.KeycardScientist));
            mind.AddAction(GetAction(() => new GoToLockerSpawnLocation<ItemOfType>(ItemType.KeycardScientist, StructureType.StandardLocker, botPlayer), ItemType.KeycardScientist));
            mind.AddAction(GetAction(() => new GoToItemSpawnInLocker<ItemOfType>(ItemType.KeycardScientist, botPlayer), ItemType.KeycardScientist));

            mind.AddAction(GetAction(() => new GoToSearchRoom<ItemOfType>(ItemType.KeycardScientist, 0, botPlayer), ItemType.KeycardScientist));
            #endregion

            #region KeycardResearchCoordinator picking up
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardResearchCoordinator, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardResearchCoordinator));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardResearchCoordinator, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardResearchCoordinator));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardResearchCoordinator, botPlayer), ItemType.KeycardResearchCoordinator));
            #endregion

            #region KeycardMTFOperative picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardMTFOperative, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardMTFOperative));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardMTFOperative, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardMTFOperative));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardMTFOperative, botPlayer), ItemType.KeycardMTFOperative));

            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<ItemOfType>(ItemType.KeycardMTFOperative, [ItemType.KeycardMTFOperative], perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardMTFOperative));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<ItemOfType>(ItemType.KeycardMTFOperative, botPlayer), ItemType.KeycardMTFOperative));

            mind.AddAction(GetAction(() => new GoToSearchRoom<ItemOfType>(ItemType.KeycardMTFOperative, FacilityZone.HeavyContainment, 0, botPlayer), ItemType.KeycardMTFOperative));
            #endregion

            #region KeycardMTFCaptain picking up
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardMTFCaptain, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardMTFCaptain));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardMTFCaptain, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardMTFCaptain));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardMTFCaptain, botPlayer), ItemType.KeycardMTFCaptain));
            #endregion

            #region KeycardChaosInsurgency picking up
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardChaosInsurgency, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardChaosInsurgency));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardChaosInsurgency, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardChaosInsurgency));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardChaosInsurgency, botPlayer), ItemType.KeycardChaosInsurgency));
            #endregion

            #region KeycardFacilityManager picking up
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardFacilityManager, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardFacilityManager));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardFacilityManager, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardFacilityManager));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardFacilityManager, botPlayer), ItemType.KeycardFacilityManager));
            #endregion

            #region KeycardO5 picking up
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(ItemType.KeycardO5, perception.GetSense<ItemsWithinSightSense>()), ItemType.KeycardO5));
            mind.AddBelief(GetBelief(() => new ItemInInventory<ItemOfType>(ItemType.KeycardO5, perception.GetSense<ItemsInInventorySense>()), ItemType.KeycardO5));
            mind.AddAction(GetAction(() => new GoToPickupItem<ItemOfType>(ItemType.KeycardO5, botPlayer), ItemType.KeycardO5));
            #endregion


            #region ContainmentLevelOne keycard picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            mind.AddBelief(GetBelief(() => new ItemInInventory<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), perception.GetSense<ItemsInInventorySense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            mind.AddAction(GetAction(() => new GoToPickupItem<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));

            ItemType[] containmentLevelOneSpawnItemTypes =
            [
                ItemType.KeycardJanitor, ItemType.KeycardZoneManager, ItemType.KeycardScientist, ItemType.KeycardResearchCoordinator,
                ItemType.KeycardGuard, ItemType.KeycardMTFPrivate, ItemType.KeycardMTFOperative, ItemType.KeycardMTFCaptain, ItemType.KeycardChaosInsurgency,
                ItemType.KeycardContainmentEngineer, ItemType.KeycardFacilityManager, ItemType.KeycardO5
            ];
            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), containmentLevelOneSpawnItemTypes, perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));

            mind.AddBelief(GetBelief(() => new ItemSpawnsInSightedLocker<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), containmentLevelOneSpawnItemTypes, perception.GetSense<LockersWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            mind.AddAction(GetAction(() => new GoToLockerSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), StructureType.StandardLocker, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            mind.AddAction(GetAction(() => new GoToItemSpawnInLocker<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            
            mind.AddAction(GetAction(() => new GoToSearchRoom<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelOne), 0, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelOne)));
            #endregion

            #region ContainmentLevelTwo keycard picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            mind.AddBelief(GetBelief(() => new ItemInInventory<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), perception.GetSense<ItemsInInventorySense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            mind.AddAction(GetAction(() => new GoToPickupItem<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));

            ItemType[] containmentLevelTwoSpawnItemTypes =
            [
                ItemType.KeycardScientist, ItemType.KeycardResearchCoordinator,
                ItemType.KeycardMTFPrivate, ItemType.KeycardMTFOperative, ItemType.KeycardMTFCaptain, ItemType.KeycardChaosInsurgency,
                ItemType.KeycardContainmentEngineer, ItemType.KeycardFacilityManager, ItemType.KeycardO5
            ];
            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), containmentLevelTwoSpawnItemTypes, perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));

            mind.AddBelief(GetBelief(() => new ItemSpawnsInSightedLocker<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), containmentLevelTwoSpawnItemTypes, perception.GetSense<LockersWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            mind.AddAction(GetAction(() => new GoToLockerSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), StructureType.StandardLocker, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            mind.AddAction(GetAction(() => new GoToItemSpawnInLocker<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            
            mind.AddAction(GetAction(() => new GoToSearchRoom<KeycardWithPermissions>(new(DoorPermissionFlags.ContainmentLevelTwo), 0, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ContainmentLevelTwo)));
            #endregion

            #region Checkpoints keycard picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            mind.AddBelief(GetBelief(() => new ItemInInventory<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), perception.GetSense<ItemsInInventorySense>()), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            mind.AddAction(GetAction(() => new GoToPickupItem<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));

            ItemType[] checkpointsSpawnItemTypes =
            [
                ItemType.KeycardZoneManager, ItemType.KeycardResearchCoordinator,
                ItemType.KeycardGuard, ItemType.KeycardMTFPrivate, ItemType.KeycardMTFOperative, ItemType.KeycardMTFCaptain, ItemType.KeycardChaosInsurgency,
                ItemType.KeycardContainmentEngineer, ItemType.KeycardFacilityManager, ItemType.KeycardO5
            ];
            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), checkpointsSpawnItemTypes, perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));

            mind.AddBelief(GetBelief(() => new ItemSpawnsInSightedLocker<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), checkpointsSpawnItemTypes, perception.GetSense<LockersWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            mind.AddAction(GetAction(() => new GoToLockerSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), StructureType.StandardLocker, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            mind.AddAction(GetAction(() => new GoToItemSpawnInLocker<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            
            mind.AddAction(GetAction(() => new GoToSearchRoom<KeycardWithPermissions>(new(DoorPermissionFlags.Checkpoints), 0, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.Checkpoints)));
            #endregion

            #region ExitGates keycard picking up and searching
            mind.AddBelief(GetBelief(() => new ItemSightedLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ExitGates), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ExitGates)));
            mind.AddBelief(GetBelief(() => new ItemInInventory<KeycardWithPermissions>(new(DoorPermissionFlags.ExitGates), perception.GetSense<ItemsInInventorySense>()), new KeycardWithPermissions(DoorPermissionFlags.ExitGates)));
            mind.AddAction(GetAction(() => new GoToPickupItem<KeycardWithPermissions>(new(DoorPermissionFlags.ExitGates), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ExitGates)));

            ItemType[] exitGatesSpawnItemTypes =
            [
                ItemType.KeycardMTFOperative, ItemType.KeycardMTFCaptain, ItemType.KeycardChaosInsurgency,
                ItemType.KeycardFacilityManager, ItemType.KeycardO5
            ];
            mind.AddBelief(GetBelief(() => new ItemSpawnsLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ExitGates), exitGatesSpawnItemTypes, perception.GetSense<RoomSightSense>(), perception.GetSense<ItemsWithinSightSense>()), new KeycardWithPermissions(DoorPermissionFlags.ExitGates)));
            mind.AddAction(GetAction(() => new GoToItemSpawnLocation<KeycardWithPermissions>(new(DoorPermissionFlags.ExitGates), botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ExitGates)));
            
            mind.AddAction(GetAction(() => new GoToSearchRoom<KeycardWithPermissions>(new(DoorPermissionFlags.ExitGates), FacilityZone.HeavyContainment, 0, botPlayer), new KeycardWithPermissions(DoorPermissionFlags.ExitGates)));
            #endregion

            #region Opening keycard doors
            var navigationBeliefs = mind.GetBelief<NavigationBeliefs>();
            foreach (var cell in navigationBeliefs.Obstacles.Keys)
            {
                mind.AddAction(GetAction(() => new OpenKeycardDoorObstacle(DoorPermissionFlags.ContainmentLevelOne, cell, navigationBeliefs, botPlayer), DoorPermissionFlags.ContainmentLevelOne, cell));
                mind.AddAction(GetAction(() => new OpenKeycardDoorObstacle(DoorPermissionFlags.ContainmentLevelTwo, cell, navigationBeliefs, botPlayer), DoorPermissionFlags.ContainmentLevelTwo, cell));
                mind.AddAction(GetAction(() => new OpenKeycardDoorObstacle(DoorPermissionFlags.Checkpoints, cell, navigationBeliefs, botPlayer), DoorPermissionFlags.Checkpoints, cell));
                mind.AddAction(GetAction(() => new OpenKeycardDoorObstacle(DoorPermissionFlags.ExitGates, cell, navigationBeliefs, botPlayer), DoorPermissionFlags.ExitGates, cell));
            }
            #endregion

            mind.AddBelief(GetBelief(() => new ItemSightedLocation<ItemOfType>(new(ItemType.Medkit), perception.GetSense<ItemsWithinSightSense>()), new ItemOfType(ItemType.Medkit)));
        }

        private void AddNavigation(FpcMind mind)
        {
            var cellWithin = GetBelief(() => new CellWithin(botPlayer));
            mind.AddBelief(cellWithin);

            var navigationBeliefs = GetBelief(() => new NavigationBeliefs(botPlayer.MindRunner));
            mind.AddBelief(navigationBeliefs);

            var sightSense = perception.GetSense<DoorsWithinSightSense>();
            foreach (var (room, mesh) in NavigationMesh.LocalMeshesByRoom)
            {
                foreach (var cell in mesh.Cells.Select(c => new TransformCell(c, room.transform)))
                {
                    mind.AddBelief(
                        GetBelief(() =>
                        {
                            var newNavCell = new NavigationCell(cell, cellWithin);
                            navigationBeliefs.NavigationCells.Add(cell, newNavCell);
                            return newNavCell;
                        },
                        cell)
                    );
                }
            }

            foreach (var (cell, transform) in NavigationMesh.CellsWithObstacles)
            {
                mind.AddBelief(
                    GetBelief(() =>
                    {
                        var obstacleLayerMask = LayerMask.GetMask("Door");
                        var position = transform.position + Vector3.up;
                        var colliders = transform.GetComponentsInChildren<Collider>().ToHashSet();

                        var newObstacle = new Obstacle(cell, position, colliders, sightSense, obstacleLayerMask);

                        navigationBeliefs.Obstacles.Add(cell, newObstacle);
                        newObstacle.OnUpdate += () => navigationBeliefs.HandleObstacleUpdate(newObstacle);

                        return newObstacle;
                    },
                    cell)
                );
            }


            mind.AddBelief(GetBelief(() => new ZoneWithin(FacilityZone.Surface, cellWithin, botPlayer.Navigator), FacilityZone.Surface));
            mind.AddBelief(GetBelief(() => new ZoneWithin(FacilityZone.Entrance, cellWithin, botPlayer.Navigator), FacilityZone.Entrance));
            mind.AddBelief(GetBelief(() => new ZoneWithin(FacilityZone.HeavyContainment, cellWithin, botPlayer.Navigator), FacilityZone.HeavyContainment));
            mind.AddBelief(GetBelief(() => new ZoneWithin(FacilityZone.LightContainment, cellWithin, botPlayer.Navigator), FacilityZone.LightContainment));
            mind.AddBelief(GetBelief(() => new ZoneEnterLocation(FacilityZone.LightContainment, FacilityZone.HeavyContainment, perception.GetSense<RoomSightSense>()), FacilityZone.LightContainment, FacilityZone.HeavyContainment));
            mind.AddBelief(GetBelief(() => new ZoneEnterLocation(FacilityZone.HeavyContainment, FacilityZone.LightContainment, perception.GetSense<RoomSightSense>()), FacilityZone.HeavyContainment, FacilityZone.LightContainment));
            mind.AddBelief(GetBelief(() => new ZoneEnterLocation(FacilityZone.HeavyContainment, FacilityZone.Entrance, perception.GetSense<RoomSightSense>()), FacilityZone.HeavyContainment, FacilityZone.Entrance));
            mind.AddBelief(GetBelief(() => new ZoneEnterLocation(FacilityZone.Entrance, FacilityZone.HeavyContainment, perception.GetSense<RoomSightSense>()), FacilityZone.Entrance, FacilityZone.HeavyContainment));
            mind.AddBelief(GetBelief(() => new ZoneEnterLocation(FacilityZone.Entrance, FacilityZone.Surface, perception.GetSense<RoomSightSense>()), FacilityZone.Entrance, FacilityZone.Surface));
            mind.AddBelief(GetBelief(() => new ZoneEnterLocation(FacilityZone.Surface, FacilityZone.Entrance, perception.GetSense<RoomSightSense>()), FacilityZone.Surface, FacilityZone.Entrance));

            mind.AddAction(GetAction(() => new GoToZoneEnterLocation(FacilityZone.LightContainment, FacilityZone.HeavyContainment, botPlayer), FacilityZone.LightContainment, FacilityZone.HeavyContainment));
            mind.AddAction(GetAction(() => new GoToZoneEnterLocation(FacilityZone.HeavyContainment, FacilityZone.LightContainment, botPlayer), FacilityZone.HeavyContainment, FacilityZone.LightContainment));
            mind.AddAction(GetAction(() => new GoToZoneEnterLocation(FacilityZone.HeavyContainment, FacilityZone.Entrance, botPlayer), FacilityZone.HeavyContainment, FacilityZone.Entrance));
            mind.AddAction(GetAction(() => new GoToZoneEnterLocation(FacilityZone.Entrance, FacilityZone.HeavyContainment, botPlayer), FacilityZone.Entrance, FacilityZone.HeavyContainment));
            mind.AddAction(GetAction(() => new GoToZoneEnterLocation(FacilityZone.Entrance, FacilityZone.Surface, botPlayer), FacilityZone.Entrance, FacilityZone.Surface));
            mind.AddAction(GetAction(() => new GoToZoneEnterLocation(FacilityZone.Surface, FacilityZone.Entrance, botPlayer), FacilityZone.Surface, FacilityZone.Entrance));

            mind.AddBelief(GetBelief(() => new RoomEnterLocation(perception.GetSense<RoomSightSense>())));
            mind.AddAction(GetAction(() => new GoToSearchRoomForZoneEnterLocation(FacilityZone.LightContainment, FacilityZone.HeavyContainment, botPlayer), FacilityZone.LightContainment, FacilityZone.HeavyContainment));
            mind.AddAction(GetAction(() => new GoToSearchRoomForZoneEnterLocation(FacilityZone.HeavyContainment, FacilityZone.LightContainment, botPlayer), FacilityZone.HeavyContainment, FacilityZone.LightContainment));
            mind.AddAction(GetAction(() => new GoToSearchRoomForZoneEnterLocation(FacilityZone.HeavyContainment, FacilityZone.Entrance, botPlayer), FacilityZone.HeavyContainment, FacilityZone.Entrance));
            mind.AddAction(GetAction(() => new GoToSearchRoomForZoneEnterLocation(FacilityZone.Entrance, FacilityZone.HeavyContainment, botPlayer), FacilityZone.Entrance, FacilityZone.HeavyContainment));
            mind.AddAction(GetAction(() => new GoToSearchRoomForZoneEnterLocation(FacilityZone.Surface, FacilityZone.Entrance, botPlayer), FacilityZone.Surface, FacilityZone.Entrance));

            foreach (var toCell in navigationBeliefs.NavigationCells.Keys)
            {
                foreach (var (fromCell, fromEdge) in toCell.AdjacentCellEdges.Concat(NavigationMesh.ForeignConnectedCellEdges[toCell]))
                {
                    mind.AddAction(
                        GetAction(() =>
                        {
                            var toEdge = fromCell.AdjacentCellEdges.TryGetValue(toCell, out var roomEdge) ? roomEdge : NavigationMesh.ForeignConnectedCellEdges[fromCell][toCell];
                            var fromEdges = fromCell.AdjacentCellEdges.Keys
                                .Concat(NavigationMesh.ForeignConnectedCellEdges[fromCell].Keys)
                                .Select(from2Cell => from2Cell.AdjacentCellEdges.TryGetValue(fromCell, out var edge) ? edge : NavigationMesh.ForeignConnectedCellEdges[from2Cell][fromCell])
                                .Except([fromEdge])
                                .ToArray();

                            var fromZone = fromCell.Transform.GetComponent<RoomIdentifier>().Zone;

                            return new GoToCell(toCell, fromCell, toEdge, fromEdges, fromZone, botPlayer);
                        },
                        toCell, fromCell)
                    );
                }
            }


            foreach (var cell in navigationBeliefs.Obstacles.Keys)
            {
                mind.AddAction(GetAction(() => new OpenNonKeycardInteractableObstacle(cell, navigationBeliefs, botPlayer), cell));
            }


            foreach (var (cellZero, cellOne, panelTransformZero, panelTransformOne) in NavigationMesh.ElevationCells)
            {
                foreach (var (cellAtLevel, panelTransformAtLevel) in (ReadOnlySpan<(TransformCell, Transform)>)[(cellZero, panelTransformZero), (cellOne, panelTransformOne)])
                {
                    mind.AddBelief(
                        GetBelief(() =>
                        {
                            var elevatorLevelBelief = new ElevatorLevel(cellAtLevel, panelTransformAtLevel.position, panelTransformAtLevel.up, sightSense);
                            navigationBeliefs.ElevatorLevels.Add(cellAtLevel, elevatorLevelBelief);
                            return elevatorLevelBelief;
                        },
                        cellAtLevel)
                    );

                    mind.AddAction(GetAction(() => new CallAndWaitForElevator(cellAtLevel, botPlayer), cellAtLevel));
                }

                mind.AddAction(GetAction(() => new TravelOnElevator(cellZero, cellOne, botPlayer), cellZero, cellOne));
                mind.AddAction(GetAction(() => new TravelOnElevator(cellOne, cellZero, botPlayer), cellOne, cellZero));
            }
        }

        #region Mind elements flyweights

        private readonly Dictionary<Type, IDictionary> beliefsObjects = [];
        private readonly Dictionary<Type, IDictionary> actionsObjects = [];
        private readonly Dictionary<Type, IGoal> goalsObjects = [];

        private TBelief GetBelief<TBelief>(Func<TBelief> create) where TBelief : class, IBelief
        {
            var @params = new ValueTuple();

            return GetBeliefInternal(create, @params);
        }

        private TBelief GetBelief<TBelief, TParam>(Func<TBelief> create, TParam param) where TBelief : class, IBelief
        {
            var @params = new ValueTuple<TParam>(param);

            return GetBeliefInternal(create, @params);
        }

        private TBelief GetBelief<TBelief, TParam1, TParam2>(Func<TBelief> create, TParam1 param1, TParam2 param2) where TBelief : IBelief
        {
            var @params = (param1, param2);

            return GetBeliefInternal(create, @params);
        }

        private TBelief GetBeliefInternal<TBelief, TParams>(Func<TBelief> create, TParams @params) where TBelief : IBelief
        {
            if (!beliefsObjects.TryGetValue(typeof((TBelief, TParams)), out var paramsObject))
            {
                paramsObject = new Dictionary<TParams, TBelief>();
                beliefsObjects.Add(typeof((TBelief, TParams)), paramsObject);
            }

            var typeBeliefs = (Dictionary<TParams, TBelief>)paramsObject;
            if (!typeBeliefs.TryGetValue(@params, out var belief))
            {
                belief = create();
                typeBeliefs.Add(@params, belief);
                Beliefs.Add(belief);

                //Debug.Log($"Created flyweight belief {belief} with params {@params}");
            }
            else
            {
                //Debug.Log($"Found flyweight belief {belief} with params {@params}");
            }

            return belief;
        }

        private TAction GetAction<TAction>(Func<TAction> create) where TAction : class, IAction
        {
            var @params = new ValueTuple();

            return GetActionInternal(create, @params);
        }

        private TAction GetAction<TAction, TParam>(Func<TAction> create, TParam param) where TAction : IAction
        {
            var @params = new ValueTuple<TParam>(param);

            return GetActionInternal(create, @params);
        }

        private TAction GetAction<TAction, TParam1, TParam2>(Func<TAction> create, TParam1 param1, TParam2 param2) where TAction : IAction
        {
            var @params = (param1, param2);

            return GetActionInternal(create, @params);
        }

        private TAction GetAction<TAction, TParam1, TParam2, TParam3>(Func<TAction> create, TParam1 param1, TParam2 param2, TParam3 param3) where TAction : IAction
        {
            var @params = (param1, param2, param3);

            return GetActionInternal(create, @params);
        }

        private TAction GetActionInternal<TAction, TParams>(Func<TAction> create, TParams @params) where TAction : IAction
        {
            if (!actionsObjects.TryGetValue(typeof((TAction, TParams)), out var paramsObject))
            {
                paramsObject = new Dictionary<TParams, TAction>();
                actionsObjects.Add(typeof((TAction, TParams)), paramsObject);
            }

            var typeActions = (Dictionary<TParams, TAction>)paramsObject;
            if (!typeActions.TryGetValue(@params, out var action))
            {
                action = create();
                typeActions.Add(@params, action);

                //Debug.Log($"Created flyweight action {action} with params {@params}");
            }
            else
            {
                //Debug.Log($"Found flyweight action {action} with params {@params}");
            }

            return action;
        }

        private TGoal GetGoal<TGoal>(Func<TGoal> create) where TGoal : class, IGoal
        {
            if (!goalsObjects.TryGetValue(typeof(TGoal), out var goalObject))
            {
                goalObject = create();
                goalsObjects.Add(typeof(TGoal), goalObject);

                //Debug.Log($"Created flyweight goal {goalObject}");
            }
            else
            {
                //Debug.Log($"Found existing flyweight goal {goalObject}");
            }

            var goal = (TGoal)goalObject;

            return goal;
        }

        #endregion

        private const DoorPermissionFlags PermissionsCheckpointContainmentLevelOneTwo = DoorPermissionFlags.Checkpoints | DoorPermissionFlags.ContainmentLevelOne | DoorPermissionFlags.ContainmentLevelTwo;
    }
}
