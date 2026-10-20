using System.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    public static class FiveFateRules
    {
        public const string Prefix = "yixian.sect.fate.five.";
        // Preserve pre-correction identity slots; names and effects are now source-backed.
        public const string Mark = Prefix + "gentle_rain";
        public const string Herbs = Prefix + "forged_edge";
        public const string WaterMark = Prefix + "heaven_cycle";
        public const string Explosion = Prefix + "endless_cycle";
        public static int Required(string id) => id == Mark || id == Herbs ? 2 : id == WaterMark || id == Explosion ? 3 : 0;
        public static bool Has(ModCombatantSnapshot actor, string id)
            => actor != null && !actor.IsDowned && Required(id) > 0 && SectRegistry.Rank(actor, FiveElementRules.Lineage) >= Required(id) && actor.Loadout.Abilities.Any(a => a.Value == id);
        public static void Start(BattleTurn turn)
        {
            if (Has(turn.Actor, Explosion)) turn.MaxHealth(-12);
            if (Has(turn.Actor, Mark)) turn.Set("nextActivate", 1);
            if (Has(turn.Actor, WaterMark)) { FiveElementRules.Activate(turn, 2); turn.Qi(4); }
        }
        public static void Activated(BattleTurn turn, int element)
        {
            if (element == 3 && Has(turn.Actor, Herbs)) turn.MaxHealth(6);
            if (Has(turn.Actor, Explosion)) FiveElementRules.LifeLoss(turn, 9);
        }
        public static void Neutral(BattleTurn turn)
        {
            if (Has(turn.Actor, Herbs)) FiveElementRules.Activate(turn, 3);
        }
    }
}
