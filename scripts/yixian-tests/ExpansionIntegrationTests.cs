using System.Reflection;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Domain;
using Reincarnation.Runtime.Content;
using YixianSect;

partial class Program
{
    static void ExpansionIntegrationTests()
    {
        var progressions=new List<ProgressionDefinition>();
        var runtimeLineages=new List<RuntimeLineageDefinition>();
        foreach(var row in JObject.Parse(File.ReadAllText(Mod+"/content/lineages.json"))["operations"].OfType<JObject>())
        {
            var f=(JObject)row["fields"];
            var steps=f["steps"].OfType<JObject>().Select(s=>new ContentProgressionStepDefinition((string)s["rank"],(int)s["price"],Operation((JObject)s["passive"]),Operation((JObject)s["active"]))).ToArray();
            var source=new ContentLineageDefinition((string)row["id"],1,(string)f["displayName"],(string)f["lineageCode"],(int)f["energyPoolKind"],steps,(string)f["flavour"],f["rankTitles"].Values<string>().ToArray());
            runtimeLineages.Add((RuntimeLineageDefinition)PrivateCtor(typeof(RuntimeLineageDefinition),source));
        }
        var projection=typeof(RuntimeLineageDefinition).Assembly.GetType("Reincarnation.Runtime.Content.RuntimeDomainCatalogProjection");
        Method(projection,"ProjectProgressions").MakeGenericMethod(typeof(RuntimeLineageDefinition)).Invoke(null,new object[]{runtimeLineages,Reincarnation.Domain.CatalogContentKind.Lineage,progressions});
        var abilities=new List<RuntimeAbilityDefinition>();
        foreach(var file in new[]{"abilities","fates"})
            foreach(var row in JObject.Parse(File.ReadAllText(Mod+"/content/"+file+".json"))["operations"].OfType<JObject>())
            {
                var f=(JObject)row["fields"];
                Check(!ContentVocabulary.Check("abilities",f).Any(),"expanded native vocabulary "+row["id"]);
                var ranks=f["ranks"].OfType<JObject>().Select(r=>new ContentAbilityRankDefinition((string)r["rank"],1,Operation((JObject)r["operation"]))).ToArray();
                var source=new ContentAbilityDefinition((string)row["id"],1,(string)f["displayName"],(string)f["sourceContentId"],(ContentAbilityMode)(int)f["mode"],ranks,null,(string)f["flavour"],(int)f["purchasePrice"],(ContentAbilityAcquisition)(int)f["acquisition"]);
                abilities.Add((RuntimeAbilityDefinition)PrivateCtor(typeof(RuntimeAbilityDefinition),source));
            }
        var shelves=new List<ShelfAbilityDefinition>();
        Method(projection,"ProjectShelfAbilities").Invoke(null,new object[]{abilities,progressions,shelves});
        Check(progressions.Count==3&&progressions.All(p=>p.MaximumRank==5),"three full five-rank native lineages");
        Check(shelves.Count==30,"eighteen sword/star paid packs and twelve independent fates projected");
        var economy=new EconomyCatalog(Array.Empty<EconomyCatalogEntry>(),progressions,shelves);
        var hub=new HubState(HubAccessKind.Full,1,new[]{new PlayerSlotId(1)});var service=new HubService(economy);
        foreach(var school in SectRegistry.Sects.Keys)
        {
            var cards=SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==school).ToList();
            var packs=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school).ToList();
            Check(cards.Count==(school==SevenStarRules.Lineage?40:48)&&packs.Count==(school==SevenStarRules.Lineage?10:12),"equal school content volume "+school);
            foreach(int rank in Enumerable.Range(1,5))Check(packs.Count(p=>(int)p["rank"]==rank)==(school==SevenStarRules.Lineage?2:rank<4?2:3),"equal per-realm pack allocation");
            foreach(int rank in Enumerable.Range(1,5))
            {
                var player=SchoolPlayer(school,rank);
                SectProgression.Refresh(player);
                Check(SectProgression.Owned(player).Count==(school==FiveElementRules.Lineage?1:2),"higher ranks do not retrospectively gift packs");
                var candidates=SectProgression.GiftCandidates(new StableId(school),rank,player.Slot,player.PurchasedShelfAbilityIds);
                Check(candidates.Count==(school!=FiveElementRules.Lineage&&rank==2?2:0),"foundation-only free choice for each school");
                foreach(var shelf in shelves)
                {
                    int points=player.RewardPoints;var result=service.PurchaseShelfAbility(hub,player,shelf.AbilityId);
                    bool expected=shelf.SourceId.Value==school&&shelf.RequiredRank<=rank;
                    Check(result.IsSuccess==expected,"school-specific native purchase gate "+school+"/"+rank+"/"+shelf.AbilityId);
                    Check(player.RewardPoints==points-(expected?shelf.Price:0),"native purchase atomic cost");
                    if(expected)Check(!service.PurchaseShelfAbility(hub,player,shelf.AbilityId).IsSuccess&&player.RewardPoints==points-shelf.Price,"duplicate pack/fate rejected without charge");
                }
                SectProgression.Refresh(player);
                Check(SectProgression.Owned(player).Count==(school==FiveElementRules.Lineage?1:packs.Count(p=>(int)p["rank"]<=rank)),"all accessible lower packs remain owned");
                Check(player.ActiveCardPackIds.Count<=8,"expanded school active pack cap");
                Check(SectProgression.Owned(player).All(id=>(id==FiveDeckRules.PackId?FiveElementRules.Lineage:SectRegistry.Owner(SectRegistry.FindPack(id)))==school),"expanded pool cannot leak other school");
            }
        }
        var fates=JObject.Parse(File.ReadAllText(Mod+"/content/fates.json"))["operations"].OfType<JObject>().ToList();
        Check(fates.Count==12&&fates.GroupBy(f=>(string)f["fields"]["sourceContentId"]).All(g=>g.Count()==4),"four independent fates per school");
        foreach(var fate in fates)Check((int)fate["fields"]["purchasePrice"]==40,"aligned fate price");
        CardTableBridge.BuildCatalog(out var _,out var combinedPacks);
        foreach(var pair in new[]{(60,24),(61,25),(70,26),(71,27)})Check(combinedPacks[pair.Item2].ShelfIndex==pair.Item1,"legacy new-school cp ordinal preserved "+pair.Item1);
        Console.WriteLine("PASS expansion integration: 136 cards/34 packs/12 fates; native projection, five-rank purchase/ownership, atomic duplicates and legacy directory order");
    }
}
