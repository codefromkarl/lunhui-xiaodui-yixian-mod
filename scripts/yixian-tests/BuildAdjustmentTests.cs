using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void BuildAdjustmentTests()
    {
        foreach(var route in SwordBuilds.FiveRoutes)
            for(int rank=1;rank<=5;rank++)
            {
                var eligible=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage&&(int)p["rank"]<=rank).ToArray();
                var owned=eligible.Select(SectRegistry.PackId).ToArray();
                var proposal=SwordBuilds.Recommend(route,rank,owned.Concat(SwordRules.Packs.Select(SectRegistry.PackId)));
                Check(proposal.Selected.Count==Math.Min(3,owned.Length),"five recommendation legal minimum");
                Check(proposal.Selected.All(owned.Contains)&&proposal.Missing.Count==0,"five recommendation never crosses school/rank");
                var anchor=route.Key=="earth_cycle"?"yixian_five_cycle":"yixian_five_roots";
                Check(proposal.Selected.Contains(SectRegistry.PackId(eligible.Single(p=>(string)p["key"]==anchor))),"five recommendation retains the declared seal anchor");
                var sparse=eligible.Where(p=>(int)p["rank"]==1).Select(SectRegistry.PackId).ToArray();
                var fallback=SwordBuilds.Recommend(route,rank,sparse);
                Check(fallback.Selected.Count==2&&fallback.Selected.All(sparse.Contains),"five sparse ownership cannot equip unpurchased packs");
            }
        var noSeal=SectRegistry.Packs.Where(p=>new[]{"yixian_five_evergreen","yixian_five_forest_sea","yixian_five_cycle_seed"}.Contains((string)p["key"])).Select(SectRegistry.PackId).ToArray();
        Check(SwordBuilds.PoolSummary(FiveElementRules.Lineage,noSeal).Contains("永久印记：无"),"missing seals are visible without banning the pool");
        var roots=SectRegistry.Packs.Single(p=>(string)p["key"]=="yixian_five_roots");
        var coverage=SwordBuilds.PoolSummary(FiveElementRules.Lineage,new[]{SectRegistry.PackId(roots)});
        Check(coverage.Contains("金、水、木、火")&&!coverage.Contains("缺印："),"seal labels use original element identities");
        var spirit=SwordBuilds.Recommend(SwordBuilds.Routes.Single(r=>r.Key=="spirit"),5,SwordRules.Packs.Select(SectRegistry.PackId));
        Check(spirit.Selected.Select(id=>(string)SectRegistry.FindPack(id)["key"]).ToHashSet().SetEquals(new[]{"yixian_sword_spirit5","yixian_sword_spirit4","yixian_sword_cloud4"}),"spirit preserves two cores and uses the declared recovery bridge");
        foreach(var route in SwordBuilds.FiveRoutes.Where(r=>r.ReuseFoundationGift))
            foreach(var gift in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage&&(int)p["rank"]==2))
            {
                var owned=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage&&((int)p["rank"]==1||p==gift)).Select(SectRegistry.PackId).ToArray();
                var proposal=SwordBuilds.Recommend(route,2,owned);
                Check(proposal.Selected.ToHashSet().SetEquals(owned)&&proposal.Missing.Count==0&&!proposal.IsTransition,
                    "foundation reuses either actual gift without recommending another purchase");
            }
        var config=JObject.Parse(File.ReadAllText("data/balance-refined-scenarios.json"));
        foreach(string school in new[]{SwordRules.Lineage,FiveElementRules.Lineage})
            foreach(var route in SwordBuilds.RoutesFor(school))
                for(int rank=1;rank<=5;rank++)
                {
                    var eligible=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school&&(int)p["rank"]<=rank).ToArray();
                    var schoolKey=school.Split('.').Last();
                    var first=config["rankPriorities"]?[schoolKey]?[route.Key]?[rank.ToString()]?.Values<string>()??Enumerable.Empty<string>();
                    var keys=first.Concat(config["routes"][schoolKey][route.Key].Values<string>()).Distinct().Where(k=>eligible.Any(p=>(string)p["key"]==k)).Take(3).ToHashSet();
                    var proposed=SwordBuilds.Recommend(route,rank,eligible.Select(SectRegistry.PackId));
                    Check(keys.SetEquals(proposed.Selected.Select(id=>(string)SectRegistry.FindPack(id)["key"])),"sword test inputs match player-facing production recommendations");
                    Check(route.ReuseFoundationGift==(config["reuseFoundationGiftRoutes"]?[schoolKey]?.Values<string>().Contains(route.Key)??false),"gift-aware config matches production policy");
                }
        Console.WriteLine("PASS historical five pack-proposal calculations and current sword recommendation/config agreement");
    }
}
