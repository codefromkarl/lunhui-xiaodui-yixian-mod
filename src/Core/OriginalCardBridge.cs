using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Reincarnation.Domain;
using Newtonsoft.Json.Linq;

namespace YixianSect
{
    [HarmonyPatch(typeof(BattleAuthorityService), "CardDrawPool")]
    internal static class OriginalConsumedCards
    {
        private static void Postfix(BattleAuthorityService __instance, object __0, ref List<StableId> __result)
        {
            var actor=(CombatantState)AccessTools.Property(__0.GetType(),"Combatant").GetValue(__0);
            var state=(ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(__instance);
            __result=__result.Where(id=>!SectRegistry.Cards.TryGetValue(id.Value,out var card)||SectCardRules.CanPlay(card,state,actor.RuntimeId)).ToList();
        }
    }

    [HarmonyPatch(typeof(BattleAuthorityService), "RunModHook")]
    internal static class OriginalDeckElements
    {
        private static void Prefix(BattleAuthorityService __instance, ModBattleHook __0)
        {
            if(__0!=ModBattleHook.BattleStart)return;
            var field=AccessTools.Field(typeof(BattleAuthorityService),"modState");
            var state=(ModStateSnapshot)field.GetValue(__instance);
            var players=(IDictionary)AccessTools.Field(typeof(BattleAuthorityService),"players").GetValue(__instance);
            foreach(var player in players.Values)
            {
                var owner=(PlayerRunState)AccessTools.Property(player.GetType(),"RunPlayer").GetValue(player);
                if(SectRegistry.Rank(owner,FiveElementRules.Lineage)==0)continue;
                var actor=(CombatantState)AccessTools.Property(player.GetType(),"Combatant").GetValue(player);
                int mask=0;
                foreach(var pack in SectRegistry.Packs.Where(p=>owner.ActiveCardPackIds.Contains(SectRegistry.PackId(p))))
                    foreach(var key in pack["cards"].Values<string>())
                        if(SectRegistry.Cards.TryGetValue(SectRegistry.CardPrefix+key,out var card))
                        {
                            int element=(int?)card["element"]??0;
                            if(element>=1&&element<=5)mask|=1<<(element-1);
                        }
                state=state.Apply(SectRegistry.Package,new[]{new ModStateEntry(SectRegistry.Package,SectRegistry.StateKey(FiveElementRules.Lineage,actor.RuntimeId,"deckElements"),ModValueKind.Integer,mask)});
            }
            field.SetValue(__instance,state);
        }
    }
}
