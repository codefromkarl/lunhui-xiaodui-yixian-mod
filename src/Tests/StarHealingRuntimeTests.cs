using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject SevenStarTeamHealing()
        {
            var school=SevenStarRules.Lineage;var f=SchoolFixture(school);
            var card=SchoolSource(school,4000013);
            var ally=new CombatantState(new StableId("yixian.selftest.healing.ally"),f.Actor.DefinitionId,
                CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(2));
            Require(ally.SetVitals(200,0).IsSuccess && f.B.Battle.AddCombatant(ally).IsSuccess,"add isolated injured allied character");
            f.System(RunDeveloperCommand.SetVitals,f.Actor.MaximumHealth,int.MinValue,int.MinValue);
            f.Prepare(card);f.Play(card);
            Require(ally.CurrentHealth==200,"starter first use unexpectedly healed ally");
            f.Prepare(card);int casterHealth=f.Actor.CurrentHealth;f.Play(card);
            Require(ally.CurrentHealth>200 && f.Actor.CurrentHealth==casterHealth,"post heal did not reach injured ally while caster was full");
            Require(ally.SetVitals(300,0).IsSuccess,"fill ally for empty healing candidate set");
            int total=SchoolValue(f,school,"healingTotal");
            f.Prepare(card);f.Play(card);
            Require(SchoolValue(f,school,"healingTotal")>total,"full-party healing did not record generated amount");
            Require(ally.CurrentHealth==300 && f.Actor.CurrentHealth==casterHealth,"overheal changed full allied health");
            Require(ally.SetVitals(0,0).IsSuccess,"down isolated ally");
            f.Prepare(card);f.Play(card);
            Require(ally.IsDowned && ally.CurrentHealth==0 && f.Actor.CurrentHealth==casterHealth,"random healing revived a downed ally or healed a full caster");
            return new JObject { ["injured_ally_healed"]=true,["full_caster_skipped"]=true,["full_party_overheal_counted"]=true,["downed_ally_not_revived"]=true,["group"]=SevenStarGroupHealing() };
        }
        private JObject SevenStarGroupHealing()
        {
            var school=SevenStarRules.Lineage;var f=SchoolFixture(school,rank:3);
            var gain=SchoolSource(school,4000015);var group=SchoolSource(school,4000040);var tree=SchoolSource(school,4000042);
            var allies=new System.Collections.Generic.List<CombatantState>{f.Actor};
            for(byte slot=2;slot<=4;slot++)
            {
                var ally=new CombatantState(new StableId("yixian.selftest.group."+slot),f.Actor.DefinitionId,CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(slot));
                Require(f.B.Battle.AddCombatant(ally).IsSuccess,"insert group healing ally");allies.Add(ally);
            }
            f.System(RunDeveloperCommand.SetVitals,f.Actor.MaximumHealth,int.MinValue,int.MinValue);
            f.Prepare(gain);f.Play(gain);f.Prepare(gain);f.Play(gain);
            Require(SchoolValue(f,school,"omen")==4,"arrange four omen with actual cards");
            f.Prepare(group,tree);int total=SchoolValue(f,school,"healingTotal");
            var shields=allies.ConvertAll(a=>a.Shield);f.Play(group);
            Require(SchoolValue(f,school,"healingTotal")==total+48,"four full allies did not generate forty-eight healing");
            for(int i=0;i<allies.Count;i++)Require(allies[i].Shield==shields[i]+12,"group shield amount or recipient mismatch");
            Require(SchoolValue(f,school,"omen")==4,"group support spent omen");
            int hp=f.Enemy.CurrentHealth;f.Play(tree);
            Require(f.Enemy.CurrentHealth<hp&&SchoolValue(f,school,"healingTotal")==total+48,"Rebirth did not attack or consumed healing history");
            return new JObject{["full_party_generated_healing"]=48,["shield_each"]=12,["omen_not_spent"]=true,["rebirth_consumes_no_history"]=true};
        }
    }
}
