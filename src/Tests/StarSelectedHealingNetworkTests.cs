using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        // Slot 2 is an admitted client, not the host. All four allies own real
        // PlayerRunState/card tables; every play traverses Mirror and host authority.
        private JObject SevenStarSelectedHealingNetwork(int source)
        {
            const byte caster=2;var school=SevenStarRules.Lineage;
            var card=SchoolSource(school,source);int fee=source==4000017?4:8,capGain=source==4000017?24:36;
            var rows=new JArray();
            foreach(string scenario in new[]{"injured_teammate","full_teammate","self","repeat_full_teammate"})
            {
                var f=NewMulti(4);
                for(byte slot=1;slot<=4;slot++)f.Upgrade(slot,school);
                for(int rank=2;rank<=5;rank++)f.Upgrade(caster,school);
                var gain=SchoolSource(school,4000015);var repeat=SchoolSource(school,4000041);
                foreach(var needed in new[]{card,gain,repeat})
                {
                    var pack=CardTableCatalog.Packs.Single(p=>p.CardIds.Contains(needed));
                    if(!f.P(caster).HasPurchasedShelfAbility(pack.UnlockAbilityId))
                        Require(f.Send(caster,RunCommand.PurchaseShelfAbility(new PlayerSlotId(caster),pack.UnlockAbilityId,f.A.Run.Revision)).IsSuccess,"selected healing buy pack");
                }
                StartMulti(f,4);
                var actor=f.Actor(caster);var chosen=f.Actor(1);var other=f.Actor(3);var down=f.Actor(4);
                int Value(string key)=>SectRegistry.Read((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B(caster)),SectRegistry.StateKey(school,actor.RuntimeId,key));
                void Play(StableId id,StableId target)
                {
                    var hand=f.Table(caster).Hand.First(h=>h.CardId==id);
                    var r=f.Action(caster,id,CardTableRules.EncodePlay(hand.InstanceId),target);
                    Require(r.IsSuccess,"selected healing network play "+id+": "+r.ErrorCode);
                    Require(!f.Table(caster).Hand.Any(h=>h.InstanceId==hand.InstanceId),"selected healing did not consume own hand instance");
                }
                bool firstPostSkipped=true;
                if(source==4000060)
                {
                    f.Prepare(caster,card);int before=Value("healingTotal"),maximum=actor.CurrentMaximumHealth;
                    var health=f.B(caster).Battle.Combatants.Where(u=>u.Kind==CombatantKind.Player).Select(u=>u.CurrentHealth).ToArray();
                    int count=f.Table(caster).Hand.Count;Play(card,chosen.RuntimeId);
                    firstPostSkipped=Value("healingTotal")==before&&health.SequenceEqual(f.B(caster).Battle.Combatants.Where(u=>u.Kind==CombatantKind.Player).Select(u=>u.CurrentHealth));
                    Require(firstPostSkipped&&actor.CurrentMaximumHealth==maximum+36,"Mountains first use healed or lost caster maximum");
                    Require(f.Table(caster).Hand.Count==count,"Mountains lost its extra draw");
                }
                f.Prepare(caster,gain);f.Play(caster,gain);
                bool repeated=scenario=="repeat_full_teammate";
                if(repeated){f.Prepare(caster,repeat);f.Play(caster,repeat);Require(Value("repeat")==1,"real repeat setup failed");}
                f.Prepare(caster,card);
                foreach(var unit in new[]{actor,chosen,other,down})
                {
                    int hp=unit==down?0:unit==other||unit==chosen&&scenario=="injured_teammate"||unit==actor&&scenario=="self"?1:unit.CurrentMaximumHealth;
                    if(unit.CurrentHealth!=hp||unit.Shield!=0)Require(unit.SetVitals(hp,0).IsSuccess,"selected healing baseline vitals");
                }
                var target=scenario=="self"?actor:chosen;
                Require(f.Catalog.TryGetFirstAbilityAction(card,out var definition)&&definition.ActionSpec.TargetKind==CombatTargetKind.AllySingle,"native heal must be AllySingle");
                var instance=f.Table(caster).Hand.First(h=>h.CardId==card).InstanceId;
                var parameter=CardTableRules.EncodePlay(instance);
                // Native rejection must not consume the card, payment, RNG, health,
                // another player's table, or school counters. Session sequence advances.
                JObject Snapshot()=>new JObject{
                    ["revision"]=f.A.Run.Revision,
                    ["state"]=JToken.FromObject(((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B(caster))).Entries),
                    ["players"]=new JArray(Enumerable.Range(1,4).Select(i=>{byte slot=(byte)i;var u=f.Actor(slot);return new JObject{
                        ["hp"]=u.CurrentHealth,["max"]=u.CurrentMaximumHealth,["shield"]=u.Shield,["qi"]=u.CurrentMagic,["ap"]=u.ActionPoints,
                        ["hand"]=new JArray(f.Table(slot).Hand.Select(h=>h.InstanceId.Value))};}))};
                var random=f.A.Run.CaptureRandomState();var beforeReject=Snapshot();
                var auxiliary=new CombatantState(new StableId("yixian.selected.aux."+fixtureNumber),actor.DefinitionId,CombatantKind.AuxiliaryTarget,CombatSide.Player,FormationRow.Front,300,60,0,0,0,BuffCatalog.Empty,new PlayerSlotId(caster));
                Require(f.B(caster).Battle.AddCombatant(auxiliary).IsSuccess,"auxiliary rejection setup");
                var invalid=new[]{default(StableId),f.B(caster).Battle.Combatants.First(u=>u.Side==CombatSide.Enemy).RuntimeId,down.RuntimeId,auxiliary.RuntimeId,new StableId("yixian.selected.missing")};
                foreach(var invalidTarget in invalid)
                {
                    var rejected=f.Action(caster,card,parameter,invalidTarget);
                    Require(!rejected.IsSuccess&&rejected.ErrorCode==SessionErrorCode.InvalidTarget,"invalid healing target accepted: "+invalidTarget+" / "+rejected.ErrorCode);
                    Require(JToken.DeepEquals(beforeReject,Snapshot())&&f.A.Run.CaptureRandomState().Equals(random),"invalid target changed authority state");
                }
                f.System(caster,RunDeveloperCommand.SetVitals,int.MinValue,fee-1,0);
                beforeReject=Snapshot();random=f.A.Run.CaptureRandomState();
                var unfunded=f.Action(caster,card,parameter,target.RuntimeId);
                Require(!unfunded.IsSuccess&&unfunded.ErrorCode==SessionErrorCode.InsufficientMagicPoints&&JToken.DeepEquals(beforeReject,Snapshot())&&f.A.Run.CaptureRandomState().Equals(random),"underfunded selected heal was not atomic");
                f.System(caster,RunDeveloperCommand.SetVitals,int.MinValue,fee,0);
                Require(MultiQuote(f,caster,card)==fee,"selected healing HUD preview lost original fee");
                int cap=actor.CurrentMaximumHealth,chosenCap=chosen.CurrentMaximumHealth,hpBefore=target.CurrentHealth,casterHp=actor.CurrentHealth;
                int total=Value("healingTotal"),plays=Value("plays"),used=Value("used"+source),omen=Value("omen");
                int amount=(source==4000017?54:72)+f.P(caster).DerivedStats.EffectiveAttributes.Constitution/2;
                int times=repeated?2:1;var peerHands=new JArray(new[]{1,3,4}.Select(s=>new JArray(f.Table((byte)s).Hand.Select(h=>h.InstanceId.Value))));
                var peerStates=new JArray(new[]{1,3,4}.Select(s=>SchoolStateFor(f,(byte)s,school)));
                Play(card,target.RuntimeId);
                int expectedCap=cap+capGain*times,expectedGenerated=amount*times;
                Require(actor.CurrentMaximumHealth==expectedCap&&chosen.CurrentMaximumHealth==chosenCap,"maximum bonus moved to recipient or repeat count changed");
                Require(target.CurrentHealth==Math.Min(target==actor?expectedCap:chosenCap,hpBefore+expectedGenerated),"selected healing health/amount differs from original formula");
                Require(target==actor||actor.CurrentHealth==casterHp,"caster maximum bonus incorrectly restored current health");
                Require(other.CurrentHealth==1&&down.CurrentHealth==0&&down.IsDowned,"selected heal rerouted to injured/downed peer");
                Require(Value("healingTotal")-total==expectedGenerated&&Value("plays")==plays+1&&Value("used"+source)==used+times,"generated heal, repeat or sequence counters incorrect");
                Require(Value("repeat")==0&&Value("omen")==omen-(source==4000017?times:0),"repeat consumption or omen maximum changed");
                Require(actor.CurrentMagic==0&&actor.ActionPoints==0,"original energy fee changed or repeated effect charged twice");
                Require(JToken.DeepEquals(peerHands,new JArray(new[]{1,3,4}.Select(s=>new JArray(f.Table((byte)s).Hand.Select(h=>h.InstanceId.Value)))))&&
                    JToken.DeepEquals(peerStates,new JArray(new[]{1,3,4}.Select(s=>SchoolStateFor(f,(byte)s,school)))),"selected heal changed peer tables or counters");
                rows.Add(new JObject{["scenario"]=scenario,["client_slot"]=caster,["target_slot"]=target.OwnerSlot.Value,["real_players"]=4,
                    ["hp_before"]=hpBefore,["hp_after"]=target.CurrentHealth,["generated_healing"]=Value("healingTotal")-total,["effect_count"]=times,
                    ["caster_max_before"]=cap,["caster_max_after"]=actor.CurrentMaximumHealth,["qi_paid"]=fee,["ap_paid"]=0,
                    ["invalid_targets_rejected"]=invalid.Length,["insufficient_atomic"]=true,["first_post_skipped"]=firstPostSkipped,["peer_tables_and_state_isolated"]=true});
            }
            return new JObject{["sourceId"]=source,["mirror_client_to_host"]=true,["scenarios"]=rows};
        }
    }
}
