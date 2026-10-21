using Newtonsoft.Json.Linq;
using System.Reflection;
using Reincarnation.Domain;
using YixianSect;
partial class Program
{
 static PlayerRunState SchoolPlayer(string school,int rank)
 {
  var p=Player(sect:false);Check(p.AddLineage(new StableId(school)).IsSuccess,"create school player");
  if(school==FiveElementRules.Lineage)SectProgression.Refresh(p);
  ((List<LineageRankState>)typeof(PlayerRunState).GetField("lineages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p))[0]=new LineageRankState(new StableId(school),rank);
  if(school==FiveElementRules.Lineage)for(int r=2;r<=rank;r++)Method(typeof(FiveDeckRules),"Learn").Invoke(null,new object[]{p,FiveDeckRules.Candidates(r).Take(2).Select(FiveDeckRules.CardId).ToArray()});
  return p;
 }
 static void CommitFive(PlayerRunState p,IReadOnlyList<StableId> cards)=>Method(typeof(FiveDeckRules),"Commit").Invoke(null,new object[]{p,cards});
 static RuleResult<ProgressionPurchaseReceipt> FivePurchase(HubService service,HubState hub,PlayerRunState p,StableId answer)
 {
  var patch=typeof(Entry).Assembly.GetType("YixianSect.FiveDeckUpgrade");
  object[] args={p,new StableId(FiveElementRules.Lineage),answer,default(RuleResult<ProgressionPurchaseReceipt>),null};
  // CoreCLR cannot detour the bundled MonoMod. Exercise the exact prefix, stock
  // purchase and postfix in order, rather than claiming a live Harmony test.
  try
  {
   bool run=(bool)Method(patch,"Prefix").Invoke(null,args);
   var result=run?service.PurchaseOrUpgradeLineage(hub,p,new StableId(FiveElementRules.Lineage),17,(StableId)args[2],null):(RuleResult<ProgressionPurchaseReceipt>)args[3];
   if(result.IsSuccess&&args[4]!=null)Check(!SectProgression.Refresh(p),"native mid-upgrade refresh accepts pending learning without changing deck");
   Method(patch,"Postfix").Invoke(null,new object[]{p,args[4],result});
   if(result.IsSuccess)SectProgression.Refresh(p);
   return result;
  }
  finally { Method(patch,"Finalizer").Invoke(null,new object[]{p}); }

 }
 static void FiveDeckTests()
 {
  var p=SchoolPlayer(FiveElementRules.Lineage,1);SectProgression.Refresh(p);
  Check(FiveDeckRules.Cards(p).Count==8&&FiveDeckRules.Unlocked(p).Count==8&&p.ActiveCardPackIds.SequenceEqual(new[]{FiveDeckRules.PackId}),"eight unlocked / eight active / one pack");
  var original=FiveDeckRules.Cards(p).ToArray();var purchases=p.PurchasedShelfAbilityIds.ToArray();SectProgression.Refresh(p);
  Check(p.PurchasedShelfAbilityIds.SequenceEqual(purchases),"starter library and selection idempotent");
  var ops=JObject.Parse(File.ReadAllText(Mod+"/content/abilities.json"))["operations"].OfType<JObject>();
  var five=ops.Where(o=>(string)o["fields"]["sourceContentId"]==FiveElementRules.Lineage).ToArray();
  Check(five.All(o=>(int)o["fields"]["acquisition"]==20),"no five cards/records are shop products");
  Check(five.Count(o=>((string)o["id"]).StartsWith(FiveDeckRules.TokenPrefix))==48&&five.Count(o=>((string)o["id"]).StartsWith(FiveDeckRules.UnlockPrefix))==48,"separate persisted library/selection identities");
  var bare=typeof(Entry).Assembly.GetType("YixianSect.FiveDeckNoBareUpgrade");object[] bareArgs={p,new StableId(FiveElementRules.Lineage),default(RuleResult)};
  Check(!(bool)Method(bare,"Prefix").Invoke(null,bareArgs),"cannot bypass learning two cards");
  var hub=new HubState(HubAccessKind.Full,1,new[]{p.Slot});
  var lineage=JObject.Parse(File.ReadAllText(Mod+"/content/lineages.json"))["operations"].OfType<JObject>().Single(o=>(string)o["id"]==FiveElementRules.Lineage);var f=(JObject)lineage["fields"];
  var steps=f["steps"].OfType<JObject>().Select(s=>new Reincarnation.Config.ContentProgressionStepDefinition((string)s["rank"],(int)s["price"],Operation((JObject)s["passive"]),Operation((JObject)s["active"]))).ToArray();
  var def=new Reincarnation.Config.ContentLineageDefinition(FiveElementRules.Lineage,1,"五行道盟","yixian_five",40,steps,"",new[]{"炼气","筑基","金丹","元婴","化神"});
  var runtime=(Reincarnation.Runtime.Content.RuntimeLineageDefinition)PrivateCtor(typeof(Reincarnation.Runtime.Content.RuntimeLineageDefinition),def);
  var progressions=new List<ProgressionDefinition>();var projection=runtime.GetType().Assembly.GetType("Reincarnation.Runtime.Content.RuntimeDomainCatalogProjection");
  Method(projection,"ProjectProgressions").MakeGenericMethod(runtime.GetType()).Invoke(null,new object[]{new[]{runtime},CatalogContentKind.Lineage,progressions});
  var service=new HubService(new EconomyCatalog(Array.Empty<EconomyCatalogEntry>(),progressions,Array.Empty<ShelfAbilityDefinition>()));
  for(int rank=2;rank<=5;rank++)
  {
   var oldLibrary=FiveDeckRules.Unlocked(p).ToArray();var incoming=FiveDeckRules.Candidates(rank).Take(2).Select(FiveDeckRules.CardId).ToArray();int points=p.RewardPoints;long revision=p.Revision;
   var draft=new FiveDeckDraft(FiveDeckRules.Candidates(rank).Select(FiveDeckRules.CardId).ToArray(),Array.Empty<StableId>(),2);
   Check(draft.Toggle(incoming[0])&&draft.Toggle(incoming[1])&&draft.Ready&&!draft.Toggle(FiveDeckRules.Candidates(rank).Skip(2).Select(FiveDeckRules.CardId).First()),"learn exactly two from current realm");
   Check(p.RewardPoints==points&&p.Revision==revision&&FiveDeckRules.Unlocked(p).SequenceEqual(oldLibrary),"learning draft/cancel is read-only");
   var answer=FiveDeckRules.EncodeUnlock(rank,draft.Selected);Check(answer.Value.Length<=128,"learn answer within native transport");
   foreach(var bad in new[]{default(StableId),new StableId("five-learn~"+rank+"~bad"),FiveDeckRules.EncodeUnlock(rank,new[]{incoming[0],incoming[0]}),FiveDeckRules.EncodeUnlock(rank,oldLibrary.Take(2).ToArray()),FiveDeckRules.EncodeUnlock(rank-1,incoming)})
   {Check(!FivePurchase(service,hub,p,bad).IsSuccess,"invalid learning rejected");Check(p.RewardPoints==points&&p.Revision==revision&&FiveDeckRules.Unlocked(p).SequenceEqual(oldLibrary)&&FiveDeckRules.Cards(p).SequenceEqual(original),"bad choice leaves rank/points/library/deck intact");}
   Check(FivePurchase(service,hub,p,answer).IsSuccess&&p.RewardPoints==points-(int)f["steps"][rank-1]["price"],"native breakthrough includes two learned cards");
   Check(FiveDeckRules.Unlocked(p).Count==8+2*(rank-1)&&oldLibrary.All(FiveDeckRules.Unlocked(p).Contains)&&incoming.All(FiveDeckRules.Unlocked(p).Contains),"library grows 8/10/12/14/16 without losing old cards");
   Check(FiveDeckRules.Cards(p).SequenceEqual(original),"unlocking never auto-equips new cards");
   Check(!FivePurchase(service,hub,p,answer).IsSuccess,"cannot replay prior unlock answer");
  }
  var library=FiveDeckRules.Unlocked(p).ToArray();var newDeck=library.Skip(8).Take(8).ToArray();int savedPoints=p.RewardPoints;long savedRevision=p.Revision;
  var edit=new FiveDeckDraft(library,original,8);
  Check(!edit.Toggle(library[8])&&edit.Ready,"full deck requires unchecking before adding");
  foreach(var id in original)Check(edit.Toggle(id),"remove any old card in edit draft");
  foreach(var id in newDeck)Check(edit.Toggle(id),"choose eight unlocked cards");
  Check(edit.Ready&&p.Revision==savedRevision&&p.RewardPoints==savedPoints&&FiveDeckRules.Cards(p).SequenceEqual(original),"deck draft has no side effects");
  var encoded=FiveDeckRules.EncodeSelection(edit.Selected);Check(encoded.Value.Length<=128&&FiveDeckRules.TrySelection(encoded,out var decoded)&&decoded.SequenceEqual(newDeck),"eight-card selection transport roundtrip");
  Check(FiveDeckRules.SetDeck(p,newDeck).IsSuccess&&p.Revision==savedRevision+1&&p.RewardPoints==savedPoints,"free deck save commits once");
  Check(FiveDeckRules.Unlocked(p).SequenceEqual(library),"saving does not consume or discard unlocked cards");
  savedRevision=p.Revision;Check(FiveDeckRules.SetDeck(p,newDeck).IsSuccess&&p.Revision==savedRevision,"same selection is a no-op");
  var unlearned=FiveDeckRules.Candidates(5).Select(FiveDeckRules.CardId).First(id=>!library.Contains(id));
  var foreign=new StableId(SectRegistry.Cards.First(c=>SectRegistry.Owner(c.Value)==SevenStarRules.Lineage).Key);
  foreach(var bad in new[]{newDeck.Take(7).ToArray(),library.Take(9).ToArray(),newDeck.Take(7).Append(newDeck[0]).ToArray(),newDeck.Take(7).Append(unlearned).ToArray(),newDeck.Take(7).Append(foreign).ToArray()})
  {Check(!FiveDeckRules.SetDeck(p,bad).IsSuccess,"invalid deck save rejected");Check(p.Revision==savedRevision&&p.RewardPoints==savedPoints&&FiveDeckRules.Cards(p).SequenceEqual(newDeck)&&FiveDeckRules.Unlocked(p).SequenceEqual(library),"invalid save atomic");}
  Check(FiveDeckRules.SetDeck(p,original).IsSuccess,"previously unequipped cards can all be selected again");
  var encoder=typeof(Entry).Assembly.GetType("YixianSect.FiveDeckEncodeSelection");object[] encodeArgs={new[]{encoded},default(StableId)};
  Check(!(bool)Method(encoder,"Prefix").Invoke(null,encodeArgs)&&(StableId)encodeArgs[1]==encoded,"stock pack gateway transports the deck answer");
  encodeArgs=new object[]{new[]{CardTableCatalog.Packs[0].UnlockAbilityId},default(StableId)};Check((bool)Method(encoder,"Prefix").Invoke(null,encodeArgs),"ordinary native pack encoding unchanged");
  var wire=typeof(Entry).Assembly.GetType("YixianSect.FiveDeckNetworkPayload");
  object[] wireArgs={CardTableRules.SetPacksActionId,default(StableId),encoded,(int)CombatActionKind.UseAbility,false,(int)FormationRow.None,-1,false};
  Check(!(bool)Method(wire,"Prefix").Invoke(null,wireArgs)&&(bool)wireArgs[7],"new eight-card shape is admitted by the network wire boundary");
  foreach(int change in new[]{1,3,4,5,6})
  {
   var badWire=(object[])wireArgs.Clone();badWire[7]=true;
   if(change==1)badWire[1]=ActorId;else if(change==3)badWire[3]=(int)CombatActionKind.Attack;else if(change==4)badWire[4]=true;else if(change==5)badWire[5]=(int)FormationRow.Front;else badWire[6]=0;
   Check(!(bool)Method(wire,"Prefix").Invoke(null,badWire)&&!(bool)badWire[7],"malformed wire fields stay rejected");
  }
  wireArgs[0]=CardTableRules.DrawActionId;Check((bool)Method(wire,"Prefix").Invoke(null,wireArgs),"unrelated network payloads retain native policy");
  var poor=SchoolPlayer(FiveElementRules.Lineage,1);SectProgression.Refresh(poor);Check(poor.SpendRewardPoints(poor.RewardPoints).IsSuccess,"empty native wallet");
  var poorOld=FiveDeckRules.Unlocked(poor).ToArray();long poorRev=poor.Revision;
  Check(!FivePurchase(service,hub,poor,FiveDeckRules.EncodeUnlock(2,FiveDeckRules.Candidates(2).Take(2).Select(FiveDeckRules.CardId).ToArray())).IsSuccess&&poor.Revision==poorRev&&FiveDeckRules.Unlocked(poor).SequenceEqual(poorOld),"insufficient points never learns/equips cards");
  var other=SchoolPlayer(FiveElementRules.Lineage,1);SectProgression.Refresh(other);Check(FiveDeckRules.Unlocked(other).Count==8&&FiveDeckRules.Cards(other).SequenceEqual(original),"library/selection isolated by player");
  Check(other.AddLineage(CardTableCatalog.LineageId).IsSuccess,"native BL08 combination allowed");SectProgression.Refresh(other);
  var nativePacks=CardTableCatalog.Packs.Take(3).Select(x=>x.UnlockAbilityId).ToArray();foreach(var id in nativePacks)if(!other.HasPurchasedShelfAbility(id))Check(other.GrantFreeShelfAbility(id).IsSuccess,"own native test pack");
  Check(!SectProgression.SetActive(other,new[]{FiveDeckRules.PackId}).IsSuccess&&SectProgression.SetActive(other,new[]{FiveDeckRules.PackId}.Concat(nativePacks.Take(2)).ToArray()).IsSuccess,"native mixed selection limits retained");
  var nativeCard=CardTableCatalog.Cards[0].AbilityId;Check(FiveDeckRules.DrawPool(p,new[]{original[0],nativeCard}).SequenceEqual(new[]{nativeCard}.Concat(original)),"pool uses actual eight and preserves native cards");
  var restore=typeof(Entry).Assembly.GetType("YixianSect.FiveDeckRestore");Check((bool)Method(restore,"Prefix").Invoke(null,new object[]{p,new[]{FiveDeckRules.PackId}}),"library save admitted");
  Check(FiveDeckRules.SetDeck(p,newDeck).IsSuccess,"stage non-starter eight for restore");
  var savedPlayer=PlayerRunState.Restore(p.Birth,p.DerivedStats,p.Inventory,p.Equipment,p.Lineages,p.StyleId,p.StyleRank,p.IsAiControlled,p.PersistentEffects,p.GeneLockLevel,p.GeneLockProgress,p.RewardPoints,p.CurrentHealth,p.CurrentMagic,p.Revision,purchasedShelfAbilityIds:p.PurchasedShelfAbilityIds,activeCardPackIds:p.ActiveCardPackIds);
  SectProgression.Refresh(savedPlayer);
  Check(FiveDeckRules.Cards(savedPlayer).SequenceEqual(newDeck)&&FiveDeckRules.Unlocked(savedPlayer).SequenceEqual(library)&&savedPlayer.Revision==p.Revision,"native player restore preserves different active eight plus all sixteen unlocks without merging starters");
  var state=new ModStateSnapshot(new[]{new ModStateEntry(SectRegistry.Package,SectRegistry.StateKey(FiveElementRules.Lineage,ActorId,"waterForce"),ModValueKind.Integer,2),new ModStateEntry(SectRegistry.Package,SectRegistry.StateKey(FiveElementRules.Lineage,ActorId,"waterMastery"),ModValueKind.Integer,1)});
  Check(Effect(Run(FiveElementRules.End,state:state,actor:Unit(school:FiveElementRules.Lineage,rank:5)),ModBattleEffectKind.Damage)==6,"retired water mastery has no effect");
  Console.WriteLine("PASS five library: permanent 8/10/12/14/16 unlocks, independent eight-card selection, free reconfiguration, errors/poor/replay atomic, native gateway and mixed pools");
 }
}
