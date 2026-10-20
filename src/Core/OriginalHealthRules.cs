using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    // The SDK has no maximum-health/life-loss plan primitive. Requests live in the
    // authoritative plan and are drained by the same command bridge as queued draws.
    public static class OriginalHealthRules
    {
        public const string PendingKey = "original.health.pending";
        public static readonly StableId MaximumStatus = new StableId("yixian.sect.status.maximum_adjustment");
        public static void Register(ICodeModContext context) => context.AddMaximumRule(ModifyMaximum);
        public static int ModifyMaximum(ModMaximumQuery query)
            => query.Kind == ModMaximumKind.Health ? Math.Max(1, query.Value + query.Unit.Statuses.Where(s => s.StatusId == MaximumStatus).Sum(s => s.FlatAmount)) : query.Value;
        public static JArray Requests(CodeBattlePlan plan)
        {
            var text = plan.GetText(PendingKey);
            return string.IsNullOrEmpty(text) ? new JArray() : JArray.Parse(text);
        }
        public static bool HasMaximum(CodeBattlePlan plan, StableId target)
            => Requests(plan).OfType<JObject>().Any(r => (string)r["kind"] == "maximum" && (string)r["target"] == target.Value);
        public static void Queue(CodeBattlePlan plan, StableId source, StableId target, string kind, int amount, bool current = false)
        {
            if (amount == 0) return;
            var requests = Requests(plan);
            if (requests.Count >= 64) throw new InvalidOperationException("单次卡牌生命调整过多。");
            requests.Add(new JObject { ["source"] = source.Value, ["target"] = target.Value, ["kind"] = kind, ["amount"] = amount, ["current"] = current });
            plan.SetState(PendingKey, requests.ToString(Newtonsoft.Json.Formatting.None));
        }
        public static void Apply(CombatantState target, StableId source, string kind, int amount, bool current)
        {
            if (kind == "heal") { if (!target.IsDowned) CombatVitalRules.ApplyHealing(target, Math.Max(0, amount)); return; }
            if (kind == "loss" || kind == "nonAttack")
            {
                amount=Math.Max(0,amount);
                if(kind=="nonAttack")
                {
                    int absorbed=Math.Min(target.Shield,amount);
                    if(absorbed>0)target.SetVitals(target.CurrentHealth,target.Shield-absorbed);
                    amount-=absorbed;
                }
                if(amount<=0||target.IsDowned)return;
                var guard=target.Statuses.FirstOrDefault(s=>s.StatusId.Value.StartsWith("yixian.sect.status.star.guard",StringComparison.Ordinal)
                    && int.TryParse(s.StatusId.Value.Substring("yixian.sect.status.star.guard".Length),out var index)&&index>=0&&index<32);
                if(guard.IsValid){target.RemoveStatus(guard.InstanceId);return;}
                int hp=Math.Max(0,target.CurrentHealth-amount);
                if(hp!=target.CurrentHealth)target.SetVitals(hp,target.Shield);
                return;
            }
            if (kind != "maximum") throw new InvalidOperationException("未知生命调整操作。");
            int hpBefore = target.CurrentHealth;
            var existing = target.Statuses.FirstOrDefault(s => s.StatusId == MaximumStatus);
            int delta = checked(existing.FlatAmount + amount);
            var status = new CombatStatusState(new StableId("yixian.sect.maximum." + target.RuntimeId.Value), MaximumStatus, source,
                new[] { new CombatStatusStackState(0) }, flatAmount: delta);
            var result = target.UpsertStatus(status);
            if (!result.IsSuccess) throw new InvalidOperationException("最大生命状态无法提交：" + result.ErrorCode);
            AccessTools.Method(typeof(CombatantState), "RefreshMaximums").Invoke(target, null);
            int desired = Math.Max(0, Math.Min(target.CurrentMaximumHealth, hpBefore + (current ? amount : 0)));
            if (desired != target.CurrentHealth) target.SetVitals(desired, target.Shield);
        }
    }

    [HarmonyPatch(typeof(BattleAuthorityService), "RunModHook")]
    internal static class OriginalHealthBridge
    {
        private static readonly FieldInfo State = AccessTools.Field(typeof(BattleAuthorityService), "modState");
        private static void Postfix(BattleAuthorityService __instance)
        {
            var state = (ModStateSnapshot)State.GetValue(__instance);
            var entry = state?.Entries.FirstOrDefault(e => e.PackageId == SectRegistry.Package && e.Key == OriginalHealthRules.PendingKey);
            if (entry == null) return;
            var requests = JArray.Parse(entry.Text).OfType<JObject>().ToArray();
            // Consume first so nested authority hooks cannot replay the same life operation.
            State.SetValue(__instance, state.Apply(SectRegistry.Package, new[] { new ModStateEntry(SectRegistry.Package, entry.Key, ModValueKind.Removed) }));
            foreach (var request in requests)
            {
                if (!__instance.Battle.TryGetCombatant(new StableId((string)request["target"]), out var target)) throw new InvalidOperationException("生命调整目标不存在。");
                OriginalHealthRules.Apply(target, new StableId((string)request["source"]), (string)request["kind"], (int)request["amount"], (bool)request["current"]);
            }
            AccessTools.Method(__instance.Battle.GetType(), "CommitChoiceChange").Invoke(__instance.Battle, null);
            AccessTools.Method(typeof(BattleAuthorityService), "EvaluateAndCommitOutcome").Invoke(__instance, new object[] { false });
        }
    }
}
