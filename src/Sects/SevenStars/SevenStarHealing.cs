using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // Resolve once per healing effect, including earlier life requests in this same plan.
    // Always queue so repeated heals and maximum changes commit in the same order we select.
    public static class SevenStarHealing
    {
        public static ModCombatantSnapshot[] Allies(BattleTurn turn)
            => turn.Context.Units.Where(u => u.Side == turn.Actor.Side && u.Kind == CombatantKind.Player && !u.IsDowned && u.CurrentHealth > 0)
                .OrderBy(u => u.RuntimeId.Value, StringComparer.Ordinal).ToArray();

        public static StableId SelectedAlly(BattleTurn turn)
        {
            var targets = turn.Context.Action?.Targets;
            if (targets == null || targets.Count != 1 || !Allies(turn).Any(u => u.RuntimeId == targets[0]))
                throw new InvalidOperationException("请选择一名存活己方角色作为治疗目标。");
            return targets[0];
        }

        public static int Heal(BattleTurn turn, int amount, bool all = false, StableId selected = default)
        {
            if (amount <= 0) return 0;
            if (!selected.IsEmpty && (all || !Allies(turn).Any(u => u.RuntimeId == selected)))
                throw new InvalidOperationException("治疗目标必须是存活己方角色。");
            SevenStarFateRules.BeforeHeal(turn);
            var requests = OriginalHealthRules.Requests(turn.Plan).OfType<JObject>().ToArray();
            var candidates = Allies(turn)
                .Select(u =>
                {
                    int health = u.CurrentHealth, maximum = u.CurrentMaximumHealth;
                    foreach (var request in requests.Where(r => (string)r["target"] == u.RuntimeId.Value))
                    {
                        int value = (int)request["amount"];
                        if ((string)request["kind"] == "maximum")
                        {
                            maximum = Math.Max(1, maximum + value);
                            health = Math.Max(0, Math.Min(maximum, health + ((bool)request["current"] ? value : 0)));
                        }
                        else if ((string)request["kind"] == "heal" && health > 0)
                            health = Math.Min(maximum, health + value);
                    }
                    return new { Unit = u, Health = health, Maximum = maximum };
                })
                .Where(c => c.Health > 0)
                .OrderBy(c => c.Unit.RuntimeId.Value, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0) return 0;
            // This is target selection, not an omen-enhanced random card payoff.
            var injured = candidates.Where(c => c.Health < c.Maximum).ToArray();
            var pool = injured.Length > 0 ? injured : candidates;
            var targets = all ? candidates : !selected.IsEmpty ? candidates.Where(c => c.Unit.RuntimeId == selected).ToArray()
                : new[] { pool[pool.Length == 1 ? 0 : turn.Plan.Random(0, pool.Length - 1)] };
            foreach (var target in targets)
                OriginalHealthRules.Queue(turn.Plan, turn.Actor.RuntimeId, target.Unit.RuntimeId, "heal", amount);
            int generated = checked(amount * targets.Length);
            turn.Set("healingTotal", checked(turn.Get("healingTotal") + generated));
            return generated;
        }
    }
}
