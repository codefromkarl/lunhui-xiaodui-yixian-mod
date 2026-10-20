using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject ExpandedSchool(string school)
        {
            var progress=new JArray();var cards=new JArray();var fates=new JArray();
            foreach(int rank in Enumerable.Range(1,5))
            {
                var f=New(rank,school:school,buyCurrent:false);int gifts=rank==1?2:3;
                Require(SectProgression.Owned(f.P).Count==gifts,"only starter and foundation choice gifted: "+school+"/"+rank);
                foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school&&(int)p["price"]>0))
                {
                    var id=SectRegistry.PackId(pack);if(f.P.HasPurchasedShelfAbility(id))continue;
                    int points=f.P.RewardPoints;long revision=f.A.Run.Revision;
                    var result=f.Send(RunCommand.PurchaseShelfAbility(Slot,id,revision));
                    bool allowed=(int)pack["rank"]<=rank;
                    Require(result.IsSuccess==allowed,"expanded native pack gate "+pack["name"]);
                    Require(f.P.RewardPoints==points-(allowed?(int)pack["price"]:0),"expanded native pack price");
                    if(!allowed)Require(f.A.Run.Revision==revision,"locked pack mutated run");
                }
                var own=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school&&(int)p["rank"]<=rank).ToList();
                Require(SectProgression.Owned(f.P).Count==own.Count&&f.P.ActiveCardPackIds.Count<=8,"all lower packs/active cap");
                foreach(var pair in SectRegistry.Fates.Where(p=>SectRegistry.Owner(p.Value)==school))
                {
                    var id=new StableId(pair.Key);int points=f.P.RewardPoints;long revision=f.A.Run.Revision;
                    var result=f.Send(RunCommand.PurchaseShelfAbility(Slot,id,revision));bool allowed=(int)pair.Value["rank"]<=rank;
                    Require(result.IsSuccess==allowed&&f.P.RewardPoints==points-(allowed?40:0),"expanded fate rank/price "+pair.Key);
                    if(allowed)Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,id,f.A.Run.Revision)).IsSuccess&&f.P.RewardPoints==points-40,"duplicate fate charged");
                    else Require(f.A.Run.Revision==revision,"locked fate mutated run");
                }
                progress.Add(new JObject{["rank"]=rank,["owned_packs"]=own.Count,["fates"]=SectRegistry.Fates.Count(p=>SectRegistry.Owner(p.Value)==school&&(int)p.Value["rank"]<=rank)});
                if(rank==5)
                {
                    var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("expanded-school",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
                    Require(mapper.TryCapture(f.A.Run,out var saved,out var error),"expanded hub save: "+error);
                    Require(mapper.TryRestore(saved,out var restored,out error),"expanded hub restore: "+error);
                    foreach(var id in SectRegistry.Fates.Where(p=>SectRegistry.Owner(p.Value)==school).Select(p=>new StableId(p.Key)))Require(restored.Players[0].HasPurchasedShelfAbility(id),"expanded fate lost on restore");
                    Require(restored.Players[0].PurchasedShelfAbilityIds.Count==f.P.PurchasedShelfAbilityIds.Count,"expanded purchases lost on restore");
                }
            }
            foreach(var card in SectRegistry.Cards.Where(p=>SectRegistry.Owner(p.Value)==school))
            {
                var id=new StableId(card.Key);var f=SchoolFixture(school,rank:(int)card.Value["rank"],required:id);f.Prepare(id);
                if(f.Actor.CurrentMagic!=f.Actor.MaximumMagic)f.System(RunDeveloperCommand.SetVitals,int.MinValue,f.Actor.MaximumMagic,1000);
                var instance=f.Table.Hand.First(h=>h.CardId==id).InstanceId;
                f.Play(id);Require(!f.Table.Hand.Any(h=>h.InstanceId==instance),"expanded card was not consumed: "+card.Key);
                cards.Add(new JObject{["name"]=card.Value["name"],["rank"]=card.Value["rank"],["status"]="passed"});
            }
            // Active passive hooks must be present in the real loadout, not just owned in a table.
            var active=New(5,school:school);
            foreach(var pair in SectRegistry.Fates.Where(p=>SectRegistry.Owner(p.Value)==school))active.Buy(new StableId(pair.Key));
            active.StartBattle();ToughEnemy(active.Enemy);
            if(school==SevenStarRules.Lineage)
            {
                Require(SchoolValue(active,school,"star")==1&&SchoolValue(active,school,"omen")==3,"original stargaze/divination opening passives absent");
                var attack=SchoolSource(school,4000003);active.Prepare(attack);active.Play(attack);
            }
            else
            {
                Require(SchoolValue(active,school,"nextActivate")==1&&(SchoolValue(active,school,"active")&2)!=0,"original five-element/water marks absent");
                var seal=SchoolSource(school,7000001);active.Prepare(seal);active.Play(seal);
                Require((SchoolValue(active,school,"active")&3)==3,"original first-card mark did not activate metal");
            }
            foreach(var pair in SectRegistry.Fates.Where(p=>SectRegistry.Owner(p.Value)==school))
            {
                Require(data.TryGetAbility(new StableId(pair.Key),out var ability),"expanded fate missing runtime definition");
                var description=RunUIPresentationMapper.BuildAbilityFunctionalOneLiner(ability,5,data);
                Require(!string.IsNullOrWhiteSpace(description)&&!description.Contains("本境界"),"fate description replaced with lineage/pack text");
                fates.Add(new JObject{["id"]=pair.Key,["description"]=description});
            }
            var visual=New(5,school:school);visual.StartBattle();var snapshot=RunAuthoritySnapshotProjector.Create(visual.A,Slot);var player=snapshot.Players.First(p=>p.Slot==Slot);
            var model=BattleUIPresentationMapper.BuildCardTable(snapshot,player,data,battles);
            var high= SectRegistry.Packs.First(p=>SectRegistry.Owner(p)==school&&(int)p["rank"]==5);
            VisualSelfTests.Run(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),System.IO.Path.GetFileNameWithoutExtension(path),school+"-high"),model,player,snapshot,data,battles,SectRegistry.PackId(high).Value,false);
            Require(cards.Count==48&&fates.Count==4,"expanded content inventory");
            return new JObject{["school"]=school,["progression"]=progress,["cards"]=cards,["fates"]=fates,["all_cards_executed"]=48,["hub_save_restore"]=true,["actual_passive_hooks"]=true};
        }
    }
}
