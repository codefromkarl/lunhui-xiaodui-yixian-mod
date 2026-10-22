using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static ModCombatantSnapshot BalanceUnit(string school,int rank,int energy,int maximum,StatBlock attributes)
    {
        var unit=new CombatantState(ActorId,new StableId("balance.actor"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,300,maximum,0,0,0,BuffCatalog.Empty,new PlayerSlotId(1));
        Method(typeof(CombatantState),"AssignEnergy").Invoke(unit,new object[]{new EnergyPoolSet(new EnergyPoolState(EnergyPoolKind.Fighting,energy,maximum),default,default)});
        var load=new ModUnitLoadout(null,null,null,new[]{new ModHeldCount(new StableId(school),rank)},default,0,Array.Empty<StableId>());
        return (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot),unit,load,attributes,0,0,0);
    }
    static void BalancePlannerAudit()
    {
        var rows=new JArray();
        var lineages=JObject.Parse(File.ReadAllText(Mod+"/content/lineages.json"))["operations"].OfType<JObject>().ToDictionary(x=>(string)x["id"]);
        foreach(string school in SectRegistry.Sects.Keys)
            for(int rank=1;rank<=5;rank++)
            {
                var step=lineages[school]["fields"]["steps"][rank-1];
                var values=step["passive"]["values"].ToDictionary(v=>(string)v["key"],v=>(int?)v["intValue"]??0);
                int Attr(string key)=>10+values.GetValueOrDefault(key);
                var attributes=new StatBlock(Attr("muscleFlat"),Attr("reactionFlat"),Attr("constitutionFlat"),Attr("energyFlat"));
                if(school==FiveElementRules.Lineage)attributes=new StatBlock(10,10,10+FiveCultivation.SupportGrowth(rank),10+FiveCultivation.PrimaryGrowth(rank));
                int maximum=EnergyPoolRules.CalculateMaximum(EnergyPoolKind.Fighting,values["maximumMagic"],attributes);
                foreach(string setup in new[]{"fresh","prepared"})
                    foreach(int qi in new[]{0,maximum/2,maximum})
                        foreach(var card in SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==school&&(int)c["rank"]<=rank))
                        {
                            var entries=new List<ModStateEntry>();
                            void Set(string key,int value)=>entries.Add(new ModStateEntry(SectRegistry.Package,SectRegistry.StateKey(school,ActorId,key),ModValueKind.Integer,value));
                            if(setup=="prepared")
                            {
                                if(school==SwordRules.Lineage){Set("cloud",2);Set("intent",3);Set("spiritDiscount",1);}
                                if(school==FiveElementRules.Lineage){Set("active",31);Set("last",1);}
                                if(school==SevenStarRules.Lineage){Set("star",3);Set("omen",1);Set("plays",2);Set("slot",2);}
                            }
                            var state=new ModStateSnapshot(entries);
                            int cost=school==SwordRules.Lineage?SwordRules.ResourceCost(card,0,setup=="prepared"?2:0,setup=="prepared"):
                                school==FiveElementRules.Lineage?FiveElementRules.PrintedCost(card,state,ActorId):SectCardRules.PrintedCost(card);
                            var row=new JObject{["school"]=school,["rank"]=rank,["card"]=card["key"],["name"]=card["name"],["setup"]=setup,["input_qi"]=qi,["cap"]=maximum,["energy_attribute"]=attributes.Energy,["attributes"]=attributes.ToString(),["cost"]=cost,["payable"]=cost<=qi};
                            if(cost<=qi)
                            {
                                Action<ModBattleContext,CodeBattlePlan> handler=school==SwordRules.Lineage?SwordRules.Play:school==FiveElementRules.Lineage?FiveElementRules.Play:SevenStarRules.Play;
                                var plan=Run(handler,Id(card),state,actor:BalanceUnit(school,rank,qi-cost,maximum,attributes));
                                int restored=plan.Effects.Where(e=>e.Kind==ModBattleEffectKind.Energy).Sum(e=>Math.Max(0,e.Energy.Amount));
                                int draw=SectRegistry.Read(plan.State,SwordRules.StateKey(ActorId,"drawPending"))+SectRegistry.Read(plan.State,"cards."+ActorId.Value+".drawPending");
                                row["planned_restore"]=restored;row["planned_draw"]=draw;
                                row["free_restore_and_draw_component"]=cost==0&&restored>0&&draw>0;
                            }
                            rows.Add(row);
                        }
            }
        Directory.CreateDirectory("logs/balance");
        var report=new JObject{["content_sha256"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Mod+"/content/cardtable.json"))).ToLowerInvariant(),["maximum_model"]="native pool formula from aligned rank attributes plus synthetic ten-point birth; excludes equipment and purchases",["attribute_model"]="synthetic ten per attribute plus full rank profile; Five uses automatic energy with body support; does not model native developer-command clamping",["scope"]="real C# planners on synthetic snapshots; fresh/prepared states; post-fee energy; excludes hit callbacks, round ticks and authoritative commits; components are not infinite-loop proofs",["rows"]=rows};
        File.WriteAllText("logs/balance/planner.json",report.ToString());
        Console.WriteLine("BALANCE planner snapshots: "+rows.Count+"; free restore/draw components: "+rows.OfType<JObject>().Count(r=>(bool?)r["free_restore_and_draw_component"]==true));
    }
}
