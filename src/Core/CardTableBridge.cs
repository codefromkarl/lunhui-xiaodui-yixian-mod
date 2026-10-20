using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    public static class CardTableBridge
    {
        // Build complete replacement lists first; roll back Cards if the second write fails.
        public static void InjectCatalog()
        {
            BuildCatalog(out var cards,out var packs);
            var beforeCards=CardTableCatalog.Cards;
            var cardsField=typeof(CardTableCatalog).GetField("Cards");
            var packsField=typeof(CardTableCatalog).GetField("Packs");
            cardsField.SetValue(null,cards);
            try {packsField.SetValue(null,packs);}
            catch {cardsField.SetValue(null,beforeCards);throw;}
        }
        public static void BuildCatalog(out IReadOnlyList<CardTableCard> resultCards,out IReadOnlyList<CardTablePack> resultPacks)
        {
            var cardCtor=typeof(CardTableCard).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();
            var packCtor=typeof(CardTablePack).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();
            var beforeCards=CardTableCatalog.Cards;var beforePacks=CardTableCatalog.Packs;
            var ownKeys=new HashSet<string>(SectRegistry.Cards.Values.Select(c=>SwordRules.Text(c,"key")),StringComparer.Ordinal);
            var packKeys=new HashSet<string>(SectRegistry.Packs.Select(p=>SwordRules.Text(p,"key")),StringComparer.Ordinal);
            var indexes=new HashSet<int>(SectRegistry.Packs.Select(p=>SwordRules.Int(p,"index")));
            if(beforePacks.Any(p=>indexes.Contains(p.ShelfIndex)&&!packKeys.Contains(p.Key)))
                throw new ArgumentException("弈仙门派货架编号与其他包冲突，未改写卡组目录。");
            if(beforeCards.Any(c=>ownKeys.Contains(c.Key)&&c.AbilityId.Value!=SectRegistry.CardPrefix+c.Key))
                throw new ArgumentException("弈仙门派卡牌key与其他目录冲突，未改写卡组目录。");
            var cards=beforeCards.Where(c=>!ownKeys.Contains(c.Key)&&!c.Key.StartsWith("yixian_sword",StringComparison.Ordinal)&&!c.Key.StartsWith("yixian_common",StringComparison.Ordinal)).ToList();
            foreach(var row in SectRegistry.Cards.Values)
            {
                var card=(CardTableCard)cardCtor.Invoke(new object[]{SwordRules.Text(row,"key"),SwordRules.Text(row,"name"),SwordRules.Int(row,"rank"),(CardTableCardKind)0,null,(CardTableMechanic)0,(CardTableRace)0});
                AccessTools.Field(typeof(CardTableCard),"<AbilityId>k__BackingField").SetValue(card,new StableId(SectRegistry.CardPrefix+card.Key));
                cards.Add(card);
            }
            var packs=beforePacks.Where(p=>!packKeys.Contains(p.Key)&&p.Key!="yixian_common").ToList();
            // Preserve the old shelf.40~49 catalog ordinals used by native cp~ parameters.
            foreach(var row in SectRegistry.Packs.OrderBy(p=>SwordRules.Int(p,"index")))
            {
                var pack=(CardTablePack)packCtor.Invoke(new object[]{SwordRules.Text(row,"key"),SwordRules.Text(row,"name"),SwordRules.Int(row,"rank"),SwordRules.Int(row,"index"),((JArray)row["cards"]).Values<string>().ToArray()});
                AccessTools.Field(typeof(CardTablePack),"<UnlockAbilityId>k__BackingField").SetValue(pack,SectRegistry.PackId(row));
                AccessTools.Field(typeof(CardTablePack),"<CardIds>k__BackingField").SetValue(pack,Array.AsReadOnly(((JArray)row["cards"]).Values<string>().Select(k=>new StableId(SectRegistry.CardPrefix+k)).ToArray()));
                packs.Add(pack);
            }
            resultCards=cards.AsReadOnly();resultPacks=packs.AsReadOnly();
        }
    }

    [HarmonyPatch(typeof(PlayerRunState),"SetActiveCardPacks")]
    internal static class SectActivePacks
    {
        private static bool Prefix(PlayerRunState __instance,IReadOnlyList<StableId> packIds,ref RuleResult __result)
        {
            // Do not alias TryGetLineageRank globally: the shop must distinguish owning BL08
            // from merely using its card engine, or buying BL08 after a school becomes impossible.
            if(SectProgression.Rank(__instance)==0||SectProgression.Native(__instance))return true;
            __result=SectProgression.SetActive(__instance,packIds);return false;
        }
    }
    [HarmonyPatch(typeof(PlayerRunState),"RefreshCardPackProgression")]
    internal static class SectPackProgression
    {
        private static bool Prefix(PlayerRunState __instance)=>SectProgression.Refresh(__instance);
    }
    [HarmonyPatch(typeof(PlayerRunState),"GetOwnedCardPackIds")]
    internal static class SectOwnedPacks
    {
        private static void Postfix(PlayerRunState __instance,ref List<StableId> __result)
        {if(SectProgression.Rank(__instance)>0&&!SectProgression.Native(__instance))__result=SectProgression.Owned(__instance);}
    }
    [HarmonyPatch(typeof(BattleAuthorityService),"CardTableRank")]
    internal static class SectCardTableRank
    {
        private static void Postfix(ref int __result,object __0)
        {if(__result==0 && AccessTools.Property(__0.GetType(),"RunPlayer").GetValue(__0) is PlayerRunState p)__result=SectProgression.Rank(p);}
    }
    [HarmonyPatch(typeof(BattleAuthorityService),"ResolveAuthoredEnergyPool")]
    internal static class SectEnergyPool
    {
        private static void Postfix(BattleActionDefinition __0,ref EnergyPoolKind __result)
        {if(__0!=null&&SectRegistry.Cards.ContainsKey(__0.ActionId.Value))__result=EnergyPoolKind.Fighting;}
    }
    [HarmonyPatch(typeof(BattleAuthorityService),"TryResolveCardTableAction")]
    internal static class SectCardPrice
    {
        private static void Postfix(BattleAuthorityService __instance,object __0,CombatActionIntent __1,ref CombatActionSpec __2,ref SessionErrorCode __4,ref bool __result)
        {
            if(!__result)return;
            var type=__0.GetType();
            var player=(PlayerRunState)AccessTools.Property(type,"RunPlayer").GetValue(__0);
            var actor=(CombatantState)AccessTools.Property(type,"Combatant").GetValue(__0);
            int cost;int ap;
            if(SectRegistry.Cards.TryGetValue(__1.ActionId.Value,out var card))
            {
                var state=(ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(__instance);
                if(!SectCardRules.CanPlay(card,state,actor.RuntimeId)){__result=false;__4=SessionErrorCode.ItemUnavailable;__2=default;return;}
                cost=SectCardRules.Cost(card, player, actor, state);ap=0;
            }
            else if(__1.ActionId==CardTableRules.DrawActionId&&SectProgression.Rank(player)>0){cost=0;ap=1;}
            else return;
            var s=__2;
            __2=new CombatActionSpec(s.ActionId,s.ActionKind,ap,cost,s.CooldownRounds,s.TargetKind,s.Range,s.AllowsDownedTarget,s.RequiresDownedTarget,s.AllowsFriendlyFire,s.AmmunitionFamilyId,s.AmmunitionRoundCost,s.AllowsEmptyTargetRow);
        }
    }
    [HarmonyPatch(typeof(BattleAuthorityService),"InitializeCardTablesOrThrow")]
    internal static class SectOpeningHand
    {
        private static void Postfix(BattleAuthorityService __instance,PlayerSlotId? __0)
        {
            var players=(System.Collections.IDictionary)AccessTools.Field(typeof(BattleAuthorityService),"players").GetValue(__instance);
            var tables=(System.Collections.IDictionary)AccessTools.Field(typeof(BattleAuthorityService),"cardTables").GetValue(__instance);
            foreach(System.Collections.DictionaryEntry entry in players)
            {
                if(__0.HasValue && !entry.Key.Equals(__0.Value))continue;
                var p=(PlayerRunState)AccessTools.Property(entry.Value.GetType(),"RunPlayer").GetValue(entry.Value);
                if(SectProgression.Rank(p)==0||!tables.Contains(entry.Key))continue;
                var table=tables[entry.Key];
                var hand=(System.Collections.IList)AccessTools.Field(table.GetType(),"Hand").GetValue(table);
                hand.Clear();
                AccessTools.Method(typeof(BattleAuthorityService),"DrawCardTableCardsOrThrow").Invoke(__instance,new[]{entry.Value,(object)3});
            }
        }
    }
    // Native BL08 grants this token again at round end. Skip only for pure sect players.
    [HarmonyPatch(typeof(BattleAuthorityService),"GrantCardTableGraveTokenOrThrow")]
    internal static class SectNoGraveToken
    {
        private static bool Prefix(object __0)
        {
            var p=(PlayerRunState)AccessTools.Property(__0.GetType(),"RunPlayer").GetValue(__0);
            return SectProgression.Rank(p)==0||SectProgression.Native(p);
        }
    }
}

namespace YixianSect
{
    [HarmonyPatch(typeof(LineageFreeAbilityPicks),"ComputeCandidates")]
    internal static class PreserveNativePackPicks
    {
        private static void Postfix(StableId lineageId,ref IReadOnlyList<StableId> __result)
        {
            if(lineageId!=CardTableCatalog.LineageId)return;
            var own=new HashSet<string>(SectRegistry.Packs.Select(p=>SectRegistry.PackId(p).Value));
            __result=__result.Where(id=>!own.Contains(id.Value)).ToArray();
        }
    }
}

namespace YixianSect
{
    // Keep drawing in the same authoritative command transaction as the script request.
    // No static battle pointer or client-local RNG is introduced.
    [HarmonyPatch(typeof(BattleAuthorityService),"RunModHook")]
    internal static class SectQueuedDraws
    {
        private static readonly FieldInfo State=AccessTools.Field(typeof(BattleAuthorityService),"modState");
        private static readonly FieldInfo Players=AccessTools.Field(typeof(BattleAuthorityService),"players");
        private static readonly MethodInfo Draw=AccessTools.Method(typeof(BattleAuthorityService),"DrawCardTableCardsOrThrow");
        private static void Postfix(BattleAuthorityService __instance)
        {
            var state=(ModStateSnapshot)State.GetValue(__instance);
            if(state==null)return;
            var players=(System.Collections.IDictionary)Players.GetValue(__instance);
            var ordered=players.Values.Cast<object>().OrderBy(p=>((PlayerRunState)AccessTools.Property(p.GetType(),"RunPlayer").GetValue(p)).Slot.Value);
            foreach(var player in ordered)
            {
                var actor=(CombatantState)AccessTools.Property(player.GetType(),"Combatant").GetValue(player);
                foreach(var key in new[]{SwordRules.StateKey(actor.RuntimeId,"drawPending"),"cards."+actor.RuntimeId.Value+".drawPending"})
                {
                    var entry=state.Entries.FirstOrDefault(e=>e.PackageId=="yixian.sect"&&e.Key==key);
                    if(entry==null)continue;
                    state=state.Apply("yixian.sect",new[]{new ModStateEntry("yixian.sect",key,ModValueKind.Removed)});
                    State.SetValue(__instance,state);
                    if(entry.Number>0&&!__instance.IsComplete&&!actor.IsDowned&&__instance.Battle.Phase==BattlePhase.Player)
                        Draw.Invoke(__instance,new[]{player,(object)entry.Number});
                }
            }
        }
    }
}

namespace YixianSect
{
    [HarmonyPatch(typeof(LineageFreeAbilityPicks),"GetOfferCount")]
    internal static class SectFreePickCount
    {
        private static void Postfix(StableId lineageId,ref int __result)
        {if(SectRegistry.IsSect(lineageId.Value))__result=2;}
    }
    [HarmonyPatch(typeof(LineageFreeAbilityPicks),"ComputeCandidates")]
    internal static class SectFoundationGift
    {
        private static bool Prefix(StableId lineageId,int rank,PlayerSlotId playerSlot,IReadOnlyList<StableId> ownedAbilityIds,ref IReadOnlyList<StableId> __result)
        {
            if(!SectRegistry.IsSect(lineageId.Value))return true;
            __result=SectProgression.GiftCandidates(lineageId,rank,playerSlot,ownedAbilityIds);
            return false;
        }
    }
}
