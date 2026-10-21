using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static void StarSelectedHealingTests()
    {
        const string school=SevenStarRules.Lineage;
        var caster=StarHealingUnit(health:300);var chosen=StarHealingUnit(2,health:300,sect:false);
        var hurt=StarHealingUnit(3,health:30,sect:false);var down=StarHealingUnit(4,health:0,sect:false);
        var auxiliary=StarHealingUnit(5,health:200,sect:false,kind:CombatantKind.AuxiliaryTarget);
        var party=new[]{chosen,hurt,down,auxiliary};
        var cards=SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==school).ToArray();
        StableId Source(int id)=>new(SectRegistry.CardPrefix+(string)cards.Single(c=>(string)c["sourceId"]==id.ToString())["key"]);
        ModBattlePlan Play(int id,ModStateSnapshot state=null,StableId target=default)
            =>Run(SevenStarRules.Play,Source(id),state,actor:caster,extra:party,target:target.IsEmpty?chosen.RuntimeId:target);
        var cure=Play(4000017,StarExpansionState(("omen",1)));
        Check((string)StarHeals(cure).Single()["target"]==chosen.RuntimeId.Value && (int)StarHeals(cure).Single()["amount"]==59,"Qi Therapy heals chosen full ally, not critically injured alternative");
        Check(StarHealth(cure).OfType<JObject>().Where(r=>(string)r["kind"]=="maximum").All(r=>(string)r["target"]==ActorId.Value&&(int)r["amount"]==24),"Qi Therapy still grants maximum only to caster");
        Check(SchoolState(cure,school,"healingTotal")==59 && cure.Random.CallCount==0,"chosen full-target overheal counts; forced amount and manual selection use no RNG");
        Check(caster.CurrentMaximumHealth==300 && chosen.CurrentMaximumHealth==300 && chosen.CurrentHealth==300,"targeted preview never mutates caster or ally snapshot");
        var first=Play(4000060);
        Check(StarHeals(first).Length==0 && StarHealth(first).OfType<JObject>().Single().Value<int>("amount")==36,"Mountains keeps caster cap and no first-use base healing");
        Check(SectRegistry.Read(first.State,"cards."+ActorId.Value+".drawPending")==1,"manual ally targeting preserves Mountains draw");
        var post=Play(4000060,StarExpansionState(("used4000060",1)));
        Check((string)StarHeals(post).Single()["target"]==chosen.RuntimeId.Value && (int)StarHeals(post).Single()["amount"]==77,"Mountains post healing follows chosen ally at unchanged magnitude");
        Check(StarHealth(post).OfType<JObject>().Where(r=>(string)r["kind"]=="maximum").All(r=>(string)r["target"]==ActorId.Value),"Mountains does not transfer its maximum bonus");
        var repeated=Play(4000060,StarExpansionState(("used4000060",1),("repeat",1)));
        Check(StarHeals(repeated).Length==2 && StarHeals(repeated).All(r=>(string)r["target"]==chosen.RuntimeId.Value) && SchoolState(repeated,school,"healingTotal")==154,"repeated post heals stay on chosen target and count both overheals");
        Check(SchoolState(repeated,school,"plays")==1,"targeted repeat advances sequence once");
        var self=Play(4000017,StarExpansionState(("omen",1)),ActorId);
        Check((string)StarHeals(self).Single()["target"]==ActorId.Value,"manual healing allows self even when another ally is hurt");
        var randomAmount=Play(4000017);
        Check(randomAmount.Random.CallCount>0 && (string)StarHeals(randomAmount).Single()["target"]==chosen.RuntimeId.Value,"unforced Qi Therapy amount stays random while recipient is fixed");
        var rejuvenated=Run(SevenStarRules.Play,Source(4000017),StarExpansionState(("omen",1)),actor:Unit(school:school,rank:5,abilities:new[]{SevenStarFateRules.Cycle}),extra:party,target:chosen.RuntimeId);
        Check(StarHealth(rejuvenated).OfType<JObject>().Where(r=>(string)r["kind"]=="maximum").Select(r=>(int)r["amount"]).SequenceEqual(new[]{24,18}) && StarHealth(rejuvenated).OfType<JObject>().Where(r=>(string)r["kind"]=="maximum").All(r=>(string)r["target"]==ActorId.Value),"Qi Therapy and Rejuvenation maximum bonuses both stay on caster");
        Check((string)StarHeals(rejuvenated).Single()["target"]==chosen.RuntimeId.Value,"Rejuvenation cannot redirect manually chosen healing");
        var definitions=JObject.Parse(File.ReadAllText(Mod+"/content/abilities.json"))["operations"].OfType<JObject>().ToDictionary(o=>(string)o["id"]);
        foreach(int id in new[]{4000017,4000060})
        {
            var spec=definitions[Source(id).Value]["fields"]["ranks"][0]["operation"];
            Check((int)spec["targetKind"]==20,"native action/UI declares AllySingle: "+id);
            Check((int)spec["values"].Single(v=>(string)v["key"]=="energyCost")["intValue"]==(id==4000017?4:8),"target selection preserves printed energy fee");
        }
        Check(cards.Where(c=>(bool?)c["targetAlly"]==true).Select(c=>(string)c["sourceId"]).OrderBy(x=>x).SequenceEqual(new[]{"4000017","4000060"}),"only two named cards gain manual targeting");
        ModBattlePlan Raw(StableId[] targets,ModStateSnapshot state)
        {
            var action=Source(4000017);
            var request=new ModActionRequest(action.Value,action,targets,targets.FirstOrDefault(),FormationRow.None,Array.Empty<BattleEffectValue>(),true);
            var context=new ModBattleContext(ModBattleHook.ScriptedAction,1,1,1,ActorId,action,new[]{caster,Enemy()}.Concat(party).ToArray(),state,new DeterministicRng(123).CaptureState(),ActorId,default,null,null,request,"test.targeted");
            var plan=(CodeBattlePlan)PrivateCtor(typeof(CodeBattlePlan),SectRegistry.Package,context);SevenStarRules.Play(context,plan);
            return (ModBattlePlan)Method(typeof(CodeBattlePlan),"Build").Invoke(plan,null);
        }
        var untouched=StarExpansionState(("omen",3),("healingTotal",48));string before=JsonConvert.SerializeObject(untouched.Entries);
        foreach(var targets in new[]{Array.Empty<StableId>(),new[]{EnemyId},new[]{down.RuntimeId},new[]{auxiliary.RuntimeId},new[]{new StableId("unknown")},new[]{chosen.RuntimeId,hurt.RuntimeId}})
        {
            bool rejected=false;try{Raw(targets,untouched);}catch(InvalidOperationException){rejected=true;}
            Check(rejected && JsonConvert.SerializeObject(untouched.Entries)==before,"script rejects missing/enemy/downed/auxiliary/unknown/multiple targets without mutating input");
        }
        var rawActor=new CombatantState(ActorId,new StableId("test.actor"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(1));
        var rawAlly=new CombatantState(chosen.RuntimeId,new StableId("test.ally"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(2));
        var rawEnemy=new CombatantState(EnemyId,new StableId("test.enemy"),CombatantKind.Enemy,CombatSide.Enemy,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty);
        var rawDead=new CombatantState(down.RuntimeId,new StableId("test.dead"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(4));rawDead.SetVitals(0,0);
        var battle=new BattleState(new StableId("test.ally.validation"),1,BuffCatalog.Empty,BattlePhase.Player,1);
        foreach(var u in new[]{rawActor,rawAlly,rawEnemy,rawDead})Check(battle.AddCombatant(u).IsSuccess,"native ally validation fixture");
        var actionSpec=new CombatActionSpec(Source(4000017),CombatActionKind.UseAbility,0,4,0,CombatTargetKind.AllySingle,5);
        foreach(var target in new[]{ActorId,chosen.RuntimeId,EnemyId,down.RuntimeId,default(StableId),new StableId("unknown")})
        {
            var intent=new CombatActionIntent(new PlayerSlotId(1),1,ActorId,CombatActionKind.UseAbility,Source(4000017),target,FormationRow.None,-1);
            var args=new object[]{battle,intent,actionSpec,rawActor,false,null};
            var error=(SessionErrorCode)Method(typeof(CombatActionValidator),"ValidateTarget").Invoke(null,args);
            Check((error==SessionErrorCode.None)==(target==ActorId||target==chosen.RuntimeId),"native AllySingle accepts self/full ally, rejects invalid target before payment");
        }
        Console.WriteLine("PASS targeted star healing: two AllySingle cards, caster-only maximum, pinned repeats/overheal, source fees/post/draw, native target validation and immutable previews");
    }
}
