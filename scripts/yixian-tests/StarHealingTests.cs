using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static ModCombatantSnapshot StarHealingUnit(byte slot=1, int health=200, bool sect=true,
        CombatSide side=CombatSide.Player, CombatantKind kind=CombatantKind.Player)
    {
        var id=slot==1?ActorId:new StableId("test.actor"+slot);
        var owner=side==CombatSide.Player?new PlayerSlotId(slot):default;
        var unit=new CombatantState(id,new StableId("test.healing"),kind,side,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,owner);
        if(health!=300)Check(unit.SetVitals(health,0).IsSuccess,"arrange healing health");
        var load=new ModUnitLoadout(null,null,null,sect?new[]{new ModHeldCount(new StableId(SevenStarRules.Lineage),5)}:null,default,0,Array.Empty<StableId>());
        return (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot),unit,load,new StatBlock(10,10,10,20),0,0,0);
    }
    static JObject[] StarHeals(ModBattlePlan plan)=>StarHealth(plan).OfType<JObject>().Where(r=>(string)r["kind"]=="heal").ToArray();
    static ModBattlePlan StarHealingPlan(ModCombatantSnapshot[] units, int[] amounts, ulong seed=17, int maximum=0)
    {
        var action=new StableId("test.star.heal");var actor=units.Single(u=>u.RuntimeId==ActorId);
        var context=new ModBattleContext(ModBattleHook.ScriptedAction,1,1,1,ActorId,action,units,ModStateSnapshot.Empty,new DeterministicRng(seed).CaptureState(),ActorId,default,null,null,null,"test.healing");
        var plan=(CodeBattlePlan)PrivateCtor(typeof(CodeBattlePlan),SectRegistry.Package,context);
        var turn=new BattleTurn(context,plan,SevenStarRules.Lineage,actor);
        if(maximum!=0)turn.MaxHealth(maximum);
        foreach(int amount in amounts)SevenStarHealing.Heal(turn,amount);
        return (ModBattlePlan)Method(typeof(CodeBattlePlan),"Build").Invoke(plan,null);
    }
    static void StarHealingTests()
    {
        const string school=SevenStarRules.Lineage;
        var caster=StarHealingUnit(health:300);var injured=StarHealingUnit(2,health:200,sect:false);
        var full=StarHealingUnit(3,health:300,sect:false);var down=StarHealingUnit(4,health:0,sect:false);
        var enemy=StarHealingUnit(5,health:100,sect:false,side:CombatSide.Enemy,kind:CombatantKind.Enemy);
        var auxiliary=StarHealingUnit(6,health:100,sect:false,kind:CombatantKind.AuxiliaryTarget);
        var cards=SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==school).ToArray();
        StableId IdFor(int id)=>new(SectRegistry.CardPrefix+(string)cards.Single(c=>(string)c["sourceId"]==id.ToString())["key"]);
        ModBattlePlan Play(int id, ModStateSnapshot state=null, ModCombatantSnapshot[] extra=null)
            =>Run(SevenStarRules.Play,IdFor(id),state,actor:caster,extra:extra??new[]{injured,full,down,enemy,auxiliary});
        var post=StarExpansionState(("used4000013",1),("omen",4));var healed=Play(4000013,post);
        var request=StarHeals(healed).Single();
        Check((string)request["target"]==injured.RuntimeId.Value && (string)request["source"]==ActorId.Value && (int)request["amount"]==11,"starter heals sole injured allied character, not full caster/enemy/downed/auxiliary; source remains caster");
        Check(healed.Random.CallCount==0 && SchoolState(healed,school,"omen")==4,"single healing target consumes neither RNG nor omen");
        Check(caster.CurrentHealth==300 && injured.CurrentHealth==200 && full.CurrentHealth==300,"healing preview never mutates snapshots");
        var noTarget=Play(4000013,post,new[]{full,down,enemy,auxiliary});
        Check(StarHeals(noTarget).Length==1 && SchoolState(noTarget,school,"healingTotal")==11 && Damage(noTarget)==38,"all full: still heal a living ally, record overheal, retain base attack");
        var initial=Play(4000013);
        Check(StarHeals(initial).Length==0 && Damage(initial)==38,"random-target starter still requires second-use post condition");

        var one=StarHealingPlan(new[]{StarHealingUnit()},new[]{11});
        Check((string)StarHeals(one).Single()["target"]==ActorId.Value && one.Random.CallCount==0,"injured caster remains eligible in solo play");
        Check(StarHeals(StarHealingPlan(new[]{caster},new[]{0,-1})).Length==0,"non-positive effects do not select targets or heal");
        var almost=StarHealingUnit(2,health:295,sect:false);var almostOther=StarHealingUnit(3,health:292,sect:false);
        var repeated=StarHealingPlan(new[]{caster,almost,almostOther},new[]{11,11,11});
        var repeatRequests=StarHeals(repeated);
        Check(repeatRequests.Length==3 && repeatRequests.Take(2).Select(r=>(string)r["target"]).Distinct().Count()==2,"same-plan repeated healing reselects after earlier target becomes full; third heal falls back to full living party");
        Check(repeatRequests.All(r=>(int)r["amount"]==11),"overhealing is capped on target, not converted into a larger/split heal");
        var ordered=StarHealingPlan(new[]{caster,almost,almostOther},new[]{11});
        var reversed=StarHealingPlan(new[]{almostOther,almost,caster},new[]{11});
        Check(JsonConvert.SerializeObject(StarHeals(ordered))==JsonConvert.SerializeObject(StarHeals(reversed)) && ordered.Random==reversed.Random,"target ordering and native seeded selection are deterministic regardless of snapshot enumeration order");
        var selected=new HashSet<string>();
        foreach(ulong seed in Enumerable.Range(1,32).Select(i=>(ulong)i))
        {
            var p=StarHealingPlan(new[]{caster,almost,almostOther},new[]{11},seed);
            selected.Add((string)StarHeals(p).Single()["target"]);
            Check(p.Random.CallCount>0,"multiple candidates use native RNG");
        }
        Check(selected.SetEquals(new[]{almost.RuntimeId.Value,almostOther.RuntimeId.Value}),"different seeds can select either injured ally, never full caster");
        var raised=StarHealingPlan(new[]{caster},new[]{11},maximum:6);
        var raisedRequests=StarHealth(raised).OfType<JObject>().ToArray();
        Check(raisedRequests.Length==2 && (string)raisedRequests[0]["kind"]=="maximum" && (string)raisedRequests[1]["target"]==ActorId.Value,"maximum increase before healing makes formerly full caster eligible");

        foreach(var sample in new[]{(4000018,23),(4000043,17),(4000036,32),(4000062,6),(4000041,11)})
        {
            var state=StarExpansionState(("used4000018",1),("omen",2));
            var p=Play(sample.Item1,state);
            var h=StarHeals(p).Single();
            Check((string)h["target"]==injured.RuntimeId.Value && (int)h["amount"]==sample.Item2,"all ordinary/post/omen-conversion heals retain magnitude and target injured ally: "+sample.Item1);
        }
        foreach(var sample in new[]{(4000093,18),(4000060,77)})
        {
            var p=Play(sample.Item1,StarExpansionState(("plays",2),("used4000060",1)));
            var h=StarHeals(p).Single();
            Check(new[]{ActorId.Value,injured.RuntimeId.Value}.Contains((string)h["target"]) && (int)h["amount"]==sample.Item2,"maximum/heal combo retains caster-only cap and randomly heals an eligible character: "+sample.Item1);
            Check(StarHealth(p).OfType<JObject>().Where(r=>(string)r["kind"]=="maximum").All(r=>(string)r["target"]==ActorId.Value),"random healing never redirects existing caster maximum bonus");
        }
        var connect=Play(4000104);var linked=Play(4000010,connect.State);
        Check(StarHeals(connect).Length==0 && (int)StarHeals(linked).Single()["amount"]==15 && (string)StarHeals(linked).Single()["target"]==injured.RuntimeId.Value,"Astral Connect heals ally only when a later astral card creates a star position");
        var plumPost=Play(4000013,StarExpansionState(("repeat",1),("used4000013",1)),new[]{almost,almostOther});
        Check(StarHeals(plumPost).Length==2 && StarHeals(plumPost).Select(r=>(string)r["target"]).Distinct().Count()==2,"real Plum repeat reselects starter heal after the first ally fills");
        var fateActor=Unit(school:school,rank:3,abilities:new[]{SevenStarFateRules.Pivots});
        var fate=Run(SevenStarRules.Play,IdFor(4000013),actor:fateActor,extra:new[]{injured});
        Check(StarHeals(fate).Length==1 && (int)StarHeals(fate)[0]["amount"]==9 && Effect(fate,ModBattleEffectKind.Shield)==9,"Post Strike fate uses random injured-target healing while its caster shield remains unchanged");
        Console.WriteLine("PASS seven-star random healing: single-target paths and fate, injured priority/full-party fallback, native RNG/no omen, repeated selection, preview isolation and max-before-heal ordering");
    }
}
