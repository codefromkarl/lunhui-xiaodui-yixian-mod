using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Network;
using Reincarnation.Runtime.Content;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private sealed class MultiFixture
        {
            public RunAuthorityService A;
            public EconomyCatalog Economy;
            public BattleAuthorityCatalog Catalog;
            public NetworkSessionAuthority Session;
            public readonly Dictionary<byte,ulong> Sequences=new Dictionary<byte,ulong>();
            public double Clock=1000;
            public PlayerRunState P(byte slot)=>A.Run.Players.Single(p=>p.Slot.Value==slot);
            public BattleAuthorityService B(byte slot){Require(A.TryGetPlayerBattle(new PlayerSlotId(slot),out var battle),"network fixture missing battle");return battle;}
            public CombatantState Actor(byte slot){var b=B(slot);Require(b.TryGetPlayerActorId(new PlayerSlotId(slot),out var id)&&b.Battle.TryGetCombatant(id,out var _),"network fixture missing actor");b.Battle.TryGetCombatant(id,out var actor);return actor;}
            public CardTableSnapshot Table(byte slot)=>B(slot).CaptureCardTable(new PlayerSlotId(slot));
            public SessionCommandContext Next(byte slot)=>new SessionCommandContext(slot,Session.SessionEpoch,++Sequences[slot]);
            public void System(byte slot,RunDeveloperCommand kind,params int[] numbers)
            {
                if(kind==RunDeveloperCommand.SetVitals&&numbers.Length==3)
                {
                    var actor=Actor(slot);
                    if((numbers[0]==int.MinValue||numbers[0]==actor.CurrentHealth)&&
                       (numbers[1]==int.MinValue||numbers[1]==actor.CurrentMagic)&&
                       (numbers[2]==int.MinValue||numbers[2]==actor.ActionPoints))return;
                }
                var r=A.Ledger.ExecuteSystem(RunSystemAction.Developer(kind,new PlayerSlotId(slot),default,numbers));Require(r.IsSuccess,"multiplayer fixture system "+kind+": "+r.ErrorCode);
            }
            public RunCommandDispatchResult Send(byte slot,RunCommand command,bool replay=false)
            {
                var battle=A.TryGetPlayerBattle(new PlayerSlotId(slot),out var b)?b.Battle:null;
                Require(NetworkRunCommandDto.TryCreate(command,new PlayerSlotId(slot),slot==1,out var dto,out var error,battle?.BattleId??default,battle?.Round??0),"network DTO rejected local command: "+error);
                // Use the game's generated Mirror writer/reader, not a parallel JSON codec.
                var generated=typeof(NetworkRunCommandDto).Assembly.GetType("Mirror.GeneratedNetworkCode");
                using(var writer=NetworkWriterPool.Get())
                {
                    AccessTools.Method(generated,"_Write_Reincarnation.Network.NetworkRunCommandDto").Invoke(null,new object[]{writer,dto});
                    using(var reader=NetworkReaderPool.Get(new ArraySegment<byte>(writer.ToArray())))
                        dto=(NetworkRunCommandDto)AccessTools.Method(generated,"_Read_Reincarnation.Network.NetworkRunCommandDto").Invoke(null,new object[]{reader});
                }
                var adapter=typeof(NetworkSessionAuthority).Assembly.GetType("Reincarnation.Network.NetworkRunCommandServerAdapter");
                return (RunCommandDispatchResult)AccessTools.Method(adapter,"AdmitDecodeAndDispatch").Invoke(null,new object[]{Session,A,replay?new SessionCommandContext(slot,Session.SessionEpoch,Sequences[slot]):Next(slot),Clock++,dto});
            }
            public RunCommandDispatchResult Action(byte slot,StableId action,StableId parameter=default,StableId target=default)
                =>Send(slot,RunCommand.SubmitBattleAction(new PlayerSlotId(slot),CombatActionKind.UseAbility,action,target,FormationRow.None,-1,A.Run.Revision,parameter));
            public void Upgrade(byte slot,string school)
            {
                int rank=SectRegistry.Rank(P(slot),school)+1;var id=new StableId(school);
                var answer=school==FiveElementRules.Lineage&&rank>1
                    ?FiveDeckRules.EncodeUnlock(rank,FiveDeckRules.Candidates(rank).Skip((slot-1)*2%FiveDeckRules.Candidates(rank).Count()).Concat(FiveDeckRules.Candidates(rank)).Take(2).Select(FiveDeckRules.CardId).ToArray())
                    :LineageFreeAbilityPicks.ComputeCandidates(Economy.ShelfAbilities,A.Run.Seed,new PlayerSlotId(slot),id,rank,P(slot).PurchasedShelfAbilityIds).FirstOrDefault();
                Require(Send(slot,RunCommand.PurchaseOrUpgradeLineage(new PlayerSlotId(slot),id,A.Run.Revision,answer)).IsSuccess,"network same-school purchase/upgrade: "+school+" slot "+slot+" rank "+rank);
            }
            public void Prepare(byte slot,StableId card)
            {
                var needed=CardTableCatalog.Packs.First(p=>p.CardIds.Contains(card)&&P(slot).HasPurchasedShelfAbility(p.UnlockAbilityId)).UnlockAbilityId;
                var active=P(slot).ActiveCardPackIds.ToList();if(!active.Contains(needed)){active.Add(needed);while(active.Count>8)active.Remove(active.First(id=>id!=needed));Require(Action(slot,CardTableRules.SetPacksActionId,CardTableRules.EncodePacks(active)).IsSuccess,"network equip card pack");}
                System(slot,RunDeveloperCommand.SetVitals,int.MinValue,Actor(slot).MaximumMagic,1000);
                for(int i=0;i<160&&!Table(slot).Hand.Any(h=>h.CardId==card);i++)
                {
                    if(Table(slot).Hand.Count>=10)Require(Action(slot,CardTableRules.DiscardActionId,CardTableRules.EncodePlay(Table(slot).Hand.First().InstanceId)).IsSuccess,"network discard");
                    Require(Action(slot,CardTableRules.DrawActionId).IsSuccess,"network draw");
                }
                Require(Table(slot).Hand.Any(h=>h.CardId==card),"network draw budget");
            }
            public void Play(byte slot,StableId card)
            {
                var hand=Table(slot).Hand.First(h=>h.CardId==card);var enemy=B(slot).Battle.Combatants.First(u=>u.Side==CombatSide.Enemy&&!u.IsDowned);
                // Current tests use self-targeted seals/guards or enemy-targeted attacks.
                StableId target=default;
                Require(Catalog.TryGetFirstAbilityAction(card,out var definition),"network ability missing");
                target=definition.ActionSpec.TargetKind==CombatTargetKind.Self?default:definition.ActionSpec.TargetKind==CombatTargetKind.AllySingle?Actor(slot).RuntimeId:enemy.RuntimeId;
                Require(Action(slot,card,CardTableRules.EncodePlay(hand.InstanceId),target).IsSuccess,"network play "+card+" slot "+slot);
            }
        }
        private MultiFixture NewMulti(int count)
        {
            var f=new MultiFixture{Economy=data.EconomyCatalog,Catalog=battles};string epoch="yixian-network-selftest-"+(++fixtureNumber);int token=0;
            RuleResult Validate(BirthSelection selection){var r=BirthValidator.Validate(selection,data);return r.IsSuccess?RuleResult.Success(0):RuleResult.Failure(r.ErrorCode,0);}
            var creation=NetworkSessionAuthority.Create(epoch,new SessionPlayerIdentity("test-host-"+fixtureNumber,"同门甲"),1,f.Clock,Validate,count,()=>"test-only-token-"+(++token));
            f.Session=creation.Authority;f.Sequences[1]=creation.HostCredentials.NextClientSequence-1;
            var births=new List<BirthSelection>();
            for(byte slot=1;slot<=count;slot++)
            {
                if(slot>1){var joined=f.Session.TryJoin(new SessionPlayerIdentity("test-peer-"+fixtureNumber+"-"+slot,"同门"+slot),slot,f.Clock++);Require(joined.TryGetValue(out var credentials),"network test peer join");f.Sequences[slot]=credentials.NextClientSequence-1;}
                var birth=new BirthSelection(new PlayerSlotId(slot),slot==1?"同门甲":"同门"+slot,BirthAppearance.Default,data.Origins[0].StableId,data.Experiences[0].StableId,data.EntryReasons[0].StableId);births.Add(birth);
                Require(f.Session.SubmitBirth(f.Next(slot),birth,f.Clock++).IsSuccess,"network birth");if(slot>1){var ready=f.Session.SetReady(f.Next(slot),true,f.Clock++);Require(ready.IsSuccess,"network ready slot "+slot+": "+ready.ErrorCode);}
            }
            Require(f.Session.TryStart(f.Next(1),f.Clock++).IsSuccess,"network start gate");
            var run=new RunState(new StableId("yixian.network.test."+fixtureNumber),91007,Difficulty.Standard,0,data.CreateWorldRoute(91007));
            foreach(var birth in births){var valid=BirthValidator.Validate(birth,data);Require(valid.TryGetValue(out var selection)&&run.AddPlayer(new PlayerRunState(selection)).IsSuccess,"add real network player");}
            Require(run.SetPhase(GamePhase.Hub).IsSuccess,"network run hub");
            var claims=(IReadOnlyList<PlayerSlotClaimState>)AccessTools.Method(typeof(NetworkSessionAuthority),"CaptureInitialRunSlotClaims").Invoke(f.Session,null);Require(run.SetSlotClaims(claims).IsSuccess,"network slot claims");
            var rewards=new AuthorityRewardGenerationContext(data.ContentOperationDispatcher,data.RewardTables,data.EconomyCatalog,ContentExecutionAuthorityScope.ServerAuthority);
            f.A=new RunAuthorityService(run,worlds,data.EconomyCatalog,data.EventContentCatalog,rewards,battles,()=>1000);Require(f.A.Initialize().IsSuccess,"network run initialize");
            f.System(1,RunDeveloperCommand.JumpToWorld,2);for(byte slot=1;slot<=count;slot++)f.System(slot,RunDeveloperCommand.AddRewardPoints,10000);
            return f;
        }
        private void StartMulti(MultiFixture f,int count)
        {
            for(byte slot=2;slot<=count;slot++)Require(f.Send(slot,RunCommand.SetHubReady(new PlayerSlotId(slot),true,f.A.Run.Revision)).IsSuccess,"network hub ready");
            Require(f.Send(1,RunCommand.EnterWorld(f.A.Run.Revision)).IsSuccess,"network world entry");
            f.System(1,RunDeveloperCommand.StartBattlefields,new[]{(int)NodeKind.Battle}.Concat(Enumerable.Range(1,count)).ToArray());
            for(byte slot=1;slot<=count;slot++)Require(ReferenceEquals(f.B(1),f.B(slot))&&f.Table(slot).Hand.Count==3,"same battlefield must own independent real card tables");
            foreach(var enemy in f.B(1).Battle.Combatants.Where(u=>u.Side==CombatSide.Enemy))ToughEnemy(enemy);
        }
        private JObject SameSchoolNetwork(string school)
        {
                var f=NewMulti(2);
                long concurrentRevision=f.A.Run.Revision;f.Upgrade(1,school);int peerPoints=f.P(2).RewardPoints;
                var concurrent=RunCommand.PurchaseOrUpgradeLineage(new PlayerSlotId(2),new StableId(school),concurrentRevision);
                data.TryGetLineage(new StableId(school),out var lineage);
                Require(f.Send(2,concurrent).IsSuccess&&SectRegistry.Rank(f.P(2),school)==1&&f.P(2).RewardPoints==peerPoints-lineage.Steps[0].Price,"independent same-school purchases from one shared revision conflicted");
                long afterBoth=f.A.Run.Revision;peerPoints=f.P(2).RewardPoints;
                Require(!f.Send(2,concurrent,true).IsSuccess&&f.A.Run.Revision==afterBoth&&f.P(2).RewardPoints==peerPoints,"duplicate network sequence charged twice");
                for(int rank=2;rank<=5;rank++)for(byte slot=1;slot<=2;slot++)f.Upgrade(slot,school);
                Require(f.P(1).Lineages.Single().LineageId.Value==school&&f.P(2).Lineages.Single().LineageId.Value==school,"same school rejected across peers");
                for(byte slot=1;slot<=2;slot++)foreach(var other in SectRegistry.Sects.Keys.Where(s=>s!=school))
                {long rev=f.A.Run.Revision;int points=f.P(slot).RewardPoints;Require(!f.Send(slot,RunCommand.PurchaseOrUpgradeLineage(new PlayerSlotId(slot),new StableId(other),rev)).IsSuccess&&f.A.Run.Revision==rev&&f.P(slot).RewardPoints==points,"per-player school exclusivity not atomic");}
                if(school!=FiveElementRules.Lineage)
                    for(byte slot=1;slot<=2;slot++)foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school&&!f.P(slot).HasPurchasedShelfAbility(PackId(p))))Require(f.Send(slot,RunCommand.PurchaseShelfAbility(new PlayerSlotId(slot),PackId(pack),f.A.Run.Revision)).IsSuccess,"network own school pack");
                else Require(FiveDeckRules.Unlocked(f.P(1)).Count==16&&FiveDeckRules.Unlocked(f.P(2)).Count==16&&!FiveDeckRules.Unlocked(f.P(1)).SequenceEqual(FiveDeckRules.Unlocked(f.P(2))),"same-school peers do not retain independent learned choices");
                StartMulti(f,2);
                var card=school==FiveElementRules.Lineage?FiveDeckRules.CardId(FiveDeckRules.Candidates(1).First(c=>(string)c["name"]=="水灵印")):school==SwordRules.Lineage?Card(1):SchoolSource(school,4000003);
                if(school!=FiveElementRules.Lineage){f.Prepare(1,card);f.Prepare(2,card);}
                else
                {
                    // Both have the starter seal; use genuine native draws, no fixture ownership edits.
                    for(byte slot=1;slot<=2;slot++)
                    {f.System(slot,RunDeveloperCommand.SetVitals,int.MinValue,f.Actor(slot).MaximumMagic,1000);for(int n=0;n<160&&!f.Table(slot).Hand.Any(h=>h.CardId==card);n++){if(f.Table(slot).Hand.Count>=10)Require(f.Action(slot,CardTableRules.DiscardActionId,CardTableRules.EncodePlay(f.Table(slot).Hand.First().InstanceId)).IsSuccess,"draw setup discard");Require(f.Action(slot,CardTableRules.DrawActionId).IsSuccess,"draw setup");}}
                }
                var peerHand=f.Table(2).Hand.Select(h=>h.InstanceId).ToArray();var peerState=SchoolStateFor(f,2,school);
                var foreign=f.Table(1).Hand.First(h=>h.CardId==card).InstanceId;long before=f.A.Run.Revision;
                Require(!f.Action(2,card,CardTableRules.EncodePlay(foreign),school==FiveElementRules.Lineage?default:f.B(2).Battle.Combatants.First(u=>u.Side==CombatSide.Enemy).RuntimeId).IsSuccess&&f.A.Run.Revision==before,"client can spend another peer's same-named hand card");
                f.Play(1,card);Require(f.Table(2).Hand.Select(h=>h.InstanceId).SequenceEqual(peerHand)&&JToken.DeepEquals(peerState,SchoolStateFor(f,2,school)),"playing same school card changed peer hand/state");f.Play(2,card);
                if(school==FiveElementRules.Lineage)
                {
                    var old=FiveDeckRules.Cards(f.P(2)).ToArray();var library=FiveDeckRules.Unlocked(f.P(2)).ToArray();var ownHand=f.Table(1).Hand.Select(h=>h.InstanceId).ToArray();
                    Require(f.Action(1,CardTableRules.SetPacksActionId,FiveDeckRules.EncodeSelection(FiveDeckRules.Unlocked(f.P(1)).Skip(8).ToArray())).IsSuccess,"network eight-card payload");
                    Require(FiveDeckRules.Cards(f.P(2)).SequenceEqual(old)&&FiveDeckRules.Unlocked(f.P(2)).SequenceEqual(library)&&f.Table(1).Hand.Select(h=>h.InstanceId).SequenceEqual(ownHand),"network reconfigure crossed player boundary");
                    var hostDeck=FiveDeckRules.Cards(f.P(1)).ToArray();var clientDeck=library.Skip(8).ToArray();
                    Require(f.Action(2,CardTableRules.SetPacksActionId,FiveDeckRules.EncodeSelection(clientDeck)).IsSuccess&&FiveDeckRules.Cards(f.P(1)).SequenceEqual(hostDeck),"remote admitted slot cannot independently change eight cards");
                    var notOwned=FiveDeckRules.Unlocked(f.P(1)).First(id=>!library.Contains(id));var injected=clientDeck.Take(7).Append(notOwned).ToArray();long protectedRevision=f.A.Run.Revision;
                    Require(!f.Action(2,CardTableRules.SetPacksActionId,FiveDeckRules.EncodeSelection(injected)).IsSuccess&&f.A.Run.Revision==protectedRevision&&FiveDeckRules.Cards(f.P(2)).SequenceEqual(clientDeck),"network client injected another same-school peer's unlearned card");
                }
                var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("same-school-network",data.CatalogVersion,data.GameplayContentHash,SessionMode.Host),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
                Require(mapper.TryCapture(f.A.Run,out var saved,out var error),"network save capture: "+error);Require(mapper.TryRestore(saved,out var restored,out error),"network save restore: "+error);
                Require(restored.Players.Count==2&&restored.Players.All(p=>p.Lineages.Single().LineageId.Value==school),"same-school roster lost on save");
                if(school==FiveElementRules.Lineage)for(byte slot=1;slot<=2;slot++)Require(FiveDeckRules.Cards(restored.Players[slot-1]).SequenceEqual(FiveDeckRules.Cards(f.P(slot)))&&FiveDeckRules.Unlocked(restored.Players[slot-1]).SequenceEqual(FiveDeckRules.Unlocked(f.P(slot))),"restored peer library/deck mixed");
                return new JObject{["school"]=school,["players"]=2,["same_school_all_five_ranks"]=true,["mirror_command_roundtrip"]=true,["cross_hand_rejected"]=true,["state_and_hand_isolation"]=true,["host_save_restore"]=true,["concurrent_same_school_purchases"]=true,["duplicate_network_sequence_rejected"]=true};
        }
        private int MultiQuote(MultiFixture f,byte slot,StableId card)
        {
            var snap=Reincarnation.Domain.RunAuthoritySnapshotProjector.Create(f.A,new PlayerSlotId(slot));
            var model=Reincarnation.UI.Flow.BattleUIPresentationMapper.BuildCardTable(snap,snap.Players.Single(p=>p.Slot.Value==slot),data,battles);
            return model.Hand.First(h=>h.CardId==card.Value).Action.CommandCost.Energy;
        }
        private JObject MixedSchoolTeam()
        {
            var f=NewMulti(3);var schools=new[]{SwordRules.Lineage,FiveElementRules.Lineage,SevenStarRules.Lineage};
            for(int rank=1;rank<=5;rank++)for(byte slot=1;slot<=3;slot++)f.Upgrade(slot,schools[slot-1]);
            foreach(byte slot in new byte[]{1,3})foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==schools[slot-1]&&!f.P(slot).HasPurchasedShelfAbility(PackId(p))))Require(f.Send(slot,RunCommand.PurchaseShelfAbility(new PlayerSlotId(slot),PackId(pack),f.A.Run.Revision)).IsSuccess,"mixed team purchase");
            StartMulti(f,3);
            for(byte slot=1;slot<=3;slot++){var a=f.Actor(slot);if(a.CurrentHealth!=a.CurrentMaximumHealth||a.Shield!=0)Require(a.SetVitals(a.CurrentMaximumHealth,0).IsSuccess,"full allied baseline");}
            var guard=FiveDeckRules.CardId(SectRegistry.Cards.Values.Single(c=>(string)c["name"]=="御空剑阵"));
            var gain=SchoolSource(SevenStarRules.Lineage,4000015);var group=SchoolSource(SevenStarRules.Lineage,4000040);
            f.Prepare(1,guard);Require(MultiQuote(f,1,guard)==4,"shieldless sword quote must cost four");
            f.Prepare(3,gain);f.Play(3,gain);f.Prepare(3,gain);f.Play(3,gain);f.Prepare(3,group);
            int omen=SectRegistry.Read((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B(3)),SectRegistry.StateKey(SevenStarRules.Lineage,f.Actor(3).RuntimeId,"omen"));Require(omen==4,"mixed team arrange four omen");
            var before=f.B(1).Battle.Combatants.Where(u=>u.Kind==CombatantKind.Player).ToDictionary(u=>u.RuntimeId,u=>u.Shield);
            int healing=SectRegistry.Read((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B(3)),SectRegistry.StateKey(SevenStarRules.Lineage,f.Actor(3).RuntimeId,"healingTotal"));
            f.Play(3,group);
            for(byte slot=1;slot<=3;slot++)Require(f.Actor(slot).Shield==before[f.Actor(slot).RuntimeId]+12,"star support did not shield all three real school actors");
            int afterHealing=SectRegistry.Read((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B(3)),SectRegistry.StateKey(SevenStarRules.Lineage,f.Actor(3).RuntimeId,"healingTotal"));Require(afterHealing-healing==36,"group generated healing must include three real recipients");
            Require(MultiQuote(f,1,guard)==0,"star shield not reflected in sword free-cast preview");int qi=f.Actor(1).CurrentMagic;var fiveBefore=SchoolStateFor(f,2,FiveElementRules.Lineage);var starBefore=SchoolStateFor(f,3,SevenStarRules.Lineage);
            f.Play(1,guard);Require(f.Actor(1).CurrentMagic==qi,"sword guard charged despite allied shield");Require(JToken.DeepEquals(fiveBefore,SchoolStateFor(f,2,FiveElementRules.Lineage))&&JToken.DeepEquals(starBefore,SchoolStateFor(f,3,SevenStarRules.Lineage)),"sword action mutated other schools' counters");
            var seal=FiveDeckRules.CardId(FiveDeckRules.Candidates(1).Single(c=>(string)c["name"]=="水灵印"));f.Prepare(2,seal);starBefore=SchoolStateFor(f,3,SevenStarRules.Lineage);f.Play(2,seal);Require(JToken.DeepEquals(starBefore,SchoolStateFor(f,3,SevenStarRules.Lineage)),"five seal counted as another actor's star action");
            return new JObject{["real_players"]=3,["real_card_tables"]=3,["star_shield_each"]=12,["generated_healing"]=36,["sword_quote_before"]=4,["sword_quote_after"]=0,["sword_actual_qi_paid"]=0,["cross_school_state_isolation"]=true};
        }
        private JObject EightSameSchool()
        {
            var f=NewMulti(8);for(int rank=1;rank<=2;rank++)for(byte slot=1;slot<=8;slot++)f.Upgrade(slot,FiveElementRules.Lineage);StartMulti(f,8);
            Require(Enumerable.Range(1,8).Select(i=>f.Actor((byte)i).RuntimeId).Distinct().Count()==8,"eight peers share an actor identity");
            for(byte slot=1;slot<=8;slot++)Require(FiveDeckRules.Unlocked(f.P(slot)).Count==10&&FiveDeckRules.Cards(f.P(slot)).Count==8&&f.Table(slot).Hand.Count==3,"eight-same-school private library/deck/table mismatch");
            return new JObject{["players"]=8,["same_five_school_purchase"]=true,["distinct_actors"]=8,["independent_libraries"]=8,["independent_card_tables"]=8,["mirror_commands"]=true};
        }
        private static JToken SchoolStateFor(MultiFixture f,byte slot,string school)
        {
            string prefix=SectRegistry.StateKey(school,f.Actor(slot).RuntimeId,"");var state=(ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B(slot));
            return JToken.FromObject(state.Entries.Where(e=>e.PackageId==SectRegistry.Package&&e.Key.StartsWith(prefix,StringComparison.Ordinal)));
        }
    }
}
