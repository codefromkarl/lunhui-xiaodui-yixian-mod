using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using Reincarnation.Runtime.Content;
using Reincarnation.Runtime.Core;
using Reincarnation.Runtime.Managers;
using Reincarnation.Runtime.Sessions;
using Reincarnation.UI.Flow;
using UnityEngine;

namespace YixianSect
{
    /// <summary>Opt-in, in-process integration tests. Never installed as the live RunManager authority.</summary>
    public sealed partial class RuntimeSelfTests : MonoBehaviour
    {
        private ICodeModContext context;
        private bool started;
        private string path;
        private readonly JArray cases = new JArray();
        private readonly JArray diagnostics = new JArray();
        private JObject report;
        private DataManager data;
        private RunAuthorityCatalog worlds;
        private BattleAuthorityCatalog battles;
        private int fixtureNumber;
        private static readonly PlayerSlotId Slot = new PlayerSlotId(1);
        private static readonly StableId Sect = new StableId(SwordRules.Lineage);

        internal static void InstallIfRequested(ICodeModContext context)
        {
            if(!Environment.GetCommandLineArgs().Any(x=>x=="-yixian-selftest"||x=="-yixian-schools-selftest"))return;
            var go=new GameObject("YixianRuntimeSelfTests");
            var runner=go.AddComponent<RuntimeSelfTests>();runner.context=context;
            runner.path=Path.Combine(context.PackageDirectory,"TestResults","runtime-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json");
            UnityEngine.Object.DontDestroyOnLoad(go);
            context.Log("[SelfTest] 已安排游戏内隔离测试；不会绑定存档主机。输出："+runner.path);
        }
        private void Update()
        {
            if(started||context==null||!GameManager.TryGetInstance(out var game)||game.Data==null||!game.Data.IsInitialized)return;
            started=true;data=game.Data;
            var live=game.Run?.Authority;long? liveRevision=live?.Run.Revision;
            var saves=SaveInventory();
            report=new JObject { ["runtime"]=Type.GetType("Mono.Runtime")!=null?"Mono":"other",["unity"]=Application.unityVersion,["startedUtc"]=DateTime.UtcNow.ToString("O"),["status"]="running",["modVersion"]=typeof(Entry).Assembly.GetName().Version.ToString(),["cases"]=cases };
            Write();
            Action<string> observe=line=>{if((line.Contains("[Warning]")||line.Contains("[Error]"))&&!line.Contains("[SelfTest]"))diagnostics.Add(line);};
            ModLog.LineWritten+=observe;
            try
            {
                Require(RunAuthorityCatalogFactory.TryCreate(data,out worlds,out var worldError),"world catalog: "+worldError);
                Require(BattleAuthorityCatalogFactory.TryCreate(data,out battles,out var battleError),"battle catalog: "+battleError);
                if(Environment.GetCommandLineArgs().Contains("-yixian-visual-selftest"))
                {
                    report["scope"]="hand-layout-and-cost";
                    Case("opening_hand_hud_card_tab",OpeningHandHud);
                    Case("printed_and_discounted_card_costs",PrintedCardCosts);
                }
                else if(Environment.GetCommandLineArgs().Contains("-yixian-costs-selftest"))
                {
                    report["scope"]="original-face-costs";
                    Case("five_original_face_costs",()=>SchoolFaceCosts(FiveElementRules.Lineage));
                    Case("star_original_face_costs",()=>SchoolFaceCosts(SevenStarRules.Lineage));
                    Case("water_wave_conditional_free",WaterWaveConditionalFree);
                }
                else
                {
                bool schoolsOnly=Environment.GetCommandLineArgs().Contains("-yixian-schools-selftest");
                report["scope"]=schoolsOnly?"schools":"full";
                if(!schoolsOnly)
                {
                Case("catalog_and_descriptions",Catalog);
                Case("progression_real_commands",Progression);
                Case("sword_build_selection_and_save",SwordBuildSelection);
                Case("sword_route_benchmarks",SwordRouteBenchmarks);
                Case("insufficient_points_atomic_rejection",Insufficient);
                Case("original_BL08_regression",Native);
                Case("draw_and_card_cost",DrawAndCost);
                Case("opening_hand_hud_card_tab",OpeningHandHud);
                Case("printed_and_discounted_card_costs",PrintedCardCosts);
                Case("cloud_sequence_real_damage",Cloud);
                Case("sword_intent_multihit_real_damage",Intent);
                Case("flying_fang_hit_returns_intent",FlyingFang);
                Case("shield_and_energy_conversion",Conversion);
                Case("react_draws_each_trigger",Again);
                Case("foundation_choice_atomicity",FoundationChoice);
                Case("effective_attribute_growth",AttributeGrowth);
                Case("shop_choice_and_pack_detail_consumption",Details);
                Case("actual_prefab_visuals",Visual);
                Case("real_health_hit_vs_shield",HealthHit);
                Case("all_cards_native_execution",AllCards);
                }
                Case("five_element_real_command_loop",FiveElementLoop);
                Case("seven_star_real_command_loop",SevenStarLoop);
                Case("five_full_content_and_fates",()=>ExpandedSchool(FiveElementRules.Lineage));
                Case("star_full_content_and_fates",()=>ExpandedSchool(SevenStarRules.Lineage));
                if(!schoolsOnly)
                {
                    Case("single_school_purchase_gate",SingleSchoolGate);
                    Case("native_health_replay_guard_stress",NativeCrashBoundaryStress);
                    Case("native_opening_state_reset",NativeOpeningReset);
                }
                }
            }
            catch(Exception ex){cases.Add(new JObject{["name"]="suite_setup",["status"]="failed",["error"]=ex.ToString()});}
            ModLog.LineWritten-=observe;
            cases.Add(new JObject{["name"]="runtime_diagnostics_clean",["status"]=diagnostics.Count==0?"passed":"failed",["warnings_and_errors"]=diagnostics});
            bool same=ReferenceEquals(live,game.Run?.Authority)&&(!liveRevision.HasValue||live.Run.Revision==liveRevision.Value);
            bool unchanged=JToken.DeepEquals(saves,SaveInventory());
            cases.Add(new JObject{["name"]="user_state_isolation",["status"]=same&&unchanged?"passed":"failed",["live_authority_unchanged"]=same,["save_inventory_unchanged"]=unchanged});
            report["completedUtc"]=DateTime.UtcNow.ToString("O");
            report["passed"]=cases.OfType<JObject>().Count(c=>(string)c["status"]=="passed");
            report["failed"]=cases.OfType<JObject>().Count(c=>(string)c["status"]=="failed");
            report["status"]=(int)report["failed"]==0?"passed":"failed";
            Write();context.Log("[SelfTest] 完成："+report["passed"]+" 通过 / "+report["failed"]+" 失败；"+path);
            SelfTestShutdown.Complete("runtime",path);
            UnityEngine.Object.Destroy(gameObject);
        }
        private static JObject SaveInventory()
        {
            var root=Path.Combine(Application.persistentDataPath,"Reincarnation","Saves","Runs");var result=new JObject();
            if(Directory.Exists(root))foreach(var file in Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(x=>x))
            {var f=new FileInfo(file);result[file.Substring(root.Length)]=f.Length+":"+f.LastWriteTimeUtc.Ticks;}
            return result;
        }
        private void Write(){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,report.ToString(Formatting.Indented));}
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        private void Case(string name,Func<JObject> test)
        {
            var sw=Stopwatch.StartNew();var row=new JObject{["name"]=name,["status"]="running"};
            cases.Add(row);report["activeCase"]=name;Write();
            try{row["evidence"]=test();row["status"]="passed";}
            catch(Exception ex){row["status"]="failed";row["error"]=ex.ToString();context.Warn("[SelfTest] "+name+"："+ex.Message);}
            row["milliseconds"]=sw.ElapsedMilliseconds;report.Remove("activeCase");Write();
        }
        private sealed class Fixture
        {
            public RunAuthorityService A;
            public long Now=1000;
            public BattleAuthorityCatalog Catalog;
            public EconomyCatalog Economy;
            public PlayerRunState P=>A.Run.Players[0];
            public BattleAuthorityService B {get {Require(A.TryGetPlayerBattle(Slot,out var b),"no player battlefield");return b;}}
            public CardTableSnapshot Table=>B.CaptureCardTable(Slot);
            public CombatantState Actor {get {Require(B.TryGetPlayerActorId(Slot,out var id),"actor id unavailable");Require(B.Battle.TryGetCombatant(id,out var actor),"actor unavailable");return actor;}}
            public CombatantState Enemy=>B.Battle.Combatants.First(x=>x.Side==CombatSide.Enemy&&!x.IsDowned);
            public RunCommandDispatchResult Send(RunCommand command)=>A.Ledger.ExecuteCommand(command);
            public void System(RunDeveloperCommand kind,params int[] numbers)
            {
                if(kind==RunDeveloperCommand.SetVitals && numbers.Length==3)
                {
                    var actor=Actor;
                    bool same=(numbers[0]==int.MinValue || numbers[0]==actor.CurrentHealth) &&
                        (numbers[1]==int.MinValue || numbers[1]==actor.CurrentMagic) &&
                        (numbers[2]==int.MinValue || numbers[2]==actor.ActionPoints);
                    if(same)return; // Native developer command rejects no-ops; fixture setup is idempotent.
                }
                var result=A.Ledger.ExecuteSystem(RunSystemAction.Developer(kind,Slot,default(StableId),numbers));Require(result.IsSuccess,"developer "+kind+": "+result.ErrorCode);
            }
            public void Buy(StableId id)
            {var r=Send(RunCommand.PurchaseShelfAbility(Slot,id,A.Run.Revision));Require(r.IsSuccess,"buy "+id+": "+r.ErrorCode);}
            public void Upgrade(StableId lineage)
            {int rank=P.Lineages.Where(l=>l.LineageId==lineage).Select(l=>l.Rank).DefaultIfEmpty().Max()+1;
                var answer=LineageFreeAbilityPicks.ComputeCandidates(Economy.ShelfAbilities,A.Run.Seed,Slot,lineage,rank,P.PurchasedShelfAbilityIds).FirstOrDefault();
                var r=Send(RunCommand.PurchaseOrUpgradeLineage(Slot,lineage,A.Run.Revision,answer));Require(r.IsSuccess,"lineage "+lineage+": "+r.ErrorCode);}
            public void StartBattle()
            {
                var enter=Send(RunCommand.EnterWorld(A.Run.Revision));Require(enter.IsSuccess,"enter world: "+enter.ErrorCode);
                System(RunDeveloperCommand.StartBattlefields,(int)NodeKind.Battle,1);
                Require(Table!=null,"missing card table");
            }
            public RunCommandDispatchResult Action(StableId id,StableId target=default(StableId),StableId parameter=default(StableId))
                =>Send(RunCommand.SubmitBattleAction(Slot,CombatActionKind.UseAbility,id,target,FormationRow.None,-1,A.Run.Revision,parameter));
            public BattleActionResultReceipt Play(StableId card)
            {
                var hand=Table.Hand.FirstOrDefault(h=>h.CardId==card);Require(hand!=null,"hand missing "+card);
                Require(Catalog.TryGetFirstAbilityAction(card,out var definition),"missing action definition");
                var target=definition.ActionSpec.TargetKind==CombatTargetKind.Self?default(StableId):Enemy.RuntimeId;
                var r=Action(card,target,CardTableRules.EncodePlay(hand.InstanceId));Require(r.IsSuccess,"play "+card+": "+r.ErrorCode);Require(r.HasBattleActionReceipt,"card action receipt missing");return r.BattleActionReceipt;
            }
            public void Prepare(params StableId[] wanted)
            {
                var packs=TestCardPool.ForCards(P,wanted);
                if(!P.ActiveCardPackIds.SequenceEqual(packs))Require(Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(packs)).IsSuccess,"fixture equip wanted packs");
                System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,1000);
                for(int i=0;i<160;i++)
                {
                    if(wanted.All(id=>Table.Hand.Any(h=>h.CardId==id)))return;
                    if(Table.Hand.Count>=10)
                    {
                        var discard=Table.Hand.FirstOrDefault(h=>!wanted.Contains(h.CardId)||Table.Hand.Count(x=>x.CardId==h.CardId)>1);
                        Require(discard!=null,"hand full with required cards");
                        var drop=Action(CardTableRules.DiscardActionId,parameter:CardTableRules.EncodePlay(discard.InstanceId));Require(drop.IsSuccess,"discard "+drop.ErrorCode);
                    }
                    var result=Action(CardTableRules.DrawActionId);Require(result.IsSuccess,"draw "+result.ErrorCode);
                }
                throw new InvalidOperationException("fixed-seed draw budget exceeded");
            }
            public int State(string key)=>SwordRules.ReadInt((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(B),Actor.RuntimeId,key);
            public void NextRound()
            {
                int round=B.Battle.Round;Require(Send(RunCommand.EndBattleTurn(Slot,A.Run.Revision)).IsSuccess,"end turn");
                Require(A.TryGetPlayerBattlefieldId(Slot,out var field),"field id");
                for(int i=0;i<100&&(B.Battle.Round==round||B.Battle.Phase!=BattlePhase.Player);i++)
                {Now+=5000;Require(A.Ledger.ExecuteSystem(RunSystemAction.TickBattlefield(field)).IsSuccess,"tick field");}
                Require(!B.IsComplete&&B.Battle.Round==round+1&&B.Battle.Phase==BattlePhase.Player,"next round unavailable");
            }
        }
        private Fixture New(int rank=1,bool native=false,bool buyCurrent=true,ulong seed=73001,string school=null)
        {
            var factory=new SoloRunFactory(data,data);
            var selection=new BirthSelection(Slot,"弈仙自测",BirthAppearance.Default,data.Origins[0].StableId,data.Experiences[0].StableId,data.EntryReasons[0].StableId);
            var created=factory.TryCreate(selection,new StableId("yixian.selftest.run."+(++fixtureNumber)),seed);
            Require(created.TryGetValue(out var creation),"birth fixture: "+created.ErrorCode);
            var rewards=new AuthorityRewardGenerationContext(data.ContentOperationDispatcher,data.RewardTables,data.EconomyCatalog,ContentExecutionAuthorityScope.LocalSinglePlayer);
            var f=new Fixture{Catalog=battles,Economy=data.EconomyCatalog};
            var authority=new RunAuthorityService(creation.Run,worlds,data.EconomyCatalog,data.EventContentCatalog,rewards,battles,()=>f.Now);
            var init=authority.Initialize();Require(init.IsSuccess,"initialize "+init.ErrorCode);
            f.A=authority;
            f.System(RunDeveloperCommand.JumpToWorld,2);
            f.System(RunDeveloperCommand.AddRewardPoints,10000);
            var id=native?CardTableCatalog.LineageId:new StableId(school??SwordRules.Lineage);
            for(int i=0;i<rank;i++)f.Upgrade(id);
            if(buyCurrent&&!native)foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==id.Value&&SwordRules.Int(p,"rank")==rank&&SwordRules.Int(p,"price")>0&&!f.P.HasPurchasedShelfAbility(PackId(p))))f.Buy(PackId(pack));
            return f;
        }
        private static StableId PackId(JObject pack)=>SectRegistry.PackId(pack);
        private static StableId Card(int n)=>new StableId(SwordRules.Cards.Single(c=>SwordRules.Text(c.Value,"sourceId")=="100"+n.ToString("D4")).Key);
        private JObject Catalog()
        {
            Require(CardTableCatalog.Cards.Count==66+SectRegistry.Cards.Count&&CardTableCatalog.Packs.Count==12+SectRegistry.Packs.Count,"catalog counts");
            foreach(int index in Enumerable.Range(40,10))Require(CardTableCatalog.Packs[12+index-40].ShelfIndex==index,"legacy cp ordinal changed: "+index);
            Require(data.ModBattlePlanner!=null,"mod planner unavailable");
            foreach(var id in SectRegistry.Cards.Keys)Require(data.ModBattlePlanner.HasActionScript(id),"handler missing "+id);
            foreach(var rank in Enumerable.Range(1,5))
            {
                Require(data.TryGetLineage(Sect,out var lineage),"lineage content missing");
                var detail=ProgressionPassiveNarrator.DescribeUpgradeDetails(lineage.Steps[rank-1].Passive,null,true);
                foreach(var pack in SwordRules.Packs.Where(p=>SwordRules.Int(p,"rank")==rank))
                {
                    Require(detail.Contains(SwordRules.Text(pack,"name")),"pack description missing");
                    foreach(var key in ((JArray)pack["cards"]).Values<string>())Require(detail.Contains(SwordRules.Text(SwordRules.Cards[SwordRules.CardPrefix+key],"name")),"card name description missing");
                }
            }
            return new JObject{["cards"]=CardTableCatalog.Cards.Count,["packs"]=CardTableCatalog.Packs.Count,["registered_actions"]=SectRegistry.Cards.Count,["description_ranks"]=5};
        }
        private JObject Progression()
        {
            var checks=new JArray();
            for(int rank=1;rank<=5;rank++)
            {
                var f=New(rank,buyCurrent:false);
                foreach(var pack in SwordRules.Packs.Where(p=>SwordRules.Int(p,"rank")==1))
                    Require(f.P.PurchasedShelfAbilityIds.Count(id=>id==PackId(pack))==1,"starter gift not unique");
                Require(SectProgression.Owned(f.P).Count==(rank==1?2:3),"unexpected later gift");
                var before=f.P.RewardPoints;
                foreach(var pack in SwordRules.Packs.Where(p=>SwordRules.Int(p,"price")>0))
                {
                    var id=PackId(pack);int points=f.P.RewardPoints;bool owned=f.P.HasPurchasedShelfAbility(id);
                    var result=f.Send(RunCommand.PurchaseShelfAbility(Slot,id,f.A.Run.Revision));
                    bool allowed=SwordRules.Int(pack,"rank")<=rank&&!owned;
                    Require(result.IsSuccess==allowed,"rank/duplicate gate "+pack["name"]+": "+result.ErrorCode);
                    Require(f.P.RewardPoints==points-(allowed?SwordRules.Int(pack,"price"):0),"price mismatch");
                    if(allowed){var duplicate=f.Send(RunCommand.PurchaseShelfAbility(Slot,id,f.A.Run.Revision));Require(!duplicate.IsSuccess&&f.P.RewardPoints==points-SwordRules.Int(pack,"price"),"duplicate charged");}
                }
                Require(SectProgression.Owned(f.P).Count==SwordRules.Packs.Count(p=>SwordRules.Int(p,"rank")<=rank),"owned count");
                Require(!f.P.HasPurchasedShelfAbility(CardTableCatalog.FusionPackId),"sect received fusion");
                Require(f.P.ActiveCardPackIds.Count<=8,"active pack cap");
                checks.Add(new JObject{["rank"]=rank,["owned"]=SectProgression.Owned(f.P).Count,["spent"]=before-f.P.RewardPoints});
            }
            return new JObject{["ranks"]=checks};
        }
        private JObject FoundationChoice()
        {
            var receipts=new JArray();
            foreach(var choice in SwordRules.Packs.Where(p=>SwordRules.Int(p,"rank")==2))
            {
                var f=New(buyCurrent:false);
                var candidates=LineageFreeAbilityPicks.ComputeCandidates(data.EconomyCatalog.ShelfAbilities,f.A.Run.Seed,Slot,Sect,2,f.P.PurchasedShelfAbilityIds);
                Require(candidates.Count==2&&candidates.Contains(PackId(choice)),"two foundation candidates");
                foreach(var wrong in new[]{default(StableId),new StableId("yixian.sect.pack.yixian_sword_cloud3"),new StableId("yixian.sect.pack.yixian_sword")})
                {
                    int points=f.P.RewardPoints;long revision=f.A.Run.Revision;long pr=f.P.Revision;
                    var r=f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,Sect,revision,wrong));
                    Require(!r.IsSuccess&&r.ErrorCode==SessionErrorCode.InvalidRequest,"invalid foundation answer accepted");
                    Require(f.P.RewardPoints==points&&f.A.Run.Revision==revision&&f.P.Revision==pr&&SectProgression.Rank(f.P)==1,"invalid answer mutated state");
                }
                data.TryGetLineage(Sect,out var line);int cost=line.Steps[1].Price;int before=f.P.RewardPoints;
                var ok=f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,Sect,f.A.Run.Revision,PackId(choice)));
                Require(ok.IsSuccess&&f.P.RewardPoints==before-cost&&SectProgression.Owned(f.P).Count==3,"foundation choice not free/unique");
                var next=LineageFreeAbilityPicks.ComputeCandidates(data.EconomyCatalog.ShelfAbilities,7,Slot,Sect,3,f.P.PurchasedShelfAbilityIds);
                Require(next.Count==0,"later candidates present");
                f.Upgrade(Sect);Require(SectProgression.Owned(f.P).Count==3,"later upgrade gifted pack");
                receipts.Add(new JObject{["choice"]=choice["name"],["charged_only_breakthrough"]=cost,["owned_after_golden_core"]=3});
            }
            return new JObject{["choices"]=receipts,["invalid_answers_atomic"]=true};
        }
        private JObject Insufficient()
        {
            var f=New(2,buyCurrent:false);
            Require(f.P.SpendRewardPoints(f.P.RewardPoints).IsSuccess,"arrange zero points");
            var id=PackId(SwordRules.Packs.First(p=>SwordRules.Int(p,"rank")==2&&!f.P.HasPurchasedShelfAbility(PackId(p))));
            long playerRevision=f.P.Revision,runRevision=f.A.Run.Revision;
            var result=f.Send(RunCommand.PurchaseShelfAbility(Slot,id,runRevision));
            Require(!result.IsSuccess&&result.ErrorCode==SessionErrorCode.InsufficientRewardPoints,"insufficient points not refused");
            Require(f.P.RewardPoints==0&&!f.P.HasPurchasedShelfAbility(id)&&f.P.Revision==playerRevision&&f.A.Run.Revision==runRevision,"failed purchase mutated state");
            return new JObject{["error"]=result.ErrorCode.ToString(),["state_unchanged"]=true};
        }
        private JObject Native()
        {
            var f=New(3,native:true,buyCurrent:false);
            Require(f.P.HasPurchasedShelfAbility(CardTableCatalog.FusionPackId),"native fusion lost");
            Require(!SwordRules.Packs.Any(p=>f.P.HasPurchasedShelfAbility(PackId(p))),"native got sect gift");
            var candidates=LineageFreeAbilityPicks.ComputeCandidates(data.EconomyCatalog.ShelfAbilities,7,Slot,CardTableCatalog.LineageId,1,Array.Empty<StableId>());
            Require(candidates.Count==3&&!candidates.Any(id=>SwordRules.Packs.Any(p=>PackId(p)==id)),"native free picks polluted");
            return new JObject{["native_fusion"]=true,["rank1_free_picks"]=candidates.Count};
        }
        private JObject DrawAndCost()
        {
            var f=New();f.StartBattle();Require(f.Table.Hand.Count==3,"initial hand not 3");
            Require(f.Table.Hand.All(h=>h.CardId.Value!="ability.lineage.BL08.card.send_to_grave"),"grave token inherited");
            f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            int openingQi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
            Require(f.Action(CardTableRules.DrawActionId).IsSuccess,"manual draw");
            Require(f.Table.Hand.Count==4&&f.Actor.ActionPoints==2&&f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==openingQi,"draw must cost exactly 1 AP and zero qi");
            f.Prepare(Card(1));f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,0);
            var enemy=f.Enemy;ToughEnemy(enemy);int hp=enemy.CurrentHealth;int qi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
            var hand=f.Table.Hand.First(h=>h.CardId==Card(1)).InstanceId;f.Play(Card(1));
            Require(!f.Table.Hand.Any(h=>h.InstanceId==hand)&&f.Actor.ActionPoints==0,"card should play at zero AP");
            Require(enemy.CurrentHealth<hp&&f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==qi,"free card must not consume qi");
            int count=f.Table.Hand.Count;
            Require(!f.Action(CardTableRules.DrawActionId).IsSuccess&&f.Table.Hand.Count==count,"draw at zero AP should fail");
            f.Prepare(Card(1));f.System(RunDeveloperCommand.SetVitals,int.MinValue,0,0);
            f.Play(Card(1));Require(f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==0,"free card blocked or charged at zero qi");
            var paid=New();paid.StartBattle();ToughEnemy(paid.Enemy);paid.Prepare(Card(21));paid.System(RunDeveloperCommand.SetVitals,int.MinValue,3,0);
            var rejected=paid.Table.Hand.First(h=>h.CardId==Card(21));long revision=paid.A.Run.Revision;
            var fail=paid.Action(Card(21),paid.Enemy.RuntimeId,CardTableRules.EncodePlay(rejected.InstanceId));
            Require(!fail.IsSuccess&&fail.ErrorCode==SessionErrorCode.InsufficientMagicPoints,"insufficient qi should reject: "+fail.ErrorCode);
            Require(paid.Table.Hand.Any(h=>h.InstanceId==rejected.InstanceId)&&paid.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==3&&paid.A.Run.Revision==revision,"rejected card mutated state");
            Require(f.Actor.SetVitals(f.Actor.CurrentHealth,100000).IsSuccess,"protect isolated actor");
            var remaining=f.Table.Hand.Select(h=>h.InstanceId).ToArray();f.NextRound();
            Require(f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(remaining),"later round auto-drew/reset the hand");
            return new JObject{["initial_hand"]=3,["manual_draw_ap"]=1,["manual_draw_qi"]=0,["zero_ap_card_played"]=true,["card_qi_cost"]=0,["free_at_zero_qi"]=true,["paid_card_required_qi"]=4,["insufficient_qi_error"]=fail.ErrorCode.ToString(),["second_round_hand_unchanged"]=true,["damage"]=hp-enemy.CurrentHealth};
        }
        private JObject PrintedCardCosts()
        {
            var rows=new JArray();
            // All nine paid sword cards, plus zero-fee controls. Values are from
            // original card-face corners, not copied from generated declarations.
            foreach(var item in new[]{new[]{1,0},new[]{32,0},new[]{9,4},new[]{21,4},new[]{29,8},new[]{36,12},new[]{59,4},new[]{103,8},new[]{25,4},new[]{98,4},new[]{107,4}})
            {
                var f=New(5);var card=Card(item[0]);
                var pack=SwordRules.Packs.Single(p=>((JArray)p["cards"]).Values<string>().Any(k=>SwordRules.CardPrefix+k==card.Value));
                if(!f.P.HasPurchasedShelfAbility(PackId(pack)))f.Buy(PackId(pack));
                f.StartBattle();ToughEnemy(f.Enemy);f.Prepare(card);
                int before=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);long revision=f.A.Run.Revision;
                var hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();
                var economies=(RunPlayerActionEconomySnapshot[])AccessTools.Method(typeof(BattleAuthorityService),"CreatePlayerActionEconomySnapshots").Invoke(f.B,new object[]{new[]{Slot}});
                var costs=economies.Single(e=>e.PlayerSlot==Slot).ResolvedActionCosts;
                var preview=costs.Single(c=>c.ActionId==card);
                Require(preview.BaseEnergyAmount+preview.SurchargeAmount==item[1],"preview cost disagrees: "+card);
                var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
                var model=BattleUIPresentationMapper.BuildCardTable(snapshot,snapshot.Players.First(p=>p.Slot==Slot),data,battles);
                var displayed=model.Hand.First(h=>h.CardId==card.Value).Action;
                Require(displayed.CommandCost!=null&&displayed.CommandCost.Energy==item[1],"HUD cost badge disagrees: "+card);
                Require(BattleUIPresentationMapper.IsCardTableCommand(displayed),"paid card routed to ability tab: "+card);
                Require(f.A.Run.Revision==revision&&f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand)&&f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==before,"cost preview mutated battle");
                f.Play(card);int spent=before-f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                Require(spent==item[1],"actual payment disagrees: "+card+" spent "+spent);
                rows.Add(new JObject{["card"]=card.Value,["original_lingqi"]=item[1]/4,["preview_qi"]=preview.BaseEnergyAmount,["hud_qi"]=displayed.CommandCost.Energy,["qi_before"]=before,["qi_after"]=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting),["actual_qi"]=spent});
            }
            var discounted=New(4);discounted.StartBattle();ToughEnemy(discounted.Enemy);discounted.Prepare(Card(36),Card(65));
            discounted.Play(Card(65));int qi=discounted.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
            discounted.Play(Card(36));Require(qi-discounted.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==8,"three-lingqi spirit discount must retain two lingqi");
            return new JObject{["tiers"]=rows,["spirit_discount_12_to_8"]=true,["flying_fang_requires_four_qi"]=true};
        }
        private JObject OpeningHandHud()
        {
            var rows=new JArray();
            foreach(var school in new[]{SwordRules.Lineage,FiveElementRules.Lineage,SevenStarRules.Lineage})
            {
                var f=New(school:school);f.StartBattle();
                var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
                var player=snapshot.Players.First(p=>p.Slot==Slot);
                var model=BattleUIPresentationMapper.BuildCardTable(snapshot,player,data,battles);
                var actions=BattleUIPresentationMapper.BuildActions(snapshot,player,data,battles,model);
                Require(model.IsVisible&&model.Hand.Count==3,"opening hand not visible as three cards: "+school);
                foreach(var held in model.Hand)
                {
                    Require(BattleUIPresentationMapper.IsCardTableCommand(held.Action),"opening card classified as an ability: "+held.CardId);
                    Require(!BattleUIPresentationMapper.IsCustomBarCommand(held.Action),"opening card allowed in custom ability bar");
                    Require(actions.Count(a=>a.StableId==held.Action.StableId)==1,"hand instance missing or duplicated");
                }
                var directory=Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path),school);
                var opening=VisualSelfTests.OpeningHandHud(model,actions,school,directory);
                f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,10);
                while(f.Table.Hand.Count<10)Require(f.Action(CardTableRules.DrawActionId).IsSuccess,"fill hand through native draw command");
                var fullSnapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);var fullPlayer=fullSnapshot.Players.First(p=>p.Slot==Slot);
                var fullModel=BattleUIPresentationMapper.BuildCardTable(fullSnapshot,fullPlayer,data,battles);
                var fullActions=BattleUIPresentationMapper.BuildActions(fullSnapshot,fullPlayer,data,battles,fullModel);
                opening["full_hand"]=VisualSelfTests.OpeningHandHud(fullModel,fullActions,school,Path.Combine(directory,"full-hand"));
                rows.Add(opening);
            }
            return new JObject{["schools"]=rows};
        }
        private JObject Cloud()
        {
            var a=New(seed:73011);a.StartBattle();a.Prepare(Card(1),Card(2));a.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            Require(a.Enemy.SetVitals(a.Enemy.CurrentHealth,10000).IsSuccess,"arrange linked shield fixture");
            a.Play(Card(1));var linkedReceipt=a.Play(Card(2));int linked=linkedReceipt.Targets.Sum(t=>t.ResolvedDamage);
            var b=New(seed:73011);b.StartBattle();b.Prepare(Card(9),Card(2));b.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            Require(b.Enemy.SetVitals(b.Enemy.CurrentHealth,10000).IsSuccess,"arrange plain shield fixture");
            b.Play(Card(9));var plainReceipt=b.Play(Card(2));int plain=plainReceipt.Targets.Sum(t=>t.ResolvedDamage);
            Require(linked>plain&&a.State("cloud")==2,"cloud sequence: linked="+linked+", plain="+plain+", chain="+a.State("cloud"));
            return new JObject{["linked_resolved_damage"]=linked,["plain_resolved_damage"]=plain,["chain"]=a.State("cloud"),["comparison"]="same-seed shielded targets; one prior attack each; formal damage receipts (not overkill-clamped HP)"};
        }
        private JObject Intent()
        {
            var f=New(3,seed:73021);f.StartBattle();f.Prepare(Card(12),Card(32));f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            f.Play(Card(12));Require(f.State("intent")==2,"intent not gained");var target=f.Enemy;ToughEnemy(target);f.Play(Card(32));
            int charged=10000-target.CurrentHealth;Require(f.State("intent")==0,"intent not consumed");
            var plain=New(3,seed:73021);plain.StartBattle();plain.Prepare(Card(12),Card(32));plain.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            plain.Play(Card(12));var key=SwordRules.StateKey(plain.Actor.RuntimeId,"intent");
            var state=(ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(plain.B);
            AccessTools.Field(typeof(BattleAuthorityService),"modState").SetValue(plain.B,state.Apply("yixian.sect",new[]{new ModStateEntry("yixian.sect",key,ModValueKind.Integer,0)}));
            var baselineTarget=plain.Enemy;ToughEnemy(baselineTarget);plain.Play(Card(32));int baseline=10000-baselineTarget.CurrentHealth;
            Require(charged>baseline,"three-hit intent bonus absent");
            return new JObject{["charged_damage"]=charged,["plain_damage"]=baseline,["remaining_intent"]=f.State("intent"),["segments"]=3,["control"]="same draws/actions/RNG; only control intent reset in isolated fixture"};
        }
        private JObject FlyingFang()
        {
            int Return(bool shield)
            {
                var f=New(seed:73022);f.StartBattle();f.Prepare(Card(12),Card(21));f.Play(Card(12));Require(f.State("intent")==2,"fang setup");
                var enemy=f.Enemy;ToughEnemy(enemy);if(shield)Require(enemy.SetVitals(enemy.CurrentHealth,100000).IsSuccess,"fang shield setup");
                f.Play(Card(21));return f.State("intent");
            }
            int health=Return(false),shieldOnly=Return(true);
            Require(health==2&&shieldOnly==0,"fang hit condition: health="+health+", shield="+shieldOnly);
            return new JObject{["health_hit_returned_intent"]=health,["shield_only_returned_intent"]=shieldOnly};
        }
        private JObject Conversion()
        {
            var f=New(2,seed:73031);f.StartBattle();f.Prepare(Card(17),Card(33));f.System(RunDeveloperCommand.SetVitals,int.MinValue,2,3);
            f.Play(Card(17));Require(f.State("intent")==3,"condense intent failed");int before=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);f.Play(Card(33));
            Require(f.State("intent")==0&&f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)>before&&f.Actor.Shield>0,"intent/qi conversion failed");
            return new JObject{["energy_before"]=before,["energy_after"]=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting),["shield"]=f.Actor.Shield};
        }
        private JObject Again()
        {
            var f=New(5,seed:73041);f.StartBattle();f.Prepare(Card(25),Card(98));f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            if(f.Actor.Shield>0)Require(f.Actor.SetVitals(f.Actor.CurrentHealth,0).IsSuccess,"clear shield for paid react");
            var instances=f.Table.Hand.Select(x=>x.InstanceId).ToArray();long revision=f.A.Run.Revision;
            RunAuthoritySnapshotProjector.Create(f.A,Slot);RunAuthoritySnapshotProjector.Create(f.A,Slot);
            Require(f.Table.Hand.Select(x=>x.InstanceId).SequenceEqual(instances)&&f.A.Run.Revision==revision&&f.State("drawPending")==0,"preview committed a draw");
            int h=f.Table.Hand.Count;int qi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);f.Play(Card(25));int first=f.Table.Hand.Count;
            Require(f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==qi-4,"react card must pay only its one lingqi fee");
            f.Play(Card(98));
            Require(first==h&&f.Table.Hand.Count==h&&f.Actor.ActionPoints==3,"react did not draw per trigger / changed AP");
            Require(f.State("drawPending")==0,"draw queue not drained");
            return new JObject{["hand_before"]=h,["hand_after_first"]=first,["hand_after_second"]=f.Table.Hand.Count,["ap"]=f.Actor.ActionPoints,["first_card_qi_cost"]=4,["preview_did_not_draw"]=true};
        }
        private JObject AttributeGrowth()
        {
            var rows=new JArray();
            foreach(int amount in new[]{10,50,150})
            {
                var f=New(seed:73061);f.System(RunDeveloperCommand.SetAttribute,(int)ExchangeAttribute.Energy,amount);
                f.StartBattle();f.Prepare(Card(1));f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
                Require(f.Enemy.SetVitals(f.Enemy.CurrentHealth,10000).IsSuccess,"growth shield fixture");
                var receipt=f.Play(Card(1));int actual=receipt.Targets.Sum(t=>t.ResolvedDamage);
                rows.Add(new JObject{["requested_energy"]=amount,["effective_energy"]=f.P.DerivedStats.EffectiveAttributes.Energy,["resolved_damage"]=actual});
            }
            Require((int)rows[0]["resolved_damage"]<(int)rows[1]["resolved_damage"]&&(int)rows[1]["resolved_damage"]<(int)rows[2]["resolved_damage"],"damage not growing with effective energy");
            return new JObject{["series"]=rows};
        }
        private JObject Details()
        {
            var f=New(2,buyCurrent:false);var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);var player=snapshot.Players.First(p=>p.Slot==Slot);
            var shelf=RunUIPresentationMapper.BuildExchangeShelf(player,data,Sect,2);
            var checkedPacks=new JArray();
            foreach(var pack in SwordRules.Packs.Where(p=>SwordRules.Int(p,"rank")==2))
            {
                var id=PackId(pack);Require(data.TryGetAbility(id,out var ability),"pack ability missing");
                var entry=shelf.Single(e=>e.StableId==id.Value);
                var choice=RunUIPresentationMapper.BuildAbilityChoiceDetail(ability,2,data);
                foreach(var key in ((JArray)pack["cards"]).Values<string>())
                {
                    var card=SwordRules.Cards[SwordRules.CardPrefix+key];var text=SwordRules.Text(card,"description");
                    Require(entry.Detail.Contains(text)&&choice.Contains(text),"shop/free choice lost full card details: "+key);
                }
                Require(entry.Specification.Length<140,"list subtitle includes full detail");
                checkedPacks.Add(new JObject{["pack"]=pack["name"],["shop_detail_chars"]=entry.Detail.Length,["choice_detail_chars"]=choice.Length});
            }
            f.StartBattle();var battleSnapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
            var model=BattleUIPresentationMapper.BuildCardTable(battleSnapshot,battleSnapshot.Players.First(p=>p.Slot==Slot),data,battles);
            Require(model.Packs.Count==12&&model.Packs.All(p=>p.Cards.Count==4),"pack preview inventory");
            foreach(var pack in model.Packs)foreach(var card in pack.Cards)
                Require(card.Detail.Contains(SwordRules.Text(SwordRules.Cards[card.CardId],"description")),"pack preview lost card detail");
            return new JObject{["shop_and_choice"]=checkedPacks,["preview_packs"]=model.Packs.Count,["preview_cards"]=model.Packs.Sum(p=>p.Cards.Count)};
        }
        private JObject Visual()
        {
            var f=New(buyCurrent:false);var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
            f.StartBattle();var battleSnapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
            var model=BattleUIPresentationMapper.BuildCardTable(battleSnapshot,battleSnapshot.Players.First(p=>p.Slot==Slot),data,battles);
            return VisualSelfTests.Run(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)),model,snapshot.Players.First(p=>p.Slot==Slot),snapshot,data,battles);
        }
        private static void ToughEnemy(CombatantState target)
        {
            // Arrange only an isolated fixture. Keep the target alive so overkill cannot hide a missing payoff.
            AccessTools.Method(typeof(CombatantState),"SetMaximumHealthPreservingWoundLayers").Invoke(target,new object[]{10000});
            AccessTools.Field(typeof(CombatantState),"ownMaximumHealth").SetValue(target,10000);
            if(target.CurrentHealth!=10000||target.Shield!=0)Require(target.SetVitals(10000,0).IsSuccess,"tough isolated target");
        }
        private JObject HealthHit()
        {
            var rule=(JObject)SwordRules.Cards[Card(9).Value]["rules"];var onHit=rule["onHit"].DeepClone();int baseline;
            // Both commands still execute the same native card. Suppress just the extra payoff for the
            // control, restore it synchronously in finally before another frame or any live operation.
            try
            {
                rule.Remove("onHit");var control=New(seed:73051);control.StartBattle();control.Prepare(Card(9));
                control.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);var e=control.Enemy;ToughEnemy(e);
                control.Play(Card(9));baseline=10000-e.CurrentHealth;
            }
            finally{rule["onHit"]=onHit;}
            var f=New(seed:73051);f.StartBattle();f.Prepare(Card(9));f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            var healthTarget=f.Enemy;ToughEnemy(healthTarget);var receipt=f.Play(Card(9));int actual=10000-healthTarget.CurrentHealth;
            Require(actual>baseline&&f.State("hitHandled")==1,"additional physical damage missing: actual="+actual+", baseline="+baseline);
            int after=healthTarget.CurrentHealth;Require(f.Action(CardTableRules.DrawActionId).IsSuccess,"post-payoff draw");
            Require(healthTarget.CurrentHealth==after,"supplemental damage repeated on a later hook");
            var shieldOnly=New(seed:73051);shieldOnly.StartBattle();shieldOnly.Prepare(Card(9));shieldOnly.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
            var target=shieldOnly.Enemy;Require(target.SetVitals(target.CurrentHealth,10000).IsSuccess,"arrange shield target");
            int hp=target.CurrentHealth;shieldOnly.Play(Card(9));
            Require(target.CurrentHealth==hp&&target.Shield<10000&&shieldOnly.State("hitHandled")==0,"shield-only payoff");
            return new JObject{["with_payoff_health_damage"]=actual,["without_payoff_health_damage"]=baseline,["additional_actual_health_damage"]=actual-baseline,["formal_receipt_damage"]=receipt.Targets.Where(t=>t.RuntimeId==healthTarget.RuntimeId).Sum(t=>t.ResolvedDamage),["shield_only_payoff"]=false,["later_hook_did_not_repeat"]=true};
        }
        private JObject AllCards()
        {
            var rows=new JArray();
            foreach(var card in SwordRules.Cards.Values)
            {
                var id=new StableId(SwordRules.CardPrefix+SwordRules.Text(card,"key"));var row=new JObject{["card"]=SwordRules.Text(card,"name")};
                try
                {
                    var f=New(SwordRules.Int(card,"rank"),seed:(ulong)(73100+rows.Count));f.StartBattle();f.Prepare(id);f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
                    ToughEnemy(f.Enemy);var before=f.Table.Hand.First(h=>h.CardId==id).InstanceId;f.Play(id);
                    Require(!f.Table.Hand.Any(h=>h.InstanceId==before),"card not consumed");row["status"]="passed";
                }
                catch(Exception ex){row["status"]="failed";row["error"]=ex.ToString();}
                rows.Add(row);
            }
            var failures=rows.OfType<JObject>().Where(r=>(string)r["status"]=="failed").ToList();
            if(failures.Count>0)throw new InvalidOperationException("card matrix failures: "+new JArray(failures).ToString(Formatting.None));
            return new JObject{["executed_cards"]=rows.Count,["cards"]=rows};
        }
    }
}
