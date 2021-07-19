﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bannerlord.ButterLib.SaveSystem.Extensions;
using BannerlordTwitch.Rewards;
using BannerlordTwitch.Util;
using BLTAdoptAHero.Actions.Util;
using BLTAdoptAHero.Util;
using HarmonyLib;
using JetBrains.Annotations;
using Microsoft.AspNet.SignalR;
using SandBox.TournamentMissions.Missions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.Source.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using TaleWorlds.TwoDimension;

namespace BLTAdoptAHero
{
    public class TournamentHub : Hub
    {
        public static void Register()
        {
            BLTOverlay.BLTOverlay.Register("tournament", 100, @"
#tournament-container {
    display: flex;
    flex-direction: row;
    margin-top: 1em;
}

#tournament-label {
    font-weight: bold;
    margin-right: 0.6em;
    display: flex;
    align-items: center;
    margin-bottom: 0.1em;
}

#tournament-items {
    display: flex;
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
}
.tournament-range {
    display: flex;
    align-items: center;
}

.tournament-entry {
    height: 0.5em;
    width: 0.5em;
    border-radius: 50%;
    display: inline-block;
    box-sizing: border-box;
    margin: 0.1em;
}

.tournament-empty {
    background-color: transparent;
    border: 0.1em solid #ffffff;
}

.tournament-in-next {
    background-color: #ffffff;
}

.tournament-last-slot {
    background-color: #ff813f;
}

.tournament-overflow {
    background-color: #29ba7f;
}

.tournament-entry-t-enter-active {
    animation: bounce-in 0.6s;
}

/*.tournament-entry-t-leave-active {*/
/*    animation: bounce-in 0.1s reverse;*/
/*}*/

@keyframes bounce-in {
    0% {
        transform: scale(1);
        opacity: 0;
    }
    25% {
        transform: scale(4);
        opacity: 0.5;
    }
    100% {
        transform: scale(1);
        opacity: 1;
    }
}
", @"
<div id='tournament-container' class='drop-shadow-highlight'>
    <div id='tournament-label' class='drop-shadow'>
        Tournament
    </div>
    <div id='tournament-items' class='drop-shadow'>
        <div v-for='index in range(0, Math.max(tournamentSize, entrants))' class='tournament-range'>
            <transition name='tournament-entry-t' tag='div' mode='out-in' appear>
                <div v-if='index < entrants && index < tournamentSize - 1' 
                     class='tournament-entry tournament-in-next' v-bind:key=""index + 'in-next'""></div>
                <div v-else-if='index < entrants && index === tournamentSize - 1' 
                     class='tournament-entry tournament-last-slot' v-bind:key=""index + 'last-slot'""></div>
                <div v-else-if='index > tournamentSize - 1' 
                     class='tournament-entry tournament-overflow' v-bind:key=""index + 'overflow'""></div>
                <div v-else 
                     class='tournament-entry tournament-empty' v-bind:key=""index + 'empty'""></div>
            </transition>
        </div>
    </div>
</div>
", @"
<!-- Tournament -->
$(function () {
    const tournament = new Vue({
        el: '#tournament-container',
        data: {
            entrants: 0,
            tournamentSize: 16
        },
        methods:{
            range : function (start, end) {
                if(end <= start)
                {
                    return [];
                }
                return Array(end - start).fill(0).map((_, idx) => start + idx)
            }
        }
    });

    $.connection.hub.url = '$url_root$/signalr';
    const tournamentHub = $.connection.tournamentHub;
    tournamentHub.client.update = function (entrants, tournamentSize) {
        tournament.entrants = entrants;
        tournament.tournamentSize = tournamentSize;
        console.log('BLT Tournament entrants set to ' + entrants + '/' + tournamentSize);
    };
    $.connection.hub.start().done(function () {
        console.log('BLT Tournament Hub started');
    });
});
");
        }
        
        public override Task OnConnected()
        {
            Refresh();
            return base.OnConnected();
        }

        [UsedImplicitly]
        public void Refresh()
        {
            (int entrants, int tournamentSize) = BLTTournamentQueueBehavior.Current?.GetTournamentQueueSize() ?? (0, 16);
            Clients.Caller.update(entrants, tournamentSize);
        }
        
        public static void Refresh(int entrants, int tournamentSize)
        {
            GlobalHost.ConnectionManager.GetHubContext<TournamentHub>()
                .Clients.All.update(entrants, tournamentSize);
        }
    }
    
    public class BLTTournamentQueueBehavior : CampaignBehaviorBase, IDisposable
    {
        public static BLTTournamentQueueBehavior Current => Campaign.Current?.GetCampaignBehavior<BLTTournamentQueueBehavior>();

        public override void RegisterEvents()
        {
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (_, _, _, _) =>
            {
                tournamentQueue.RemoveAll(e => e.Hero == null || e.Hero.IsDead);
            });
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                var usedHeroList = tournamentQueue.Select(t => t.Hero).ToList();
                dataStore.SyncData("UsedHeroObjectList", ref usedHeroList);
                var queue = tournamentQueue.Select(e => new TournamentQueueEntrySavable
                {
                    HeroIndex = usedHeroList.IndexOf(e.Hero),
                    IsSub = e.IsSub,
                    EntryFee = e.EntryFee,
                }).ToList();
                dataStore.SyncDataAsJson("Queue2", ref queue);
            }
            else
            {
                List<Hero> usedHeroList = null;
                dataStore.SyncData("UsedHeroObjectList", ref usedHeroList);
                List<TournamentQueueEntrySavable> queue = null;
                dataStore.SyncDataAsJson("Queue2", ref queue);
                if (usedHeroList != null && queue != null)
                {
                    tournamentQueue = queue.Select(e => new TournamentQueueEntry
                    {
                        Hero = usedHeroList[e.HeroIndex],
                        IsSub = e.IsSub,
                        EntryFee = e.EntryFee,
                    }).ToList();
                }
            }
            tournamentQueue ??= new();
            tournamentQueue.RemoveAll(e => e.Hero == null || e.Hero.IsDead);
            UpdatePanel();
        }

        public (int entrants, int tournamentSize) GetTournamentQueueSize()
        {
            return (tournamentQueue.Count, 16);
        }
        
        private void UpdatePanel()
        {
            (int entrants, int tournamentSize) = GetTournamentQueueSize();
            TournamentHub.Refresh(entrants, tournamentSize);
        }

        private class TournamentQueueEntry
        {
            [UsedImplicitly] 
            public Hero Hero { get; set; }
            [UsedImplicitly] 
            public bool IsSub { get; set; }
            [UsedImplicitly] 
            public int EntryFee { get; set; }

            public TournamentQueueEntry(Hero hero = null, bool isSub = false, int entryFee = 0)
            {
                Hero = hero;
                IsSub = isSub;
                EntryFee = entryFee;
            }
        }

        private class TournamentQueueEntrySavable
        {
            [SaveableProperty(0)]
            public int HeroIndex { get; set; }
            [SaveableProperty(1)]
            public bool IsSub { get; set; }
            [SaveableProperty(2)]
            public int EntryFee { get; set; }
        }
            
        private List<TournamentQueueEntry> tournamentQueue = new();
        private readonly List<TournamentQueueEntry> activeTournament = new();

        private enum TournamentMode
        {
            None,
            Watch,
            Join
        }
        private TournamentMode mode = TournamentMode.None;

        public bool TournamentAvailable => tournamentQueue.Any();
            
        public (bool success, string reply) AddToQueue(Hero hero, bool isSub, int entryFree)
        {
            if (tournamentQueue.Any(sh => sh.Hero == hero))
            {
                return (false, $"You are already in the tournament queue!");
            }

            tournamentQueue.Add(new TournamentQueueEntry(hero, isSub, entryFree));
            UpdatePanel();
            return (true, $"You are position {tournamentQueue.Count} in the tournament queue!");
        }
            
        public void JoinViewerTournament()
        {
            mode = TournamentMode.Join;
            var tournamentGame = Campaign.Current.Models.TournamentModel.CreateTournament(Settlement.CurrentSettlement.Town);
            SetPlaceholderPrize(tournamentGame);
            tournamentGame.PrepareForTournamentGame(true);
        }

        public void WatchViewerTournament()
        {
            mode = TournamentMode.Watch;
            var tournamentGame = Campaign.Current.Models.TournamentModel.CreateTournament(Settlement.CurrentSettlement.Town);
            SetPlaceholderPrize(tournamentGame);
            tournamentGame.PrepareForTournamentGame(false);
        }

        public void GetParticipantCharacters(Settlement settlement, List<CharacterObject> __result)
        {
            activeTournament.Clear();

            if (Settlement.CurrentSettlement == settlement && mode != TournamentMode.None)
            {
                __result.Clear();
                if(mode == TournamentMode.Join)
                    __result.Add(Hero.MainHero.CharacterObject);
                
                int viewersToAddCount = Math.Min(16 - __result.Count, tournamentQueue.Count);
                    
                var viewersToAdd = tournamentQueue.Take(viewersToAddCount).ToList();
                __result.AddRange(viewersToAdd.Select(q => q.Hero.CharacterObject));
                activeTournament.AddRange(viewersToAdd);
                tournamentQueue.RemoveRange(0, viewersToAddCount);
                
                var basicTroops = MBObjectManager.Instance.GetObjectTypeList<CultureObject>()
                    .SelectMany(c => new[] {c.BasicTroop, c.EliteBasicTroop})
                    .Where(t => t != null)
                    .ToList();

                while (__result.Count < 16)
                {
                    __result.Add(basicTroops.SelectRandom());
                }
                
                UpdatePanel();

                mode = TournamentMode.None;
            }
        }

        public void GetTeamWeaponEquipmentList(List<Equipment> equipments)
        {
            var replacementWeapon =
                HeroHelpers.AllItems.FirstOrDefault(i => i.StringId == "empire_sword_1_t2_blunt");
            foreach (var e in equipments)
            {
                if (BLTAdoptAHeroModule.TournamentConfig.NoHorses)
                {
                    e[EquipmentIndex.Horse] = EquipmentElement.Invalid;
                    e[EquipmentIndex.HorseHarness] = EquipmentElement.Invalid;
                }

                if (replacementWeapon != null && BLTAdoptAHeroModule.TournamentConfig.NoSpears)
                {
                    foreach (var (element, index) in e.YieldWeaponSlots())
                    {
                        if (element.Item?.Type == ItemObject.ItemTypeEnum.Polearm && !element.Item.IsSwingable())
                        {
                            e[index] = new(replacementWeapon);
                        }
                    }
                }
            }
        }
            
        public void PrepareForTournamentGame()
        {
            if (!activeTournament.Any())
            {
                return;
            }

            var savedArmor = new Dictionary<Hero, List<(EquipmentIndex slot, EquipmentElement element)>>();
            if (BLTAdoptAHeroModule.TournamentConfig.NormalizeArmor)
            {
                var tier = (ItemObject.ItemTiers)Math.Max(0, Math.Min(5, BLTAdoptAHeroModule.TournamentConfig.NormalizeArmorTier - 1));
                var culture = Settlement.CurrentSettlement.Culture;
                var replacements = SkillGroup.ArmorIndexType
                    .Select(slotItemTypePair =>
                    (
                        slot: slotItemTypePair.slot, 
                        item: HeroHelpers.AllItems.FirstOrDefault(i 
                                  => i.Culture == culture && i.Tier == tier && i.ItemType == slotItemTypePair.itemType)
                              ?? HeroHelpers.AllItems.FirstOrDefault(i 
                                  => i.Tier == tier && i.ItemType == slotItemTypePair.itemType)
                    )).ToList();

                foreach (var entry in activeTournament)
                {
                    savedArmor.Add(entry.Hero, SkillGroup.ArmorIndexType.Select(slotItemTypePair 
                        => (slotItemTypePair.slot, entry.Hero.BattleEquipment[slotItemTypePair.slot])).ToList());

                    foreach (var (slot, item) in replacements)
                    {
                        entry.Hero.BattleEquipment[slot] = new(item);
                    }
                }
            }
				
            var tournamentBehavior = MissionState.Current.CurrentMission.GetMissionBehaviour<TournamentBehavior>();

            tournamentBehavior.TournamentEnd += () =>
            {
                // Win results, put winner last
                foreach (var entry in activeTournament
                    .OrderBy(e => e.Hero == tournamentBehavior.Winner.Character?.HeroObject)
                )
                {
                    if (entry.Hero != null && savedArmor.TryGetValue(entry.Hero, out var originalGear))
                    {
                        foreach (var (slot, element) in originalGear)
                        {
                            entry.Hero.BattleEquipment[slot] = element;
                        }
                    }
                        
                    float actualBoost = entry.IsSub ? Math.Max(BLTAdoptAHeroModule.CommonConfig.SubBoost, 1) : 1;
                    var results = new List<string>();
                    if (entry.Hero != null && entry.Hero == tournamentBehavior.Winner.Character?.HeroObject)
                    {
                        results.Add("WINNER!");

                        BLTAdoptAHeroCampaignBehavior.Current.IncreaseTournamentChampionships(entry.Hero);
                        // Winner gets their gold back also
                        int actualGold = (int) (BLTAdoptAHeroModule.TournamentConfig.WinGold * actualBoost + entry.EntryFee);
                        if (actualGold > 0)
                        {
                            BLTAdoptAHeroCampaignBehavior.Current.ChangeHeroGold(entry.Hero, actualGold);
                            results.Add($"{Naming.Inc}{actualGold}{Naming.Gold}");
                        }

                        int xp = (int) (BLTAdoptAHeroModule.TournamentConfig.WinXP * actualBoost);
                        if (xp > 0)
                        {
                            (bool success, string description) = SkillXP.ImproveSkill(entry.Hero, xp, SkillsEnum.All, auto: true);
                            if (success)
                            {
                                results.Add(description);
                            }
                        }

                        var (item, itemModifier, slot) = GeneratePrize(entry.Hero);
                        if (item == null)
                        {
                            results.Add($"no prize available for you!");
                        }
                        else
                        {
                            var element = new EquipmentElement(item, itemModifier);
                            bool isCustom = BLTCustomItemsCampaignBehavior.Current.IsRegistered(itemModifier);

                            // We always put our custom items into the heroes storage, even if we won't use them right now
                            if (isCustom)
                            {
                                BLTAdoptAHeroCampaignBehavior.Current.AddCustomItem(entry.Hero, element);
                            }
                                
                            if (slot != EquipmentIndex.None)
                            {
                                entry.Hero.BattleEquipment[slot] = element;
                                results.Add($"received {element.GetModifiedItemName()}");
                            }
                            else if (!isCustom)
                            {
                                // Sell non-custom items
                                BLTAdoptAHeroCampaignBehavior.Current.ChangeHeroGold(entry.Hero, item.Value * 5);
                                results.Add($"sold {element.GetModifiedItemName()} for {item.Value}{Naming.Gold} (not needed)");
                            }
                            else
                            {
                                // should never happen really, as custom items are only created when they can be equipped 
                                results.Add($"received {element.GetModifiedItemName()} (put in storage)");
                            }
                        }
                    }
                    else
                    {
                        int xp = (int) (BLTAdoptAHeroModule.TournamentConfig.ParticipateXP * actualBoost);
                        if (xp > 0)
                        {
                            (bool success, string description) =
                                SkillXP.ImproveSkill(entry.Hero, xp, SkillsEnum.All, auto: true);
                            if (success)
                            {
                                results.Add(description);
                            }
                        }
                    }

                    if (results.Any() && entry.Hero != null)
                    {
                        Log.LogFeedResponse(entry.Hero.FirstName.ToString(), results.ToArray());
                    }
                }

                activeTournament.Clear();
            };
        }

        #region Custom Prize Generation
        private static ItemObject CreateCustomWeapon(Hero hero, HeroClassDef heroClass, EquipmentType weaponType)
        {
            if (!CustomItems.CraftableEquipmentTypes.Contains(weaponType))
            {
                // Get the highest tier we can for the weapon type
                var item = EquipHero.FindRandomTieredEquipment(5, hero, EquipHero.FindFlags.IgnoreAbility,
                    o => o.IsEquipmentType(weaponType) && EquipHero.IsWeaponUsableByHeroAndClass(hero, o, heroClass));
                return item;
            }
            else
            {
                return CustomItems.CreateCraftedWeapon(hero, weaponType, 5);
            }
        }
            
        private static ItemModifier GenerateItemModifier(ItemObject item, string modifierName)
        {
            string modifiedName = $"{modifierName} {{ITEMNAME}}";
            float modifierPower = BLTAdoptAHeroModule.TournamentConfig.CustomPrize.Power;
            if (item.WeaponComponent?.PrimaryWeapon?.IsMeleeWeapon == true
                || item.WeaponComponent?.PrimaryWeapon?.IsPolearm == true
                || item.WeaponComponent?.PrimaryWeapon?.IsRangedWeapon == true
            )
            {
                return BLTCustomItemsCampaignBehavior.Current.CreateWeaponModifier(
                    modifiedName,
                    (int) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.WeaponDamage.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.WeaponDamage.Max) * modifierPower),
                    (int) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.WeaponSpeed.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.WeaponSpeed.Max) * modifierPower),
                    (int) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.WeaponMissileSpeed.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.WeaponMissileSpeed.Max) * modifierPower),
                    (short) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.ThrowingStack.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.ThrowingStack.Max) * modifierPower)
                );
            }
            else if (item.WeaponComponent?.PrimaryWeapon?.IsAmmo == true)
            {
                return BLTCustomItemsCampaignBehavior.Current.CreateAmmoModifier(
                    modifiedName,
                    (int) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.AmmoDamage.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.AmmoDamage.Max) * modifierPower),
                    (short) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.ArrowStack.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.ArrowStack.Max) * modifierPower)
                );
            }
            else if (item.HasArmorComponent)
            {
                return BLTCustomItemsCampaignBehavior.Current.CreateArmorModifier(
                    modifiedName,
                    (int) Mathf.Ceil(MBRandom.RandomInt(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.Armor.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.Armor.Max) * modifierPower)
                );
            }
            else if (item.IsMountable)
            {
                return BLTCustomItemsCampaignBehavior.Current.CreateMountModifier(
                    modifiedName,
                    MBRandom.RandomFloatRanged(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountManeuver.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountManeuver.Max) * modifierPower,
                    MBRandom.RandomFloatRanged(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountSpeed.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountSpeed.Max) * modifierPower,
                    MBRandom.RandomFloatRanged(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountChargeDamage.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountChargeDamage.Max) * modifierPower,
                    MBRandom.RandomFloatRanged(
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountHitPoints.Min, 
                        BLTAdoptAHeroModule.TournamentConfig.CustomPrize.MountHitPoints.Max) * modifierPower
                );
            }
            else
            {
                Log.Error($"Cannot generate modifier for {item.Name}: its modifier requirements could not be determined");
                return null;
            }
        }
            
#if DEBUG
        [CommandLineFunctionality.CommandLineArgumentFunction("testprize", "blt")]
        [UsedImplicitly]
        public static string TestTournamentCustomPrize(List<string> strings)
        {
            if (strings.Count == 1)
            {
                int count = int.Parse(strings[0]);
                for (int i = 0; i < count; i++)
                {
                    var (item, modifier, _) = GeneratePrize(Hero.MainHero);
                    if (item == null)
                    {
                        return $"Couldn't generate a matching item";
                    }
                    var equipment = new EquipmentElement(item, modifier);
                    Hero.MainHero.PartyBelongedTo.ItemRoster.AddToCounts(equipment, 1);
                }
                return $"Added {count} items to {Hero.MainHero.Name}";
            }
            else if (strings.Count == 3)
            {
                int count = int.Parse(strings[2]);
                var prizeType = (GlobalTournamentConfig.PrizeType) Enum.Parse(typeof(GlobalTournamentConfig.PrizeType), strings[0]);
                var classDef = BLTAdoptAHeroModule.HeroClassConfig.FindClass(strings[1]);

                for (int i = 0; i < count; i++)
                {
                    var (item, modifier, _) = GeneratePrizeType(prizeType, 6, Hero.MainHero, classDef);
                
                    if (item == null)
                    {
                        return $"Couldn't generate a matching item";
                    }

                    var equipment = new EquipmentElement(item, modifier);
                
                    Hero.MainHero.PartyBelongedTo.ItemRoster.AddToCounts(equipment, 1);
                }

                return $"Added {count} items to {Hero.MainHero.Name}";
            }
            else
            {
                return "Expected 1 or 3 arguments: blt.testprize <number to make> OR blt.testprize Weapon/Armor/Mount <class name> <number to make>";
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("testprize2", "blt")]
        [UsedImplicitly]
        public static string TestTournamentCustomPrize2(List<string> strings)
        {
            foreach (var h in BLTAdoptAHeroCampaignBehavior.GetAllAdoptedHeroes())
            {
                var (item, itemModifier, slot) = GeneratePrize(h);
                if (item != null)
                {
                    var element = new EquipmentElement(item, itemModifier);
                    BLTAdoptAHeroCampaignBehavior.Current.AddCustomItem(h, element);
                    if (slot != EquipmentIndex.None)
                    {
                        h.BattleEquipment[slot] = element;
                    }
                    //(bool upgraded, string failReason) = UpgradeToItem(h, new(item, itemModifier), itemModifier != null);
                    // if (!upgraded)
                    // {
                    //     Log.Error($"Failed to upgrade {item.Name} for {h.Name}: {failReason}");
                    // }
                }
                else
                {
                    Log.Error($"Failed to generate prize for {h.Name}");
                }
            }
            
            GameStateManager.Current?.UpdateInventoryUI();

            // if (GameStateManager.Current.ActiveState is InventoryState inventoryState)
            // {
            //     inventoryState.InventoryLogic?.Reset();
            // }

            return "done";
        }
#endif
        
        private static (ItemObject item, ItemModifier modifier, EquipmentIndex slot) GeneratePrizeType(GlobalTournamentConfig.PrizeType prizeType, int tier, Hero hero, HeroClassDef heroClass)
        {
            return prizeType switch
            {
                GlobalTournamentConfig.PrizeType.Weapon => GeneratePrizeTypeWeapon(tier, hero, heroClass),
                GlobalTournamentConfig.PrizeType.Armor => GeneratePrizeTypeArmor(tier, hero),
                GlobalTournamentConfig.PrizeType.Mount => GeneratePrizeTypeMount(tier, hero, heroClass),
                _ => throw new ArgumentOutOfRangeException(nameof(prizeType), prizeType, null)
            };
        }

        private static (ItemObject item, ItemModifier modifier, EquipmentIndex slot) GeneratePrizeTypeWeapon(
            int tier, Hero hero, HeroClassDef heroClass)
        {
            // List of heroes custom items, so we can avoid giving duplicates (it will include what they are carrying, as all custom items are registered)
            var heroCustomWeapons = BLTAdoptAHeroCampaignBehavior.Current.GetCustomItems(hero);

            // List of heroes current weapons
            var heroWeapons = hero.BattleEquipment.YieldFilledWeaponSlots().ToList();

            var replaceableHeroWeapons = heroWeapons
                .Where(w =>
                    // Must be lower than the desired tier
                    (int)w.element.Item.Tier < tier 
                    // Must not be a custom item
                    && !BLTCustomItemsCampaignBehavior.Current.IsRegistered(w.element.ItemModifier))
                .Select(w => (w.index, w.element.Item.GetEquipmentType()));


            // Weapon classes we can generate a prize for, with some heuristics to avoid some edge cases, and getting duplicates
            var weaponClasses = 
                (heroClass?.IndexedWeapons ?? replaceableHeroWeapons)
                .Where(s =>
                    // No shields, they aren't cool rewards and don't support any modifiers
                    s.type != EquipmentType.Shield
                    // Exclude bolts if hero doesn't have a crossbow already
                    && (s.type != EquipmentType.Bolts || heroWeapons.Any(i => i.element.Item.WeaponComponent?.PrimaryWeapon?.AmmoClass == WeaponClass.Bolt))
                    // Exclude arrows if hero doesn't have a bow
                    && (s.type != EquipmentType.Arrows || heroWeapons.Any(i => i.element.Item.WeaponComponent?.PrimaryWeapon?.AmmoClass == WeaponClass.Arrow))
                    // Exclude any weapons we already have enough custom versions of (if we have class then we can match the class count, otherwise we just limit it to 1)
                    && heroCustomWeapons.Count(i => i.Item.IsEquipmentType(s.type)) < (heroClass?.Weapons.Count(w => w == s.type) ?? 1)
                )
                .Shuffle()
                .ToList();

            if (!weaponClasses.Any())
            {
                return default;
            }

            // Tier > 5 indicates custom weapons with modifiers
            if (tier > 5)
            {
                // Custom "modified" item
                var (item, index) = weaponClasses
                    .Select(c => (
                        item: CreateCustomWeapon(hero, heroClass, c.type),
                        index: c.index))
                    .FirstOrDefault(w => w.item != null);
                return item == null 
                        ? default 
                        : (item, GenerateItemModifier(item, "Prize"), index)
                    ;
            }
            else
            {
                // Find a random item fitting the weapon class requirements
                var (item, index) = weaponClasses
                    .Select(c => (
                        item: EquipHero.FindRandomTieredEquipment(tier, hero, EquipHero.FindFlags.IgnoreAbility | EquipHero.FindFlags.RequireExactTier, 
                            i => i.IsEquipmentType(c.type)),
                        index: c.index))
                    .FirstOrDefault(w => w.item != null);
                return item == null || hero.BattleEquipment[index].Item?.Tier >= item.Tier
                    ? default 
                    : (item, null, index);
            }
        }

        private static (ItemObject item, ItemModifier modifier, EquipmentIndex slot) GeneratePrizeTypeArmor(int tier, Hero hero)
        {
            // List of custom items the hero already has, and armor they are wearing that is as good or better than the tier we want 
            var heroBetterArmor = BLTAdoptAHeroCampaignBehavior.Current
                .GetCustomItems(hero)
                .Concat(hero.BattleEquipment.YieldFilledArmorSlots().Where(e => (int)e.Item.Tier >= tier));

            // Select randomly from the various armor types we can choose between
            var (index, itemType) = SkillGroup.ArmorIndexType
                // Exclude any armors we already have an equal or better version of
                .Where(i => heroBetterArmor.All(i2 => i2.Item.ItemType != i.itemType))
                .SelectRandom();

            if (index == default)
            {
                return default;
            }
                
            // Custom "modified" item
            if (tier > 5)
            {
                var armor = EquipHero.FindRandomTieredEquipment(5, hero, 
                    EquipHero.FindFlags.IgnoreAbility,
                    o => o.ItemType == itemType);
                return armor == null ? default : (armor, GenerateItemModifier(armor, "Prize"), index);
            }
            else
            {
                var armor = EquipHero.FindRandomTieredEquipment(tier, hero, 
                    EquipHero.FindFlags.IgnoreAbility | EquipHero.FindFlags.RequireExactTier,
                    o => o.ItemType == itemType);
                // if no armor was found, or its the same tier as what we have then return null
                return armor == null || hero.BattleEquipment.YieldFilledArmorSlots().Any(i2 => i2.Item.Type == armor.Type && i2.Item.Tier >= armor.Tier) 
                    ? default 
                    : (armor, null, index);
            }
        }

        private static (ItemObject item, ItemModifier modifier, EquipmentIndex slot) GeneratePrizeTypeMount(
            int tier, Hero hero, HeroClassDef heroClass)
        {
            var currentMount = hero.BattleEquipment.Horse;
            // If we are generating is non custom prize, and the hero has a non custom mount already,
            // of equal or better tier, we don't replace it
            if (tier <= 5 && !currentMount.IsEmpty && (int) currentMount.Item.Tier >= tier)
            {
                return default;
            }

            // If the hero has a custom mount already, then we don't give them another, or any non custom one
            if (BLTAdoptAHeroCampaignBehavior.Current.GetCustomItems(hero).Any(i => i.Item.ItemType == ItemObject.ItemTypeEnum.Horse))
            {
                return default;
            }
                
            bool IsCorrectMountFamily(ItemObject item)
            {  
                // Must match hero class requirements
                return (heroClass == null
                        || heroClass.UseHorse && item.HorseComponent.Monster.FamilyType is (int) EquipHero.MountFamilyType.horse 
                        || heroClass.UseCamel && item.HorseComponent.Monster.FamilyType is (int) EquipHero.MountFamilyType.camel)
                       // Must also not differ from current mount family type (or saddle can get messed up)
                       && (currentMount.IsEmpty 
                           || currentMount.Item.HorseComponent.Monster.FamilyType == item.HorseComponent.Monster.FamilyType
                       );
            }
                
            // Find mounts of the correct family type and tier
            var mount = HeroHelpers.AllItems
                .Where(item =>
                    item.IsMountable
                    // If we are making a custom mount then use any mount over Tier 2, otherwise match the tier exactly 
                    && (tier > 5 && (int)item.Tier >= 2 || (int)item.Tier == tier)  
                    && IsCorrectMountFamily(item)
                )
                .SelectRandom();

            if (mount == null)
            {
                return default;
            }

            var modifier = tier > 5 
                ? GenerateItemModifier(mount, "Prize") 
                : null;
            return (mount, modifier, EquipmentIndex.Horse);
        }

        private static (ItemObject item, ItemModifier modifier, EquipmentIndex slot) GeneratePrize(Hero hero)
        {
            var heroClass = BLTAdoptAHeroCampaignBehavior.Current.GetClass(hero);

            // Randomize the reward tier order, by random weighting
            var tiers = BLTAdoptAHeroModule.TournamentConfig.PrizeTierWeights
                .OrderRandomWeighted(tier => tier.weight).ToList();
            //int tier = BLTAdoptAHeroModule.TournamentConfig.PrizeTierWeights.SelectRandomWeighted(t => t.weight).tier;
            bool shouldUseHorse = EquipHero.HeroShouldUseHorse(hero, heroClass);
            return BLTAdoptAHeroModule.TournamentConfig.PrizeTypeWeights
                    // Exclude mount when it shouldn't be used by the hero or they already have a tournament reward horse
                    .Where(p => shouldUseHorse || p.type != GlobalTournamentConfig.PrizeType.Mount)
                    // Randomize the reward type order, by random weighting
                    .OrderRandomWeighted(type => type.weight)
                    .SelectMany(type => 
                        tiers.Select(tier => GeneratePrizeType(type.type, tier.tier, hero, heroClass)))
                    .FirstOrDefault(i => i != default)
                ;
        }

        #endregion 
        
        #region Betting
        private bool bettingOpen;
        private Dictionary<Hero, (int team, int bet)> activeBets;

        public void OpenBetting(TournamentBehavior tournamentBehavior)
        {
            if (BLTAdoptAHeroModule.TournamentConfig.EnableBetting 
                && tournamentBehavior.CurrentMatch != null
                && (tournamentBehavior.CurrentRoundIndex == 3 || !BLTAdoptAHeroModule.TournamentConfig.BettingOnFinalOnly))
            {
                var teams = TournamentHelpers.TeamNames.Take(tournamentBehavior.CurrentMatch.Teams.Count());
                string round = tournamentBehavior.CurrentRoundIndex < 3
                    ? $"round {tournamentBehavior.CurrentRoundIndex + 1}"
                    : "final";
                ActionManager.SendChat($"Betting is now OPEN for {round} match: {string.Join(" vs ", teams)}!");
                activeBets = new();
            }
            bettingOpen = true;
        }
        
        public (bool success, string failReason) PlaceBet(Hero hero, string team, int bet)
        {
            var tournamentBehavior = Mission.Current?.GetMissionBehaviour<TournamentBehavior>();
            if (tournamentBehavior == null)
            {
                return (false, "Tournament is not active");
            }

            if (!BLTAdoptAHeroModule.TournamentConfig.EnableBetting)
            {
                return (false, "Betting is disabled");
            }
            
            if (!bettingOpen)
            {
                return (false, "Betting is closed");
            }
            
            if (tournamentBehavior.CurrentRoundIndex != 3 && BLTAdoptAHeroModule.TournamentConfig.BettingOnFinalOnly)
            {
                return (false, "Betting is only allowed on the final");
            }

            if (activeBets == null)
            {
                return (false, "Betting is disabled");
            }

            if (activeBets.ContainsKey(hero))
            {
                return (false, "You already placed a bet");
            }

            int teamsCount = tournamentBehavior.CurrentMatch.Teams.Count();
            var activeTeams = TournamentHelpers.TeamNames.Take(teamsCount).ToArray();
            int teamIdx = activeTeams.IndexOf(team.ToLower());
            if (teamIdx == -1)
            {
                return (false, $"Team name must be one of {string.Join(", ", activeTeams)}");
            }
            
            int heroGold = BLTAdoptAHeroCampaignBehavior.Current.GetHeroGold(hero);
            if (heroGold < bet)
            {
                return (false, Naming.NotEnoughGold(bet, heroGold));
            }
            
            activeBets.Add(hero, (teamIdx, bet));
            
            // Take the actual money
            BLTAdoptAHeroCampaignBehavior.Current.ChangeHeroGold(hero, -bet);

            return (true, null);
        }

        public void CloseBetting(TournamentBehavior tournamentBehavior)
        {
            // We use this being non-null as an indicator that betting was active
            if (activeBets != null)
            {
                var betTotals = activeBets.Values
                    .Select(b => (name: TournamentHelpers.TeamNames[b.team], b.bet))
                    .GroupBy(b => b.name)
                    .Select(g => $"{g.Key} {g.Select(x => x.bet).Sum()}{Naming.Gold}")
                    .ToList()
                    ;
                ActionManager.SendChat(betTotals.Any()
                    ? $"Betting is now CLOSED: {string.Join(", ", betTotals)}"
                    : $"Betting is now CLOSED: no bets placed"
                );
            }
            
            bettingOpen = false;
        }

        private void CompleteBetting(TournamentMatch lastMatch)
        {
            if (activeBets != null)
            {
                double totalBet = activeBets.Values.Sum(v => v.bet);

                var allWonBets = activeBets
                    .Where(kv => lastMatch.Winners.Contains(lastMatch.Teams.ElementAt(kv.Value.team).Participants.First()))
                    .Select(kv => (
                        hero: kv.Key,
                        bet: kv.Value.bet
                    ))
                    .ToList();

                double winningTotalBet = allWonBets.Sum(v => v.bet);

                foreach ((var hero, int bet) in allWonBets.OrderByDescending(b => b.bet))
                {
                    int winnings = (int) (totalBet * bet / winningTotalBet);
                    int newGold = BLTAdoptAHeroCampaignBehavior.Current.ChangeHeroGold(hero, winnings);
                    Log.LogFeedResponse(hero.FirstName.ToString(),
                        $"WON BET {Naming.Inc}{winnings}{Naming.Gold}{Naming.To}{newGold}{Naming.Gold}");
                }

                activeBets = null;
            }
        }

        #endregion

        public void EndCurrentMatchPrefix(TournamentBehavior tournamentBehavior)
        {
            // If the tournament is over we need to make sure player gets the real prize. 
            // Need to do this before EndCurrentMatch, as the player gets the prize in this function.
            if (tournamentBehavior.CurrentRoundIndex == 3)
            {
                // Reset the prize if the player won
                if (originalPrize != null
                    && tournamentBehavior.CurrentMatch.IsPlayerWinner())
                {
                    SetPrize(tournamentBehavior.TournamentGame, originalPrize);
                }
            }
        }

        public void EndCurrentMatchPostfix(TournamentBehavior tournamentBehavior)
        {
            CompleteBetting(tournamentBehavior.LastMatch);

            if(tournamentBehavior.CurrentMatch != null)
            {
                OpenBetting(tournamentBehavior);
            }   
            
            // End round effects (as there is no event handler for it :/)
            foreach (var entry in activeTournament)
            {
                float actualBoost = entry.IsSub ? Math.Max(BLTAdoptAHeroModule.CommonConfig.SubBoost, 1) : 1;
                    
                var results = new List<string>();

                if(tournamentBehavior.LastMatch.Winners.Any(w => w.Character?.HeroObject == entry.Hero))
                {
                    int actualGold = (int) (BLTAdoptAHeroModule.TournamentConfig.WinMatchGold * actualBoost);
                    if (actualGold > 0)
                    {
                        BLTAdoptAHeroCampaignBehavior.Current.ChangeHeroGold(entry.Hero, actualGold);
                        results.Add($"{Naming.Inc}{actualGold}{Naming.Gold}");
                    }
                    int xp = (int) (BLTAdoptAHeroModule.TournamentConfig.WinMatchXP * actualBoost);
                    if (xp > 0)
                    {
                        (bool success, string description) =
                            SkillXP.ImproveSkill(entry.Hero, xp, SkillsEnum.All, auto: true);
                        if (success)
                        {
                            results.Add(description);
                        }
                    }
                    BLTAdoptAHeroCampaignBehavior.Current.IncreaseTournamentWins(entry.Hero);
                }
                else if (tournamentBehavior.LastMatch.Participants.Any(w => w.Character?.HeroObject == entry.Hero))
                {
                    int xp = (int) (BLTAdoptAHeroModule.TournamentConfig.ParticipateMatchXP * actualBoost);
                    if (xp > 0)
                    {
                        (bool success, string description) =
                            SkillXP.ImproveSkill(entry.Hero, xp, SkillsEnum.All, auto: true);
                        if (success)
                        {
                            results.Add(description);
                        }
                    }
                    BLTAdoptAHeroCampaignBehavior.Current.IncreaseTournamentLosses(entry.Hero);
                }
                if (results.Any())
                {
                    Log.LogFeedResponse(entry.Hero.FirstName.ToString(), results.ToArray());
                }
            }
        }

        private ItemObject originalPrize;
        
        private void SetPlaceholderPrize(TournamentGame tournamentGame)
        {
            originalPrize = tournamentGame.Prize;
            SetPrize(tournamentGame, DefaultItems.Charcoal);
        }

        private static void SetPrize(TournamentGame tournamentGame, ItemObject prize)
        {
            AccessTools.Property(typeof(TournamentGame), nameof(TournamentGame.Prize))
                .SetValue(tournamentGame, prize);
        }

        private void ReleaseUnmanagedResources()
        {
            TournamentHub.Refresh(0, 0);
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~BLTTournamentQueueBehavior()
        {
            ReleaseUnmanagedResources();
        }
    }
}