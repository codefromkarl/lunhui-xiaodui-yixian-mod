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
    public sealed class FateRuntimeTests : MonoBehaviour
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
            if(!Environment.GetCommandLineArgs().Any(x=>x=="-yixian-fates-selftest"))return;
            var go=new GameObject("YixianFateRuntimeTests");
            var runner=go.AddComponent<FateRuntimeTests>();runner.context=context;
            runner.path=Path.Combine(context.PackageDirectory,"TestResults","fates-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".json");
            UnityEngine.Object.DontDestroyOnLoad(go);
            context.Log("[FateTest] 已安排游戏内隔离测试；不会绑定存档主机。输出："+runner.path);
        }
        private void Update()
        {
            if(started||context==null||!GameManager.TryGetInstance(out var game)||game.Data==null||!game.Data.IsInitialized)return;
            started=true;data=game.Data;
            var live=game.Run?.Authority;long? liveRevision=live?.Run.Revision;
            var saves=SaveInventory();
            report=new JObject { ["runtime"]=Type.GetType("Mono.Runtime")!=null?"Mono":"other",["unity"]=Application.unityVersion,["startedUtc"]=DateTime.UtcNow.ToString("O"),["status"]="running",["modVersion"]=typeof(Entry).Assembly.GetName().Version.ToString(),["cases"]=cases };
            Write();
            Action<string> observe=line=>{if((line.Contains("[Warning]")||line.Contains("[Error]"))&&!line.Contains("[FateTest]"))diagnostics.Add(line);};
            ModLog.LineWritten+=observe;
            try
            {
                Require(RunAuthorityCatalogFactory.TryCreate(data,out worlds,out var worldError),"world catalog: "+worldError);
                Require(BattleAuthorityCatalogFactory.TryCreate(data,out battles,out var battleError),"battle catalog: "+battleError);
                Case("fates_catalog_purchase_and_restore",FatesCatalog);
                Case("fates_growth_real_commands",FatesGrowth);
                Case("fates_first_card_shield_retention",FatesGuard);
                Case("fates_unconditional_cloud_and_cost",FatesSea);
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
            Write();context.Log("[FateTest] 完成："+report["passed"]+" 通过 / "+report["failed"]+" 失败；"+path);
            SelfTestShutdown.Complete("fates",path);
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
            catch(Exception ex){row["status"]="failed";row["error"]=ex.ToString();context.Warn("[FateTest] "+name+"："+ex.Message);}
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
            public void NextRound()
            {
                int round=B.Battle.Round;Require(Send(RunCommand.EndBattleTurn(Slot,A.Run.Revision)).IsSuccess,"end turn");
                Require(A.TryGetPlayerBattlefieldId(Slot,out var field),"field id");
                for(int i=0;i<100&&(B.Battle.Round==round||B.Battle.Phase!=BattlePhase.Player);i++)
                {Now+=5000;Require(A.Ledger.ExecuteSystem(RunSystemAction.TickBattlefield(field)).IsSuccess,"tick field");}
                Require(!B.IsComplete&&B.Battle.Round==round+1&&B.Battle.Phase==BattlePhase.Player,"next round unavailable");
            }
            public int State(string key)=>SwordRules.ReadInt((ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(B),Actor.RuntimeId,key);
        }
        private Fixture New(int rank=1,bool native=false,bool buyCurrent=true,ulong seed=73001)
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
            var id=native?CardTableCatalog.LineageId:Sect;
            for(int i=0;i<rank;i++)f.Upgrade(id);
            if(buyCurrent&&!native)foreach(var pack in SwordRules.Packs.Where(p=>SwordRules.Int(p,"rank")==rank&&SwordRules.Int(p,"price")>0&&!f.P.HasPurchasedShelfAbility(PackId(p))))f.Buy(PackId(pack));
            return f;
        }
        private static StableId PackId(JObject pack)=>SectRegistry.PackId(pack);
        private static StableId Card(int n)=>new StableId(SwordRules.Cards.Single(c=>SwordRules.Text(c.Value,"sourceId")=="100"+n.ToString("D4")).Key);
        private JObject FatesCatalog()
        {
            var ids=new[]{FateRules.Rhyme,FateRules.CloudGuard,FateRules.FormationGuard,FateRules.CloudSea};var rows=new JArray();
            foreach(var id in ids)
            {
                Require(data.TryGetAbility(new StableId(id),out var ability),"missing fate content "+id);
                Require(ability.Mode==Reincarnation.Config.ContentAbilityMode.Passive,"not passive");
                Require(data.EconomyCatalog.TryGetShelfAbility(ability.StableId,out var shelf),"missing shelf "+id);
                Require(shelf.RequiredRank==FateRules.Required(id),"required rank");
                var text=RunUIPresentationMapper.BuildAbilityFunctionalOneLiner(ability,5,data);
                Require(!text.Contains("本境界新增")&&!text.Contains("卡组"),"fate displays lineage/pack text");
                rows.Add(new JObject{["id"]=id,["rank"]=shelf.RequiredRank,["price"]=shelf.Price,["summary"]=text});
            }
            var low=New(1,buyCurrent:false);var before=low.P.RewardPoints;
            foreach(var id in ids)Require(!low.Send(RunCommand.PurchaseShelfAbility(Slot,new StableId(id),low.A.Run.Revision)).IsSuccess,"rank gate accepted");
            Require(low.P.RewardPoints==before,"rank gate charged");
            var f=New(3,buyCurrent:false);
            foreach(var id in ids){f.Buy(new StableId(id));var points=f.P.RewardPoints;Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,new StableId(id),f.A.Run.Revision)).IsSuccess&&f.P.RewardPoints==points,"duplicate charged");}
            var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("fate-selftest",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
            Require(mapper.TryCapture(f.A.Run,out var saved,out var error),"memory save capture: "+error);
            Require(mapper.TryRestore(saved,out var restored,out error),"memory save restore: "+error);
            Require(ids.All(id=>restored.Players[0].HasPurchasedShelfAbility(new StableId(id))),"purchases lost on restore");
            return new JObject{["abilities"]=rows,["purchase_restore"]=true};
        }
        private JObject FatesGrowth()
        {
            var rows=new JArray();
            for(int rank=2;rank<=5;rank++)
            {
                var f=New(rank);f.Buy(new StableId(FateRules.Rhyme));f.Buy(new StableId(FateRules.CloudGuard));
                // Keep low-rank cloud available when testing higher-rank growth.
                var pack=PackId(SwordRules.Packs.Single(p=>((JArray)p["cards"]).Values<string>().Contains("yixian_sword_1")));
                if(!f.P.HasPurchasedShelfAbility(pack))f.Buy(pack);
                f.StartBattle();Require(f.Enemy.SetVitals(f.Enemy.CurrentHealth,100000).IsSuccess,"arrange durable enemy");Require(f.State("intent")==2*rank,"first-round intent "+rank);
                f.Prepare(Card(1));int shield=f.Actor.Shield;f.Play(Card(1));
                Require(f.Actor.Shield-shield==3*rank,"cloud shield growth "+rank);
                int now=f.State("intent");Require(now==0,"intent not consumed");
                var ctx=AccessTools.Field(typeof(BattleAuthorityService),"players").GetValue(f.B) as System.Collections.IDictionary;
                var state=ctx[Slot];var snapshot=(ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService),"modState").GetValue(f.B);
                // Duplicate hook test uses the real authoritative hook, and must not re-grant.
                AccessTools.Method(typeof(BattleAuthorityService),"RunModHook").Invoke(f.B,new object[]{ModBattleHook.RoundStart,default(StableId),default(StableId)});
                Require(f.State("intent")==now,"round hook duplicate grant");
                f.NextRound();Require(f.State("intent")==2*rank,"next-round intent "+rank);
                rows.Add(new JObject{["rank"]=rank,["intent"]=f.State("intent"),["cloud_shield"]=3*rank});
            }
            return new JObject{["growth"]=rows};
        }
        private JObject FatesGuard()
        {
            var f=New(3);f.Buy(new StableId(FateRules.FormationGuard));f.StartBattle();f.Prepare(Card(12));
            Require(f.State("fateGuardRound")==0,"draw activated protection");
            f.Play(Card(12));Require(f.State("fateGuardRound")==f.B.Battle.Round,"first nonformation card did not activate");
            int Retain(int amount)
            {
                var players=(System.Collections.IDictionary)AccessTools.Field(typeof(BattleAuthorityService),"players").GetValue(f.B);
                return (int)AccessTools.Method(typeof(BattleAuthorityService),"CalculateRetainedShieldForCurrentBattle").Invoke(f.B,new object[]{players[Slot],amount,0});
            }
            Require(Retain(80)==80,"Mono retention patch not effective");
            Require(f.Actor.SetVitals(f.Actor.CurrentHealth,80).IsSuccess,"arrange shield");
            Require(CombatVitalRules.ApplyDamage(f.Actor,30).IsSuccess&&f.Actor.Shield==50,"protection blocks damage consumption");
            f.NextRound();int baseline=Retain(80);Require(baseline<80,"guard leaked to no-card next round");
            return new JObject{["protected_retained"]=80,["damage_consumes_shield"]=30,["next_round_retained"]=baseline};
        }
        private JObject FatesSea()
        {
            var f=New(5);f.Buy(new StableId(FateRules.CloudSea));f.Buy(new StableId(FateRules.CloudGuard));
            var low=PackId(SwordRules.Packs.Single(p=>((JArray)p["cards"]).Values<string>().Contains("yixian_sword_3")));if(!f.P.HasPurchasedShelfAbility(low))f.Buy(low);
            f.StartBattle();Require(f.Enemy.SetVitals(f.Enemy.CurrentHealth,100000).IsSuccess,"arrange durable enemy");f.Prepare(Card(3),Card(25));int shield=f.Actor.Shield;f.Play(Card(3));
            Require(f.Actor.Shield>shield+15&&f.State("cloud")==1,"first cloud missing linked shield");
            f.Play(Card(25));Require(f.State("cloud")==1,"formation broke cloud count");
            f.Prepare(Card(98));var before=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);f.Play(Card(98));
            Require(before-f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)==0,"linked discount should be free");
            Require(f.State("cloud")==3,"real cloud count inflated/lost");
            return new JObject{["first_cloud_linked"]=true,["other_card_preserves_count"]=true,["discount_cost"]=0,["cloud_count"]=f.State("cloud")};
        }
    }
}
