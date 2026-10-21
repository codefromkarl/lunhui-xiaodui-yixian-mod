using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // Authored group cards copy a complete attack to each target, never split a
    // fixed damage budget. Reuse Sword's authority/snapshot geometry projection.
    public static class SchoolGroupAttack
    {
        public static StableId Primary(BattleTurn turn)
            => (turn.Context.Action?.Targets ?? Array.Empty<StableId>())
                .Select(id => turn.Context.Units.FirstOrDefault(u => u.RuntimeId == id && u.Side != turn.Actor.Side
                    && !u.IsDowned && u.Kind != CombatantKind.AuxiliaryTarget)).FirstOrDefault(u => u != null)?.RuntimeId
                ?? turn.Context.Units.FirstOrDefault(u => u.Side != turn.Actor.Side && !u.IsDowned
                    && u.Kind != CombatantKind.AuxiliaryTarget)?.RuntimeId ?? default;

        public static StableId[] Targets(SwordTargeting.Layout layout, StableId actorId, StableId primaryId,
            JObject card, bool enabled)
        {
            var actor = layout.Units.FirstOrDefault(u => u.Id == actorId);
            var primary = layout.Units.FirstOrDefault(u => u.Id == primaryId);
            if (actor == null || primary == null || primary.Side == actor.Side || primary.Downed
                || primary.Kind == CombatantKind.AuxiliaryTarget) return Array.Empty<StableId>();
            var group = card?["groupAttack"] as JObject;
            if (!enabled || group == null) return new[] { primaryId };
            string shape = (string)group["shape"];
            if (shape == "row")
                return new[] { primaryId }.Concat(SwordTargeting.Select(layout, actorId, primaryId,
                    new JObject { ["area"] = group.DeepClone() }, false)).ToArray();
            if (shape != "all") throw new ArgumentException("未知门派群攻形状：" + shape);
            return new[] { primaryId }.Concat(layout.Units.Where(u => u.Id != primaryId && u.Side == primary.Side
                && !u.Downed && u.Kind != CombatantKind.AuxiliaryTarget && !u.AreaImmune)
                .OrderBy(u => u.Id).Select(u => u.Id)).ToArray();
        }

        public static StableId[] Targets(BattleTurn turn, JObject card, bool enabled)
            => Targets(SwordTargeting.For(turn.Context), turn.Actor.RuntimeId, Primary(turn), card, enabled);

        public static bool PreviewEnabled(JObject card, RunCombatantSnapshot actor)
        {
            string sect = SectRegistry.Owner(card);
            int Counter(string key) => actor.Statuses.Where(s => s.StatusId.Value == "yixian.sect.status."
                + SectRegistry.Prefix(sect) + "." + key).Sum(s => s.Stacks.Count);
            if (sect == SevenStarRules.Lineage)
            {
                if ((bool?)card["groupAttack"]?["position"] != true) return true;
                int slot = SevenStarRules.Slot(Counter("plays") + 1);
                return actor.Statuses.Any(s => s.StatusId.Value == SevenStarRules.StatusPrefix + "position" + slot)
                    || ((string)card["name"]).StartsWith("星弈", StringComparison.Ordinal)
                        && Counter("astralLinks") > 0;
            }
            int element = (int?)card["groupAttack"]?["element"] ?? 0;
            if (element == 0) return true;
            string[] names = { "", "metal", "water", "wood", "fire", "earth" };
            return actor.Statuses.Any(s => s.StatusId.Value == "yixian.sect.status.five." + names[element])
                || FiveElementRules.Links(Counter("last"), element)
                || Counter("nextActivate") > 0
                || (int?)card["rules"]?["activate"] == element;
        }
    }
}
