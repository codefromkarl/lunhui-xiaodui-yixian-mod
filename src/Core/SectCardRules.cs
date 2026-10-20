using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public static class SectCardRules
    {
        public const int QiPerLingqi = 4;
        public static int PrintedCost(JObject card) => checked(QiPerLingqi * ((int?)card["rules"]?["cost"] ?? 0));
        public static bool CanPlay(JObject card,ModStateSnapshot state,StableId actor)
        {
            if(SectRegistry.Owner(card)==FiveElementRules.Lineage)return FiveElementRules.CanPlay(card,state,actor);
            if(SectRegistry.Owner(card)==SevenStarRules.Lineage)return SevenStarRules.CanPlay(card,actor,state);
            return true;
        }
        public static int Cost(JObject card, PlayerRunState player, CombatantState actor, ModStateSnapshot state)
        {
            if (SectRegistry.Owner(card) == SwordRules.Lineage)
                return SwordRules.ResourceCost(card, actor.Shield, FateRules.CostChain(player, SwordRules.ReadInt(state, actor.RuntimeId, "cloud")), SwordRules.ReadInt(state, actor.RuntimeId, "spiritDiscount") > 0);
            if (SectRegistry.Owner(card) == FiveElementRules.Lineage)
                return FiveElementRules.PrintedCost(card,state,actor.RuntimeId);
            return PrintedCost(card);
        }
    }
}
