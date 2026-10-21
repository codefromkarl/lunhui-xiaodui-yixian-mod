using System.Reflection;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Domain;
using Reincarnation.Runtime.Content;
using Reincarnation.Modding;
using YixianSect;
using DKind=Reincarnation.Domain.CatalogContentKind;
class FateTests
{
 static string Root=Environment.GetEnvironmentVariable("YIXIAN_MOD_ROOT")??"mod/yixian.sect";
 static int checks;
 static void Check(bool x,string m){if(!x)throw new Exception(m);checks++;}
 static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
 static object Private(Type t,params object[] a)=>Activator.CreateInstance(t,BindingFlags.NonPublic|BindingFlags.Instance,null,a,null);
 static readonly StableId Actor=new("fate.player"),Target=new("fate.enemy");
 static ModCombatantSnapshot Unit(int rank=3,string[] fates=null,bool sect=true,StableId id=default)
 {
  if(id.IsEmpty)id=Actor;
  var u=new CombatantState(id,new StableId("test.player"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,1000,200,0,0,0,BuffCatalog.Empty,new PlayerSlotId(1));
  Method(typeof(CombatantState),"AssignEnergy").Invoke(u,new object[]{new EnergyPoolSet(new EnergyPoolState(EnergyPoolKind.Fighting,80,200),default,default)});
  var load=new ModUnitLoadout(null,null,null,sect?new[]{new ModHeldCount(new StableId(SwordRules.Lineage),rank)}:null,default,0,(fates??Array.Empty<string>()).Select(x=>new StableId(x)).ToArray());
  return (ModCombatantSnapshot)Private(typeof(ModCombatantSnapshot),u,load,new StatBlock(0,0,0,0),0,0,0);
 }
 static JObject Card(int n)=>SwordRules.Cards.Values.Single(c=>(string)c["sourceId"]=="100"+n.ToString("D4"));
 static StableId Id(int n)=>new(SwordRules.CardPrefix+(string)Card(n)["key"]);
 static ModBattlePlan Run(Action<ModBattleContext,CodeBattlePlan> handler,ModCombatantSnapshot u,ModStateSnapshot state=null,int round=1,StableId action=default,ModCombatantSnapshot extra=null)
 {
  var enemy=new CombatantState(Target,new StableId("test.enemy"),CombatantKind.Enemy,CombatSide.Enemy,FormationRow.Front,1000,0,0,0,0,BuffCatalog.Empty);
  var units=new List<ModCombatantSnapshot>{u,(ModCombatantSnapshot)Private(typeof(ModCombatantSnapshot),enemy,ModUnitLoadout.None,null,null,null,null)};if(extra!=null)units.Add(extra);
  var req=new ModActionRequest(action.Value,action,new[]{Target},Target,FormationRow.Front,Array.Empty<BattleEffectValue>(),false);
  var ctx=new ModBattleContext(ModBattleHook.ScriptedAction,1,1,round,u.RuntimeId,action,units,state??ModStateSnapshot.Empty,new DeterministicRngState(7,3,0),u.RuntimeId,default,null,null,req,"fates.test");
  var plan=(CodeBattlePlan)Private(typeof(CodeBattlePlan),"yixian.sect",ctx);handler(ctx,plan);return (ModBattlePlan)Method(typeof(CodeBattlePlan),"Build").Invoke(plan,null);
 }
 static int State(ModBattlePlan p,string k,StableId id=default)=>SwordRules.ReadInt(p.State,id.IsEmpty?Actor:id,k);
 static int Damage(ModBattlePlan p)=>p.Operations.Sum(o=>o.Values.FirstOrDefault(v=>v.Key=="e0.flat").IntegerValue);
 static int Shield(ModBattlePlan p)=>p.Effects.Where(e=>e.Kind==ModBattleEffectKind.Shield).Sum(e=>e.Amount);
 static PlayerRunState Player(int rank=3)
 {
  var sel=new BirthSelection(new PlayerSlotId(1),"fates",BirthAppearance.Default,new StableId("origin.test"),new StableId("experience.test"),new StableId("reason.test"));var stats=new StatBlock(10,10,10,10);var inv=new InventoryState();var equip=new EquipmentState();
  var birth=new ValidatedBirthSelection(sel,stats,inv.CaptureSnapshot(),equip.CaptureSnapshot(),10000,MindLightKind.EternalInstant);
  var p=new PlayerRunState(birth,new DerivedStats(stats,300,60,0,0,0,0,15000,10000,0,0),inv,equip,Array.Empty<StableId>(),0,0,10000);
  p.AddLineage(new StableId(SwordRules.Lineage));((List<LineageRankState>)typeof(PlayerRunState).GetField("lineages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p))[0]=new LineageRankState(new StableId(SwordRules.Lineage),rank);return p;
 }
 static ContentOperationDefinition Op(JObject o)=>new((string)o["handlerKey"],(ContentTargetKind)(int)o["targetKind"],"",o["values"].OfType<JObject>().Select(v=>new ContentValueDefinition((string)v["key"],(int?)v["intValue"]??0,(string)v["stringValue"]??"")).ToArray());
 static void Catalog()
 {
  var doc=JObject.Parse(File.ReadAllText(Root+"/content/fates.json"));
  var swordFates=doc["operations"].OfType<JObject>().Where(o=>(string)o["fields"]?["sourceContentId"]==SwordRules.Lineage).ToArray();
  Check(swordFates.Length==4,"four sword fates");
  var shelves=new List<ShelfAbilityDefinition>();var runtimes=new List<RuntimeAbilityDefinition>();
  foreach(JObject o in swordFates)
  {
   var f=(JObject)o["fields"];var ranks=f["ranks"].OfType<JObject>().Select(r=>new ContentAbilityRankDefinition((string)r["rank"],1,Op((JObject)r["operation"]))).ToArray();
   var def=new ContentAbilityDefinition((string)o["id"],1,(string)f["displayName"],SwordRules.Lineage,ContentAbilityMode.Passive,ranks,null,(string)f["flavour"],(int)f["purchasePrice"],ContentAbilityAcquisition.Learned);
   var rt=(RuntimeAbilityDefinition)Private(typeof(RuntimeAbilityDefinition),def);runtimes.Add(rt);
   Check(ContentVocabulary.Check("abilities",f).Count()==0,"vocabulary "+o["id"]);
   var shelf=new ShelfAbilityDefinition(rt.StableId,new StableId(SwordRules.Lineage),DKind.Lineage,FateRules.Required(rt.StableId.Value),(int)f["purchasePrice"]);shelves.Add(shelf);
   Check(shelf.Price==20,"approved fate price "+rt.StableId);
   foreach(var ar in rt.Ranks)
   {
    int rank=RuntimeAbilityDefinition.RequiredRank(ar);
    Check(M6ProgressionContentResolver.TryCreateShelfPlan(ar.Operation,shelf,rank,out var plan,out var error),"passive projection "+error);
    Check(ProgressionPassiveBattleRules.TryCreateProfile(new[]{ProgressionPassivePlanSlot.Shelf(plan,EnergyPoolKind.Fighting)},out var _,out var pe),"valid metadata-only passive "+pe);
   }
   var p=Player(1);var hub=new HubState(HubAccessKind.Full,1,new[]{p.Slot});var svc=new HubService(new EconomyCatalog(Array.Empty<EconomyCatalogEntry>(),new[]{new ProgressionDefinition(new StableId(SwordRules.Lineage),DKind.Lineage,new[]{1,1,1,1,1})},new[]{shelf}));
   for(int r=1;r<=5;r++)
   {
    p=Player(r);int money=p.RewardPoints;var result=svc.PurchaseShelfAbility(hub,p,shelf.AbilityId);
    Check(result.IsSuccess==(r>=shelf.RequiredRank),"purchase gate rank "+r);
    Check(p.RewardPoints==money-(result.IsSuccess?shelf.Price:0),"price/atomic gate");
    if(result.IsSuccess){Check(FateRules.Has(p,shelf.AbilityId.Value),"purchased recognized");Check(!svc.PurchaseShelfAbility(hub,p,shelf.AbilityId).IsSuccess&&p.RewardPoints==money-shelf.Price,"duplicate no charge");}
   }
   p=Player(5);p.SpendRewardPoints(p.RewardPoints);Check(!svc.PurchaseShelfAbility(hub,p,shelf.AbilityId).IsSuccess&&p.PurchasedShelfAbilityIds.Count==0,"insufficient no acquisition");
  }
  // Exercise the actual projection entry point, not hand-built shelves alone.
  var type=typeof(RuntimeLineageDefinition).Assembly.GetType("Reincarnation.Runtime.Content.RuntimeDomainCatalogProjection");var projected=new List<ShelfAbilityDefinition>();
  Method(type,"ProjectShelfAbilities").Invoke(null,new object[]{runtimes,new[]{new ProgressionDefinition(new StableId(SwordRules.Lineage),DKind.Lineage,new[]{1,1,1,1,1})},projected});Check(projected.Count==4,"actual shelf projection");
  foreach(var t in typeof(Entry).Assembly.GetTypes())foreach(HarmonyLib.HarmonyPatch h in t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch),false))Check(HarmonyLib.AccessTools.Method(h.info.declaringType,h.info.methodName,h.info.argumentTypes)!=null,"patch target "+t.Name);
  Console.WriteLine("PASS catalog: content vocabulary, native passive/shelf projection, gates, prices, repeat/insufficient rejection and all patch targets");
 }
 static void Battle()
 {
  var no=Unit();Check(State(Run(FateRules.Round,no),"intent")==0,"unowned no intent");
  for(int rank=2;rank<=5;rank++)
  {
   var u=Unit(rank,new[]{FateRules.Rhyme,FateRules.CloudGuard});var first=Run(FateRules.Round,u);Check(State(first,"intent")==2*rank,"first round intent "+rank);
   var same=Run(FateRules.Round,u,first.State);Check(State(same,"intent")==2*rank,"no double round start");
   var next=Run(FateRules.Round,u,same.State,2);Check(State(next,"intent")==4*rank,"round2 accumulation");
   var multi=Run(SwordRules.Play,u,first.State,action:Id(32));var plain=Run(SwordRules.Play,no,action:Id(32));Check(Damage(multi)-Damage(plain)==2*rank*3*3&&State(multi,"intent")==0,"multi consumes once");
   var cloud=Run(SwordRules.Play,u,action:Id(1));Check(Shield(cloud)==3*rank,"one cloud shield "+rank);
   Check(Shield(Run(SwordRules.Play,u,action:Id(32)))==0,"noncloud no shield");
  }
  var sea=Unit(5,new[]{FateRules.CloudSea,FateRules.CloudGuard});
  var standalone=Run(SwordRules.Play,no,action:Id(2));var unlocked=Run(SwordRules.Play,sea,action:Id(2));Check(Damage(unlocked)-Damage(standalone)==9&&State(unlocked,"cloud")==1,"first cloud linked with real count1");
  var non=Run(SwordRules.Play,sea,unlocked.State,action:Id(12));Check(State(non,"cloud")==1,"other sect card preserves count");
  var twice=Run(SwordRules.Play,sea,non.State,action:Id(2));Check(State(twice,"cloud")==2,"real cloud count not inflated");
  Check(Shield(Run(SwordRules.Play,sea,action:Id(42)))==24,"multihit cloud shield once plus native9");
  var mate=Unit(5,new[]{FateRules.Rhyme},id:new StableId("fate.mate"));var isolation=Run(FateRules.Round,no,extra:mate);Check(State(isolation,"intent")==0&&State(isolation,"intent",mate.RuntimeId)==10,"per-player isolation");
  Check(!FateRules.Has(Unit(5,new[]{FateRules.Rhyme},false),FateRules.Rhyme),"no sect no fate");
  var guarded=Unit(3,new[]{FateRules.FormationGuard});Check(State(Run(FateRules.Resolved,guarded,action:CardTableRules.DrawActionId),"fateGuardRound")==0,"draw not first card");
  var active=Run(FateRules.Resolved,guarded,action:Id(12));Check(State(active,"fateGuardRound")==1,"nonformation first card guard");
  Check(active.Effects.Any(e=>e.Kind==ModBattleEffectKind.Status&&e.Content==FateRules.GuardStatus),"visible guard feedback");
  var duplicate=Run(FateRules.Resolved,guarded,active.State,action:Id(12));Check(duplicate.Effects.All(e=>e.Kind!=ModBattleEffectKind.Status),"first card triggers only once");
  var owner=Player();owner.GrantFreeShelfAbility(new StableId(FateRules.FormationGuard));
  var actor=new CombatantState(Actor,new StableId("test"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,0,0,0,0,BuffCatalog.Empty,new PlayerSlotId(1));
  Check(FateRules.Retain(owner,actor,active.State,1,80,20)==80,"natural retention100");Check(FateRules.Retain(owner,actor,active.State,2,80,20)==20,"no card next round expires");Check(FateRules.Retain(Player(),actor,active.State,1,80,20)==20,"unowned baseline");
  actor.SetVitals(300,80);CombatVitalRules.ApplyDamage(actor,30);Check(actor.Shield==50,"damage consumes protected shield");
  var p=Player(5);p.GrantFreeShelfAbility(new StableId(FateRules.CloudSea));Check(SwordRules.ResourceCost(Card(98),0,FateRules.CostChain(p,0),false)==0,"first cloud discount reaches zero");
  var reset=Run(SwordRules.Start,guarded,active.State);Check(State(reset,"fateGuardRound")==0,"battle reset guard");
  Console.WriteLine("PASS battle: rank growth, first/next round, no duplicate, multihit consumption, cloud guard/sea, first-card retention, expiry, costs and actor isolation");
 }
 static int Main()
 {
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{var managed=Environment.GetEnvironmentVariable("GAME_MANAGED")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local/share/Steam/steamapps/common/轮回小队 Demo/ReincarnationSquad_Data/Managed");var p=Path.Combine(managed,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
  try{SwordRules.Configure(JObject.Parse(File.ReadAllText(Root+"/content/cardtable.json")));Catalog();Battle();Console.WriteLine("PASS "+checks+" checks; offline plans/native catalogs, not Mono detour acceptance");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
