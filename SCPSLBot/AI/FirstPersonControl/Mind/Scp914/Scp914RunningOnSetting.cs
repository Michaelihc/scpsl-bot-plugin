using LabApi.Events.Arguments.Scp914Events;
using LabApi.Events.Handlers;
using MapGeneration;
using MEC;
using Scp914;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Scp914
{
    internal class Scp914RunningOnSetting : IBelief
    {
        public readonly Scp914KnobSetting Setting;
        public event Action OnUpdate;

        private readonly RoomSightSense roomSightSense;

        public Scp914RunningOnSetting(Scp914KnobSetting setting, RoomSightSense roomSightSense)
        {
            this.Setting = setting;
            this.roomSightSense = roomSightSense;

            Scp914Events.Activated += OnActivateEvent;
        }

        public void OnActivateEvent(Scp914ActivatedEventArgs args)
        {
            if (this.roomSightSense.RoomWithin.Name != RoomName.Lcz914)
            {
                return;
            }

            this.Update(args.KnobSetting);

            Timing.RunCoroutine(Scp914RunningCoroutine());
        }

        private IEnumerator<float> Scp914RunningCoroutine()
        {
            yield return Timing.WaitForSeconds(10f);

            this.ItemsTransformedTime = Time.time;

            yield return Timing.WaitForSeconds(5f);

            this.Update(null);
        }

        public Scp914KnobSetting? RunningKnobSetting { get; private set; }
        public float? ItemsTransformedTime { get; private set; }

        private void Update(Scp914KnobSetting? newSetting)
        {
            if (newSetting != this.RunningKnobSetting)
            {
                this.RunningKnobSetting = newSetting;
                this.OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(Scp914RunningOnSetting)}({Setting}): {this.RunningKnobSetting}";
        }
    }
}
