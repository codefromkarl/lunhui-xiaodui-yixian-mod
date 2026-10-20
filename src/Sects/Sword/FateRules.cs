using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using Reincarnation.Runtime.Content;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public static class FateRules
    {
        public const string Prefix = "yixian.sect.fate.sword.";
        public const string Rhyme = Prefix + "sword_rhyme";
        public const string CloudGuard = Prefix + "cloud_way";
        public const string FormationGuard = Prefix + "formation_guard";
        public const string CloudSea = Prefix + "cloud_sea";
        public static readonly StableId GuardStatus = new StableId("yixian.sect.status.fate_guard");
        public static int Required(string id) => id == Rhyme || id == CloudGuard ? 2 : id == FormationGuard || id == CloudSea ? 3 : 0;
        public static int Rank(ModCombatantSnapshot unit) => unit?.Loadout.Lineages.Where(l => l.Id.Value == SwordRules.Lineage).Select(l => l.Count).DefaultIfEmpty().Max() ?? 0;
        public static bool Has(ModCombatantSnapshot unit, string id) => unit != null && !unit.IsDowned && Required(id) > 0 && Rank(unit) >= Required(id) && unit.Loadout.Abilities.Any(a => a.Value == id);
        public static bool Has(PlayerRunState player, string id) => player != null && Required(id) > 0 && SectProgression.Rank(player) >= Required(id) && player.HasPurchasedShelfAbility(new StableId(id));
        public static bool IsCloud(JObject card) => SwordRules.Text(card, "name").StartsWith("云剑", StringComparison.Ordinal);
        public static bool Linked(ModCombatantSnapshot unit, int chain) => chain > 0 || Has(unit, CloudSea);
        public static int CostChain(PlayerRunState owner, int chain) => Has(owner, CloudSea) ? Math.Max(1, chain) : chain;
        public static void Register(ICodeModContext context)
        {
            context.OnBattle("round_start", Round);
            context.OnBattle("action_resolved", Resolved);
        }
        public static void Round(ModBattleContext context, CodeBattlePlan plan)
        {
            foreach (var unit in context.Units.Where(u => Has(u, FormationGuard)))
                if (plan.GetInt(SwordRules.StateKey(unit.RuntimeId, "fateGuardRound")) != context.Round) plan.ClearStatus(unit.RuntimeId, GuardStatus);
            foreach (var unit in context.Units.Where(u => Has(u, Rhyme)))
            {
                var marker = SwordRules.StateKey(unit.RuntimeId, "fateIntentRound");
                if (plan.GetInt(marker) == context.Round) continue;
                var key = SwordRules.StateKey(unit.RuntimeId, "intent");
                int amount = checked(plan.GetInt(key) + 2 * Rank(unit));
                plan.SetState(key, amount);
                plan.SetState(marker, context.Round);
                var status = new StableId("yixian.sect.status.intent");
                plan.ClearStatus(unit.RuntimeId, status);
                plan.Status(unit.RuntimeId, unit.RuntimeId, status, amount);
            }
        }
        // Called only for the actively played card, never a recursively repeated formation.
        public static void Played(ModBattleContext context, CodeBattlePlan plan, ModCombatantSnapshot actor, JObject card)
        {
            if (IsCloud(card) && Has(actor, CloudGuard)) plan.Shield(actor.RuntimeId, 3 * Rank(actor));
        }
        public static void Resolved(ModBattleContext context, CodeBattlePlan plan)
        {
            var actor = context.Units.FirstOrDefault(u => u.RuntimeId == context.ActorId);
            if (!Has(actor, FormationGuard) || (!SwordRules.Cards.ContainsKey(context.ActionId.Value) && !CardTableCatalog.TryGetCard(context.ActionId, out var _))) return;
            // ActionResolved is emitted only after a successful action. Draw/discard/move/attack
            // are not catalog cards, so they cannot activate this protection.
            var key = SwordRules.StateKey(actor.RuntimeId, "fateGuardRound");
            if (plan.GetInt(key) == context.Round) return;
            plan.SetState(key, context.Round);
            plan.Status(actor.RuntimeId, actor.RuntimeId, GuardStatus, 1);
        }
        public static int Retain(PlayerRunState owner, CombatantState actor, ModStateSnapshot state, int round, int shield, int original)
            => actor != null && Has(owner, FormationGuard) && SwordRules.ReadInt(state, actor.RuntimeId, "fateGuardRound") == round ? shield : original;

        public static string Name(string id) => id == Rhyme ? "剑韵修炼" : id == CloudGuard ? "云之道" : id == FormationGuard ? "剑阵守护" : id == CloudSea ? "云海连绵" : "";
        public static string Describe(string id, int rank)
        {
            if (id == Rhyme) return "每回合开始获得 " + (2 * rank) + " 剑意，首回合生效一次。\n随境界自动成长：炼气2／筑基4／金丹6／元婴8／化神10。剑意进入原有剑意池，整张攻击剑招消费一次、各段生效。";
            if (id == CloudGuard) return "每主动打出一张云剑，额外获得 " + (3 * rank) + " 护盾。\n随境界自动成长：炼气3／筑基6／金丹9／元婴12／化神15。按整张牌触发一次，不按攻击段数或目标数重复；自动重复结算不触发。";
            if (id == FormationGuard) return "每回合成功打出第一张牌后，本回合结束时护盾不自然衰减（保留100%）。\n任何牌均可触发，不限剑阵；敌人伤害、技能支付仍消耗护盾。整回合未打牌则沿用原生保留率。";
            if (id == CloudSea) return "所有云剑无需前置即可触发连云效果；打出其他牌不打断实际云剑计数。\n不限次数，不增加虚拟云剑次数，不赠未购买的卡牌。连云减费可将斗气费用降至0。";
            return "";
        }
    }

    // Change only natural retention; damage, shield payments, native floors and previews
    // still follow their native paths. The marker is authoritative ModState per actor/round.
    [HarmonyPatch(typeof(BattleAuthorityService), "CalculateRetainedShieldForCurrentBattle")]
    internal static class FateShieldRetention
    {
        private static readonly System.Reflection.FieldInfo State = AccessTools.Field(typeof(BattleAuthorityService), "modState");
        private static void Postfix(BattleAuthorityService __instance, object __0, int __1, ref int __result)
        {
            if (__0 == null) return;
            var type = __0.GetType();
            var owner = (PlayerRunState)AccessTools.Property(type, "RunPlayer").GetValue(__0);
            var actor = (CombatantState)AccessTools.Property(type, "Combatant").GetValue(__0);
            __result = FateRules.Retain(owner, actor, (ModStateSnapshot)State.GetValue(__instance), __instance.Battle.Round, __1, __result);
        }
    }
    [HarmonyPatch(typeof(RunUIPresentationMapper), "DescribeAbilityOperation")]
    internal static class FateDescription
    {
        private static void Postfix(RuntimeContentOperation operation, StableId abilityId, ref string __result)
        {
            if (FateRules.Required(abilityId.Value) == 0) return;
            int rank = operation != null && operation.TryGetValue("step", out var step) ? step.IntValue + 1 : FateRules.Required(abilityId.Value);
            __result = FateRules.Describe(abilityId.Value, rank) + "\n本局购买一次，突破后自动成长；无需主动释放，不占手牌。";
        }
    }
    [HarmonyPatch(typeof(RunUIPresentationMapper), "BuildAbilityFunctionalOneLiner")]
    internal static class FateOneLiner
    {
        private static void Postfix(RuntimeAbilityDefinition ability, int ownedRank, ref string __result)
        {
            if (ability == null || FateRules.Required(ability.StableId.Value) == 0) return;
            __result = FateRules.Describe(ability.StableId.Value, Math.Max(ownedRank, FateRules.Required(ability.StableId.Value))).Split('\n')[0];
        }
    }
    [HarmonyPatch(typeof(SectDescriptions), "Rank")]
    internal static class FateRankDescription
    {
        private static void Postfix(int rank, ref string __result)
        {
            if (rank == 2) __result += "\n本境界开放仙命：剑韵修炼（40点）、云之道（40点）；随当前境界成长。";
            if (rank == 3) __result += "\n本境界开放仙命：剑阵守护（40点）、云海连绵（40点）；购买后完整生效。";
        }
    }
}
