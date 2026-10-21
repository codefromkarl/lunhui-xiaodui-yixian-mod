using System.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    // Automatic round draws use the existing authoritative native draw bridge.
    public static class SectDrawRules
    {
        public const int OpeningHand = 3;
        public const int RoundDraw = 2;
        private static string RoundKey(StableId actor) => "cards." + actor.Value + ".drawRound";
        private static bool IsPlayer(ModCombatantSnapshot actor)
            => actor.Kind == CombatantKind.Player && !actor.IsDowned
                && SectRegistry.Sects.Keys.Any(s => SectRegistry.Rank(actor, s) > 0);

        public static void Register(ICodeModContext context)
        {
            context.OnBattle("battle_start", Start);
            context.OnBattle("round_start", Round);
        }

        public static void Start(ModBattleContext context, CodeBattlePlan plan)
        {
            foreach (var actor in context.Units.Where(IsPlayer))
            {
                plan.RemoveState(RoundKey(actor.RuntimeId));
                plan.RemoveState("cards." + actor.RuntimeId.Value + ".drawPending");
            }
        }

        public static void Round(ModBattleContext context, CodeBattlePlan plan)
        {
            foreach (var actor in context.Units.Where(IsPlayer))
            {
                string key = RoundKey(actor.RuntimeId);
                if (context.Round <= 1 || context.Round <= plan.GetInt(key)) continue;
                plan.SetState(key, context.Round);
                string draw = "cards." + actor.RuntimeId.Value + ".drawPending";
                plan.SetState(draw, plan.GetInt(draw) + RoundDraw);
            }
        }

    }
}
