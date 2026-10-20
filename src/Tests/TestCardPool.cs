using System.Collections.Generic;
using System.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    internal static class TestCardPool
    {
        // Test arrangement is explicit now that purchases no longer equip every new pack.
        internal static IReadOnlyList<StableId> ForCards(PlayerRunState player,IReadOnlyList<StableId> cards)
        {
            var required=cards.Select(card=>CardTableCatalog.Packs.FirstOrDefault(p=>player.HasPurchasedShelfAbility(p.UnlockAbilityId)&&p.CardIds.Contains(card))
                ?? throw new System.InvalidOperationException("fixture requested an unowned card pack: "+card)).Select(p=>p.UnlockAbilityId).Distinct().ToArray();
            var result=player.ActiveCardPackIds.ToList();
            foreach(var id in required)if(!result.Contains(id))result.Add(id);
            while(result.Count>8)result.Remove(result.Last(id=>!required.Contains(id)));
            return result;
        }
    }
}
