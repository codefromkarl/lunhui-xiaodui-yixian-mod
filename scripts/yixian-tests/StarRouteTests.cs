using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static void StarRouteTests()
    {
        const string school=SevenStarRules.Lineage;
        var cards=SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==school).ToArray();
        StableId Source(int id)=>new(SectRegistry.CardPrefix+(string)cards.Single(c=>(string)c["sourceId"]==id.ToString())["key"]);
        ModBattlePlan Play(int id,ModStateSnapshot state=null,ModCombatantSnapshot actor=null,ModCombatantSnapshot[] extra=null)
            =>Run(SevenStarRules.Play,Source(id),state,actor:actor??StarHealingUnit(health:300),extra:extra);
        int Read(ModBattlePlan p,string key)=>SchoolState(p,school,key);
        var party=Enumerable.Range(2,3).Select(i=>StarHealingUnit((byte)i,health:300,sect:false)).ToArray();
        var full=StarHealingUnit(health:300);
        var group=Play(4000040,StarExpansionState(("omen",4)),full,party);
        Check(StarHeals(group).Length==4 && StarHeals(group).All(h=>(int)h["amount"]==12),"four full allies each receive twelve generated healing");
        Check(group.Effects.Count(e=>e.Kind==ModBattleEffectKind.Shield&&e.Amount==12)==4 && Read(group,"healingTotal")==48,"Liangyi shields all four allies and counts all forty-eight healing including overheal");
        Check(Read(group,"omen")==4 && group.Random.CallCount==0,"group support neither consumes omen nor randomly selects recipients");
        Check(group.Effects.Where(e=>e.Kind==ModBattleEffectKind.Energy).All(e=>e.Target==ActorId),"Liangyi energy remains caster-only");
        var tree=Play(4000042,group.State);
        Check(Damage(tree)==59 && Read(tree,"healingTotal")==48,"forty-eight generated healing produces nine Rebirth bonus damage, without spending the total");
        Check(SectCardRules.PrintedCost(cards.Single(c=>(string)c["sourceId"]=="4000042"))==4,"Rebirth preserves one-lingqi original face fee");
        foreach(int n in new[]{1,2,4})foreach(int q in new[]{0,1,4})
        {
            var members=Enumerable.Range(2,n-1).Select(i=>StarHealingUnit((byte)i,health:300,sect:false)).ToArray();
            var p=Play(4000040,StarExpansionState(("omen",q)),full,members);
            Check(Read(p,"healingTotal")==3*q*n && StarHeals(p).Length==(q==0?0:n),"group formula scales by live recipients and omen: "+n+"/"+q);
            Check(p.Effects.Count(e=>e.Kind==ModBattleEffectKind.Shield)==(q==0?0:n),"zero omen creates neither fake shields nor healing");
        }
        var invalid=new[]{StarHealingUnit(2,health:0,sect:false),StarHealingUnit(3,health:100,sect:false,side:CombatSide.Enemy,kind:CombatantKind.Enemy),StarHealingUnit(4,health:100,sect:false,kind:CombatantKind.AuxiliaryTarget)};
        var filtered=Play(4000040,StarExpansionState(("omen",4)),full,invalid);
        Check(StarHeals(filtered).Length==1 && Read(filtered,"healingTotal")==12 && filtered.Effects.Count(e=>e.Kind==ModBattleEffectKind.Shield)==1,"group excludes downed, enemy and auxiliary targets from both shields and healing accounting");
        var deadGroup=StarHealth(filtered).OfType<JObject>().Where(h=>(string)h["kind"]=="heal");
        Check(deadGroup.All(h=>(string)h["target"]==ActorId.Value),"full living caster still receives overheal when no ally is eligible");
        foreach(int rank in new[]{3,4,5})foreach(int q in new[]{0,1,5})
        {
            var p=Play(4000068,StarExpansionState(("omen",q),("omenUsed",99)),Unit(school:school,rank:rank));
            Check(Read(p,"omen")==q-(q>0?1:0)+rank-2,"Thunder Hexagram fixed rank refund ignores historical spending: "+rank+"/"+q);
            Check(Read(p,"omenUsed")==99+(q>0?1:0),"only the original attack roll consumes omen");
        }
        var twice=Play(4000068,StarExpansionState(("repeat",1)),full);
        Check(Read(twice,"omen")==5 && Read(twice,"plays")==1,"rank-five zero-omen repeated Thunder refunds three per effect with one intervening spend");
        var formation=Play(4000068,StarExpansionState(("hexFormation",3)),full);
        Check(StarHealth(formation).OfType<JObject>().Any(h=>(string)h["kind"]=="nonAttack"&&(int)h["amount"]==27),"fixed rank refund still triggers existing six-lines immediate nonattack damage");
        foreach(int total in new[]{0,14,15,29,30,48,150})
        {
            var p=Play(4000042,StarExpansionState(("healingTotal",total)));
            Check(Damage(p)==50+3*(total/15) && Read(p,"healingTotal")==total,"Rebirth exact floor and non-consumption: "+total);
        }
        var other=StarHealingUnit(2,health:300);
        Check(Damage(Play(4000042,StarExpansionState(("healingTotal",150)),other))==50,"another actor cannot borrow caster healing history");
        var snapshot=StarExpansionState(("healingTotal",48),("omen",4));string serialized=JsonConvert.SerializeObject(snapshot.Entries);
        Check(Damage(Play(4000042,snapshot))==59 && JsonConvert.SerializeObject(snapshot.Entries)==serialized,"Rebirth preview does not mutate saved healing amount");
        var restored=new ModStateSnapshot(JsonConvert.DeserializeObject<ModStateEntry[]>(serialized));
        Check(Damage(Play(4000042,restored))==59,"healing growth survives authoritative state serialization");
        var cleared=Run(SevenStarRules.Start,state:snapshot,actor:full);
        Check(Read(cleared,"healingTotal")==0,"new battle clears accumulated healing");
        var calm=Play(4000027);var noRepeat=Play(4000027,calm.State);
        Check(Read(calm,"calm")==2 && Read(noRepeat,"calm")==2 && !SevenStarRules.CanPlay(cards.Single(c=>(string)c["sourceId"]=="4000027"),ActorId,calm.State),"Calm is once-per-battle persistent and does not stack");
        var calmGain=Play(4000008,calm.State,Unit(school:school,rank:5,energy:0));
        Check(Read(calmGain,"healingTotal")==12,"eight actual restored energy generates twelve healing");
        var capped=Play(4000008,calm.State,Unit(school:school,rank:5,energy:60));
        Check(Read(capped,"healingTotal")==0 && StarHeals(capped).Length==0,"full energy cannot manufacture Calm healing");
        var partial=Play(4000008,calm.State,Unit(school:school,rank:5,energy:59));
        var remainder=Play(4000008,partial.State,Unit(school:school,rank:5,energy:57));
        Check(Read(partial,"calmRemainder")==1 && Read(remainder,"calmRemainder")==0 && Read(remainder,"healingTotal")==6,"fractional energy carries until four actual restored energy, never theory output");
        var cure=Play(4000017,StarExpansionState(("omen",1)));
        Check(StarHeals(cure).Single().Value<int>("amount")==59 && Read(cure,"healingTotal")==59 && Read(cure,"omen")==0,"Qi Therapy max roll adds caster constitution and counts full generated healing");
        Check(StarHealth(cure).OfType<JObject>().First().Value<int>("amount")==24,"Qi Therapy keeps its independent caster maximum bonus");
        var extraEnemy=StarHealingUnit(7,health:300,sect:false,side:CombatSide.Enemy,kind:CombatantKind.Enemy);
        var thunder=Play(4000063,StarExpansionState(("omen",2)),full,new[]{extraEnemy});
        Check(thunder.Operations.Count==4 && thunder.Operations.SelectMany(o=>o.Targets).Distinct().Count()==2 && Damage(thunder)==136,"two full thunder waves hit each enemy without splitting damage");
        var five=Play(4000046,StarExpansionState(("omen",5)),full,new[]{extraEnemy});
        Check(five.Operations.Count==10 && Damage(five)==280 && five.Operations.SelectMany(o=>o.Targets).All(id=>id==EnemyId||id==extraEnemy.RuntimeId),"five lightning successes each cover all living enemies at full damage");
        var solo=Play(4000063,StarExpansionState(("omen",2)));
        Check(solo.Operations.All(o=>o.Targets.Single()==EnemyId),"single enemy receives both original thunder segments");
        var invalidEnemies=new[]{StarHealingUnit(6,health:0,sect:false,side:CombatSide.Enemy,kind:CombatantKind.Enemy),StarHealingUnit(7,health:100,sect:false,side:CombatSide.Enemy,kind:CombatantKind.AuxiliaryTarget),StarHealingUnit(8,health:100,sect:false)};
        var filteredThunder=Play(4000063,StarExpansionState(("omen",2)),full,invalidEnemies);
        Check(filteredThunder.Operations.All(o=>o.Targets.Single()==EnemyId),"lightning never redirects into downed enemies, auxiliary targets or allied characters");
        Check(!cards.Any(c=>new[]{"internal","triggerInternal","soulCharges","injuryRounds","oddInternal","evenExternal"}.Any(k=>c["rules"][k]!=null)),"no remaining card can produce or detonate injury");
        Console.WriteLine("PASS star routes: group shields/healing, overheal growth, rank refund, Calm actual energy, Rebirth floors/save/isolation, full-damage group lightning and no injury");
    }
}
