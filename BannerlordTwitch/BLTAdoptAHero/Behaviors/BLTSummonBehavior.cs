﻿using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordTwitch.Helpers;
using BannerlordTwitch.Util;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BLTAdoptAHero
{
    internal class BLTSummonBehavior : AutoMissionBehavior<BLTSummonBehavior>
    {
        public class RetinueState
        {
            public CharacterObject Troop;
            public Agent Agent;
            // We must record this separately, as the Agent.State is undefined once the Agent is deleted (the internal handle gets reused by the engine)
            public AgentState State;
        }

        public class SummonedHero
        {
            public Hero Hero;
            public bool WasPlayerSide;
            public FormationClass Formation;
            public PartyBase Party;
            public AgentState State;
            public Agent CurrentAgent;
            public float SummonTime;
            public int TimesSummoned = 0;
            public List<RetinueState> Retinue { get; set; } = new();

            public int ActiveRetinue => Retinue.Count(r => r.State == AgentState.Active);

            private float CooldownTime => BLTAdoptAHeroModule.CommonConfig.CooldownEnabled
                ? BLTAdoptAHeroModule.CommonConfig.GetCooldownTime(TimesSummoned) : 0;

            public bool InCooldown => BLTAdoptAHeroModule.CommonConfig.CooldownEnabled && SummonTime + CooldownTime > MBCommon.GetTotalMissionTime();
            public float CooldownRemaining => !BLTAdoptAHeroModule.CommonConfig.CooldownEnabled ? 0 : Math.Max(0, SummonTime + CooldownTime - MBCommon.GetTotalMissionTime());
            public float CoolDownFraction => !BLTAdoptAHeroModule.CommonConfig.CooldownEnabled ? 1 : 1f - CooldownRemaining / CooldownTime;
        }

        private readonly List<SummonedHero> summonedHeroes = new();
        private readonly List<Action> onTickActions = new();

        public SummonedHero GetSummonedHero(Hero hero)
            => summonedHeroes.FirstOrDefault(h => h.Hero == hero);

        public SummonedHero GetSummonedHeroForRetinue(Agent retinueAgent) => summonedHeroes.FirstOrDefault(h => h.Retinue.Any(r => r.Agent == retinueAgent));

        public SummonedHero AddSummonedHero(Hero hero, bool playerSide, FormationClass formationClass, PartyBase party)
        {
            var newSummonedHero = new SummonedHero
            {
                Hero = hero,
                WasPlayerSide = playerSide,
                Formation = formationClass,
                Party = party,
                SummonTime = MBCommon.GetTotalMissionTime(), 
            };
            summonedHeroes.Add(newSummonedHero);
            return newSummonedHero;
        }
            
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            SafeCall(() =>
            {
                var hero = summonedHeroes.FirstOrDefault(h => h.CurrentAgent == affectedAgent);
                if (hero != null)
                {
                    hero.State = agentState;
                }

                // Set the final retinue state
                var retinue = summonedHeroes.SelectMany(h => h.Retinue).FirstOrDefault(r => r.Agent == affectedAgent);
                if (retinue != null)
                {
                    retinue.State = agentState;
                }
            });
        }

        public void DoNextTick(Action action)
        {
            onTickActions.Add(action);
        }

        public override void OnMissionTick(float dt)
        {
            SafeCall(() =>
            {
                var actionsToDo = onTickActions.ToList();
                onTickActions.Clear();
                foreach (var action in actionsToDo)
                {
                    action();
                }
            });
        }

        protected override void OnEndMission()
        {
            SafeCall(() =>
            {
                // Remove still living retinue troops from their parties
                foreach (var h in summonedHeroes)
                {
                    foreach (var r in h.Retinue.Where(r => r.State != AgentState.Killed))
                    {
                        h.Party?.MemberRoster?.AddToCounts(r.Troop, -1);
                    }
                }
            });
        }
    }
}