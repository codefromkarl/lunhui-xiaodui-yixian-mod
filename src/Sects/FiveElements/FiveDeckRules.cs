using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public static class FiveDeckRules
    {
        public const int Size = 8;
        public const int UnlocksPerRank = 2;
        public const string PackKey = "five_deck";
        public const string TokenPrefix = "yixian.sect.deckcard.";
        public const string UnlockPrefix = "yixian.sect.unlockedcard.";
        public const string SelectionPrefix = "five-deck~";
        public static readonly StableId PackId = new StableId(SectIds.Pack(PackKey));
        private static readonly FieldInfo Purchased = AccessTools.Field(typeof(PlayerRunState), "purchasedShelfAbilityIds");
        private static readonly FieldInfo Active = AccessTools.Field(typeof(PlayerRunState), "activeCardPackIds");
        private static readonly FieldInfo Known = AccessTools.Field(typeof(PlayerRunState), "knownCardPackIds");
        public static IEnumerable<JObject> Candidates(int rank) => SectRegistry.Cards.Values.Where(c => SectRegistry.Owner(c) == FiveElementRules.Lineage && (int)c["rank"] == rank);
        public static StableId CardId(JObject card) => new StableId(SectRegistry.CardPrefix + (string)card["key"]);
        public static StableId Token(StableId card) => new StableId(TokenPrefix + card.Value.Substring(SectRegistry.CardPrefix.Length));
        public static StableId UnlockToken(StableId card) => new StableId(UnlockPrefix + card.Value.Substring(SectRegistry.CardPrefix.Length));
        private static IReadOnlyList<StableId> Read(IEnumerable<StableId> purchases,string prefix) => purchases.Where(id => id.Value.StartsWith(prefix, StringComparison.Ordinal)).Select(id => new StableId(SectRegistry.CardPrefix + id.Value.Substring(prefix.Length))).ToArray();
        public static IReadOnlyList<StableId> Cards(IEnumerable<StableId> purchases) => Read(purchases,TokenPrefix);
        public static IReadOnlyList<StableId> Cards(PlayerRunState player) => Cards(player.PurchasedShelfAbilityIds);
        public static IReadOnlyList<StableId> Unlocked(IEnumerable<StableId> purchases) => Read(purchases,UnlockPrefix);
        public static IReadOnlyList<StableId> Unlocked(PlayerRunState player) => Unlocked(player.PurchasedShelfAbilityIds);
        private static bool OwnCard(StableId id,int rank) => SectRegistry.Cards.TryGetValue(id.Value,out var c) && SectRegistry.Owner(c)==FiveElementRules.Lineage && (int)c["rank"]<=rank;
        public static bool Valid(IReadOnlyList<StableId> cards,int rank) => cards!=null && cards.Count==Size && cards.Distinct().Count()==Size && cards.All(id=>OwnCard(id,rank));
        public static bool ValidLibrary(IReadOnlyList<StableId> unlocked,int rank)
            => rank>=1 && rank<=5 && unlocked!=null && unlocked.Count==Size+(rank-1)*UnlocksPerRank && unlocked.Distinct().Count()==unlocked.Count && unlocked.All(id=>OwnCard(id,rank))
                && Enumerable.Range(1,rank).All(r=>unlocked.Count(id=>(int)SectRegistry.Cards[id.Value]["rank"]==r)==(r==1?Size:UnlocksPerRank));
        public static bool ValidSelection(PlayerRunState player,IReadOnlyList<StableId> cards) => Valid(cards,SectRegistry.Rank(player,FiveElementRules.Lineage)) && cards.All(Unlocked(player).Contains);
        public static bool IsLegacyPack(StableId id) => SectRegistry.FindPack(id) is JObject p && SectRegistry.Owner(p) == FiveElementRules.Lineage;
        public static bool IsRetired(StableId id) => IsLegacyPack(id) || id.Value.StartsWith("yixian.sect.selection.", StringComparison.Ordinal);
        public static bool Refresh(PlayerRunState player)
        {
            int rank=SectRegistry.Rank(player,FiveElementRules.Lineage);if(rank==0)return false;
            // Native Restore first constructs a rank-1 player, then loads the
            // saved purchases/ranks. Do not seed starter selection into that
            // half-built object or it would merge with a saved different eight.
            if(player.PurchasedShelfAbilityIds==null)return true;
            var purchased=(List<StableId>)Purchased.GetValue(player);
            if(Cards(player).Count==0 && Unlocked(player).Count==0)
            {
                var starter=Candidates(1).Select(CardId).ToArray();
                purchased.AddRange(starter.Select(UnlockToken));Commit(player,starter);
            }
            if(!(ValidLibrary(Unlocked(player),rank)||(FiveDeckUpgrade.Permitted(player)&&ValidLibrary(Unlocked(player),rank-1)))||!ValidSelection(player,Cards(player)))throw new InvalidOperationException("五行牌库或八张卡包无效，请用对应旧版继续旧局或开启新局。");
            if(!purchased.Contains(PackId))purchased.Add(PackId);
            var active=(List<StableId>)Active.GetValue(player);active.RemoveAll(IsRetired);
            if(!active.Contains(PackId))active.Add(PackId);
            while(active.Count>8)active.Remove(active.Last(id=>id!=PackId));
            ((HashSet<StableId>)Known.GetValue(player)).Add(PackId);return true;
        }
        // Called inside a progression/selection transaction, never by UI staging.
        internal static void Commit(PlayerRunState player,IReadOnlyList<StableId> cards)
        {
            if(!ValidSelection(player,cards))throw new ArgumentException("从已解锁五行牌中选择八张不同牌。");
            var purchased=(List<StableId>)Purchased.GetValue(player);purchased.RemoveAll(id=>id.Value.StartsWith(TokenPrefix,StringComparison.Ordinal));purchased.AddRange(cards.Select(Token));
        }
        internal static void Learn(PlayerRunState player,IReadOnlyList<StableId> cards)
        {
            var purchased=(List<StableId>)Purchased.GetValue(player);purchased.AddRange(cards.Select(UnlockToken));
        }
        public static StableId EncodeUnlock(int rank,IReadOnlyList<StableId> added) => Encode("five-learn~"+rank,added);
        public static StableId EncodeSelection(IReadOnlyList<StableId> cards) => Encode("five-deck",cards);
        private static StableId Encode(string prefix,IReadOnlyList<StableId> cards) => new StableId(prefix+"~"+string.Join("~",cards.Select(id=>(string)SectRegistry.Cards[id.Value]["sourceId"])));
        private static bool Decode(StableId answer,string prefix,int count,out IReadOnlyList<StableId> cards)
        {
            cards=null;if(!answer.Value.StartsWith(prefix,StringComparison.Ordinal))return false;
            var parts=answer.Value.Substring(prefix.Length).Split('~');if(parts.Length!=count)return false;
            var result=new List<StableId>();
            foreach(string source in parts)
            {
                var card=SectRegistry.Cards.Values.FirstOrDefault(c=>SectRegistry.Owner(c)==FiveElementRules.Lineage&&(string)c["sourceId"]==source);
                if(card==null)return false;result.Add(CardId(card));
            }
            if(result.Distinct().Count()!=count)return false;cards=result;return true;
        }
        public static bool TryUnlock(IReadOnlyList<StableId> unlocked,int rank,StableId answer,out IReadOnlyList<StableId> added)
        {
            added=null;return rank>=2&&rank<=5&&ValidLibrary(unlocked,rank-1)&&Decode(answer,"five-learn~"+rank+"~",UnlocksPerRank,out added)
                &&added.All(id=>!unlocked.Contains(id)&&(int)SectRegistry.Cards[id.Value]["rank"]==rank);
        }
        public static bool TrySelection(StableId answer,out IReadOnlyList<StableId> cards) => Decode(answer,SelectionPrefix,Size,out cards);
        public static RuleResult SetDeck(PlayerRunState player,IReadOnlyList<StableId> cards)
        {
            if(!ValidSelection(player,cards))return RuleResult.Failure(SessionErrorCode.InvalidRequest,player.Revision);
            if(Cards(player).SequenceEqual(cards))return RuleResult.Success(player.Revision);
            if(player.Revision==long.MaxValue)return RuleResult.Failure(SessionErrorCode.InvalidRequest,player.Revision);
            AccessTools.Method(typeof(RevisionedState),"EnsureRevisionCanAdvance").Invoke(player,null);Commit(player,cards);
            return RuleResult.Success((long)AccessTools.Method(typeof(RevisionedState),"CommitRevision").Invoke(player,null));
        }
        public static IEnumerable<StableId> ActiveCards(PlayerRunState player) => player.ActiveCardPackIds.SelectMany(id => id == PackId ? Cards(player) : SectRegistry.FindPack(id)?["cards"]?.Values<string>().Select(k => new StableId(SectRegistry.CardPrefix + k)) ?? Enumerable.Empty<StableId>());
        public static List<StableId> DrawPool(PlayerRunState player,IEnumerable<StableId> nativePool)
        {
            if(SectRegistry.Rank(player,FiveElementRules.Lineage)==0)return nativePool.ToList();
            var result=nativePool.Where(id=>!SectRegistry.Cards.TryGetValue(id.Value,out var c)||SectRegistry.Owner(c)!=FiveElementRules.Lineage).ToList();
            if(player.ActiveCardPackIds.Contains(PackId))result.AddRange(Cards(player));return result;
        }
    }

    [HarmonyPatch(typeof(HubService),"PurchaseOrUpgradeLineage",new[]{typeof(HubState),typeof(PlayerRunState),typeof(StableId),typeof(ulong),typeof(StableId),typeof(RunState)})]
    internal static class FiveDeckUpgrade
    {
        private sealed class Permit { }
        private static readonly ConditionalWeakTable<PlayerRunState,Permit> Permits=new ConditionalWeakTable<PlayerRunState,Permit>();
        internal static bool Permitted(PlayerRunState player)=>Permits.TryGetValue(player,out var _);
        private static bool Prefix(PlayerRunState player,StableId lineageId,ref StableId freeAbilityAnswer,ref RuleResult<ProgressionPurchaseReceipt> __result,out IReadOnlyList<StableId> __state)
        {
            __state=null;if(lineageId.Value!=FiveElementRules.Lineage||player==null)return true;
            int rank=SectRegistry.Rank(player,FiveElementRules.Lineage)+1;
            if(rank==1&&freeAbilityAnswer.IsEmpty)return true;
            if(!FiveDeckRules.TryUnlock(FiveDeckRules.Unlocked(player),rank,freeAbilityAnswer,out __state))
            {__result=RuleResult<ProgressionPurchaseReceipt>.Failure(SessionErrorCode.InvalidRequest,player.Revision);return false;}
            Permits.Add(player,new Permit());freeAbilityAnswer=default;return true;
        }
        private static void Postfix(PlayerRunState player,IReadOnlyList<StableId> __state,RuleResult<ProgressionPurchaseReceipt> __result)
        {if(__state!=null&&__result.IsSuccess)FiveDeckRules.Learn(player,__state);}
        private static void Finalizer(PlayerRunState player){if(player!=null)Permits.Remove(player);}
    }
    [HarmonyPatch(typeof(PlayerRunState),"UpgradeLineage")]
    internal static class FiveDeckNoBareUpgrade
    {
        private static bool Prefix(PlayerRunState __instance,StableId lineageId,ref RuleResult __result)
        {
            if(lineageId.Value!=FiveElementRules.Lineage||FiveDeckUpgrade.Permitted(__instance))return true;
            __result=RuleResult.Failure(SessionErrorCode.InvalidRequest,__instance.Revision);return false;
        }
    }
    [HarmonyPatch(typeof(PlayerRunState),"PurchaseShelfAbility")]
    internal static class FiveDeckNoPurchases
    {
        private static bool Prefix(PlayerRunState __instance,StableId abilityId,ref RuleResult __result)
        {
            if(abilityId!=FiveDeckRules.PackId&&!abilityId.Value.StartsWith(FiveDeckRules.TokenPrefix,StringComparison.Ordinal)&&!abilityId.Value.StartsWith(FiveDeckRules.UnlockPrefix,StringComparison.Ordinal)&&!FiveDeckRules.IsRetired(abilityId))return true;
            __result=RuleResult.Failure(SessionErrorCode.ItemUnavailable,__instance.Revision);return false;
        }
    }
    [HarmonyPatch(typeof(PlayerRunState),"RestoreCardPacks")]
    internal static class FiveDeckRestore
    {
        private static bool Prefix(PlayerRunState __instance,IReadOnlyList<StableId> saved)
        {
            int rank=SectRegistry.Rank(__instance,FiveElementRules.Lineage);if(rank==0)return true;
            if(!FiveDeckRules.ValidLibrary(FiveDeckRules.Unlocked(__instance),rank)||!FiveDeckRules.ValidSelection(__instance,FiveDeckRules.Cards(__instance))||saved==null||!saved.Contains(FiveDeckRules.PackId)||saved.Any(FiveDeckRules.IsRetired))
                throw new ArgumentException("此五行存档不包含解锁牌库，请使用对应旧版继续旧局或开启新局。");
            return true;
        }
    }
}
