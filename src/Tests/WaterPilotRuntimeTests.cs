using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Content;
using Reincarnation.UI.Flow;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using UnityEngine;
using UnityEngine.UI;
namespace YixianSect
{
 public sealed partial class RuntimeSelfTests
 {
  private JObject WaterPilotContract()
  {
   var ranks=new JArray();
   for(int rank=1;rank<=5;rank++)
   {
    var f=New(rank,school:FiveElementRules.Lineage,buyCurrent:false,seed:91001);var old=f.P.ActiveCardPackIds.ToArray();int before=f.P.RewardPoints;
    foreach(var entry in FiveCardSelection.Entries.Where(e=>(int)e["rank"]<=rank&&(int?)SectRegistry.Cards[SectRegistry.CardPrefix+(string)e["cardKey"]]["element"]==2))
     if(!FiveCardSelection.Owns(f.P,entry))f.Buy(FiveCardSelection.Id(entry));
    Require(f.P.ActiveCardPackIds.SequenceEqual(old),"buying individual cards changed active pool");
    var selected=FiveCardSelection.Water(f.P);Require(selected.Count==new[]{2,3,5,7,10}[rank-1],"water realm inventory");
    if(rank>1)
    {
     Require(data.TryGetAbility(selected.Last(),out var singleAbility),"single purchase ability missing");
     var purchaseDescription=ProgressionPassiveNarrator.Describe(singleAbility.Ranks[0].Operation);
     Require(purchaseDescription.Contains("单卡解锁")&&purchaseDescription.Contains("消耗"),"water shop omitted card effects/fee");
    }
    int points=f.P.RewardPoints;long revision=f.A.Run.Revision;
    var repeated=f.Send(RunCommand.PurchaseShelfAbility(Slot,selected.Last(),revision));
    Require(!repeated.IsSuccess&&f.P.RewardPoints==points&&f.A.Run.Revision==revision,"duplicate owned water charged or committed");
    var future=FiveCardSelection.Entries.FirstOrDefault(e=>(int)e["rank"]>rank&&(int?)SectRegistry.Cards[SectRegistry.CardPrefix+(string)e["cardKey"]]["element"]==2);
    if(future!=null)Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,FiveCardSelection.Id(future),f.A.Run.Revision)).IsSuccess&&f.P.RewardPoints==points,"future card purchase accepted");
    Require(f.P.SetActiveCardPacks(selected).IsSuccess,"water pool before start");f.StartBattle();
    Require(SectRegistry.Read(SchoolState(f),SectRegistry.StateKey(FiveElementRules.Lineage,f.Actor.RuntimeId,"waterMastery"))==(rank>=FiveCardSelection.WaterMasteryMinimumRank&&FiveCardSelection.WaterMasteryMultiplier>1?1:0),"pure water mastery opening eligibility");
    var selectedCards=new HashSet<StableId>(selected.Select(id=>new StableId(SectRegistry.CardPrefix+(string)FiveCardSelection.Find(id)["cardKey"])));
    Require(f.Table.Hand.Count==3&&f.Table.Hand.All(h=>selectedCards.Contains(h.CardId)),"opening hand outside selected pool");
    var hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();
    var reversed=selected.Reverse().ToArray();
    Require(f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(reversed)).IsSuccess,"formal singleton selection rejected");
    Require(f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand)&&f.P.RewardPoints==points,"selection changed hand or points");
    var invalid=selected.Concat(selected.Take(1)).ToArray();revision=f.A.Run.Revision;
    Require(!f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(invalid)).IsSuccess&&f.A.Run.Revision==revision,"duplicate selection committed");
    var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("water-pilot",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
    Require(mapper.TryCapture(f.A.Run,out var saved,out var error),"water save capture: "+error);
    Require(mapper.TryRestore(saved,out var restored,out error),"water save restore: "+error);
    Require(restored.Players[0].ActiveCardPackIds.SequenceEqual(reversed),"water save restore lost/reordered cards");
    Require(restored.Players[0].Revision==f.P.Revision,"water restore changed saved player revision");
    JObject ui=null;
    if(rank==5)
    {
     var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);var model=BattleUIPresentationMapper.BuildCardTable(snapshot,snapshot.Players.First(p=>p.Slot==Slot),data,battles);
     ui=VisualSelfTests.WaterSelectionControls(model);
     var mix=FiveCardSelection.Owned(f.P).Take(10).ToArray();Require(mix.Length>=8,"mixed ownership fixture");
     Require(f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(mix)).IsSuccess,"mixed-element singleton selection rejected");
     Require(SectRegistry.Read(SchoolState(f),SectRegistry.StateKey(FiveElementRules.Lineage,f.Actor.RuntimeId,"waterMastery"))==0,"mixing did not cancel mastery");
     Require(f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(selected)).IsSuccess,"switch back to water rejected");
     Require(SectRegistry.Read(SchoolState(f),SectRegistry.StateKey(FiveElementRules.Lineage,f.Actor.RuntimeId,"waterMastery"))==0,"switching back incorrectly regranted mastery");
     Require(f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(old)).IsSuccess,"legacy pack mode no longer selectable");
    }
    ranks.Add(new JObject{["rank"]=rank,["water_cards"]=selected.Count,["single_spend"]=before-points,["memory_save_restore"]=true,["formal_selection"]=true,["hand_unchanged"]=true,["ui"]=ui});
   }
   var gifted=New(1,school:FiveElementRules.Lineage,buyCurrent:false,seed:91002);var nurture=SectRegistry.Packs.Single(p=>(string)p["key"]=="yixian_five_nurture");
   Require(gifted.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,new StableId(FiveElementRules.Lineage),gifted.A.Run.Revision,PackId(nurture))).IsSuccess,"nurture gift fixture");
   var wave=FiveCardSelection.Entries.Single(e=>(string)SectRegistry.Cards[SectRegistry.CardPrefix+(string)e["cardKey"]]["name"]=="水灵·波澜");
   int giftPoints=gifted.P.RewardPoints;long giftRevision=gifted.A.Run.Revision;
   Require(gifted.P.HasPurchasedShelfAbility(FiveCardSelection.Id(wave)),"mixed gift did not imply water ownership");
   Require(!gifted.Send(RunCommand.PurchaseShelfAbility(Slot,FiveCardSelection.Id(wave),giftRevision)).IsSuccess&&gifted.P.RewardPoints==giftPoints&&gifted.A.Run.Revision==giftRevision,"gifted water could be purchased twice");
   var giftSnapshot=RunAuthoritySnapshotProjector.Create(gifted.A,Slot).Players.First(p=>p.Slot==Slot);
   Require(giftSnapshot.HasPurchasedShelfAbility(FiveCardSelection.Id(wave)),"shop snapshot does not mark gifted water owned");
   var transition=New(1,school:FiveElementRules.Lineage,buyCurrent:false,seed:91004);
   var oldWater=FiveCardSelection.Water(transition.P);Require(transition.P.SetActiveCardPacks(oldWater).IsSuccess,"transition initial selection");transition.Upgrade(new StableId(FiveElementRules.Lineage));
   Require(transition.P.ActiveCardPackIds.SequenceEqual(oldWater),"breakthrough rewrote old selected water");
   var transitionMapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("water-transition",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
   Require(transitionMapper.TryCapture(transition.A.Run,out var transitionSave,out var transitionError)&&transitionMapper.TryRestore(transitionSave,out var transitionRun,out transitionError)&&transitionRun.Players[0].ActiveCardPackIds.SequenceEqual(oldWater),"transition save lost old smaller selection: "+transitionError);
   var nativeMixed=New(2,school:FiveElementRules.Lineage,buyCurrent:false,seed:91003);nativeMixed.Upgrade(CardTableCatalog.LineageId);
   var nativeActive=nativeMixed.P.ActiveCardPackIds.ToArray();
   if(!FiveCardSelection.Owns(nativeMixed.P,wave))nativeMixed.Buy(FiveCardSelection.Id(wave));
   Require(nativeMixed.P.ActiveCardPackIds.SequenceEqual(nativeActive),"BL08 mix purchase auto-equipped singleton entries");
   Require(nativeMixed.P.SetActiveCardPacks(FiveCardSelection.Water(nativeMixed.P)).IsSuccess,"BL08 mix cannot select water cards");
   nativeMixed.StartBattle();Require(nativeMixed.Table.Hand.Count>=3,"BL08 mix draw failed");
   return new JObject{["native_BL08_mixed_selection"]=true,["ranks"]=ranks,["original_gift_ownership_no_repurchase"]=true};
  }
 }
 internal static partial class VisualSelfTests
 {
  internal static JObject WaterSelectionControls(CardTableViewModel model)
  {
   var root=new GameObject("WaterSelectionControls",typeof(RectTransform),typeof(Canvas));
   try
   {
    var hud=Prefab<GameHUDWindow>();var source=Field<CardTablePackPanel>(hud.GetComponent<GameHUDWindow>(),"cardPackPrefab");
    var panel=UnityEngine.Object.Instantiate(source,root.transform,false);foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))raycaster.enabled=false;
    int submits=0;IReadOnlyList<string> submitted=null;
    panel.Show(model,Field<CardTableArtSet>(hud.GetComponent<GameHUDWindow>(),"cardTableArt"),ids=>{submits++;submitted=ids;});
    var preset=panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="FiveWaterPreset");preset.onClick.Invoke();
    Require(Field<HashSet<string>>(panel,"selected").Count==10&&submits==0,"water UI did not stage ten owned cards");
    var confirm=Field<Button>(panel,"confirm");Require(confirm.interactable,"water UI cannot confirm more than eight cards");confirm.onClick.Invoke();
    Require(submits==1&&submitted.Count==10&&submitted.All(id=>FiveCardSelection.Is(new StableId(id))),"water UI native confirmation incorrect");
    AccessTools.Field(typeof(CardTablePackPanel),"fate").SetValue(panel,true);
    AccessTools.Method(typeof(CardTablePackPanel),"Render").Invoke(panel,null);
    Require(Field<System.Collections.IList>(panel,"rows").Cast<Component>().Count(x=>x.gameObject.activeSelf)==10,"fate page hid singleton selected cards");
    return new JObject{["fate_page_card_visibility"]=true,["staged_only"]=true,["native_confirm_submits"]=1,["selected_cards"]=10,["rendering_validated"]=false};
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
