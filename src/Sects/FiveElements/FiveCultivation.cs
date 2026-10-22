using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    // No profile purchase or deck-dependent efficiency. Native persistent modifiers
    // are resolved WITHOUT Five growth before selecting the receiving attribute.
    public static class FiveCultivation
    {
        public const string Prefix = "yixian.sect.practice.five."; // retired save identities only
        public const string BattleKey = "cultivation.primary";
        private static readonly int[] Attributes = { 40, 20, 30 };
        private static JObject Config => (JObject)SectRegistry.Sects[FiveElementRules.Lineage]["cultivation"];
        public static int Rate => (int)Config["fallbackBp"];
        public static int Growth(int rank) => rank >= 1 && rank <= 5 ? (int)Config["growth"][rank - 1] : 0;
        public static int PrimaryGrowth(int rank) => rank >= 1 && rank <= 5 ? (int)Config["primaryGrowth"][rank - 1] : 0;
        public static int SupportGrowth(int rank) => Growth(rank) - PrimaryGrowth(rank);
        public static string AttributeName(int attribute) => attribute == 40 ? "能量" : attribute == 20 ? "反应" : attribute == 30 ? "体质" : "未知";
        private static int Attribute(int id, StatBlock stats) => AttributeScaling.Read((CombatScalingAttribute)id, stats);
        public static int SelectPrimary(StatBlock stats, int previous = 40)
        {
            int highest = Attributes.Max(a => Attribute(a, stats));
            return Attributes.Contains(previous) && Attribute(previous, stats) == highest ? previous : Attributes.First(a => Attribute(a, stats) == highest);
        }
        public static void Validate()
        {
            var growth = Config["growth"]?.Values<int>().ToArray();
            var primary = Config["primaryGrowth"]?.Values<int>().ToArray();
            if (Rate < 1 || Rate > 10000 || growth == null || growth.Length != 5 || growth.Any(g => g < 0)
                || primary == null || primary.Length != 5 || primary.Where((g, i) => g <= growth[i] / 2 || g > growth[i]).Any())
                throw new ArgumentException("五行自动成长或折算率无效。");
            foreach (var card in SectRegistry.Cards.Values.Where(c => SectRegistry.Owner(c) == FiveElementRules.Lineage))
                foreach (var rule in ((JObject)card["rules"]).Properties().Where(p => p.Name.EndsWith("Scaling", StringComparison.Ordinal)))
                {
                    var terms = rule.Value as JArray;
                    if (terms == null || terms.Count < 2 || terms.OfType<JObject>().Select(t => (int)t["attribute"]).Distinct().Count() != terms.Count
                        || terms.OfType<JObject>().Any(t => !Attributes.Contains((int)t["attribute"]) || (int)t["bp"] <= 0))
                        throw new ArgumentException("五行卡牌必须声明有效多属性：" + card["name"]);
                }
        }
        public static int Read(BattleTurn turn)
        {
            int primary = turn.Get(BattleKey);
            return Attributes.Contains(primary) ? primary : SelectPrimary(turn.Actor.Attributes.GetValueOrDefault());
        }
        public static void Lock(BattleTurn turn) => turn.Set(BattleKey, SelectPrimary(turn.Actor.Attributes.GetValueOrDefault()));
        public static int Bonus(JObject rules, string key, StatBlock stats, int primary)
        {
            var terms = rules[key + "Scaling"] as JArray;
            if (terms == null) return 0;
            long native = 0, total = 0;
            foreach (JObject term in terms)
            {
                int attr = (int)term["attribute"], bp = (int)term["bp"];
                if (!Attributes.Contains(attr) || bp <= 0) throw new ArgumentException("五行成长必须使用能量／反应／体质的正基点。");
                native += (long)Attribute(attr, stats) * bp; total += bp;
            }
            if (!Attributes.Contains(primary)) throw new ArgumentException("五行自动主属性无效。");
            long inherited = (long)Attribute(primary, stats) * total * Rate;
            return checked((int)(Math.Max(native * 10000L, inherited) / 100000000L));
        }
        public static int Value(BattleTurn turn, JObject rules, string key)
            => ((int?)rules[key] ?? 0) + Bonus(rules, key, turn.Actor.Attributes.GetValueOrDefault(), Read(turn));

        private sealed class BattleOwner { internal WeakReference<BattleAuthorityService> Authority; }
        private static readonly ConditionalWeakTable<PlayerRunState, BattleOwner> Battles = new ConditionalWeakTable<PlayerRunState, BattleOwner>();
        internal static void Bind(PlayerRunState player, BattleAuthorityService authority)
            => Battles.GetValue(player, _ => new BattleOwner()).Authority = new WeakReference<BattleAuthorityService>(authority);
        private static int LockedPrimary(PlayerRunState player, RunState run)
        {
            if (!Battles.TryGetValue(player, out var owner) || !owner.Authority.TryGetTarget(out var battle)
                || !ReferenceEquals(battle.Run, run) || battle.IsComplete) return 0;
            var state = (ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService), "modState").GetValue(battle);
            return SectRegistry.Read(state, SectRegistry.StateKey(FiveElementRules.Lineage, new StableId("combatant.player." + player.Slot.Value), BattleKey));
        }
        public static StatModifier GrowthModifier(PlayerRunState player, int primary)
        {
            int index = player.Lineages.ToList().FindIndex(l => l.LineageId.Value == FiveElementRules.Lineage);
            if (index < 0) throw new ArgumentException("五行成长需要五行血统。");
            var lineage = player.Lineages[index];
            string attribute = primary == 40 ? "energyFlat" : primary == 20 ? "reactionFlat" : "constitutionFlat";
            string support = primary == 40 ? "constitutionFlat" : "energyFlat";
            var spec = new ProgressionAbilitySpec(ProgressionSourceKind.Lineage, ProgressionEffectMode.Passive, lineage.LineageId, "yixian_five", lineage.Rank - 1,
                ProgressionTargetKind.Self, new[] { new ProgressionEffectValue(attribute, PrimaryGrowth(lineage.Rank), ""),
                    new ProgressionEffectValue(support, SupportGrowth(lineage.Rank), "") });
            var plan = (ProgressionAbilityPlan)Activator.CreateInstance(typeof(ProgressionAbilityPlan), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { spec }, null);
            var slot = index == 0 ? ProgressionPassiveSlot.Primary : player.EnergyPools.Secondary.IsAbsent ? ProgressionPassiveSlot.OffhandSharedPool : ProgressionPassiveSlot.OffhandOwnPool;
            var sourcePool = index == 0 || player.EnergyPools.Secondary.IsAbsent ? player.EnergyPools.Primary.Kind : player.EnergyPools.Secondary.Kind;
            if (!ProgressionPassiveBattleRules.TryCreateProfile(new[] { new ProgressionPassivePlanSlot(plan, slot, sourcePool) }, out var profile, out var error))
                throw new InvalidOperationException("五行自动成长无法投影：" + error);
            return ProgressionPassiveBattleRules.CreateStatModifier(profile, new StableId("passive.yixian.five.auto." + player.Slot.Value));
        }
        public static void AppendGrowth(RunState run, PlayerRunState player, List<StatModifier> modifiers)
        {
            if (SectRegistry.Rank(player, FiveElementRules.Lineage) == 0) return;
            var stats = DerivedStatCalculator.Calculate(player.Birth.FinalBirthStats, modifiers, player.EnergyPools.Primary.Kind).EffectiveAttributes;
            int primary = LockedPrimary(player, run);
            if (!Attributes.Contains(primary)) primary = SelectPrimary(stats, SelectPrimary(player.DerivedStats.EffectiveAttributes));
            modifiers.Add(GrowthModifier(player, primary));
        }
    }

    [HarmonyPatch(typeof(BattleAuthorityService), "CreatePersistentStatModifiers")]
    internal static class FiveCultivationGrowth
    {
        private static void Postfix(RunState run, PlayerRunState runPlayer, List<StatModifier> __result)
            => FiveCultivation.AppendGrowth(run, runPlayer, __result);
    }
    [HarmonyPatch(typeof(BattleAuthorityService), "ResolveBattleDerivedStats")]
    internal static class FiveCultivationBattleOwner
    {
        private static void Prefix(BattleAuthorityService __instance, PlayerRunState runPlayer) => FiveCultivation.Bind(runPlayer, __instance);
    }
    // Old open dialogs cannot overwrite the automatic system. Retained IDs only
    // allow existing native saves to restore; no migration writes or new purchases.
    [HarmonyPatch(typeof(RunAuthorityService), "PurchaseShelfAbility")]
    internal static class FiveCultivationChoice
    {
        private static bool Prefix(RunAuthorityService __instance, StableId abilityId, ref RuleResult<ShelfAbilityPurchaseReceipt> __result)
        {
            if (!abilityId.Value.StartsWith(FiveCultivation.Prefix, StringComparison.Ordinal)) return true;
            __result = RuleResult<ShelfAbilityPurchaseReceipt>.Failure(SessionErrorCode.ItemUnavailable, __instance.Run.Revision);
            return false;
        }
    }
}
