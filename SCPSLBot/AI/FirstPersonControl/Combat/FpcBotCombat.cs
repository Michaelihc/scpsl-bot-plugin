using CustomPlayerEffects;
using Interactables;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Firearms.ShotEvents;
using MapGeneration;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp173;
using PlayerRoles.PlayableScps.Scp096;
using PlayerRoles.Subroutines;
using PlayerStatsSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Combat
{
    internal sealed class FpcBotCombat
    {
        private const float MaxTargetDistance = 36f;
        private const float HumanChaseDistance = 11f;
        private const float HumanRetreatDistance = 4.5f;
        private const float ScpAttackRange = 3.2f;
        private const float Scp049AttackRange = 2.4f;
        private const float Scp0492AttackRange = 2.4f;
        private const float Scp106AttackRange = 4f;
        private const float Scp173SnapRange = 1.45f;
        private const float Scp173BlinkRange = 8f;
        private const float Scp173BreakneckBlinkRange = 14.4f;
        private const float Scp173BlinkHoldSeconds = 0.28f;
        private const float Scp173BlinkAimAngle = 12f;
        private const float Scp173BreakneckChaseDistance = 9f;
        private const float ScpDamageStrafeSeconds = 1f;
        private const float SurfaceTargetDistance = 1000f;
        private const float StrictScpAttackAimAngle = 14f;
        private const float ScpAttackCooldown = 0.85f;
        private const float Scp049SenseCooldown = 4f;
        private const float Scp096RageDelay = 6.6f;
        private const float Scp096RageHoldSeconds = 0.65f;
        private const float Scp096PostRageCooldown = 6.5f;
        private const float DoorInteractDistance = 2f;
        private const float TargetSwitchLockSeconds = 2f;
        private const float TargetSwitchDistanceRatio = 0.65f;
        public static BotCombatDifficulty Difficulty { get; set; } = BotCombatDifficulty.Hardest;

        private readonly FpcBotPlayer botPlayer;
        private readonly List<DummyAction> dummyActions = new();
        private int dummyActionsFrame = -1;
        private readonly System.Random random = new();

        private ReferenceHub currentTarget;
        private float currentTargetChaseUntil;
        private float currentTargetSelectedTime;
        private float nextStrafeFlipTime;
        private float nextShotTime;
        private float nextReloadAttemptTime;
        private float nextScp049SenseTime;
        private float scp096AttackAllowedTime;
        private float scp096RageEndTime;
        private float scp096RageReleaseTime;
        private float scp096NextRageAllowedTime;
        private float nextScp096DebugLogTime;
        private float nextSurfaceDoorDebugLogTime;
        private ReferenceHub scp096RageTarget;
        private bool scp173BlinkHeld;
        private bool scp173BreakneckLikelyActive;
        private Vector3 scp173HeldBlinkAimPoint;
        private float scpDamageStrafeUntil;
        private float scp173BlinkReleaseTime;
        private float scp173BreakneckDisableAllowedTime;
        private int strafeDirection = 1;

        public FpcBotCombat(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        public void NotifyDamagedBy(ReferenceHub attacker)
        {
            var botHub = botPlayer.BotHub.PlayerHub;
            if (botHub.roleManager.CurrentRole.Team != Team.SCPs || !AreHostile(botHub, attacker))
            {
                return;
            }

            currentTarget = attacker;
            currentTargetSelectedTime = Time.time;
            currentTargetChaseUntil = Time.time + BotCombatDifficultySettings.For(Difficulty).ChaseAfterLostLosSeconds;
            scpDamageStrafeUntil = Time.time + ScpDamageStrafeSeconds;
            nextStrafeFlipTime = 0f;
        }

        public bool Tick()
        {
            var settings = BotCombatDifficultySettings.For(Difficulty);
            var botHub = botPlayer.BotHub.PlayerHub;
            var role = botHub.roleManager.CurrentRole.RoleTypeId;
            EndScp096RageIfExpired(role);
            ReleaseHeldScp173BlinkIfNeeded(role);

            if (TrySelectScp096RageTarget(role, out var target))
            {
                SetCurrentTarget(target.Hub, resetCombatTiming: currentTarget != target.Hub);
                currentTargetChaseUntil = Time.time + settings.ChaseAfterLostLosSeconds;
            }
            else if (TrySelectVisibleTarget(out target))
            {
                if (currentTarget != target.Hub)
                {
                    SetCurrentTarget(target.Hub, resetCombatTiming: true);
                }

                currentTargetChaseUntil = Time.time + settings.ChaseAfterLostLosSeconds;
            }
            else if (!TrySelectRememberedTarget(out target) && !TrySelectSurfaceTarget(out target))
            {
                currentTarget = null;
                currentTargetSelectedTime = 0f;
                ClearScp096RageTarget();
                return false;
            }
            else if (target != null && currentTarget != target.Hub)
            {
                SetCurrentTarget(target.Hub, resetCombatTiming: true);
                currentTargetChaseUntil = Time.time + settings.ChaseAfterLostLosSeconds;
            }

            UpdateStrafeDirection();
            ReleaseHeldScp096RageKeyIfNeeded();

            var isScp = botHub.roleManager.CurrentRole.Team == Team.SCPs;

            if (isScp)
            {
                RunScpCombat(target, role);
            }
            else
            {
                RunHumanCombat(target);
            }

            return true;
        }

        private void SetCurrentTarget(ReferenceHub target, bool resetCombatTiming)
        {
            currentTarget = target;
            currentTargetSelectedTime = Time.time;

            if (!resetCombatTiming)
            {
                return;
            }

            nextStrafeFlipTime = 0f;
            nextShotTime = 0f;
        }

        private void RunHumanCombat(CombatTarget target)
        {
            var firearm = EnsureFirearmEquipped();
            var targetPosition = target.Hub.transform.position;
            var surfaceDoorBlockingTarget = OpenSurfaceDoorTowardTarget(target.Hub);

            if (surfaceDoorBlockingTarget || target.Distance > HumanChaseDistance)
            {
                MoveToCombatPosition(targetPosition);
            }
            else
            {
                StrafeAroundTarget(targetPosition, target.Distance, HumanRetreatDistance, HumanChaseDistance);
            }

            botPlayer.LookToPosition(target.AimPoint);

            if (!target.HasLineOfSight)
            {
                return;
            }

            if (firearm == null || Time.time < nextShotTime)
            {
                return;
            }

            var settings = BotCombatDifficultySettings.For(Difficulty);

            PrepareActionForShot(firearm);

            if (IsReloading(firearm))
            {
                return;
            }

            if (ShouldReload(firearm))
            {
                TryReloadNormally();
                return;
            }

            if (!IsAimedAt(target.AimPoint, settings.AimAngleDegrees))
            {
                return;
            }

            nextShotTime = Time.time + settings.ShotCooldownSeconds;

            if (!TryClickDummyShoot())
            {
                FireDirectly(firearm, target.Hub);
            }
        }

        private void RunScpCombat(CombatTarget target, RoleTypeId role)
        {
            var targetPosition = target.Hub.transform.position;
            MoveToCombatPosition(targetPosition);
            OpenSurfaceDoorTowardTarget(target.Hub);
            ApplyScpDamageStrafe(targetPosition);

            var aimPoint = GetScpAimPoint(target);
            botPlayer.LookToPosition(aimPoint);

            if (role == RoleTypeId.Scp173)
            {
                RunScp173Combat(target);
                return;
            }

            TryUseScpChaseAbility(role);

            if (TryUseStrictScpAttackAbility(role, target))
            {
                return;
            }

            var attackRange = GetScpAttackRange(role);
            var closeScp096Target = role == RoleTypeId.Scp096 && target.Distance <= attackRange;
            if (!target.HasLineOfSight && !closeScp096Target)
            {
                return;
            }

            if (role == RoleTypeId.Scp096 && !IsScp096ReadyToAttack(target.Hub))
            {
                return;
            }

            var maxAttackAimAngle = closeScp096Target ? 180f : 35f;
            if (target.Distance > attackRange || Time.time < nextShotTime || !IsAimedAt(aimPoint, maxAttackAimAngle))
            {
                return;
            }

            nextShotTime = Time.time + ScpAttackCooldown;
            if (!TryUseScpAttackAbility(role, target.Hub) && role == RoleTypeId.Scp096)
            {
                LogScp096Debug("attack dummy action failed or was unavailable.");
            }
        }

        private bool TryUseStrictScpAttackAbility(RoleTypeId role, CombatTarget target)
        {
            if (role is not (RoleTypeId.Scp049 or RoleTypeId.Scp0492 or RoleTypeId.Scp106))
            {
                return false;
            }

            if (target.Distance > GetScpAttackRange(role) || Time.time < nextShotTime)
            {
                return true;
            }

            if (!IsAimedAt(GetScpAimPoint(target), StrictScpAttackAimAngle))
            {
                return true;
            }

            nextShotTime = Time.time + ScpAttackCooldown;
            TryUseScpAttackAbility(role, target.Hub);
            return true;
        }

        private static Vector3 GetScpAimPoint(CombatTarget target)
        {
            var bodyBase = target.Hub.transform.position;
            var camera = target.Hub.PlayerCameraReference;
            var head = camera != null ? camera.position : bodyBase + Vector3.up * 1.65f;
            return Vector3.Lerp(bodyBase, head, 0.55f);
        }

        private void RunScp173Combat(CombatTarget target)
        {
            var targetPosition = target.Hub.transform.position;
            var observed = IsScp173Observed();
            var breakneckActive = IsScp173BreakneckActive();

            scp173BreakneckLikelyActive = breakneckActive || scp173BreakneckLikelyActive && Time.time < scp173BreakneckDisableAllowedTime + 12f;

            if (scp173BlinkHeld)
            {
                botPlayer.LookToPosition(scp173HeldBlinkAimPoint);
                return;
            }

            if (target.Distance <= 3f && !observed)
            {
                TryClickFirstDummyAction(Scp173TantrumActions);
            }

            if (breakneckActive && target.Distance <= Scp173SnapRange + 0.4f && Time.time >= scp173BreakneckDisableAllowedTime)
            {
                if (TryClickFirstDummyAction(Scp173BreakneckActions))
                {
                    scp173BreakneckLikelyActive = false;
                }

                return;
            }

            if (target.Distance <= Scp173SnapRange)
            {
                botPlayer.LookToPosition(GetScpAimPoint(target));

                if (!observed && !breakneckActive && Time.time >= nextShotTime && IsAimedAt(GetScpAimPoint(target), StrictScpAttackAimAngle))
                {
                    nextShotTime = Time.time + ScpAttackCooldown;
                    TryClickFirstDummyAction(Scp173SnapActions);
                }

                return;
            }

            if (target.Distance > Scp173BreakneckChaseDistance && !breakneckActive && TryGetScp173Breakneck(out var breakneck) && breakneck.Cooldown.IsReady)
            {
                if (TryClickFirstDummyAction(Scp173BreakneckActions))
                {
                    scp173BreakneckLikelyActive = true;
                    scp173BreakneckDisableAllowedTime = Time.time + 1.2f;
                }
            }

            var blinkRange = breakneckActive || scp173BreakneckLikelyActive ? Scp173BreakneckBlinkRange : Scp173BlinkRange;
            var shouldBlinkForward = target.Distance > blinkRange;
            if ((!target.HasLineOfSight && !shouldBlinkForward) || !IsScp173BlinkReady())
            {
                botPlayer.LookToPosition(GetScpAimPoint(target));
                return;
            }

            var blinkAimPoint = shouldBlinkForward
                ? GetScp173ForwardBlinkAimPoint(target, blinkRange)
                : GetScp173BlinkAimPoint(target);
            botPlayer.LookToPosition(blinkAimPoint);
            if (!IsAimedAt(blinkAimPoint, Scp173BlinkAimAngle))
            {
                return;
            }

            if (TryClickGroupedDummyAction("Scp173TeleportAbility", "Zoom->Hold"))
            {
                scp173BlinkHeld = true;
                scp173HeldBlinkAimPoint = blinkAimPoint;
                scp173BlinkReleaseTime = Time.time + Scp173BlinkHoldSeconds;
            }
        }

        private void ReleaseHeldScp173BlinkIfNeeded(RoleTypeId role)
        {
            if (!scp173BlinkHeld)
            {
                return;
            }

            if (role != RoleTypeId.Scp173)
            {
                scp173BlinkHeld = false;
                scp173HeldBlinkAimPoint = default;
                scp173BlinkReleaseTime = 0f;
                return;
            }

            if (Time.time < scp173BlinkReleaseTime)
            {
                return;
            }

            TryClickGroupedDummyAction("Scp173TeleportAbility", "Zoom->Release");
            scp173BlinkHeld = false;
            scp173HeldBlinkAimPoint = default;
            scp173BlinkReleaseTime = 0f;
        }

        private Vector3 GetScp173BlinkAimPoint(CombatTarget target)
        {
            var targetPosition = target.Hub.transform.position;
            var awayFromTarget = Vector3.ProjectOnPlane(botPlayer.PlayerPosition - targetPosition, Vector3.up);
            if (awayFromTarget.sqrMagnitude < 0.01f)
            {
                awayFromTarget = -botPlayer.PlayerForward;
            }

            return targetPosition + awayFromTarget.normalized * 0.55f + Vector3.up * 0.15f;
        }

        private Vector3 GetScp173ForwardBlinkAimPoint(CombatTarget target, float blinkRange)
        {
            var direction = Vector3.ProjectOnPlane(target.Hub.transform.position - botPlayer.PlayerPosition, Vector3.up);
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector3.ProjectOnPlane(botPlayer.PlayerForward, Vector3.up);
            }

            var forwardDistance = Mathf.Max(Scp173SnapRange + 0.5f, blinkRange + 2.25f);
            return botPlayer.PlayerPosition + direction.normalized * forwardDistance + Vector3.up * 0.2f;
        }

        private void ApplyScpDamageStrafe(Vector3 targetPosition)
        {
            if (Time.time >= scpDamageStrafeUntil)
            {
                return;
            }

            var toTarget = Vector3.ProjectOnPlane(targetPosition - botPlayer.PlayerPosition, Vector3.up);
            if (toTarget.sqrMagnitude < 0.01f)
            {
                return;
            }

            var forward = toTarget.normalized;
            var right = Vector3.Cross(Vector3.up, forward).normalized * strafeDirection;
            var currentWorldMove = Vector3.ProjectOnPlane(
                botPlayer.FpcRole.FpcModule.transform.TransformDirection(botPlayer.Move.DesiredLocalDirection),
                Vector3.up);

            if (currentWorldMove.sqrMagnitude < 0.01f)
            {
                currentWorldMove = forward;
            }

            var settings = BotCombatDifficultySettings.For(Difficulty);
            var worldMove = Vector3.Normalize(currentWorldMove.normalized + right * settings.StrafeSpeed);
            botPlayer.Move.DesiredLocalDirection = botPlayer.FpcRole.FpcModule.transform.InverseTransformDirection(worldMove);
        }

        private bool IsScp173Observed()
        {
            return TryGetScp173Subroutine<Scp173ObserversTracker>(out var observers) && observers.IsObserved;
        }

        private bool IsScp173BlinkReady()
        {
            return TryGetScp173Subroutine<Scp173BlinkTimer>(out var blinkTimer) && blinkTimer.AbilityReady;
        }

        private bool IsScp173BreakneckActive()
        {
            return TryGetScp173Breakneck(out var breakneck) && breakneck.IsActive;
        }

        private bool TryGetScp173Breakneck(out Scp173BreakneckSpeedsAbility breakneck)
        {
            return TryGetScp173Subroutine(out breakneck);
        }

        private bool TryGetScp173Subroutine<T>(out T subroutine) where T : SubroutineBase
        {
            if (botPlayer.BotHub.PlayerHub.roleManager.CurrentRole is Scp173Role scp173Role)
            {
                return scp173Role.SubroutineModule.TryGetSubroutine(out subroutine);
            }

            subroutine = null;
            return false;
        }

        private void MoveToCombatPosition(Vector3 targetPosition)
        {
            botPlayer.MoveToPosition(targetPosition);
            OpenBlockingDoorOnCombatPath();
        }

        private void OpenBlockingDoorOnCombatPath()
        {
            foreach (var (point, nextPoint) in botPlayer.Navigator.PathSegments)
            {
                if (!Physics.Linecast(point, nextPoint, out var hit, LayerMask.GetMask("Door")))
                {
                    continue;
                }

                var door = hit.collider.GetComponentInParent<DoorVariant>();
                if (!CanOpenCombatDoor(door))
                {
                    continue;
                }

                var doorPlane = new Plane(door.transform.forward, door.transform.position);
                var distance = Mathf.Abs(doorPlane.GetDistanceToPoint(botPlayer.PlayerPosition));

                if (distance <= DoorInteractDistance)
                {
                    if (!botPlayer.OpenDoor(door, DoorInteractDistance))
                    {
                        botPlayer.LookToPosition(door.transform.position + Vector3.up);
                    }
                }
                else
                {
                    botPlayer.MoveToPosition(hit.point);
                }

                return;
            }
        }

        private static bool CanOpenCombatDoor(DoorVariant door)
        {
            return door
                   && !door.IsConsideredOpen()
                   && door is not DummyDoor
                   && door is not ElevatorDoor
                   && door is not BasicNonInteractableDoor;
        }

        private bool OpenSurfaceDoorTowardTarget(ReferenceHub target)
        {
            if (target == null
                || !IsOnSurface(botPlayer.PlayerPosition)
                || !IsOnSurface(target.transform.position))
            {
                return false;
            }

            var origin = botPlayer.PlayerPosition + Vector3.up * 0.8f;
            var destination = target.transform.position + Vector3.up * 0.8f;
            var direction = destination - origin;
            var distanceToTarget = direction.magnitude;
            if (distanceToTarget < 0.1f)
            {
                return false;
            }

            var hits = Physics.RaycastAll(
                origin,
                direction / distanceToTarget,
                distanceToTarget,
                LayerMask.GetMask("Door", "Glass"),
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            foreach (var hit in hits)
            {
                var door = hit.collider.GetComponentInParent<DoorVariant>();
                if (!CanOpenCombatDoor(door))
                {
                    continue;
                }

                var distanceToDoor = Vector3.Distance(botPlayer.PlayerPosition, hit.point);
                LogSurfaceDoorDebug($"blocked by {door.name} at {distanceToDoor:F1}m.");

                if (distanceToDoor <= DoorInteractDistance + 0.75f)
                {
                    botPlayer.LookToPosition(door.transform.position + Vector3.up);
                    if (!botPlayer.OpenDoor(door, DoorInteractDistance + 0.75f)
                        && !botPlayer.InteractDoorDirectly(door, DoorInteractDistance + 0.75f))
                    {
                        LogSurfaceDoorDebug($"interaction failed for {door.name}.");
                    }
                }
                else
                {
                    botPlayer.MoveToPosition(hit.point);
                }

                return true;
            }

            return false;
        }

        private void LogSurfaceDoorDebug(string message)
        {
            if (Time.time < nextSurfaceDoorDebugLogTime)
            {
                return;
            }

            nextSurfaceDoorDebugLogTime = Time.time + 1f;
            if (BotLog.Verbose) Debug.Log($"[SCPSLBot] Surface door: {message}");
        }

        private static float GetScpAttackRange(RoleTypeId role)
        {
            return role switch
            {
                RoleTypeId.Scp049 => Scp049AttackRange,
                RoleTypeId.Scp0492 => Scp0492AttackRange,
                RoleTypeId.Scp106 => Scp106AttackRange,
                _ => ScpAttackRange,
            };
        }

        private void TryUseScpChaseAbility(RoleTypeId role)
        {
            if (role != RoleTypeId.Scp049 || Time.time < nextScp049SenseTime)
            {
                return;
            }

            if (TryClickFirstDummyAction(Scp049SenseActions))
            {
                nextScp049SenseTime = Time.time + Scp049SenseCooldown;
            }
        }

        private bool IsScp096ReadyToAttack(ReferenceHub target)
        {
            if (Time.time < scp096NextRageAllowedTime)
            {
                LogScp096Debug("waiting for post-rage cooldown.");
                return false;
            }

            if (scp096RageTarget != target)
            {
                if (TryAttachScp096RageTarget(target))
                {
                    return Time.time >= scp096AttackAllowedTime;
                }

                scp096AttackAllowedTime = Time.time + Scp096RageDelay;
                var rageDuration = BotCombatDifficultySettings.For(Difficulty).Scp096RageDurationSeconds;
                if (!TryStartScp096Rage(target, rageDuration))
                {
                    ClearScp096RageTarget();
                    return false;
                }

                scp096RageTarget = target;
                scp096RageEndTime = scp096AttackAllowedTime + rageDuration;

                return false;
            }

            if (!TryEnsureScp096StillRaging(target))
            {
                return false;
            }

            return Time.time >= scp096AttackAllowedTime;
        }

        private bool TrySelectScp096RageTarget(RoleTypeId role, out CombatTarget target)
        {
            target = null;
            if (role != RoleTypeId.Scp096
                || scp096RageTarget == null
                || scp096RageEndTime <= 0f
                || Time.time >= scp096RageEndTime
                || !AreHostile(botPlayer.BotHub.PlayerHub, scp096RageTarget))
            {
                return false;
            }

            var distance = Vector3.Distance(botPlayer.PlayerPosition, scp096RageTarget.transform.position);
            target = BuildTarget(scp096RageTarget, distance) ?? BuildTarget(scp096RageTarget, distance, false);
            return true;
        }

        private void EndScp096RageIfExpired(RoleTypeId role)
        {
            if (role != RoleTypeId.Scp096 || scp096RageEndTime <= 0f || Time.time < scp096RageEndTime)
            {
                return;
            }

            TryEndScp096Rage();
            scp096NextRageAllowedTime = Time.time + Scp096PostRageCooldown;
            ClearScp096RageTarget();
            currentTargetChaseUntil = 0f;
        }

        private void ClearScp096RageTarget()
        {
            scp096RageTarget = null;
            scp096AttackAllowedTime = 0f;
            scp096RageEndTime = 0f;
            scp096RageReleaseTime = 0f;
        }

        private bool TryAttachScp096RageTarget(ReferenceHub target)
        {
            if (botPlayer.BotHub.PlayerHub.roleManager.CurrentRole is not Scp096Role scp096Role)
            {
                return false;
            }

            if (scp096Role.StateController.RageState is not (Scp096RageState.Enraged or Scp096RageState.Distressed))
            {
                return false;
            }

            AddScp096RageTarget(scp096Role, target);
            scp096RageTarget = target;

            if (scp096Role.StateController.RageState is Scp096RageState.Enraged)
            {
                scp096AttackAllowedTime = Time.time;
            }
            else if (scp096AttackAllowedTime <= Time.time)
            {
                scp096AttackAllowedTime = Time.time + 0.5f;
            }

            if (scp096RageEndTime <= Time.time)
            {
                scp096RageEndTime = Time.time + BotCombatDifficultySettings.For(Difficulty).Scp096RageDurationSeconds;
            }

            LogScp096Debug($"attached new rage target while {scp096Role.StateController.RageState}.");
            return true;
        }

        private bool TryEnsureScp096StillRaging(ReferenceHub target)
        {
            if (botPlayer.BotHub.PlayerHub.roleManager.CurrentRole is not Scp096Role scp096Role)
            {
                LogScp096Debug("current role is no longer Scp096.");
                return false;
            }

            AddScp096RageTarget(scp096Role, target);

            if (scp096Role.StateController.RageState is Scp096RageState.Enraged)
            {
                return true;
            }

            if (scp096Role.StateController.RageState is Scp096RageState.Distressed)
            {
                LogScp096Debug("is distressed, waiting for enraged state.");
                return false;
            }

            var rageDuration = BotCombatDifficultySettings.For(Difficulty).Scp096RageDurationSeconds;
            if (!TryStartScp096Rage(target, rageDuration))
            {
                LogScp096Debug($"could not restart rage from state {scp096Role.StateController.RageState}.");
                return false;
            }

            scp096AttackAllowedTime = Time.time + Scp096RageDelay;
            scp096RageEndTime = scp096AttackAllowedTime + rageDuration;
            LogScp096Debug("restarted rage after native state returned to docile.");
            return false;
        }

        private bool TryStartScp096Rage(ReferenceHub target, float duration)
        {
            if (botPlayer.BotHub.PlayerHub.roleManager.CurrentRole is not Scp096Role scp096Role)
            {
                LogScp096Debug("cannot rage because current role is not Scp096.");
                return false;
            }

            if (!scp096Role.SubroutineModule.TryGetSubroutine<Scp096RageManager>(out var rageManager))
            {
                LogScp096Debug("cannot rage because Scp096RageManager is unavailable.");
                return false;
            }

            if (scp096Role.StateController.RageState is not Scp096RageState.Docile)
            {
                LogScp096Debug($"cannot start rage from state {scp096Role.StateController.RageState}.");
                return false;
            }

            AddScp096RageTarget(scp096Role, target);

            rageManager.ServerEnrage(Mathf.Max(20f, duration));
            LogScp096Debug("started rage.");

            return true;
        }

        private static void AddScp096RageTarget(Scp096Role scp096Role, ReferenceHub target)
        {
            if (scp096Role.SubroutineModule.TryGetSubroutine<Scp096TargetsTracker>(out var targetsTracker))
            {
                targetsTracker.AddTarget(target, isLooking: true);
            }
        }

        private bool TryEndScp096Rage()
        {
            if (botPlayer.BotHub.PlayerHub.roleManager.CurrentRole is not Scp096Role scp096Role
                || !scp096Role.SubroutineModule.TryGetSubroutine<Scp096RageManager>(out var rageManager))
            {
                return false;
            }

            if (scp096Role.StateController.RageState is Scp096RageState.Enraged or Scp096RageState.Distressed)
            {
                rageManager.ServerEndEnrage();
            }

            return true;
        }

        private void TryEnableScp096RageInput()
        {
            var behaviours = botPlayer.FpcRole.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour.GetType().Name != "Scp096RageCycleAbility")
                {
                    continue;
                }

                behaviour.GetType()
                    .GetMethod("ServerTryEnableInput", new[] { typeof(float) })
                    ?.Invoke(behaviour, new object[] { 10f });
                return;
            }
        }

        private void ReleaseHeldScp096RageKeyIfNeeded()
        {
            if (scp096RageReleaseTime <= 0f || Time.time < scp096RageReleaseTime)
            {
                return;
            }

            scp096RageReleaseTime = 0f;
            TryClickGroupedDummyAction("Scp096RageCycleAbility", "Reload->Release");
        }

        private bool TryUseScpAttackAbility(RoleTypeId role, ReferenceHub target = null)
        {
            if (role == RoleTypeId.Scp096 && TryUseNativeScp096Attack(target))
            {
                return true;
            }

            if (role == RoleTypeId.Scp106 && TryForceScp106PocketOnCorrodingTarget())
            {
                return true;
            }

            var actions = role switch
            {
                RoleTypeId.Scp049 => Scp049AttackActions,
                RoleTypeId.Scp0492 => Scp0492AttackActions,
                RoleTypeId.Scp096 => Scp096AttackActions,
                RoleTypeId.Scp106 => Scp106AttackActions,
                RoleTypeId.Scp3114 => Scp3114AttackActions,
                RoleTypeId.Scp939 => Scp939AttackActions,
                _ => Array.Empty<string>(),
            };

            return TryClickFirstDummyAction(actions);
        }

        private bool TryUseNativeScp096Attack(ReferenceHub target)
        {
            if (botPlayer.BotHub.PlayerHub.roleManager.CurrentRole is not Scp096Role scp096Role)
            {
                return false;
            }

            if (scp096Role.StateController.RageState is not Scp096RageState.Enraged)
            {
                LogScp096Debug($"cannot attack while state is {scp096Role.StateController.RageState}.");
                return false;
            }

            if (scp096Role.StateController.AbilityState is not Scp096AbilityState.None)
            {
                return true;
            }

            if (!scp096Role.SubroutineModule.TryGetSubroutine<Scp096AttackAbility>(out var attackAbility))
            {
                LogScp096Debug("native attack ability is unavailable.");
                return false;
            }

            var serverAttack = typeof(Scp096AttackAbility).GetMethod("ServerAttack", BindingFlags.Instance | BindingFlags.NonPublic);
            if (serverAttack == null)
            {
                LogScp096Debug("native ServerAttack method was not found.");
                return false;
            }

            serverAttack.Invoke(attackAbility, null);

            var hitResultField = typeof(Scp096AttackAbility).GetField("_hitResult", BindingFlags.Instance | BindingFlags.NonPublic);
            var hitResult = hitResultField?.GetValue(attackAbility);
            if (hitResult is Scp096HitResult result && result == Scp096HitResult.None && target != null)
            {
                TryApplyDirectScp096CloseDamage(scp096Role, target);
            }

            return true;
        }

        private void TryApplyDirectScp096CloseDamage(Scp096Role scp096Role, ReferenceHub target)
        {
            if (target == null || !AreHostile(botPlayer.BotHub.PlayerHub, target))
            {
                return;
            }

            var distance = Vector3.Distance(botPlayer.PlayerPosition, target.transform.position);
            if (distance > ScpAttackRange)
            {
                return;
            }

            target.playerStats.DealDamage(new Scp096DamageHandler(scp096Role, 60f, Scp096DamageHandler.AttackType.SlapRight));
            LogScp096Debug("applied direct close-range 096 slap fallback.");
        }

        private void LogScp096Debug(string message)
        {
            if (Time.time < nextScp096DebugLogTime)
            {
                return;
            }

            nextScp096DebugLogTime = Time.time + 1f;
            if (BotLog.Verbose) Debug.Log($"[SCPSLBot] 096 {message}");
        }

        private bool TryForceScp106PocketOnCorrodingTarget()
        {
            if (currentTarget == null)
            {
                return false;
            }

            var effects = currentTarget.playerEffectsController;
            var corroding = effects.GetEffect<Corroding>();
            if (!corroding.IsEnabled)
            {
                return false;
            }

            corroding.AttackerHub = botPlayer.BotHub.PlayerHub;
            effects.EnableEffect<PocketCorroding>();
            return true;
        }

        private void StrafeAroundTarget(Vector3 targetPosition, float distance, float retreatDistance, float chaseDistance)
        {
            var settings = BotCombatDifficultySettings.For(Difficulty);
            var toTarget = Vector3.ProjectOnPlane(targetPosition - botPlayer.PlayerPosition, Vector3.up);
            if (toTarget.sqrMagnitude < 0.01f)
            {
                botPlayer.Move.DesiredLocalDirection = Vector3.zero;
                return;
            }

            var forward = toTarget.normalized;
            var right = Vector3.Cross(Vector3.up, forward).normalized * strafeDirection;
            var distanceBias = Vector3.zero;

            if (distance < retreatDistance)
            {
                distanceBias = -forward;
            }
            else if (distance > chaseDistance * 0.75f)
            {
                distanceBias = forward * 0.45f;
            }

            var worldMove = Vector3.Normalize(right + distanceBias) * settings.StrafeSpeed;
            botPlayer.Move.DesiredLocalDirection = botPlayer.FpcRole.FpcModule.transform.InverseTransformDirection(worldMove);
        }

        private bool TrySelectVisibleTarget(out CombatTarget target)
        {
            target = null;
            var botHub = botPlayer.BotHub.PlayerHub;
            var botPosition = botPlayer.PlayerPosition;

            CombatTarget best = null;
            CombatTarget currentVisible = null;
            foreach (var candidate in ReferenceHub.AllHubs)
            {
                if (!AreHostile(botHub, candidate))
                {
                    continue;
                }

                var distance = Vector3.Distance(botPosition, candidate.transform.position);
                if (distance > MaxTargetDistance)
                {
                    continue;
                }

                var selection = BuildTarget(candidate, distance);
                if (selection == null)
                {
                    continue;
                }

                if (candidate == currentTarget)
                {
                    currentVisible = selection;
                }

                if (best == null || selection.Distance < best.Distance)
                {
                    best = selection;
                }
            }

            target = SelectStickyVisibleTarget(best, currentVisible);
            return target != null;
        }

        private CombatTarget SelectStickyVisibleTarget(CombatTarget best, CombatTarget currentVisible)
        {
            if (best == null || currentVisible == null || best.Hub == currentTarget)
            {
                return best;
            }

            if (Time.time - currentTargetSelectedTime < TargetSwitchLockSeconds)
            {
                return currentVisible;
            }

            return best.Distance <= currentVisible.Distance * TargetSwitchDistanceRatio
                ? best
                : currentVisible;
        }

        private bool TrySelectSurfaceTarget(out CombatTarget target)
        {
            target = null;
            var botHub = botPlayer.BotHub.PlayerHub;
            if (!IsOnSurface(botPlayer.PlayerPosition))
            {
                return false;
            }

            CombatTarget best = null;
            foreach (var candidate in ReferenceHub.AllHubs)
            {
                if (!AreHostile(botHub, candidate) || !IsOnSurface(candidate.transform.position))
                {
                    continue;
                }

                var distance = Vector3.Distance(botPlayer.PlayerPosition, candidate.transform.position);
                if (distance > SurfaceTargetDistance)
                {
                    continue;
                }

                var selection = BuildTarget(candidate, distance, false);
                if (best == null || selection.Distance < best.Distance)
                {
                    best = selection;
                }
            }

            target = best;
            return target != null;
        }

        private bool TrySelectRememberedTarget(out CombatTarget target)
        {
            target = null;

            if (currentTarget == null || Time.time > currentTargetChaseUntil)
            {
                return false;
            }

            if (!AreHostile(botPlayer.BotHub.PlayerHub, currentTarget))
            {
                return false;
            }

            var distance = Vector3.Distance(botPlayer.PlayerPosition, currentTarget.transform.position);
            if (distance > MaxTargetDistance * 1.5f)
            {
                return false;
            }

            target = BuildTarget(currentTarget, distance, false);
            return true;
        }

        private CombatTarget BuildTarget(ReferenceHub hub, float distance, bool requireLineOfSight = true)
        {
            var bodyBase = hub.transform.position;
            var camera = hub.PlayerCameraReference;
            var headAimPoint = camera != null ? camera.position : bodyBase + Vector3.up * 1.65f;
            var torsoAimPoint = Vector3.Lerp(bodyBase, headAimPoint, 0.55f);
            var eyeOrigin = botPlayer.CameraPosition;

            if (!requireLineOfSight)
            {
                return new CombatTarget(hub, distance, torsoAimPoint, false);
            }

            if (HasLineOfSight(botPlayer.BotHub.PlayerHub, hub, eyeOrigin, headAimPoint))
            {
                return new CombatTarget(hub, distance, headAimPoint, true);
            }

            if (HasLineOfSight(botPlayer.BotHub.PlayerHub, hub, eyeOrigin, torsoAimPoint))
            {
                return new CombatTarget(hub, distance, torsoAimPoint, true);
            }

            return null;
        }

        private Firearm EnsureFirearmEquipped()
        {
            var inventory = botPlayer.BotHub.PlayerHub.inventory;
            if (inventory.CurInstance is Firearm currentFirearm)
            {
                return currentFirearm;
            }

            var firearm = inventory.UserInventory.Items.Values.OfType<Firearm>().FirstOrDefault();
            if (firearm == null)
            {
                firearm = inventory.ServerAddItem(ItemType.GunCOM15, ItemAddReason.AdminCommand) as Firearm;
            }

            if (firearm == null)
            {
                return null;
            }

            inventory.ServerSelectItem(firearm.ItemSerial);
            PrepareActionForShot(firearm);
            return firearm;
        }

        private void PrepareActionForShot(Firearm firearm)
        {
            if (firearm.TryGetModule<AutomaticActionModule>(out var action))
            {
                action.Cocked = true;
                if (!action.OpenBolt
                    && action.AmmoStored <= 0
                    && firearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo)
                    && primaryAmmo.AmmoStored > 0)
                {
                    action.ServerCycleAction();
                }

                action.BoltLocked = false;
                action.ServerResync();
            }
        }

        private static bool ShouldReload(Firearm firearm)
        {
            if (!firearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo))
            {
                return false;
            }

            if (GetLoadedAmmo(firearm, primaryAmmo) > 1)
            {
                return false;
            }

            return firearm.Owner.inventory.GetCurAmmo(primaryAmmo.AmmoType) > 0;
        }

        private static int GetLoadedAmmo(Firearm firearm, IPrimaryAmmoContainerModule primaryAmmo)
        {
            var loaded = primaryAmmo.AmmoStored;
            if (firearm.TryGetModule<AutomaticActionModule>(out var action))
            {
                loaded += action.AmmoStored;
            }

            return loaded;
        }

        private static bool IsReloading(Firearm firearm)
        {
            return firearm.TryGetModule<IReloaderModule>(out var reloader) && reloader.IsReloadingOrUnloading;
        }

        private void TryReloadNormally()
        {
            if (Time.time < nextReloadAttemptTime)
            {
                return;
            }

            nextReloadAttemptTime = Time.time + 0.45f;
            TryClickDummyAction("Reload->Click");
        }

        private bool TryClickDummyShoot()
        {
            return TryClickDummyAction("Shoot->Click");
        }

        private bool TryClickFirstDummyAction(IEnumerable<string> actionNames)
        {
            foreach (var actionName in actionNames)
            {
                if (TryClickCategorizedOrFlatDummyAction(actionName))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryClickCategorizedOrFlatDummyAction(string actionName)
        {
            var separatorIndex = actionName.IndexOf(':');
            if (separatorIndex > 0 && separatorIndex < actionName.Length - 1)
            {
                return TryClickGroupedDummyAction(actionName[..separatorIndex], actionName[(separatorIndex + 1)..]);
            }

            return TryClickDummyAction(actionName);
        }

        private bool TryHoldGroupedDummyAction(string categoryName, string actionName)
        {
            if (TryClickGroupedDummyAction(categoryName, $"{actionName}->Hold"))
            {
                return true;
            }

            return TryClickGroupedDummyAction(categoryName, $"{actionName}->Click");
        }

        // Rebuilds the dummy-action list at most once per frame. It was previously regenerated on
        // every shoot/reload/ability lookup (several per tick), each call allocating via
        // ServerGetActions + PopulateDummyActions. Invoking an action can change the available set,
        // so callers invalidate the cache (dummyActionsFrame = -1) after a successful Invoke.
        private void EnsureDummyActions()
        {
            if (dummyActionsFrame == Time.frameCount)
            {
                return;
            }

            dummyActionsFrame = Time.frameCount;
            dummyActions.Clear();
            dummyActions.AddRange(DummyActionCollector.ServerGetActions(botPlayer.BotHub.PlayerHub));
            botPlayer.BotHub.PlayerHub.inventory.PopulateDummyActions(dummyActions.Add, _ => { });
        }

        private bool TryClickDummyAction(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return false;
            }

            EnsureDummyActions();

            var dummyAction = FindDummyAction(actionName);
            if (dummyAction.Action == null)
            {
                return false;
            }

            dummyAction.Action.Invoke();
            dummyActionsFrame = -1;
            return true;
        }

        private bool TryClickGroupedDummyAction(string categoryName, string actionName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(actionName))
            {
                return false;
            }

            EnsureDummyActions();

            var dummyAction = FindDummyAction(categoryName, actionName);
            if (dummyAction.Action == null)
            {
                return false;
            }

            dummyAction.Action.Invoke();
            dummyActionsFrame = -1;
            return true;
        }

        private DummyAction FindDummyAction(string actionName)
        {
            foreach (var variant in GetActionNameVariants(actionName))
            {
                var exactMatch = dummyActions.FirstOrDefault(a => string.Equals(a.Name, variant, StringComparison.OrdinalIgnoreCase));
                if (exactMatch.Action != null)
                {
                    return exactMatch;
                }
            }

            var moduleName = GetDummyActionModuleName(actionName);
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return default;
            }

            return dummyActions
                .Where(a => !string.IsNullOrWhiteSpace(a.Name)
                            && a.Action != null
                            && a.Name.IndexOf(moduleName, StringComparison.OrdinalIgnoreCase) >= 0
                            && a.Name.IndexOf("Destroy", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(a => GetDummyActionScore(a.Name))
                .FirstOrDefault();
        }

        private DummyAction FindDummyAction(string categoryName, string actionName)
        {
            var categoryIndex = dummyActions.FindIndex(a => a.Action == null
                                                           && string.Equals(a.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (categoryIndex < 0)
            {
                categoryIndex = dummyActions.FindIndex(a => a.Action == null
                                                           && a.Name.IndexOf(categoryName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (categoryIndex < 0)
            {
                return default;
            }

            foreach (var variant in GetActionNameVariants(actionName))
            {
                for (var i = categoryIndex + 1; i < dummyActions.Count; i++)
                {
                    var action = dummyActions[i];
                    if (action.Action == null)
                    {
                        break;
                    }

                    if (string.Equals(action.Name, variant, StringComparison.OrdinalIgnoreCase))
                    {
                        return action;
                    }
                }
            }

            return default;
        }

        private static IEnumerable<string> GetActionNameVariants(string actionName)
        {
            var trimmed = actionName.Trim();
            yield return trimmed;

            if (trimmed.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                yield return trimmed.Replace("->", ".");
            }

            if (trimmed.IndexOf(".", StringComparison.Ordinal) >= 0)
            {
                yield return trimmed.Replace(".", "->");
            }
        }

        private static string GetDummyActionModuleName(string actionName)
        {
            var trimmed = actionName.Trim();
            var arrowIndex = trimmed.IndexOf("->", StringComparison.Ordinal);
            var dotIndex = trimmed.IndexOf(".", StringComparison.Ordinal);
            var splitIndex = arrowIndex >= 0 && dotIndex >= 0
                ? Math.Min(arrowIndex, dotIndex)
                : Math.Max(arrowIndex, dotIndex);

            return splitIndex >= 0 ? trimmed[..splitIndex] : trimmed;
        }

        private static int GetDummyActionScore(string actionName)
        {
            if (actionName.IndexOf("Selected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }

            if (actionName.IndexOf("Click", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            if (actionName.IndexOf("New", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            if (actionName.IndexOf("Hold", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3;
            }

            return 4;
        }

        private void FireDirectly(Firearm firearm, ReferenceHub target)
        {
            if (firearm.TryGetModule<IHitregModule>(out var hitreg))
            {
                hitreg.Fire(target, new BulletShotEvent(firearm.ItemId));
                return;
            }

            target.playerStats.DealDamage(new FirearmDamageHandler(firearm, 18f, 0.35f)
            {
                Hitbox = HitboxType.Body
            });
        }

        private void UpdateStrafeDirection()
        {
            var settings = BotCombatDifficultySettings.For(Difficulty);
            if (Time.time < nextStrafeFlipTime)
            {
                return;
            }

            strafeDirection = random.Next(0, 2) == 0 ? -1 : 1;
            nextStrafeFlipTime = Time.time + settings.MinStrafeFlipSeconds
                                 + (float)random.NextDouble() * (settings.MaxStrafeFlipSeconds - settings.MinStrafeFlipSeconds);
        }

        private bool IsAimedAt(Vector3 aimPoint, float maxAngle)
        {
            var direction = aimPoint - botPlayer.CameraPosition;
            if (direction.sqrMagnitude < 0.01f)
            {
                return true;
            }

            return Vector3.Angle(botPlayer.CameraForward, direction) <= maxAngle;
        }

        private static bool AreHostile(ReferenceHub bot, ReferenceHub candidate)
        {
            if (candidate == null || candidate == bot || !IsCombatTarget(candidate) || !IsCombatTarget(bot))
            {
                return false;
            }

            var botTeam = bot.roleManager.CurrentRole.Team;
            var candidateTeam = candidate.roleManager.CurrentRole.Team;

            if (botTeam == Team.SCPs)
            {
                return candidateTeam != Team.SCPs;
            }

            if (candidateTeam == Team.SCPs)
            {
                return true;
            }

            var botRole = bot.roleManager.CurrentRole.RoleTypeId;
            var candidateRole = candidate.roleManager.CurrentRole.RoleTypeId;

            if (IsFoundationHumanRole(botRole) && IsFoundationHumanRole(candidateRole))
            {
                return false;
            }

            if (IsChaosHumanRole(botRole) && IsChaosHumanRole(candidateRole))
            {
                return false;
            }

            return candidateTeam != botTeam;
        }

        private static bool IsCombatTarget(ReferenceHub hub)
        {
            if (hub == null)
            {
                return false;
            }

            var role = hub.roleManager.CurrentRole;
            return role.RoleTypeId != RoleTypeId.None
                   && role.RoleTypeId != RoleTypeId.Spectator
                   && role.Team != Team.Dead
                   && hub.IsAlive();
        }

        private static bool IsOnSurface(Vector3 position)
        {
            return RoomUtils.TryGetRoom(position, out var room) && room.Zone == FacilityZone.Surface;
        }

        private static bool IsFoundationHumanRole(RoleTypeId role)
        {
            return role is RoleTypeId.NtfCaptain
                or RoleTypeId.NtfPrivate
                or RoleTypeId.NtfSergeant
                or RoleTypeId.NtfSpecialist
                or RoleTypeId.FacilityGuard
                or RoleTypeId.Scientist;
        }

        private static bool IsChaosHumanRole(RoleTypeId role)
        {
            return role is RoleTypeId.ChaosConscript
                or RoleTypeId.ChaosMarauder
                or RoleTypeId.ChaosRepressor
                or RoleTypeId.ChaosRifleman
                or RoleTypeId.ClassD;
        }

        private static readonly int CombatVisionMask =
            LayerMask.GetMask("Default", "Door", "InteractableNoPlayerCollision", "Glass");

        private static bool HasLineOfSight(ReferenceHub source, ReferenceHub target, Vector3 origin, Vector3 aimPoint)
        {
            var direction = aimPoint - origin;
            var distance = direction.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            // Single environment-only linecast: the aim point is visible if no wall/door/glass is
            // between the eye and it. Replaces a per-candidate, per-tick RaycastAll(~0)+Array.Sort
            // (heap allocation + delegate allocation each call). Player/hitbox layers are excluded so
            // the target's own body and bystanders never false-block.
            return !Physics.Linecast(origin, aimPoint, CombatVisionMask, QueryTriggerInteraction.Ignore);
        }

        private sealed class CombatTarget
        {
            public CombatTarget(ReferenceHub hub, float distance, Vector3 aimPoint, bool hasLineOfSight)
            {
                Hub = hub;
                Distance = distance;
                AimPoint = aimPoint;
                HasLineOfSight = hasLineOfSight;
            }

            public ReferenceHub Hub { get; }
            public float Distance { get; }
            public Vector3 AimPoint { get; }
            public bool HasLineOfSight { get; }
        }

        private static readonly string[] Scp939AttackActions =
        {
            "Scp939ClawAbility:Shoot->Click",
        };

        private static readonly string[] Scp106AttackActions =
        {
            "Scp106Attack:Shoot->Click",
        };

        private static readonly string[] Scp3114AttackActions =
        {
            "Scp3114Slap:Shoot->Click",
        };

        private static readonly string[] Scp096AttackActions =
        {
            "Scp096AttackAbility:Shoot->Click",
        };

        private static readonly string[] Scp049SenseActions =
        {
            "Scp049SenseAbility:ToggleFlashlight->Click",
        };

        private static readonly string[] Scp049AttackActions =
        {
            "Scp049AttackAbility:Shoot->Click",
        };

        private static readonly string[] Scp0492AttackActions =
        {
            "ZombieAttackAbility:Shoot->Click",
            "Shoot->Click",
        };

        private static readonly string[] Scp173SnapActions =
        {
            "Scp173SnapAbility:Shoot->Click",
            "Shoot->Click",
        };

        private static readonly string[] Scp173TantrumActions =
        {
            "Scp173TantrumAbility:ToggleFlashlight->Click",
        };

        private static readonly string[] Scp173BreakneckActions =
        {
            "Scp173BreakneckSpeedsAbility:Run->Click",
        };
    }

    internal enum BotCombatDifficulty
    {
        Easy,
        Normal,
        Hard,
        Hardest,
    }

    internal sealed class BotCombatDifficultySettings
    {
        public float ShotCooldownSeconds { get; private init; }
        public float AimAngleDegrees { get; private init; }
        public float StrafeSpeed { get; private init; }
        public float MinStrafeFlipSeconds { get; private init; }
        public float MaxStrafeFlipSeconds { get; private init; }
        public float ChaseAfterLostLosSeconds { get; private init; }
        public float Scp096RageDurationSeconds { get; private init; }

        public static BotCombatDifficultySettings For(BotCombatDifficulty difficulty)
        {
            return difficulty switch
            {
                BotCombatDifficulty.Easy => new()
                {
                    ShotCooldownSeconds = 0.26f,
                    AimAngleDegrees = 14f,
                    StrafeSpeed = 1.0f,
                    MinStrafeFlipSeconds = 1.0f,
                    MaxStrafeFlipSeconds = 1.6f,
                    ChaseAfterLostLosSeconds = 1.5f,
                    Scp096RageDurationSeconds = 30f,
                },
                BotCombatDifficulty.Normal => new()
                {
                    ShotCooldownSeconds = 0.14f,
                    AimAngleDegrees = 10f,
                    StrafeSpeed = 1.35f,
                    MinStrafeFlipSeconds = 0.55f,
                    MaxStrafeFlipSeconds = 0.95f,
                    ChaseAfterLostLosSeconds = 3f,
                    Scp096RageDurationSeconds = 40f,
                },
                BotCombatDifficulty.Hard => new()
                {
                    ShotCooldownSeconds = 0.055f,
                    AimAngleDegrees = 6f,
                    StrafeSpeed = 1.95f,
                    MinStrafeFlipSeconds = 0.18f,
                    MaxStrafeFlipSeconds = 0.32f,
                    ChaseAfterLostLosSeconds = 5f,
                    Scp096RageDurationSeconds = 50f,
                },
                BotCombatDifficulty.Hardest => new()
                {
                    ShotCooldownSeconds = 0.024f,
                    AimAngleDegrees = 3.5f,
                    StrafeSpeed = 2.45f,
                    MinStrafeFlipSeconds = 0.07f,
                    MaxStrafeFlipSeconds = 0.14f,
                    ChaseAfterLostLosSeconds = 8f,
                    Scp096RageDurationSeconds = 60f,
                },
                _ => For(BotCombatDifficulty.Normal),
            };
        }
    }
}
