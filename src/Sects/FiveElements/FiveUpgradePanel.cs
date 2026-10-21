using System;
using System.Linq;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;

namespace YixianSect
{
    internal static class FiveUpgradePanel
    {
        private static T Read<T>(object owner,string field)=>(T)AccessTools.Field(owner.GetType(),field).GetValue(owner);
        internal static void Open(ShopWindow window,RunPlayerSnapshot player,int rank)
        {
            var candidates=FiveDeckRules.Candidates(rank).Select(FiveDeckRules.CardId).ToArray();
            var draft=new FiveDeckDraft(candidates,Array.Empty<StableId>(),FiveDeckRules.UnlocksPerRank);
            var step=Read<ExchangeLadderStepViewModel>(window,"currentLadderStep");
            FiveCardPicker.Open(window.transform,"FiveDeckUpgrade","五行突破 · "+step.RankName,"选择两张新牌加入本局牌库；当前八张不变，可在战斗卡包管理中调整。","确认解锁与突破",draft,Read<TMP_Text>(window,"freePickTitleLabel").font,selected=>
            {
                var names=string.Join("、",selected.Select(id=>(string)SectRegistry.Cards[id.Value]["name"]));
                var card=Read<LineageStyleCardViewModel>(window,"currentProgressionCard");
                var intent=new ProjectUIIntent(ProjectUIIntentKind.UpgradeLineage,FiveElementRules.Lineage,FiveDeckRules.EncodeUnlock(rank,selected).Value,0,false,"",0,0,-1L,-1L);
                AccessTools.Method(typeof(ShopWindow),"RequestExchangeConfirmation").Invoke(window,new object[]{"确认五行突破","解锁："+names+"\n原有牌库保留，当前八张不变。\n消耗 "+step.Price+" 奖励点，不另收购牌费。","突破并解锁","五行道盟 · "+step.RankName,step.Price,card.Icon,intent});
            });
        }
    }
    [HarmonyPatch(typeof(ShopWindow),"TryOpenFreePick")]
    internal static class FiveDeckOpenUpgrade
    {
        private static bool Prefix(ShopWindow __instance,StableId lineageId,int targetRank,ref bool __result)
        {
            if(lineageId.Value!=FiveElementRules.Lineage||targetRank==1)return true;
            var player=(RunPlayerSnapshot)AccessTools.Field(typeof(ShopWindow),"player").GetValue(__instance);
            FiveUpgradePanel.Open(__instance,player,targetRank);__result=true;return false;
        }
    }
}
