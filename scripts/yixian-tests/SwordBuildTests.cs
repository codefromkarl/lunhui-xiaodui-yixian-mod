using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void SwordBuildTests()
    {
        var service=new HubService(ProjectedEconomy);var hub=new HubState(HubAccessKind.Full,1,new[]{new PlayerSlotId(1)});
        var p=Player(5);SectProgression.Refresh(p);
        var first=SwordRules.Packs.First(pack=>(int)pack["rank"]==2);
        Check(service.PurchaseShelfAbility(hub,p,SwordRules.PackId(first)).IsSuccess,"build arrange first purchase");SectProgression.Refresh(p);
        Check(p.ActiveCardPackIds.Count==3,"minimum third pack filled");var established=p.ActiveCardPackIds.Reverse().ToArray();
        Check(SectProgression.SetActive(p,established).IsSuccess,"build order arrangement");
        foreach(var pack in SwordRules.Packs.Where(pack=>(int)pack["price"]>0&&!p.HasPurchasedShelfAbility(SwordRules.PackId(pack))))
        {
            Check(service.PurchaseShelfAbility(hub,p,SwordRules.PackId(pack)).IsSuccess,"buy pack for build");SectProgression.Refresh(p);
            Check(p.ActiveCardPackIds.SequenceEqual(established),"purchase did not equip/unorder pack");
        }
        for(int rank=1;rank<=5;rank++)
        {
            var owned=SwordRules.Packs.Where(pack=>(int)pack["rank"]<=rank).Select(SwordRules.PackId).ToArray();
            foreach(var route in SwordBuilds.Routes)
            {
                var proposal=SwordBuilds.Recommend(route,rank,owned);
                Check(proposal.Selected.Count==Math.Min(3,owned.Length)&&proposal.Selected.Distinct().Count()==proposal.Selected.Count,"route valid size "+rank);
                Check(proposal.Selected.All(owned.Contains)&&proposal.Missing.Count==0,"route ownership and realm "+rank);
                var stats=SwordBuilds.Inspect(proposal.Selected);Check(stats.Cards==4*proposal.Selected.Count&&stats.Cloud<=stats.Cards&&stats.Qi<=stats.Cards&&stats.Draw<=stats.Cards,"pool exact size");
                Check(proposal.IsTransition==(route.Key=="spirit"&&rank<4),"spirit maturity marked");
            }
        }
        var sparse=SwordRules.Packs.Where(pack=>(int)pack["rank"]==1).Select(SwordRules.PackId).ToArray();
        foreach(var route in SwordBuilds.Routes)
        {
            var proposal=SwordBuilds.Recommend(route,5,sparse);
            Check(proposal.Selected.All(sparse.Contains)&&proposal.Selected.Count==2&&proposal.IsTransition&&proposal.Missing.Count==3,"unowned core never equipped");
        }
        var clouds=new[]{40,42,44}.Select(i=>SwordRules.PackId(SwordRules.Packs.Single(pack=>(int)pack["index"]==i))).ToArray();
        var cloudPool=SwordBuilds.Inspect(clouds);Check(cloudPool.Cloud==9&&cloudPool.Cards==12&&cloudPool.CloudPercent==75,"cloud pool probability");
        var diluted=SwordBuilds.Inspect(clouds.Concat(new[]{SwordRules.PackId(SwordRules.Packs.Single(pack=>(int)pack["index"]==41))}));
        Check(diluted.Cloud==9&&diluted.Cards==16&&diluted.CloudPercent==56,"dilution visible without rounding up");
        Check(SwordBuilds.Inspect(Array.Empty<StableId>()).CloudPercent==0,"empty pool summary");
        Check(SwordBuilds.Routes.Select(route=>string.Join(",",SwordBuilds.Recommend(route,5,SectProgression.Owned(p)).Selected)).Distinct().Count()==3,"late routes have different pools");
        Check(p.ActiveCardPackIds.SequenceEqual(established),"recommendations are read-only");
        Console.WriteLine("PASS sword builds: purchase/equip separation, native minimum, ordered save inputs, five-realm proposals/transition/missing core, pool probabilities, distinct routes");
    }
}
