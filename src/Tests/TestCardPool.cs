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
            if(SectRegistry.Rank(player,FiveElementRules.Lineage)>0)
            {
                // This explicit isolated fixture arrangement covers individual card
                // formulas; it is not a production reconfiguration entry point.
                var wanted=cards.Where(id=>SectRegistry.Cards.TryGetValue(id.Value,out var c)&&SectRegistry.Owner(c)==FiveElementRules.Lineage).Distinct().ToArray();
                if(wanted.Length>0)
                {
                    // Keep a legal-sized fixture library: at most two learned cards
                    // per higher realm. This is explicit test arrangement, not UI access.
                    int rank=SectRegistry.Rank(player,FiveElementRules.Lineage);
                    var library=FiveDeckRules.Candidates(1).Select(FiveDeckRules.CardId).ToList();
                    for(int r=2;r<=rank;r++)
                    {
                        var learnedRequired=wanted.Where(id=>(int)SectRegistry.Cards[id.Value]["rank"]==r).ToArray();
                        if(learnedRequired.Length>2)throw new System.InvalidOperationException("fixture exceeds two learned cards per realm");
                        library.AddRange(learnedRequired.Concat(FiveDeckRules.Unlocked(player).Where(id=>(int)SectRegistry.Cards[id.Value]["rank"]==r&&!learnedRequired.Contains(id))).Take(2));
                    }
                    var purchased=(List<StableId>)HarmonyLib.AccessTools.Field(typeof(PlayerRunState),"purchasedShelfAbilityIds").GetValue(player);
                    purchased.RemoveAll(id=>id.Value.StartsWith(FiveDeckRules.UnlockPrefix,System.StringComparison.Ordinal));purchased.AddRange(library.Select(FiveDeckRules.UnlockToken));
                    var next=wanted.Concat(FiveDeckRules.Cards(player).Where(id=>library.Contains(id)&&!wanted.Contains(id))).Concat(library).Distinct().Take(8).ToArray();
                    FiveDeckRules.Commit(player,next);
                }
                return player.ActiveCardPackIds;
            }
            var required=cards.Select(card=>CardTableCatalog.Packs.FirstOrDefault(p=>player.HasPurchasedShelfAbility(p.UnlockAbilityId)&&p.CardIds.Contains(card))
                ?? throw new System.InvalidOperationException("fixture requested an unowned card pack: "+card)).Select(p=>p.UnlockAbilityId).Distinct().ToArray();
            var result=player.ActiveCardPackIds.ToList();
            foreach(var id in required)if(!result.Contains(id))result.Add(id);
            while(result.Count>8)result.Remove(result.Last(id=>!required.Contains(id)));
            return result;
        }
    }
}
