using System.Reflection;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    sealed class OriginalMaximumTestRule : IMaximumRule
    {
        public int Maximum(CombatantState unit,ModMaximumKind kind,int value)
            =>OriginalHealthRules.ModifyMaximum(new ModMaximumQuery(unit,kind,value,null));
    }
    static void OriginalPrimitivesTests()
    {
        CombatantState Target(int shield=0)
        {
            var target=new CombatantState(EnemyId,new StableId("test.original"),CombatantKind.Enemy,CombatSide.Enemy,FormationRow.Front,300,20,0,0,0,BuffCatalog.Empty);
            Method(typeof(CombatantState),"AttachMaximumRule").Invoke(target,new object[]{new OriginalMaximumTestRule()});
            if(shield>0)target.SetVitals(300,shield);return target;
        }
        var target=Target(50);
        OriginalHealthRules.Apply(target,ActorId,"maximum",60,false);
        Check(target.CurrentMaximumHealth==360&&target.CurrentHealth==300&&target.Shield==50,"max-only increase preserves current health and shield");
        OriginalHealthRules.Apply(target,ActorId,"heal",90,false);
        Check(target.CurrentHealth==360,"heal after max increase uses newly raised cap");
        target.SetVitals(200,50);
        OriginalHealthRules.Apply(target,ActorId,"maximum",-90,true);
        Check(target.CurrentMaximumHealth==270&&target.CurrentHealth==110&&target.Shield==50,"combined life/max loss subtracts once, bypasses shield");
        var full=Target();OriginalHealthRules.Apply(full,ActorId,"maximum",-9,true);
        Check(full.CurrentHealth==291&&full.CurrentMaximumHealth==291,"full-health combined reduction does not double subtract");
        OriginalHealthRules.Apply(target,ActorId,"maximum",-1000,false);
        Check(target.CurrentMaximumHealth==1&&target.CurrentHealth==1,"maximum health floor and clamp are native-safe");
        var loss=Target(80);OriginalHealthRules.Apply(loss,ActorId,"loss",7,false);
        Check(loss.CurrentHealth==293&&loss.Shield==80,"life loss is not shielded damage");
        var damage=Target(30);
        damage.UpsertStatus(new CombatStatusState(new StableId("test.guard.1"),new StableId("yixian.sect.status.star.guard0"),ActorId,1,0));
        damage.UpsertStatus(new CombatStatusState(new StableId("test.guard.2"),new StableId("yixian.sect.status.star.guard1"),ActorId,1,0));
        OriginalHealthRules.Apply(damage,ActorId,"nonAttack",20,false);
        Check(damage.Shield==10&&damage.CurrentHealth==300&&damage.Statuses.Count==2,"shield absorbs nonattack damage before consuming life guard");
        OriginalHealthRules.Apply(damage,ActorId,"nonAttack",20,false);
        Check(damage.Shield==0&&damage.CurrentHealth==300&&damage.Statuses.Count==1,"life guard consumes one instance only after shield is exhausted");
        OriginalHealthRules.Apply(damage,ActorId,"loss",8,false);
        Check(damage.CurrentHealth==300&&damage.Statuses.Count==0,"direct life loss consumes the other guard once");
        OriginalHealthRules.Apply(damage,ActorId,"nonAttack",10,false);
        Check(damage.CurrentHealth==290&&damage.Shield==0,"unprotected nonattack damage reduces health");
        OriginalHealthRules.Apply(damage,ActorId,"loss",1000,false);
        Check(damage.IsDowned&&damage.CurrentHealth==0,"direct life loss can down a target without negative health");
        var actor=Unit(school:SevenStarRules.Lineage);
        var plan=Run((context,p)=>
        {
            var turn=new BattleTurn(context,p,SevenStarRules.Lineage);
            turn.MaxHealth(12);turn.Heal(6);turn.NonAttackDamage(9);turn.DirectDamage(3);
        },actor:actor);
        var text=plan.State.Entries.Single(e=>e.Key==OriginalHealthRules.PendingKey).Text;
        var requests=JArray.Parse(text);
        Check(requests.Select(r=>(string)r["kind"]).SequenceEqual(new[]{"maximum","heal","nonAttack","loss"}),"life operations preserve source order in authoritative plan");
        Check(plan.Effects.All(e=>e.Kind!=ModBattleEffectKind.Damage&&e.Kind!=ModBattleEffectKind.Heal),"life/max queue is not falsely emitted as normal damage or pre-cap healing");
        Check(actor.CurrentHealth==300&&actor.CurrentMaximumHealth==300,"planning does not change input snapshot");
        Console.WriteLine("PASS original life primitives: exact temporary maximum/current health, life loss versus shielded nonattack damage, guards, command queue and preview isolation");
    }
}
