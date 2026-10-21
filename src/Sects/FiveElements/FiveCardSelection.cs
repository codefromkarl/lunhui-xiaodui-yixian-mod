using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // Singleton native pack identities are the persisted selection representation.
    // The 36 existing product packs and their native cp ordinals remain unchanged.
    public static class FiveCardSelection
    {
        public const string Prefix="yixian.sect.selection.";
        public static readonly List<JObject> Entries=new List<JObject>();
        private static int[] minimum={2,3,4,6,8};
        public static int Maximum { get; private set; }=16;
        public static int WaterMasteryMinimumRank { get; private set; }=2;
        public static int WaterMasteryMultiplier { get; private set; }=1;
        public static bool IsPureWater(PlayerRunState player)
        {
            var active=player.ActiveCardPackIds;
            return active.Count>0&&active.All(id=>Find(id)!=null||SectRegistry.FindPack(id)!=null)&&
                ActiveCards(player).All(id=>SectRegistry.Cards.TryGetValue(id.Value,out var c)&&SectRegistry.Owner(c)==FiveElementRules.Lineage&&(int?)c["element"]==2);
        }
        public static void Configure(JObject config)
        {
            var prices=config["waterPriceByRank"].Values<int>().ToArray();
            minimum=config["minimumCardsByRank"].Values<int>().ToArray();Maximum=(int)config["maximumCards"];
            if(prices.Length!=5||minimum.Length!=5||prices.Any(x=>x<0)||minimum.Any(x=>x<2||x>Maximum)||Maximum>32)throw new ArgumentException("元素购卡配置无效。");
            WaterMasteryMinimumRank=(int?)config["waterMasteryMinimumRank"]??2;WaterMasteryMultiplier=(int?)config["waterMasteryMultiplier"]??1;
            if(WaterMasteryMinimumRank<2||WaterMasteryMinimumRank>5||WaterMasteryMultiplier<1||WaterMasteryMultiplier>3)throw new ArgumentException("纯水专精配置无效。");
            Entries.Clear();int i=(int)config["selectionIndexStart"];
            foreach(var card in SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==FiveElementRules.Lineage))
            {
                string key=(string)card["key"];int rank=(int)card["rank"];
                Entries.Add(new JObject{["key"]="select_"+key,["name"]=(string)card["name"],["rank"]=rank,["index"]=i++,
                    ["price"]=(int?)card["element"]==2&&rank>1?prices[rank-1]:0,["cardKey"]=key,["cards"]=new JArray(key),["sect"]=FiveElementRules.Lineage});
            }
        }
        public static StableId Id(JObject entry)=>new StableId(Prefix+(string)entry["cardKey"]);
        public static JObject Find(StableId id)=>Entries.FirstOrDefault(e=>Id(e)==id);
        public static bool Is(StableId id)=>id.Value.StartsWith(Prefix,StringComparison.Ordinal);
        public static int Minimum(int rank)=>minimum[Math.Max(0,Math.Min(4,rank-1))];
        public static bool Owns(PlayerRunState player,JObject entry)
        {
            if(entry==null||SectRegistry.Rank(player,FiveElementRules.Lineage)<(int)entry["rank"])return false;
            var purchased=new HashSet<StableId>(player.PurchasedShelfAbilityIds);
            return purchased.Contains(Id(entry))||SectRegistry.Packs.Any(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage&&
                purchased.Contains(SectRegistry.PackId(p))&&p["cards"].Values<string>().Contains((string)entry["cardKey"]));
        }
        public static List<StableId> Owned(PlayerRunState player)=>Entries.Where(e=>Owns(player,e)).Select(Id).ToList();
        public static bool OwnedSet(PlayerRunState player,IReadOnlyList<StableId> ids,int minimumCount)
            =>ids!=null&&ids.Count>=minimumCount&&ids.Count<=Maximum&&ids.Distinct().Count()==ids.Count&&ids.All(id=>Owns(player,Find(id)));
        public static bool Valid(PlayerRunState player,IReadOnlyList<StableId> ids)
            =>OwnedSet(player,ids,Minimum(SectRegistry.Rank(player,FiveElementRules.Lineage)));
        public static IReadOnlyList<StableId> Water(PlayerRunState player)=>Entries.Where(e=>Owns(player,e)&&
            (int?)SectRegistry.Cards[SectRegistry.CardPrefix+(string)e["cardKey"]]["element"]==2).Select(Id).ToArray();
        public static IEnumerable<StableId> ActiveCards(PlayerRunState player)
            =>player.ActiveCardPackIds.SelectMany(id=>Find(id)?["cards"]?.Values<string>()??SectRegistry.FindPack(id)?["cards"]?.Values<string>()??Enumerable.Empty<string>()).Select(k=>new StableId(SectRegistry.CardPrefix+k));
    }
    [HarmonyPatch(typeof(PlayerRunState),"HasPurchasedShelfAbility")]
    internal static class FiveDerivedCardOwnership
    {
        private static void Postfix(PlayerRunState __instance,StableId __0,ref bool __result)
        {if(!__result&&FiveCardSelection.Is(__0))__result=FiveCardSelection.Owns(__instance,FiveCardSelection.Find(__0));}
    }
}
namespace YixianSect
{
    [HarmonyLib.HarmonyPatch(typeof(Reincarnation.Domain.PlayerRunState),"PurchaseShelfAbility")]
    internal static class FiveSinglePurchaseGuard
    {
        private static bool Prefix(Reincarnation.Domain.PlayerRunState __instance,Reincarnation.Domain.StableId abilityId,int price,ref Reincarnation.Domain.RuleResult __result)
        {
            if(!FiveCardSelection.Is(abilityId))return true;
            var entry=FiveCardSelection.Find(abilityId);
            if(entry==null||SectRegistry.Rank(__instance,FiveElementRules.Lineage)<(int)entry["rank"]||price!=(int)entry["price"]||price<=0)
                __result=Reincarnation.Domain.RuleResult.Failure(Reincarnation.Domain.SessionErrorCode.InvalidRequest,__instance.Revision);
            else if(FiveCardSelection.Owns(__instance,entry))__result=Reincarnation.Domain.RuleResult.Failure(Reincarnation.Domain.SessionErrorCode.ItemUnavailable,__instance.Revision);
            else return true;
            return false;
        }
    }
    // The stock restore truncates pack IDs at eight. Validate our card selections
    // and restore exactly, without changing the saved revision or purchased list.
    [HarmonyLib.HarmonyPatch(typeof(Reincarnation.Domain.PlayerRunState),"RestoreCardPacks")]
    internal static class FiveRestoreSelection
    {
        private static bool Prefix(Reincarnation.Domain.PlayerRunState __instance,System.Collections.Generic.IReadOnlyList<Reincarnation.Domain.StableId> saved)
        {
            if(!(saved?.Any(FiveCardSelection.Is)??false))return true;
            if(!FiveCardSelection.OwnedSet(__instance,saved,2))throw new System.ArgumentException("存档中的五行逐卡选择无效。");
            var active=(System.Collections.Generic.List<Reincarnation.Domain.StableId>)HarmonyLib.AccessTools.Field(typeof(Reincarnation.Domain.PlayerRunState),"activeCardPackIds").GetValue(__instance);
            active.Clear();active.AddRange(saved);
            var known=(System.Collections.Generic.HashSet<Reincarnation.Domain.StableId>)HarmonyLib.AccessTools.Field(typeof(Reincarnation.Domain.PlayerRunState),"knownCardPackIds").GetValue(__instance);
            foreach(var id in saved)known.Add(id);
            return false;
        }
    }
}

namespace YixianSect
{
    [HarmonyLib.HarmonyPatch(typeof(Reincarnation.Domain.RunPlayerSnapshot),"HasPurchasedShelfAbility")]
    internal static class FiveSnapshotCardOwnership
    {
        private static void Postfix(Reincarnation.Domain.RunPlayerSnapshot __instance,Reincarnation.Domain.StableId __0,ref bool __result)
        {
            if(__result||!FiveCardSelection.Is(__0))return;
            var entry=FiveCardSelection.Find(__0);if(entry==null||!__instance.Lineages.Any(l=>l.LineageId.Value==FiveElementRules.Lineage&&l.Rank>=(int)entry["rank"]))return;
            var purchased=new System.Collections.Generic.HashSet<Reincarnation.Domain.StableId>(__instance.PurchasedShelfAbilityIds);
            __result=SectRegistry.Packs.Any(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage&&purchased.Contains(SectRegistry.PackId(p))&&p["cards"].Values<string>().Contains((string)entry["cardKey"]));
        }
    }
}

namespace YixianSect
{
    [HarmonyLib.HarmonyPatch(typeof(Reincarnation.Domain.BattleAuthorityService),"SubmitCardPackSelection")]
    internal static class FiveMasterySelectionBoundary
    {
        private static void Postfix(Reincarnation.Domain.BattleAuthorityService __instance,object __0,Reincarnation.Domain.RuleResult<Reincarnation.Domain.BattleActionResultReceipt> __result)
        {
            if(!__result.IsSuccess)return;
            var player=(Reincarnation.Domain.PlayerRunState)HarmonyLib.AccessTools.Property(__0.GetType(),"RunPlayer").GetValue(__0);
            if(FiveCardSelection.IsPureWater(player))return;
            var actor=(Reincarnation.Domain.CombatantState)HarmonyLib.AccessTools.Property(__0.GetType(),"Combatant").GetValue(__0);
            var field=HarmonyLib.AccessTools.Field(typeof(Reincarnation.Domain.BattleAuthorityService),"modState");
            var state=(Reincarnation.Domain.ModStateSnapshot)field.GetValue(__instance);
            string key=SectRegistry.StateKey(FiveElementRules.Lineage,actor.RuntimeId,"waterMastery");
            if(SectRegistry.Read(state,key)>0)field.SetValue(__instance,state.Apply(SectRegistry.Package,new[]{new Reincarnation.Domain.ModStateEntry(SectRegistry.Package,key,Reincarnation.Domain.ModValueKind.Integer,0)}));
        }
    }
}
