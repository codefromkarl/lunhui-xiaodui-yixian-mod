using System.Reflection;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Domain;
using Reincarnation.Modding;
using Reincarnation.Runtime.Content;
using YixianSect;
using DKind=Reincarnation.Domain.CatalogContentKind;

partial class Program
{
 static readonly string Mod=Environment.GetEnvironmentVariable("YIXIAN_MOD_ROOT")??Path.GetFullPath("mod/yixian.sect");
 static readonly string Managed=Environment.GetEnvironmentVariable("GAME_MANAGED")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local/share/Steam/steamapps/common/轮回小队 Demo/ReincarnationSquad_Data/Managed");
 static int checks;
 static EconomyCatalog ProjectedEconomy;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static);
 static object PrivateCtor(Type t,params object[] a)=>Activator.CreateInstance(t,BindingFlags.Instance|BindingFlags.NonPublic,null,a,null);
 static JObject Card(int id)=>SwordRules.Cards.Values.Single(c=>(string)c["sourceId"]=="100"+id.ToString("D4"));
 static StableId Id(JObject c)=>new(SwordRules.CardPrefix+(string)c["key"]);
 static readonly StableId ActorId=new("test.actor"),EnemyId=new("test.enemy");
 static ModCombatantSnapshot Unit(bool sect=true,int energy=24,int shield=0,byte slot=1,StatBlock? attributes=null,string school=null,int rank=1,string[] abilities=null)
 {
  var unit=new CombatantState(slot==1?ActorId:new StableId("test.actor"+slot),new StableId("lineage.test"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(slot));
  unit.SetVitals(300,shield);
  var pools=new EnergyPoolSet(new EnergyPoolState(EnergyPoolKind.Fighting,energy,60),default,default);
  Method(typeof(CombatantState),"AssignEnergy").Invoke(unit,new object[]{pools});
  var load=new ModUnitLoadout(null,null,null,sect?new[]{new ModHeldCount(new StableId(school??SwordRules.Lineage),rank)}:null,default,0,(abilities??Array.Empty<string>()).Select(id=>new StableId(id)).ToArray());
  return (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot),unit,load,attributes??new StatBlock(10,10,10,20),0,0,0);
 }
 static ModCombatantSnapshot Enemy()
 {
  var e=new CombatantState(EnemyId,new StableId("enemy.test"),CombatantKind.Enemy,CombatSide.Enemy,FormationRow.Front,300,20,0,0,0,BuffCatalog.Empty);
  return (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot),e,ModUnitLoadout.None,null,null,null,null);
 }
 static ModBattlePlan Run(Action<ModBattleContext,CodeBattlePlan> handler,StableId action=default,ModStateSnapshot state=null,int round=1,ModCombatantSnapshot actor=null,ModBattleHit[] hits=null,ModCombatantSnapshot[] extra=null)
 {
  actor??=Unit();var units=new List<ModCombatantSnapshot>{actor,Enemy()};if(extra!=null)units.AddRange(extra);
  var request=new ModActionRequest(action.Value,action,new[]{EnemyId},EnemyId,FormationRow.Front,Array.Empty<BattleEffectValue>(),false);
  var c=new ModBattleContext(ModBattleHook.ScriptedAction,1,1,round,actor.RuntimeId,action,units,state??ModStateSnapshot.Empty,new DeterministicRngState(17,3,0),actor.RuntimeId,default,hits,null,request,"test.battle");
  var p=(CodeBattlePlan)PrivateCtor(typeof(CodeBattlePlan),"yixian.sect",c);handler(c,p);
  return (ModBattlePlan)Method(typeof(CodeBattlePlan),"Build").Invoke(p,null);
 }
 static ModStateSnapshot State(params (string,int)[] values)=>new(values.Select(x=>new ModStateEntry("yixian.sect",SwordRules.StateKey(ActorId,x.Item1),ModValueKind.Integer,x.Item2)).ToArray());
 static int S(ModBattlePlan p,string key)=>SwordRules.ReadInt(p.State,ActorId,key);
 static int Damage(ModBattlePlan p)=>p.Operations.Sum(o=>o.Values.FirstOrDefault(v=>v.Key=="e0.flat").IntegerValue);
 static ContentOperationDefinition Operation(JObject row)
 {
  return new((string)row["handlerKey"],(ContentTargetKind)(int)row["targetKind"],"",((JArray)row["values"]??new JArray()).OfType<JObject>().Select(v=>new ContentValueDefinition((string)v["key"],(int?)v["intValue"]??0,(string)v["stringValue"]??"")).ToArray());
 }
 static void CatalogTests()
 {
  var table=JObject.Parse(File.ReadAllText(Mod+"/content/cardtable.json"));SwordRules.Configure(table);
  FiveCardSelection.Configure(JObject.Parse(File.ReadAllText(Mod+"/content/five-water-pilot.json")));
  Check(SwordRules.Cards.Count==48 && SwordRules.Packs.Count==12,"inventory");
  var source=JArray.Parse(File.ReadAllText("data/sources/sword-original-cards.json"));
  foreach(JObject c in table["cards"].Where(c=>SectRegistry.Owner((JObject)c)==SwordRules.Lineage))
  {
   if(c["sourceId"]!=null){var s=source.OfType<JObject>().Single(x=>(string)x["id"]==(string)c["sourceId"]);Check((int)c["rank"]==Array.IndexOf(new[]{"炼气","筑基","金丹","元婴","化神"},(string)s["realm"])+1,"original rank: "+c["name"]);}
  }
  foreach(int rank in Enumerable.Range(1,5))Check(SwordRules.Packs.Count(p=>(int)p["rank"]==rank)==(rank>=4?3:2),"realm pack count "+rank);
  Check(SwordRules.Packs.All(p=>((JArray)p["cards"]).Count==4),"four cards per pack");
  var paidKeys=SwordRules.Packs.SelectMany(p=>((JArray)p["cards"]).Values<string>()).ToList();
  Check(paidKeys.Count==48&&paidKeys.Distinct().Count()==48,"complete unique paid pool");
  var manifest=JObject.Parse(File.ReadAllText(Mod+"/mod.json"));
  foreach(JProperty asset in ((JObject)manifest["assets"]).Properties())Check(File.Exists(Mod+"/"+(string)asset.Value["file"]),"asset "+asset.Name);
  var abilities=JObject.Parse(File.ReadAllText(Mod+"/content/abilities.json"))["operations"].OfType<JObject>().Where(a=>(string)a["fields"]["sourceContentId"]==SwordRules.Lineage).ToList();
  var runtimeAbilities=new List<RuntimeAbilityDefinition>();
  foreach(var a in abilities)
  {
   var f=(JObject)a["fields"];var ar=(JObject)f["ranks"][0];var op=Operation((JObject)ar["operation"]);
   var config=new ContentAbilityDefinition((string)a["id"],1,(string)f["displayName"],(string)f["sourceContentId"],(ContentAbilityMode)(int)f["mode"],new[]{new ContentAbilityRankDefinition((string)ar["rank"],1,op)},null,(string)f["flavour"],(int)f["purchasePrice"],(ContentAbilityAcquisition)(int)f["acquisition"]);
   var runtime=(RuntimeAbilityDefinition)PrivateCtor(typeof(RuntimeAbilityDefinition),config);
   runtimeAbilities.Add(runtime);
   Check(runtime.Ranks.Count==1,"runtime projection "+a["id"]);
   var findings=ContentVocabulary.Check("abilities",f).ToList();Check(findings.Count==0,"vocabulary: "+a["id"]+" "+string.Join(";",findings.Select(x=>x.Message)));
   if((int)f["mode"]==20)
   {
    var shelf=new ShelfAbilityDefinition(runtime.StableId,new StableId(SwordRules.Lineage),DKind.Lineage,RuntimeAbilityDefinition.RequiredRank(runtime.Ranks[0]),(int)f["purchasePrice"]);
    Check(M6ProgressionContentResolver.TryCreateShelfPlan(runtime.Ranks[0].Operation,shelf,shelf.RequiredRank,out var _,out var error),"shelf passive "+error);
   }
   else if(SwordRules.Cards.TryGetValue((string)a["id"],out var card))
   {
    var values=((JArray)ar["operation"]["values"]).OfType<JObject>().ToDictionary(v=>(string)v["key"],v=>(int)v["intValue"]);
    Check(values["apCost"]==0&&values["energyPool"]==(int)EnergyPoolKind.Fighting,"authored card is zero AP / fighting: "+a["id"]);
    Check(values["energyCost"]==SwordRules.ResourceCost(card,0,0,false),"authored / dynamic undiscounted energy cost: "+a["id"]);
   }
  }
  var lineageFields=(JObject)JObject.Parse(File.ReadAllText(Mod+"/content/lineages.json"))["operations"][0]["fields"];
  var steps=lineageFields["steps"].OfType<JObject>().Select(step=>new ContentProgressionStepDefinition((string)step["rank"],(int)step["price"],Operation((JObject)step["passive"]),Operation((JObject)step["active"]))).ToArray();
  var lineage=new ContentLineageDefinition(SwordRules.Lineage,1,"云灵剑宗","yixian_sword",40,steps,"",new[]{"炼气","筑基","金丹","元婴","化神"});
  var rl=(RuntimeLineageDefinition)PrivateCtor(typeof(RuntimeLineageDefinition),lineage);
  var projection=typeof(RuntimeLineageDefinition).Assembly.GetType("Reincarnation.Runtime.Content.RuntimeDomainCatalogProjection");
  var progressions=new List<ProgressionDefinition>();
  Method(projection,"ProjectProgressions").MakeGenericMethod(typeof(RuntimeLineageDefinition)).Invoke(null,new object[]{new[]{rl},DKind.Lineage,progressions});
  Check(progressions.Single().PoolBases.SequenceEqual(new[]{16,24,32,40,48}),"actual lineage energy pool projection");
  var projectedShelves=new List<ShelfAbilityDefinition>();
  Method(projection,"ProjectShelfAbilities").Invoke(null,new object[]{runtimeAbilities,progressions,projectedShelves});
  Check(projectedShelves.Count==10&&projectedShelves.All(x=>x.SourceId.Value==SwordRules.Lineage),"actual shop projection");
  ProjectedEconomy=new EconomyCatalog(Array.Empty<EconomyCatalogEntry>(),progressions,projectedShelves);
  foreach(var rank in Enumerable.Range(1,5))
  {
   Check(M6ProgressionContentResolver.TryCreateLineagePlan(rl,rank,ProgressionEffectMode.Passive,out var _,out var error),"lineage plan "+error);
   Check(SectDescriptions.Rank(rank).Contains("本境界"),"rank description "+rank);
  }
  foreach(JObject op in JObject.Parse(File.ReadAllText(Mod+"/content/statuses.json"))["operations"])
  {
   var f=(JObject)op["fields"];
   var definition=new ContentStatusDefinition((string)op["id"],1,(string)f["displayName"],(ContentStatusCategory)20,9999,0,false,new ContentOperationDefinition("",ContentTargetKind.None,""),BuffPolarity.Neutral,BuffEnding.UntilBattleEnd,BuffStackDecay.Unchanged,BuffReapply.AddStack,dispellable:false,uniquePerTarget:true);
   var rs=(RuntimeStatusDefinition)PrivateCtor(typeof(RuntimeStatusDefinition),definition);
   Check(rs.Source.Ending==BuffEnding.UntilBattleEnd&&rs.Source.Modifiers.Count==0,"cosmetic status has no damage modifier");
   Check(ContentVocabulary.Check("statuses",f).Count()==0,"status vocabulary");
  }
  var repeat=abilities.Single(a=>(string)a["id"]==Id(Card(64)).Value);
  Check((int)repeat["fields"]["ranks"][0]["operation"]["targetKind"]==50,"formation replay targets enemy, not self");
  Check((int)Card(24)["rank"]==2&&(int)Card(40)["rank"]==4,"legacy cards ranks");
  foreach(var p in SwordRules.Packs)
   foreach(var k in ((JArray)p["cards"]).Values<string>())Check((int)SwordRules.Cards[SwordRules.CardPrefix+k]["rank"]==(int)p["rank"],"rank isolation");
  foreach(var pack in SwordRules.Packs)
  {
   var d=SectDescriptions.PackDetail(pack);
   foreach(var key in ((JArray)pack["cards"]).Values<string>())
   {
    var c=SwordRules.Cards[SwordRules.CardPrefix+key];
    Check(d.Contains((string)c["name"])&&d.Contains((string)c["description"]),"complete pack detail "+key);
   }
   Check(SectDescriptions.PackSummary(pack).Length<90,"compact list summary");
  }
  Check(!SwordRules.Cards.Keys.Any(k=>k.Contains("common"))&&!SwordRules.Packs.Any(p=>(string)p["key"]=="yixian_common"),"no generic pack/cards");
  Console.WriteLine("PASS catalog: 48 source cards / 12 four-card packs / 10 paid shelves; complete generated details and source ranks");
 }
 static PlayerRunState Player(int rank=1,bool native=false,bool sect=true)
 {
  var selection=new BirthSelection(new PlayerSlotId(1),"test",BirthAppearance.Default,new StableId("origin.test"),new StableId("experience.test"),new StableId("reason.test"));
  var inv=new InventoryState();var equip=new EquipmentState();var stats=new StatBlock(10,10,10,10);
  var birth=new ValidatedBirthSelection(selection,stats,inv.CaptureSnapshot(),equip.CaptureSnapshot(),10000,MindLightKind.EternalInstant);
  var p=new PlayerRunState(birth,new DerivedStats(stats,300,60,0,0,0,0,15000,10000,0,0),inv,equip,Array.Empty<StableId>(),0,0,10000);
  if(sect)Check(p.AddLineage(new StableId(SwordRules.Lineage)).IsSuccess,"add sect");
  if(native)Check(p.AddLineage(CardTableCatalog.LineageId).IsSuccess,"add native");
  if(sect){var ranks=(List<LineageRankState>)typeof(PlayerRunState).GetField("lineages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);ranks[0]=new LineageRankState(new StableId(SwordRules.Lineage),rank);}
  return p;
 }
 static void ProgressionTests()
 {
  var hub=new HubState(HubAccessKind.Full,1,new[]{new PlayerSlotId(1)});var service=new HubService(ProjectedEconomy);
  for(int rank=1;rank<=5;rank++)
  {
   var p=Player(rank);int points=p.RewardPoints;
   Check(!SectProgression.Refresh(p),"pure sect handles progression");
   Check(SectProgression.Owned(p).Count==2&&p.ActiveCardPackIds.Count==2,"two starter gifts and active");
   Check(p.RewardPoints==points,"starter gifts free");
   SectProgression.Refresh(p);Check(SectProgression.Owned(p).Count==2,"gifts idempotent");
   foreach(var pack in SwordRules.Packs.Where(x=>(int)x["price"]>0))
   {
    var result=service.PurchaseShelfAbility(hub,p,SwordRules.PackId(pack));
    Check(result.IsSuccess==((int)pack["rank"]<=rank),"purchase gate "+rank+" "+pack["name"]);
   }
   SectProgression.Refresh(p);
   Check(SectProgression.Owned(p).Count==SwordRules.Packs.Count(pack=>(int)pack["rank"]<=rank),"all lower packs retained");
   Check(!p.HasPurchasedShelfAbility(CardTableCatalog.FusionPackId),"no auto fusion");
   Check(p.ActiveCardPackIds.Count<=8,"active cap");
   var candidates=SectProgression.GiftCandidates(new StableId(SwordRules.Lineage),rank,p.Slot,Array.Empty<StableId>());
   Check(candidates.Count==(rank==2?2:0),"only foundation choice");
   if(rank==2)
   {
    Check(!LineageFreeAbilityPicks.IsAcceptedAnswer(candidates,default),"must choose one");
    Check(!LineageFreeAbilityPicks.IsAcceptedAnswer(candidates,new StableId("yixian.sect.pack.yixian_sword_cloud3")),"cannot choose higher pack");
    Check(LineageFreeAbilityPicks.IsAcceptedAnswer(candidates,candidates[0]),"valid foundation choice");
    Check(SectProgression.GiftCandidates(new StableId(SwordRules.Lineage),2,p.Slot,candidates.Take(1).ToArray()).Count==1,"owned excluded");
   }
  }
  var original=Player(sect:false,native:true);Check(SectProgression.Refresh(original),"native delegates unchanged");
  Check(!SwordRules.Packs.Any(p=>original.HasPurchasedShelfAbility(SwordRules.PackId(p))),"native no sect gift");
  var mixed=Player(native:true);Check(SectProgression.Refresh(mixed),"mixed delegates native");
  Check(SwordRules.Packs.Where(p=>(int)p["rank"]==1).All(p=>mixed.HasPurchasedShelfAbility(SwordRules.PackId(p))),"mixed still gets sect starter gifts");
  Console.WriteLine("PASS progression: twin gifts, idempotence, foundation-only two choices, gates and native/mixed delegation");
 }
 static int Effect(ModBattlePlan p,ModBattleEffectKind k)=>p.Effects.Where(e=>e.Kind==k).Sum(e=>e.Amount);
 static void BattleTests()
 {
  var a=Run(SwordRules.Play,Id(Card(1)));var b=Run(SwordRules.Play,Id(Card(2)),a.State);
  var standalone=Run(SwordRules.Play,Id(Card(2)));
  Check(Damage(b)-Damage(standalone)==17,"scaled cloud conditional bonus");
  var block=Run(SwordRules.Play,Id(Card(10)),a.State);var after=Run(SwordRules.Play,Id(Card(2)),block.State);
  Check(S(after,"cloud")==1&&Damage(after)==Damage(standalone)+6,"non-cloud breaks but intent works");
  var charged=Run(SwordRules.Play,Id(Card(12)));Check(S(charged,"intent")==2,"intent accumulation");
  var multi=Run(SwordRules.Play,Id(Card(32)),charged.State);
  Check(multi.Operations.Count==3&&S(multi,"intent")==0&&Damage(multi)==60,"intent per segment consumed once");
  foreach(var op in multi.Operations)Check(AuthoredSkillRules.TryCreateEffects(new BattleEffectOperation(op.HandlerKey,CombatTargetKind.EnemySingle,default,op.Values),out var parsed,out var _)&&parsed.Count==1,"retained native effect");
  var tooth=Run(SwordRules.Play,Id(Card(21)),charged.State);
  Check(Damage(tooth)==46&&S(tooth,"intent")==0,"flying fang consumes intent for its attack");
  var returned=Run(SwordRules.Hits,state:tooth.State,hits:new[]{new ModBattleHit(ActorId,EnemyId,5,0,DamageType.Physical,true,false)});
  Check(S(returned,"intent")==2,"flying fang real-health hit returns exactly spent intent");
  Check(S(Run(SwordRules.Hits,state:tooth.State,hits:new[]{new ModBattleHit(ActorId,EnemyId,0,5,DamageType.Physical,true,false)}),"intent")==0,"flying fang shield-only no return");
  Check(!SwordRules.Cards.Values.Any(c=>(string)c["sourceId"]=="1000013"),"gale sword removed");
  foreach(int late in new[]{4,5})
  {
   var packs=SwordRules.Packs.Where(p=>(int)p["rank"]==late).ToList();
   Check(packs.Count==3&&packs.Any(p=>((string)p["name"]).EndsWith("·灵剑"))&&packs.Any(p=>((string)p["name"]).EndsWith("·剑意剑阵")),"three late-game themes");
   var cloud=packs.Single(p=>((string)p["name"]).EndsWith("·云剑"));
   Check(((JArray)cloud["cards"]).Values<string>().All(k=>SwordRules.HasTag(SwordRules.Cards[SwordRules.CardPrefix+k],"cloud")),"four actual cloud cards");
  }
  Check(Effect(Run(SwordRules.Play,Id(Card(107)),State(("intent",3)),actor:Unit(energy:8)),ModBattleEffectKind.Shield)==18,"flowing light includes its newly gained intent");
  var max=Run(SwordRules.Play,Id(Card(25)),round:3);var again=Run(SwordRules.Play,Id(Card(25)),max.State,3);
  Check(S(max,"drawPending")==1&&S(again,"drawPending")==2,"each react queues another draw");
  Check(again.Effects.All(e=>e.Kind!=ModBattleEffectKind.ActionPoints),"no AP refund");
  Check(SwordRules.ResourceCost(Card(25),20,0,false)==0,"shield discount reaches zero");
  Check(SwordRules.ResourceCost(Card(98),0,1,false)==0,"cloud discount reaches zero");
  Check(SwordRules.ResourceCost(Card(59),0,0,true)==0,"spirit discount reaches zero");
  var mate=Run(SwordRules.Play,Id(Card(2)),a.State,actor:Unit(slot:2));Check(Damage(mate)==Damage(standalone),"independent actors");
  var preview1=Run(SwordRules.Play,Id(Card(32)),charged.State);var preview2=Run(SwordRules.Play,Id(Card(32)),charged.State);
  Check(Damage(preview1)==Damage(preview2)&&SwordRules.ReadInt(charged.State,ActorId,"intent")==2,"planning immutable");
  Check(S(Run(SwordRules.Start,state:b.State),"cloud")==0,"battle reset");
  var conversion=Run(SwordRules.Play,Id(Card(33)),State(("intent",3)));
  Check(S(conversion,"intent")==0&&conversion.Effects.Any(e=>e.Kind==ModBattleEffectKind.Energy),"intent conversion");
  var last=Run(SwordRules.Play,Id(Card(46)),actor:Unit(shield:30));
  var replay=Run(SwordRules.Play,Id(Card(64)),last.State,actor:Unit(shield:30));
  Check(replay.Operations.Count==1&&Effect(replay,ModBattleEffectKind.Shield)==40,"scaled formation replay");
  var strike=Run(SwordRules.Play,Id(Card(9)));var onhit=Run(SwordRules.Hits,state:strike.State,hits:new[]{new ModBattleHit(ActorId,EnemyId,5,0,DamageType.Physical,true,false)});
  var payoff=onhit.Effects.Single(e=>e.Kind==ModBattleEffectKind.Damage);
  Check(payoff.Amount==30&&payoff.Source==ActorId&&payoff.Target==EnemyId&&payoff.Option==(int)DamageType.Physical,"scaled physical health-hit payoff");
  Check(onhit.Operations.Count==0,"payoff is not a second attack roll");
  Check(Effect(Run(SwordRules.Hits,state:onhit.State,hits:new[]{new ModBattleHit(ActorId,EnemyId,4,0,DamageType.Physical,true,false)}),ModBattleEffectKind.Damage)==0,"no recursive payoff");
  Check(Effect(Run(SwordRules.Hits,state:strike.State,hits:new[]{new ModBattleHit(ActorId,EnemyId,0,5,DamageType.Physical,true,false)}),ModBattleEffectKind.Damage)==0,"shield-only no payoff");
  var multipleHits=Run(SwordRules.Hits,state:strike.State,hits:new[]{new ModBattleHit(ActorId,EnemyId,5,0,DamageType.Physical,true,false),new ModBattleHit(ActorId,EnemyId,6,0,DamageType.Physical,true,false)});
  Check(multipleHits.Effects.Count(e=>e.Kind==ModBattleEffectKind.Damage)==1,"multiple health hits pay off once per card");
  Check(Effect(Run(SwordRules.Hits,state:strike.State,hits:new[]{new ModBattleHit(ActorId,ActorId,5,0,DamageType.Physical,true,false)}),ModBattleEffectKind.Damage)==0,"friendly health damage is not a payoff");
  foreach(var c in SwordRules.Cards.Values)
  {
   var plan=Run(SwordRules.Play,Id(c));Check(plan.State!=null,"all card plans");
   foreach(var op in plan.Operations)Check(AuthoredSkillRules.TryCreateEffects(new BattleEffectOperation(op.HandlerKey,CombatTargetKind.EnemySingle,default,op.Values),out var parsed,out var error)&&parsed.Count==1,"retained effect "+c["name"]+": "+error);
  }
  foreach(int stat in new[]{0,10,50,200})
  {
   var attrs=new StatBlock(stat,stat,stat,stat);
   Check(Damage(Run(SwordRules.Play,Id(Card(1)),actor:Unit(attributes:attrs)))==18+stat*8000/10000,"energy damage formula "+stat);
   Check(Damage(Run(SwordRules.Play,Id(Card(32)),actor:Unit(attributes:attrs)))==3*(9+stat*5000/10000),"reaction multihit formula "+stat);
   Check(Damage(Run(SwordRules.Play,Id(Card(22)),actor:Unit(attributes:attrs)))==15+stat,"muscle formula "+stat);
   Check(Effect(Run(SwordRules.Play,Id(Card(5)),actor:Unit(energy:0,attributes:attrs)),ModBattleEffectKind.Shield)==15+stat*6000/10000,"shield scaling "+stat);
   var heal=Run(SwordRules.Play,Id(Card(16)),State(("cloud",1)),actor:Unit(attributes:attrs));
   Check(Effect(heal,ModBattleEffectKind.Heal)==9+stat*4000/10000,"constitution heal "+stat);
   var qi=Run(SwordRules.Play,Id(Card(5)),actor:Unit(energy:0,attributes:attrs));
   Check(qi.Effects.Single(e=>e.Kind==ModBattleEffectKind.Energy).Energy.Amount==4+stat*1500/10000,"energy recovery scaling "+stat);
  }
  Check(Effect(Run(SwordRules.Play,Id(Card(14))),ModBattleEffectKind.Damage)==0&&Damage(Run(SwordRules.Play,Id(Card(14))))==0,"unlinked no-edge has no attribute-only damage");
  Console.WriteLine("PASS battle: 48 planners/native effects; scaled damage/shield/heal/qi; cloud, intent, queued draws, cost discounts and hit conditions");
 }
 static void PatchTargets()
 {
  foreach(var t in typeof(Entry).Assembly.GetTypes())
   foreach(HarmonyLib.HarmonyPatch patch in t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch),false))
    Check(HarmonyLib.AccessTools.Method(patch.info.declaringType,patch.info.methodName,patch.info.argumentTypes)!=null,"patch target: "+t.Name);
  Console.WriteLine("PASS all Harmony target methods resolve; detour execution requires game Mono (bundled MonoMod uses a removed CoreCLR ILGenerator API).");
 }
 static int Main(string[] args)
 {
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{var p=Managed+"/"+new AssemblyName(e.Name).Name+".dll";return File.Exists(p)?Assembly.LoadFrom(p):null;};
  try{CatalogTests();if(args.Contains("--balance-plans")){BalancePlannerAudit();return 0;}PatchTargets();ProgressionTests();SwordBuildTests();BuildAdjustmentTests();FiveSelectionTests();BattleTests();CardCostTests();DescriptionTests();SchoolTests();IdentityTests();FiveExpansionTests();StarExpansionTests();ExpansionIntegrationTests();OriginalPrimitivesTests();Console.WriteLine("PASS total checks "+checks+"; .NET 8 isolated tests, no Unity process/save changes.");return 0;}
  catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
