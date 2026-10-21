using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject BalanceMatrix()
        {
            var config=JObject.Parse(File.ReadAllText(Path.Combine(context.PackageDirectory,"TestResults","balance-config.json")));
            int rounds=(int)config["rounds"], cap=(int)config["maxPlaysPerRound"];
            Require(rounds>=1&&rounds<=8&&cap>=1&&cap<=48,"balance budget invalid");
            var seeds=config["seeds"].Values<ulong>().ToArray();
            Require(seeds.Length>=1&&seeds.Length<=16,"balance seed budget invalid");
            var rows=new JArray();
            string output=Path.Combine(Path.GetDirectoryName(path),"balance-"+Path.GetFileName(path));
            var result=new JObject{["schema"]=1,["scope"]="real commands, random hand, bounded endurance resource probe; not campaign/win-rate evidence",
                ["config"]=config,["rows"]=rows,["five_deck_size"]=FiveDeckRules.Size};
            using(var hash=SHA256.Create())
                result["content_sha256"]=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Path.Combine(context.PackageDirectory,"content","cardtable.json")))).Replace("-","").ToLowerInvariant();
            foreach(string schoolKey in config["schools"]?.Values<string>()??new[]{"sword","five","star"})
            {
                string school="yixian.sect.lineage."+schoolKey;
                for(int rank=1;rank<=5;rank++)
                {
                    if(config["ranks"]!=null&&!config["ranks"].Values<int>().Contains(rank))continue;
                    var profiles=schoolKey=="five" ? ((JObject)config["fiveDeckUnlocks"] ?? throw new InvalidOperationException("五行已改为固定八张；请使用包含fiveDeckUnlocks的新配置，旧混包、强制换牌与纯水结果仅作历史。" )).Properties().Select(p=>p.Name).ToArray() : new[]{"gifts","newest"}.Concat(((JObject)config["routes"][schoolKey]).Properties().Select(p=>p.Name)).ToArray();
                    foreach(string profile in profiles)
                        foreach(string policy in config["policies"].Values<string>())
                            for(int si=0;si<seeds.Length;si++)
                                foreach(int foundation in schoolKey=="five"?new[]{0}:config["foundationChoices"]?.Values<int>()??new[]{si%2})
                                    rows.Add(BalanceBattle(config,schoolKey,school,rank,profile,policy,seeds[si],foundation,false));
                    if(config["fateSensitivityRanks"].Values<int>().Contains(rank))
                        foreach(string profile in schoolKey=="five"?profiles:profiles.Skip(2))
                            for(int si=0;si<seeds.Length;si++)
                                foreach(int foundation in schoolKey=="five"?new[]{0}:config["foundationChoices"]?.Values<int>()??new[]{si%2})
                                    rows.Add(BalanceBattle(config,schoolKey,school,rank,profile,"setup_resource",seeds[si],foundation,true));
                    File.WriteAllText(output,result.ToString());
                    context.Log("[Balance] "+schoolKey+" rank "+rank+"; rows="+rows.Count);
                }
            }
            int errors=rows.OfType<JObject>().Count(r=>(string)r["status"]=="error");
            result["errors"]=errors;result["status"]=errors==0?"completed":"failed";
            File.WriteAllText(output,result.ToString());
            Require(errors==0,"balance probe errors: "+errors+"; see "+output);
            var contract=(bool?)config["productionRecommendations"]==true?BuildAdjustmentContract():null;
            var waterContract=(bool?)config["fiveDeckContract"]==true?FiveFixedDeckContract():null;
            return new JObject{["five_deck_contract"]=waterContract,["matches"]=rows.Count,["errors"]=errors,["artifact"]=output,["seed_count"]=seeds.Length,["round_budget"]=rounds,["play_cap"]=cap,["recommendation_contract"]=contract};
        }

        private JObject BalanceBattle(JObject config,string schoolKey,string school,int rank,string profile,string policy,ulong seed,int foundation,bool fates)
        {
            var row=new JObject{["school"]=schoolKey,["rank"]=rank,["profile"]=profile,["policy"]=policy,["seed"]=seed,["foundation_choice"]=foundation,["fates"]=fates};
            var turns=new JArray();row["turns"]=turns;
            try
            {
                var f=New(1,school:school,buyCurrent:false,seed:seed);
                var sect=new StableId(school);int before=f.P.RewardPoints;
                var schoolPacks=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school).ToArray();
                Require(foundation==0||foundation==1,"invalid foundation choice");
                bool fixedDeck=schoolKey=="five";
                var foundationPacks=schoolPacks.Where(p=>(int)p["rank"]==2).ToArray();
                for(int r=2;r<=rank;r++)
                {
                    StableId choice;
                    if(fixedDeck)
                    {
                        var sources=config["fiveDeckUnlocks"]?[profile]?[r.ToString()]?.Values<string>().ToArray();
                        Require(sources!=null&&sources.Length==2,"library route must name two current-realm unlock source IDs");
                        choice=new StableId("five-learn~"+r+"~"+string.Join("~",sources));
                    }
                    else choice=r==2?PackId(foundationPacks[foundation]):default;
                    Require(f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,sect,f.A.Run.Revision,choice)).IsSuccess,"balance rank purchase");
                }
                row["rank_cost_after_entry"]=before-f.P.RewardPoints;
                var eligible=schoolPacks.Where(p=>(int)p["rank"]<=rank).ToArray();
                JObject[] wanted;
                if(fixedDeck)wanted=Array.Empty<JObject>();
                else if(profile=="gifts")wanted=eligible.Where(p=>f.P.HasPurchasedShelfAbility(PackId(p))).ToArray();
                else if(profile=="newest")wanted=eligible.OrderByDescending(p=>(int)p["rank"]).ThenBy(p=>(int)p["index"]).Take(3).ToArray();
                else
                {
                    var basePriority=config["routes"][schoolKey][profile].Values<string>();
                    var first=config["rankPriorities"]?[schoolKey]?[profile]?[rank.ToString()]?.Values<string>()??Enumerable.Empty<string>();
                    var priority=first.Concat(basePriority).Distinct().ToArray();
                    bool reuseGift=rank==2&&(config["reuseFoundationGiftRoutes"]?[schoolKey]?.Values<string>().Contains(profile)??false);
                    var recommendationOwned=reuseGift?eligible.Where(p=>f.P.HasPurchasedShelfAbility(PackId(p))).ToArray():eligible;
                    if(reuseGift)
                    {
                        var gift=priority.Select(k=>recommendationOwned.FirstOrDefault(p=>(string)p["key"]==k&&(int)p["rank"]==2)).FirstOrDefault(p=>p!=null);
                        Require(gift!=null,"foundation gift missing");
                        priority=priority.Where(k=>eligible.Any(p=>(string)p["key"]==k&&((int)p["rank"]==1||p==gift))).Concat(priority).Distinct().ToArray();
                        row["reused_foundation_gift"]=true;
                    }
                    wanted=priority.Select(key=>eligible.FirstOrDefault(p=>(string)p["key"]==key)).Where(p=>p!=null).Take(3).ToArray();
                    if((bool?)config["productionRecommendations"]==true&&schoolKey!="star")
                    {
                        var route=SwordBuilds.RoutesFor(school).Single(r=>r.Key==profile);
                        var proposed=SwordBuilds.Recommend(route,rank,recommendationOwned.Select(PackId));
                        Require(new HashSet<StableId>(wanted.Select(PackId)).SetEquals(proposed.Selected),"configured/production recommendation drift");
                        wanted=proposed.Selected.Select(id=>eligible.Single(p=>PackId(p)==id)).ToArray();
                        row["selection_source"]="production_recommendation";
                    }
                }
                before=f.P.RewardPoints;
                if(!fixedDeck&&(config["extraPacksByRank"]?[schoolKey]?[profile]?[rank.ToString()]??config["extraPacks"]?[schoolKey]?[profile]) is JArray extra)
                    wanted=wanted.Concat(extra.Values<string>().Select(key=>eligible.Single(p=>(string)p["key"]==key))).Distinct().ToArray();
                foreach(var pack in wanted)if(!f.P.HasPurchasedShelfAbility(PackId(pack)))f.Buy(PackId(pack));
                row["pack_spend"]=before-f.P.RewardPoints;
                var active=fixedDeck?new List<StableId>{FiveDeckRules.PackId}:wanted.Select(PackId).ToList();
                foreach(var id in SectProgression.Owned(f.P))if(!fixedDeck&&active.Count<Math.Min(3,SectProgression.Owned(f.P).Count)&&!active.Contains(id))active.Add(id);
                // Equal sets must map a common RNG seed to an equal pool order.
                if(!fixedDeck)active=active.OrderBy(id=>(int)schoolPacks.Single(p=>PackId(p)==id)["index"]).ToList();
                Require(f.P.SetActiveCardPacks(active).IsSuccess,"balance pack selection");
                row["packs"]=new JArray(active.Select(id=>id.Value));
                row["draw_pool"]=new JArray(FiveDeckRules.ActiveCards(f.P).Select(id=>id.Value));
                if(fixedDeck && config["fiveDeckSelections"]?[profile]?[rank.ToString()] is JArray deckSources)
                {
                    var answer=new StableId("five-deck~"+string.Join("~",deckSources.Values<string>()));
                    // Stage the same legal choice in the hub fixture; runtime contract
                    // below separately covers the authoritative battle command.
                    Require(FiveDeckRules.TrySelection(answer,out var selection)&&FiveDeckRules.SetDeck(f.P,selection).IsSuccess,"configured eight-card selection invalid");
                    row["draw_pool"]=new JArray(FiveDeckRules.Cards(f.P).Select(id=>id.Value));
                }
                before=f.P.RewardPoints;
                if(fates)
                {
                    if(schoolKey=="sword")
                        foreach(string id in new[]{FateRules.Rhyme,FateRules.CloudGuard,FateRules.FormationGuard,FateRules.CloudSea})
                        {int required=id==FateRules.Rhyme||id==FateRules.CloudGuard?2:3;if(rank>=required)f.Buy(new StableId(id));}
                    else foreach(var fate in SectRegistry.Fates.Where(p=>SectRegistry.Owner(p.Value)==school&&(int)p.Value["rank"]<=rank))f.Buy(new StableId(fate.Key));
                }
                row["fate_spend"]=before-f.P.RewardPoints;
                foreach(var attribute in new[]{ExchangeAttribute.Muscle,ExchangeAttribute.Reaction,ExchangeAttribute.Constitution,ExchangeAttribute.Energy})
                    f.System(RunDeveloperCommand.SetAttribute,(int)attribute,(int)config["baseAttribute"]);
                row["effective_attributes"]=JObject.FromObject(f.P.DerivedStats.EffectiveAttributes);
                row["controlled_attributes"]=row["effective_attributes"].DeepClone();
                f.StartBattle();var enemy=f.Enemy;var actor=f.Actor;var battle=f.B;
                // A raw ownMaximumHealth edit is recalculated by native round
                // transitions and would masquerade as damage. Use the persistent
                // native maximum-health status once at fixture setup instead.
                foreach(var unit in new[]{enemy,actor})
                    OriginalHealthRules.Apply(unit,unit.RuntimeId,"maximum",10000-unit.CurrentMaximumHealth,true);
                foreach(var unit in battle.Battle.Combatants.Where(u=>u.Side==CombatSide.Enemy))
                    Require(unit.RaiseActionSkipsTo(99).IsSuccess,"balance sandbag skip setup");
                row["enemy_actions_disabled"]=true;
                row["endurance_fixture_health"]=10000;
                row["opening_qi"]=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                row["qi_cap"]=f.Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting);
                row["opening_ap"]=f.Actor.ActionPoints;
                row["opening_hand"]=BalanceHand(f);
                int totalPlays=0,manual=0,freeDraws=0,healthDamage=0,shieldDamage=0;
                for(int turn=0;turn<(int)config["rounds"];turn++)
                {
                    if(battle.IsComplete||actor.IsDowned)break;
                    var tr=new JObject{["round"]=f.B.Battle.Round,["qi_start"]=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting),["ap_start"]=f.Actor.ActionPoints,["hand_start"]=f.Table.Hand.Count};
                    var trace=new JArray();tr["trace"]=trace;turns.Add(tr);
                    int played=0;string stop="";
                    while(played<(int)config["maxPlaysPerRound"]&&!battle.IsComplete&&!actor.IsDowned)
                    {
                        var state=SchoolState(f);int qi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                        var candidates=f.Table.Hand.Where(h=>SectRegistry.Cards.ContainsKey(h.CardId.Value))
                            .Select(h=>SectRegistry.Cards[h.CardId.Value]).Where(c=>SectCardRules.CanPlay(c,state,f.Actor.RuntimeId)&&SectCardRules.Cost(c,f.P,f.Actor,state)<=qi).ToArray();
                        if(candidates.Length==0)
                        {
                            if(f.Actor.ActionPoints>0&&f.Table.Hand.Count<10)
                            {
                                var draw=f.Action(CardTableRules.DrawActionId);
                                if(!draw.IsSuccess){stop="draw_rejected:"+draw.ErrorCode;break;}
                                manual++;trace.Add(new JObject{["draw"]=true,["qi"]=qi,["hand"]=battle.CaptureCardTable(Slot)?.Hand.Count??0});
                                if(battle.IsComplete||actor.IsDowned){stop="battle_terminal";break;}continue;
                            }
                            stop=f.Table.Hand.Count==0?"empty_hand":f.Table.Hand.Any(h=>SectRegistry.Cards.TryGetValue(h.CardId.Value,out var c)&&SectCardRules.CanPlay(c,state,f.Actor.RuntimeId))?"qi_blocked":"consumed_hand";
                            break;
                        }
                        var card=policy=="hand_order"?candidates[0]:candidates.OrderByDescending(c=>BalancePriority(f,c,schoolKey)).First();
                        int cost=SectCardRules.Cost(card,f.P,f.Actor,state),hand=f.Table.Hand.Count;
                        int enemyHealth=enemy.CurrentHealth,enemyShield=enemy.Shield,actorHealth=f.Actor.CurrentHealth;
                        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"balance-active.json"),new JObject{["report"]=Path.GetFileName(path),["school"]=schoolKey,["rank"]=rank,["profile"]=profile,["policy"]=policy,["seed"]=seed,["fates"]=fates,["round"]=battle.Battle.Round,["card"]=card["key"]}.ToString());
                        f.Play(new StableId(SectRegistry.CardPrefix+(string)card["key"]));played++;totalPlays++;
                        int after=actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                        int drawn=Math.Max(0,(battle.CaptureCardTable(Slot)?.Hand.Count??(hand-1))-hand+1);freeDraws+=drawn;
                        int hd=Math.Max(0,enemyHealth-enemy.CurrentHealth),sd=Math.Max(0,enemyShield-enemy.Shield);healthDamage+=hd;shieldDamage+=sd;
                        trace.Add(new JObject{["card"]=card["key"],["name"]=card["name"],["quoted_cost"]=cost,["qi_before"]=qi,["qi_after"]=after,["drawn"]=drawn,["health_damage"]=hd,["shield_damage"]=sd,["actor_health_delta"]=f.Actor.CurrentHealth-actorHealth});
                        if((bool?)config["measureWaterResources"]==true)
                        {
                            var step=(JObject)trace.Last;var rules=(JObject)card["rules"];
                            int restored=(int?)rules["qi"]??0;
                            if(rules["qiBp"]!=null)restored+=(int)((long)f.P.DerivedStats.EffectiveAttributes.Energy*(int)rules["qiBp"]/10000);
                            step["authored_direct_qi_restore"]=restored;
                            step["authored_direct_qi_overflow"]=Math.Max(0,restored-Math.Max(0,actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting)-qi+cost));
                        }
                        Require(after>=0&&after<=f.Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting),"balance energy out of bounds");
                        Require(!SchoolState(f).Entries.Any(e=>e.PackageId==SectRegistry.Package&&(e.Key==OriginalHealthRules.PendingKey||e.Key=="cards."+f.Actor.RuntimeId.Value+".drawPending"||e.Key==SwordRules.StateKey(f.Actor.RuntimeId,"drawPending"))),"balance pending queue leak");
                    }
                    if(played==(int)config["maxPlaysPerRound"])stop="play_cap";
                    if(battle.IsComplete||actor.IsDowned)stop="battle_terminal";
                    tr["plays"]=played;tr["stop"]=stop;tr["qi_end"]=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);tr["ap_end"]=f.Actor.ActionPoints;tr["hand_end"]=battle.CaptureCardTable(Slot)?.Hand.Count??0;tr["shield_end"]=f.Actor.Shield;
                    tr["mechanics"]=new JObject();
                    foreach(string key in new[]{"active","waterForce","star","omen","plays","cloud","intent"})
                        tr["mechanics"][key]=SectRegistry.Read(SchoolState(f),SectRegistry.StateKey(school,f.Actor.RuntimeId,key));
                    if(turn+1<(int)config["rounds"])
                    {
                        int beforeTransition=enemy.CurrentHealth;
                        tr["enemy_max_before_transition"]=enemy.CurrentMaximumHealth;
                        tr["enemy_max_status_before"]=enemy.Statuses.Where(s=>s.StatusId==OriginalHealthRules.MaximumStatus).Sum(s=>s.FlatAmount);
                        bool advanced=BalanceAdvance(f);
                        tr["between_round_health_damage"]=Math.Max(0,beforeTransition-enemy.CurrentHealth);
                        tr["enemy_max_after_transition"]=enemy.CurrentMaximumHealth;
                        tr["enemy_max_status_after"]=enemy.Statuses.Where(s=>s.StatusId==OriginalHealthRules.MaximumStatus).Sum(s=>s.FlatAmount);
                        if(!advanced)break;
                    }
                }
                row["plays"]=totalPlays;row["manual_draws"]=manual;row["free_draws"]=freeDraws;row["player_phase_health_damage"]=healthDamage;row["player_phase_shield_damage"]=shieldDamage;
                row["rounds_observed"]=turns.Count;
                row["battle_complete"]=battle.IsComplete;
                row["actor_downed"]=actor.IsDowned;
                row["status"]="completed";
            }
            catch(Exception ex){row["status"]="error";row["error"]=ex.ToString();}
            return row;
        }

        private static JArray BalanceHand(Fixture f)
            =>new JArray(f.Table.Hand.Select(h=>new JObject{["id"]=h.CardId.Value,["cost"]=SectCardRules.Cost(SectRegistry.Cards[h.CardId.Value],f.P,f.Actor,SchoolState(f))}));

        private static int BalancePriority(Fixture f,JObject card,string school)
        {
            var r=(JObject)card["rules"];var state=SchoolState(f);int qi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
            int Read(string key)=>SectRegistry.Read(state,SectRegistry.StateKey("yixian.sect.lineage."+school,f.Actor.RuntimeId,key));
            int I(string key)=>(int?)r[key]??0;
            int score=0;
            if(school=="five"&&I("activate")>0&&(Read("active")&(1<<(I("activate")-1)))==0)score+=100;
            if(qi<f.Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting)/2&&(I("qi")>0||I("qiBp")>0))score+=80;
            if(school=="sword"&&r["persist"]!=null&&Read((string)r["persist"])==0)score+=45;
            if(school=="star"&&I("starGain")>0&&Read("star")<3)score+=45;
            if(school=="star"&&I("omenGain")>0&&Read("omen")<2)score+=35;
            if(I("damage")>0||I("randomMax")>0||I("chanceAttack")>0||I("lifeLoss")>0)score+=20;
            if(school=="sword"&&SwordRules.HasTag(card,"cloud"))score+=Read("cloud")>0?25:10;
            return score;
        }

        private static bool BalanceAdvance(Fixture f)
        {
            if(f.B.IsComplete||f.Actor.IsDowned)return false;
            int round=f.B.Battle.Round;
            Require(f.Send(RunCommand.EndBattleTurn(Slot,f.A.Run.Revision)).IsSuccess,"balance end turn");
            Require(f.A.TryGetPlayerBattlefieldId(Slot,out var field),"balance battlefield missing");
            for(int i=0;i<100;i++)
            {
                if(f.B.IsComplete)return false;
                if(f.B.Battle.Round>round&&f.B.Battle.Phase==BattlePhase.Player)return true;
                f.Now+=5000;Require(f.A.Ledger.ExecuteSystem(RunSystemAction.TickBattlefield(field)).IsSuccess,"balance tick failed");
            }
            throw new InvalidOperationException("balance round budget exceeded");
        }
    }
}
