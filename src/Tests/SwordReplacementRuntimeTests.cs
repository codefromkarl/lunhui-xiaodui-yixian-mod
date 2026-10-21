using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private Fixture ReplacementFixture(int source)
        {
            var f=New(5,buyCurrent:false,seed:75501);
            var card=SwordRules.Cards[Card(source).Value];var pack=SwordRules.Packs.Single(p=>((JArray)p["cards"]).Values<string>().Contains((string)card["key"]));
            var packId=SwordRules.PackId(pack);if(!f.P.HasPurchasedShelfAbility(packId))f.Buy(packId);
            Require(f.P.SetActiveCardPacks(f.P.ActiveCardPackIds.Concat(new[]{packId}).Distinct().ToArray()).IsSuccess,"replacement fixture activate pack");
            f.StartBattle();ToughEnemy(f.Enemy);f.Prepare(Card(source));
            ((DeterministicRng)AccessTools.Property(typeof(RunState),"Random").GetValue(f.A.Run)).RestoreState(new DeterministicRng(75503).CaptureState());
            return f;
        }
        private JObject SwordReplacements()
        {
            var rows=new JArray();
            foreach(int source in new[]{11,8,37})
            {
                var f=ReplacementFixture(source);var id=Card(source);
                CostHandView(f,id,4);
                int qi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting),ap=f.Actor.ActionPoints,hp=f.Enemy.CurrentHealth,shield=f.Actor.Shield;
                int body=f.P.DerivedStats.EffectiveAttributes.Constitution;
                var receipt=f.Play(id);int damage=hp-f.Enemy.CurrentHealth;
                Require(f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==qi-4&&f.Actor.ActionPoints==ap,"replacement must pay exactly four qi and zero AP");
                Require(damage>0,"replacement fixture must make real health hit, source="+source);
                int delayed=0;
                if(source==11)Require(f.State("intent")==3,"shape native hit did not award three intent once");
                if(source==8)
                {
                    delayed=21+body*5000/10000;
                    Require(f.State("nextShield")==delayed&&f.Actor.Shield==shield,"reflection must queue one body-scaled shield without immediate shield");
                    // Keep this isolated fixture alive through the enemy phase. Compare
                    // a matched no-queue control because native shield retention is nonzero.
                    Require(f.Actor.SetVitals(f.Actor.CurrentHealth,10000).IsSuccess,"reflection survival setup shield");
                    try { f.NextRound(); }
                    catch(Exception ex) { throw new InvalidOperationException("reflection advance: round="+f.B.Battle.Round+" phase="+f.B.Battle.Phase+" complete="+f.B.IsComplete+" actorHp="+f.Actor.CurrentHealth+" enemyHp="+f.Enemy.CurrentHealth+" pending="+f.State("nextShield"),ex); }
                    int withGrant=f.Actor.Shield;
                    Require(f.State("nextShield")==0,"reflection queue not cleared at next round");
                    var control=ReplacementFixture(8);control.Play(Card(8));
                    Require(control.State("nextShield")==delayed,"matched reflection control differs before round");
                    // Counterfactual fixture only: remove exactly the queued grant,
                    // retaining identical cast, resources, RNG and survival setup.
                    var state=(ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(control.B);
                    AccessTools.Field(typeof(BattleAuthorityService),"modState").SetValue(control.B,state.Apply(SectRegistry.Package,new[]{new ModStateEntry(SectRegistry.Package,SwordRules.StateKey(control.Actor.RuntimeId,"nextShield"),ModValueKind.Removed)}));
                    Require(control.Actor.SetVitals(control.Actor.CurrentHealth,10000).IsSuccess,"control survival shield");
                    control.NextRound();
                    Require(withGrant-control.Actor.Shield==delayed,"reflection native grant differs from matched retained-shield control: with="+withGrant+" control="+control.Actor.Shield+" expectedDelta="+delayed);
                }
                if(source==37)Require(receipt.PresentationSteps.Count==4,"flow native presentation must retain all four damage segments, got "+receipt.PresentationSteps.Count);
                rows.Add(new JObject{["card"]=SwordRules.Cards[Card(source).Value]["name"],["level"]=1,["qi_paid"]=4,["ap_paid"]=0,["health_damage"]=damage,["delayed_shield"]=delayed,["presentation_segments"]=receipt.PresentationSteps.Count});
            }
            foreach(int source in new[]{11,8})
            {
                var f=ReplacementFixture(source);Require(f.Enemy.SetVitals(f.Enemy.CurrentHealth,10000).IsSuccess,"arrange shield-only enemy");
                int hp=f.Enemy.CurrentHealth;f.Play(Card(source));
                Require(f.Enemy.CurrentHealth==hp&&f.State(source==11?"intent":"nextShield")==0,"shield-only hit incorrectly triggered replacement payoff");
            }
            return new JObject{["three_base_cards"]=rows,["shield_only_no_rewards"]=true,["native_payments_and_damage"]=true,["four_presentation_steps"]=true};
        }
    }
}
