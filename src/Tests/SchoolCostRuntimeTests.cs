using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.ViewModels;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private CardTableCardViewModel CostHandView(Fixture f,StableId card,int expected)
        {
            var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
            var model=BattleUIPresentationMapper.BuildCardTable(snapshot,snapshot.Players.First(p=>p.Slot==Slot),data,battles);
            var hand=model.Hand.First(h=>h.CardId==card.Value);
            var economies=(RunPlayerActionEconomySnapshot[])AccessTools.Method(typeof(BattleAuthorityService),"CreatePlayerActionEconomySnapshots").Invoke(f.B,new object[]{new[]{Slot}});
            var cost=economies.Single(p=>p.PlayerSlot==Slot).ResolvedActionCosts.Single(c=>c.ActionId==card);
            Require(cost.BaseEnergyAmount+cost.SurchargeAmount==expected,"authority preview differs from original face: "+card);
            Require(hand.Action.CommandCost!=null&&hand.Action.CommandCost.Energy==expected,"HUD fee differs from original face: "+card);
            Require(BattleUIPresentationMapper.IsCardTableCommand(hand.Action),"card routed into abilities: "+card);
            return hand;
        }
        private Fixture CostFixture(string school,StableId card)
        {
            var f=New(5,school:school,buyCurrent:false);
            var pack=SectRegistry.Packs.Single(p=>SectRegistry.Owner(p)==school&&((JArray)p["cards"]).Values<string>().Any(k=>SectRegistry.CardPrefix+k==card.Value));
            var id=SectRegistry.PackId(pack);if(!f.P.HasPurchasedShelfAbility(id))f.Buy(id);
            f.StartBattle();ToughEnemy(f.Enemy);f.Prepare(card);
            return f;
        }
        private JObject SchoolFaceCosts(string school)
        {
            // Opt-in launcher copies this independently inspected artwork ledger.
            // Never derive expected payments from the runtime's own rules.cost.
            var ledger=(JObject)JObject.Parse(File.ReadAllText(Path.Combine(context.PackageDirectory,"TestResults","original-costs-fixture.json")))["cards"];
            string prefix=school==FiveElementRules.Lineage?"700":"400";
            var cards=SectRegistry.Cards.Where(c=>SectRegistry.Owner(c.Value)==school).ToArray();
            Require(cards.Length==48&&cards.Select(c=>(string)c.Value["sourceId"]).OrderBy(x=>x).SequenceEqual(ledger.Properties().Where(p=>p.Name.StartsWith(prefix,StringComparison.Ordinal)).Select(p=>p.Name).OrderBy(x=>x)),"original fixture must cover exactly 48 school cards");
            var rows=new JArray();int paid=0,free=0;
            foreach(var pair in cards)
            {
                var card=new StableId(pair.Key);string sid=(string)pair.Value["sourceId"];
                int lingqi=(int)ledger[sid]["cost"],expected=checked(lingqi*4);
                Require((int)pair.Value["rules"]["cost"]==lingqi,"content differs from inspected original face: "+sid);
                var f=CostFixture(school,card);
                if(expected>0)
                {
                    paid++;f.System(RunDeveloperCommand.SetVitals,int.MinValue,expected-1,0);
                    long revision=f.A.Run.Revision;var hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();
                    var random=f.A.Run.CaptureRandomState();var state=JToken.FromObject(SchoolState(f).Entries);
                    var view=CostHandView(f,card,expected);
                    Require(view.Action.CommandCost.InsufficientEnergy&&!view.Action.IsAvailable,"insufficient-energy HUD should block: "+card);
                    Require(f.A.Run.Revision==revision&&f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand)&&f.Actor.CurrentMagic==expected-1,"preview changed battle state");
                    Require(battles.TryGetFirstAbilityAction(card,out var definition),"missing paid action");
                    var target=definition.ActionSpec.TargetKind==CombatTargetKind.Self?default(StableId):f.Enemy.RuntimeId;
                    var rejected=f.Action(card,target,CardTableRules.EncodePlay(new StableId(view.InstanceId)));
                    Require(!rejected.IsSuccess&&rejected.ErrorCode==SessionErrorCode.InsufficientMagicPoints,"underfunded original card accepted: "+card+" / "+rejected.ErrorCode);
                    Require(f.A.Run.Revision==revision&&f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand)&&f.Actor.CurrentMagic==expected-1&&f.Actor.ActionPoints==0,"rejected card mutated payment/hand/revision");
                    Require(f.A.Run.CaptureRandomState().Equals(random)&&JToken.DeepEquals(state,JToken.FromObject(SchoolState(f).Entries)),"rejected card mutated RNG or school state");
                }
                else free++;
                // Exact funds for paid cards, zero funds for free cards. Recovery
                // can hide a charge in net deltas, so free play at zero is tested.
                f.System(RunDeveloperCommand.SetVitals,int.MinValue,expected,0);
                var funded=CostHandView(f,card,expected);var instance=new StableId(funded.InstanceId);
                Require(funded.Action.IsAvailable&&!funded.Action.CommandCost.InsufficientEnergy,"funded card unavailable: "+card);
                f.Play(card);
                Require(!f.Table.Hand.Any(h=>h.InstanceId==instance)&&f.Actor.ActionPoints==0,"accepted card did not consume its hand instance at zero AP");
                if(expected>0)Require(f.Actor.CurrentMagic==0,"actual fee differs from original face: "+card);
                rows.Add(new JObject{["sourceId"]=sid,["name"]=pair.Value["name"],["original_lingqi"]=lingqi,["authority_preview_qi"]=expected,["hud_qi"]=funded.Action.CommandCost.Energy,["paid_qi"]=expected,["energy_after"]=f.Actor.CurrentMagic,["free_at_zero"]=expected==0,["insufficient_rejected_atomically"]=expected>0});
            }
            Require(paid==(school==FiveElementRules.Lineage?13:11)&&free+paid==48,"manual paid-card census differs");
            return new JObject{["school"]=school,["cards"]=rows,["paid_cards"]=paid,["free_at_zero_cards"]=free,["insufficient_rejections"]=paid};
        }
        private JObject WaterWaveConditionalFree()
        {
            var wave=SchoolSource(FiveElementRules.Lineage,7000019);var seal=SchoolSource(FiveElementRules.Lineage,7000006);
            var f=CostFixture(FiveElementRules.Lineage,wave);f.Prepare(wave,seal);
            CostHandView(f,wave,8);f.Play(seal);
            Require((SchoolValue(f,FiveElementRules.Lineage,"active")&2)!=0,"water seal did not activate water");
            f.System(RunDeveloperCommand.SetVitals,int.MinValue,0,0);var view=CostHandView(f,wave,0);
            Require(view.Action.IsAvailable,"water wave should be usable for free after water activation");
            int before=SchoolValue(f,FiveElementRules.Lineage,"waterForce");
            f.Play(wave);int gained=SchoolValue(f,FiveElementRules.Lineage,"waterForce")-before;
            Require(f.Actor.CurrentMagic==0,"conditional free wave paid energy: "+f.Actor.CurrentMagic);
            Require(gained==2,"conditional free wave water force gain: "+gained);
            return new JObject{["sourceId"]="7000019",["printed_qi"]=8,["water_condition_qi"]=0,["played_at_zero_qi"]=true,["water_gained"]=gained};
        }
    }
}
