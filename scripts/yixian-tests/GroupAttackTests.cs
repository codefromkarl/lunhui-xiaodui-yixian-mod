using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static ModStateSnapshot GroupState(string school, params (string,int)[] values)
        =>new(values.Select(v=>new ModStateEntry(SectRegistry.Package,SectRegistry.StateKey(school,ActorId,v.Item1),ModValueKind.Integer,v.Item2)).ToArray());
    static JObject GroupCard(string name)=>SectRegistry.Cards.Values.Single(c=>(string)c["name"]==name);
    static ModBattlePlan GroupPlay(JObject card, ModCombatantSnapshot[] enemies, ModStateSnapshot state=null, bool preview=false, ModCombatantSnapshot actor=null)
    {
        actor??=Unit(school:SectRegistry.Owner(card),rank:5,energy:0,attributes:new StatBlock(0,0,0,0));
        var action=new StableId(SectRegistry.CardPrefix+(string)card["key"]);
        var request=new ModActionRequest(action.Value,action,new[]{EnemyId},EnemyId,FormationRow.Front,Array.Empty<BattleEffectValue>(),preview);
        var context=new ModBattleContext(ModBattleHook.ScriptedAction,1,1,1,ActorId,action,new[]{actor}.Concat(enemies).ToArray(),state??ModStateSnapshot.Empty,
            new DeterministicRngState(17,3,0),ActorId,default,null,null,request,"test.group");
        var plan=(CodeBattlePlan)PrivateCtor(typeof(CodeBattlePlan),SectRegistry.Package,context);
        if(SectRegistry.Owner(card)==SevenStarRules.Lineage)SevenStarRules.Play(context,plan);else FiveElementRules.Play(context,plan);
        return (ModBattlePlan)Method(typeof(CodeBattlePlan),"Build").Invoke(plan,null);
    }
    static int GroupDamage(ModBattlePlan plan, StableId target)=>plan.Operations.Where(o=>o.Targets.Contains(target)).Sum(o=>o.Values.Single(v=>v.Key=="e0.flat").IntegerValue);
    static void GroupAttackTests()
    {
        var specs=new[]{("落雷术",1,0,54),("星弈·打",0,0,63),("轰雷掣电",2,0,72),("五雷轰顶",5,0,150),
            ("水灵·涛",0,2,39),("土灵·流沙",0,5,42),("火灵·烈燎原",0,4,81),("水灵·雨纷纷",0,2,30)};
        foreach(var (name,omen,element,expected) in specs)
        {
            var card=GroupCard(name);string school=SectRegistry.Owner(card);
            var state=GroupState(school,("omen",omen),("star",2),("plays",2),("active",element==0?0:1<<(element-1)),("attack",3),("waterForce",5),("lostShield",36));
            string saved=JsonConvert.SerializeObject(state.Entries);
            ModBattlePlan solo=null;
            foreach(int n in new[]{1,3,5})
            {
                var enemies=Enumerable.Range(0,n).Select(i=>AreaEnemy(i==0?EnemyId.Value:"group."+i,FormationRow.Front)).ToArray();
                var plan=GroupPlay(card,enemies,state);solo??=plan;
                Check(enemies.All(e=>GroupDamage(plan,e.RuntimeId)==expected),"each target takes full original formula: "+name+"/"+n+" actual="+GroupDamage(plan,EnemyId)+" expected="+expected);
                Check(plan.Operations.Sum(o=>o.Values.Single(v=>v.Key=="e0.flat").IntegerValue)==expected*n,"total grows linearly with enemy count, not divided: "+name+"/"+n);
                Check(SchoolState(plan,school,"omen")==SchoolState(solo,school,"omen") && plan.Random.Equals(solo.Random),"omen and random draws depend on waves not enemies: "+name+"/"+n);
                Check(plan.Effects.Count(e=>e.Kind==ModBattleEffectKind.Energy)==solo.Effects.Count(e=>e.Kind==ModBattleEffectKind.Energy),"energy rewards not copied: "+name+"/"+n);
                var preview=GroupPlay(card,enemies,state,true);
                Check(plan.Operations.Select(o=>(o.Targets[0],o.Values.Single(v=>v.Key=="e0.flat").IntegerValue)).SequenceEqual(
                    preview.Operations.Select(o=>(o.Targets[0],o.Values.Single(v=>v.Key=="e0.flat").IntegerValue))),"preview equals execution: "+name+"/"+n);
                Check(SectCardRules.PrintedCost(card)==new[]{4,4,0,4,8,0,8,4}[Array.FindIndex(specs,s=>s.Item1==name)],"printed original cost unchanged: "+name);
                foreach(var op in plan.Operations)
                    Check(AuthoredSkillRules.TryCreateEffects(new BattleEffectOperation(op.HandlerKey,CombatTargetKind.EnemySingle,default,op.Values),out var effects,out var error)&&effects.Count==1,"native independent attack: "+name+" "+error);
            }
            foreach(int stat in new[]{20,100})
            {
                var actor=Unit(school:school,rank:5,energy:0,attributes:new StatBlock(stat/2,stat,stat/3,stat*2));
                var single=GroupPlay(card,new[]{Enemy()},state,actor:actor);
                var many=GroupPlay(card,new[]{Enemy(),AreaEnemy("growth.extra",FormationRow.Front)},state,actor:actor);
                Check(GroupDamage(many,EnemyId)==GroupDamage(single,EnemyId) && GroupDamage(many,new StableId("growth.extra"))==GroupDamage(single,EnemyId),"full asymmetric attribute/attack/star/lost-shield growth is copied once per enemy: "+name+"/"+stat);
            }
            Check(JsonConvert.SerializeObject(state.Entries)==saved,"group planning preserves input state: "+name);
            var offRow=AreaEnemy("group.back",FormationRow.Back);
            var cross=GroupPlay(card,new[]{Enemy(),offRow},state);
            Check((GroupDamage(cross,offRow.RuntimeId)>0)==((string)card["groupAttack"]["shape"]=="all"),"row/full scope differs: "+name);
            var invalid=new[]{Enemy(),AreaEnemy("group.dead",FormationRow.Front,true),AreaEnemy("group.aux",FormationRow.Front,kind:CombatantKind.AuxiliaryTarget),Unit(sect:false,slot:3)};
            var filtered=GroupPlay(card,invalid,state);
            Check(filtered.Operations.All(o=>o.Targets.Single()==EnemyId),"no dead, friendly or auxiliary target: "+name);
        }
        foreach(string name in new[]{"星弈·打","水灵·涛","土灵·流沙","火灵·烈燎原"})
        {
            var card=GroupCard(name);var plan=GroupPlay(card,new[]{Enemy(),AreaEnemy("group.extra",FormationRow.Front)});
            Check(plan.Operations.All(o=>o.Targets.Single()==EnemyId),"unmet condition remains original single-target card: "+name);
        }
        var rain=GroupCard("水灵·雨纷纷");var other=AreaEnemy("group.extra",FormationRow.Front);
        var both=GroupPlay(rain,new[]{Enemy(),other},GroupState(FiveElementRules.Lineage,("active",6),("waterForce",5),("attack",3)));
        Check(GroupDamage(both,EnemyId)==GroupDamage(both,other.RuntimeId)+12,"rain water branch full group, wood branch adds only original focused attack");
        var wood=GroupPlay(rain,new[]{Enemy(),other},GroupState(FiveElementRules.Lineage,("active",4)));
        Check(GroupDamage(wood,other.RuntimeId)==0 && GroupDamage(wood,EnemyId)>0,"wood-only rain never manufactures a water group attack");
        var linked=GroupPlay(GroupCard("水灵·涛"),new[]{Enemy(),other},GroupState(FiveElementRules.Lineage,("last",1)));
        Check(GroupDamage(linked,other.RuntimeId)>0,"ordinary generating link enables water group without permanent activation");
        var activated=GroupPlay(GroupCard("水灵·涛"),new[]{Enemy(),other},GroupState(FiveElementRules.Lineage,("nextActivate",1)));
        Check(GroupDamage(activated,other.RuntimeId)>0,"first-card activation enables group in same play");
        var astral=GroupPlay(GroupCard("星弈·打"),new[]{Enemy(),other},GroupState(SevenStarRules.Lineage,("astralLinks",1)));
        Check(GroupDamage(astral,other.RuntimeId)>0,"astral link marks current slot before deciding group condition");
        foreach(int omen in new[]{0,2,5})
        {
            var state=GroupState(SevenStarRules.Lineage,("omen",omen));var card=GroupCard("五雷轰顶");
            var solo=GroupPlay(card,new[]{Enemy()},state);var group=GroupPlay(card,new[]{Enemy(),other},state);
            Check(GroupDamage(group,EnemyId)==GroupDamage(solo,EnemyId)&&GroupDamage(group,other.RuntimeId)==GroupDamage(solo,EnemyId),"five thunder shares actual successful waves without changing primary: "+omen);
            Check(group.Random.Equals(solo.Random)&&SchoolState(group,SevenStarRules.Lineage,"omen")==0,"five thunder has five checks, no target RNG or extra omen spending: "+omen);
        }
        var thunder=GroupCard("轰雷掣电");
        var repeated=GroupPlay(thunder,new[]{Enemy(),other},GroupState(SevenStarRules.Lineage,("omen",4),("repeat",1)));
        Check(GroupDamage(repeated,EnemyId)==120 && GroupDamage(repeated,other.RuntimeId)==120 && SchoolState(repeated,SevenStarRules.Lineage,"plays")==1,"repeat copies full group waves but counts one card");
        Check(repeated.Effects.Where(e=>e.Kind==ModBattleEffectKind.Energy).Sum(e=>e.Energy.Amount)==16,"repeated four omen waves restore sixteen, not thirty-two");
        var water=GroupCard("水灵·涛");
        var charged=GroupPlay(water,new[]{Enemy(),other},GroupState(FiveElementRules.Lineage,("active",2),("sharp",4),("nextWaterCharges",1),("nextWaterPerHealth",5),("springUntil",3),("springAmount",6),("pierceNext",1)));
        Check(charged.Operations.Where(o=>o.Targets.Contains(other.RuntimeId)).All(o=>o.Values.Single(v=>v.Key=="e0.damage").IntegerValue==10),"group secondaries never copy next-attack piercing");
        var primaryHit=new ModBattleHit(ActorId,EnemyId,30,0,DamageType.Physical,true,false);
        var secondaryHit=new ModBattleHit(ActorId,other.RuntimeId,90,0,DamageType.Physical,true,false);
        var rewards=Run(FiveElementRules.Hits,state:charged.State,actor:Unit(school:FiveElementRules.Lineage,rank:5),hits:new[]{primaryHit,secondaryHit},extra:new[]{other});
        var alone=Run(FiveElementRules.Hits,state:charged.State,actor:Unit(school:FiveElementRules.Lineage,rank:5),hits:new[]{primaryHit},extra:new[]{other});
        Check(JsonConvert.SerializeObject(rewards.State.Entries)==JsonConvert.SerializeObject(alone.State.Entries),"secondary hits cannot multiply sharp, spring or next-water rewards");
        var sideOnly=Run(FiveElementRules.Hits,state:charged.State,actor:Unit(school:FiveElementRules.Lineage,rank:5),hits:new[]{secondaryHit},extra:new[]{other});
        Check(SchoolState(sideOnly,FiveElementRules.Lineage,"sharp")==4 && SchoolState(sideOnly,FiveElementRules.Lineage,"hitHandled")==0,"secondary cannot spend sharp when primary misses");
        var fire=GroupCard("火灵·烈燎原");var firePlan=GroupPlay(fire,new[]{Enemy(),other},GroupState(FiveElementRules.Lineage,("active",8)));
        var resolved=Run(FiveElementRules.Resolved,new StableId(SectRegistry.CardPrefix+(string)fire["key"]),firePlan.State,actor:Unit(school:FiveElementRules.Lineage,rank:5),extra:new[]{other});
        Check(StarHealth(resolved).OfType<JObject>().Where(j=>(string)j["kind"]=="maximum").All(j=>(string)j["target"]==EnemyId.Value),"fire cap only targets original primary");
        Check(resolved.State.Entries.Any(e=>e.Key==SectRegistry.StateKey(FiveElementRules.Lineage,ActorId,"groupPrimary")&&string.IsNullOrEmpty(e.Text)),"group pending primary cleared after resolution");
        var points=new SwordTargeting.Layout(new[]{AreaPoint(ActorId.Value,-2000,0,side:CombatSide.Player),AreaPoint(EnemyId.Value,0,0),AreaPoint("good",0,1000),AreaPoint("immune",0,1,immune:true)},true);
        foreach(string name in new[]{"落雷术","轰雷掣电"})
            Check(SchoolGroupAttack.Targets(points,ActorId,EnemyId,GroupCard(name),true).Select(id=>id.Value).SequenceEqual(new[]{EnemyId.Value,"good"}),"tactical group filters immune and shares geometry: "+name);
        RunCombatantSnapshot Snapshot(string school,params (string key,int count)[] counters)
        {
            var statuses=counters.Select((v,i)=>new RunCombatStatusSnapshot(new StableId("group.status."+i),new StableId("yixian.sect.status."+SectRegistry.Prefix(school)+"."+v.key),ActorId,Enumerable.Repeat(new CombatStatusStackState(0),v.count).ToArray())).ToArray();
            return new RunCombatantSnapshot(ActorId,new StableId("test.hero"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,new PlayerSlotId(1),100,100,0,40,40,3,0,0,0,0,1,statuses,Array.Empty<RunAbilityCooldownSnapshot>());
        }
        var starCard=GroupCard("星弈·打");
        Check(SchoolGroupAttack.PreviewEnabled(starCard,Snapshot(SevenStarRules.Lineage,("plays",2),("position3",1))),"UI predicts next slot rather than previous slot");
        Check(!SchoolGroupAttack.PreviewEnabled(starCard,Snapshot(SevenStarRules.Lineage,("plays",1),("position3",1))),"UI does not invent star group on unmarked next slot");
        Check(SchoolGroupAttack.PreviewEnabled(starCard,Snapshot(SevenStarRules.Lineage,("astralLinks",1))),"UI predicts same-card astral link position");
        Check(SchoolGroupAttack.PreviewEnabled(water,Snapshot(FiveElementRules.Lineage,("last",1))) && SchoolGroupAttack.PreviewEnabled(water,Snapshot(FiveElementRules.Lineage,("water",1))) && SchoolGroupAttack.PreviewEnabled(water,Snapshot(FiveElementRules.Lineage,("nextActivate",1))),"UI recognizes generating link, active water and same-card activation");
        Check(!SchoolGroupAttack.PreviewEnabled(water,Snapshot(FiveElementRules.Lineage)),"UI leaves unready water single");
        Console.WriteLine("PASS full-damage group attacks: eight cards, 1/3/5 enemy linear totals, conditions, shared wave RNG/omen, repeat and primary-only rewards");
    }
}
