using System.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    // Internal keys retain their authored slots; old invented effects are replaced, not migrated.
    public static class SevenStarFateRules
    {
        public const string Prefix = "yixian.sect.fate.star.";
        public const string Practice = Prefix + "astral_practice"; // 卜卦
        public const string Observatory = Prefix + "observatory"; // 观星
        public const string Pivots = Prefix + "seven_pivots"; // 后发制人
        public const string Cycle = Prefix + "destiny_cycle"; // 妙手回春
        public static int Required(string id) => id == Practice || id == Observatory ? 2 : id == Pivots || id == Cycle ? 3 : 0;
        public static bool Has(ModCombatantSnapshot unit, string id)
            => unit != null && !unit.IsDowned && Required(id) > 0 && SectRegistry.Rank(unit, SevenStarRules.Lineage) >= Required(id) && unit.Loadout.Abilities.Any(a => a.Value == id);
        public static int Rank(ModCombatantSnapshot unit) => SectRegistry.Rank(unit, SevenStarRules.Lineage);
        public static int InitialOmen(int rank) => rank < 4 ? 1 : rank - 2;
        public static int Rejuvenation(ModCombatantSnapshot unit) => Has(unit, Cycle) ? (Rank(unit) + 1) * 3 : 0;
        public static void Start(BattleTurn turn)
        {
            if (Has(turn.Actor, Practice)) SevenStarRules.Gain(turn, "omen", InitialOmen(Rank(turn.Actor)));
            if (Has(turn.Actor, Observatory)) SevenStarRules.Gain(turn, "star", 1);
            if (Has(turn.Actor, Cycle)) turn.MaxHealth(Rejuvenation(turn.Actor));
        }
        public static void BeforeHeal(BattleTurn turn)
        {
            int amount = Rejuvenation(turn.Actor);
            if (amount > 0) turn.MaxHealth(amount);
        }
        public static void FirstPostCard(BattleTurn turn)
        {
            if (!Has(turn.Actor, Pivots)) return;
            int amount = Rank(turn.Actor) * 3;
            turn.MaxHealth(amount); turn.Shield(amount);
            SevenStarHealing.Heal(turn, amount);
        }
    }
}
