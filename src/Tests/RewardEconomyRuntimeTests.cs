using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject RewardEconomy()
        {
            int[] steps={15,55,130,240,380},packPrices={0,20,35,50,75};var rows=new JArray();
            for(int i=1;i<=15;i++)
            {
                Require(data.TryGetLineage(new StableId("lineage.BL"+i.ToString("D2")),out var native),"native lineage missing");
                Require(native.Steps.Select(step=>step.Price).SequenceEqual(steps),"Yixian progression differs from native lineage "+i);
            }
            foreach(string school in SectRegistry.Sects.Keys)
            {
                var f=New(0,school:school,buyCurrent:false);int start=f.P.RewardPoints;var paid=new JArray();
                for(int rank=1;rank<=5;rank++)
                {
                    int points=f.P.RewardPoints;f.Upgrade(new StableId(school));
                    Require(points-f.P.RewardPoints==steps[rank-1],"native lineage price differs: "+school+"/"+rank);
                    paid.Add(steps[rank-1]);
                    if(school==FiveElementRules.Lineage)Require(FiveDeckRules.Unlocked(f.P).Count==8+2*(rank-1),"Five unlock library charged or changed");
                    else Require(SectProgression.Owned(f.P).Count==(rank==1?2:3),"starter/foundation gifts no longer free");
                }
                Require(start-f.P.RewardPoints==820,"total progression must cost 820");
                int packsPaid=0;
                foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==school))
                {
                    if(school==FiveElementRules.Lineage){Require((int)pack["price"]==0,"retired Five data pack shows a reward fee");continue;}
                    if(f.P.HasPurchasedShelfAbility(PackId(pack)))continue;
                    int points=f.P.RewardPoints;f.Buy(PackId(pack));int price=packPrices[(int)pack["rank"]-1];
                    Require(points-f.P.RewardPoints==price,"native pack price differs: "+pack["key"]);packsPaid+=price;
                }
                int fatePaid=0;
                foreach(var fate in f.Economy.ShelfAbilities.Where(a=>a.SourceId.Value==school&&a.AbilityId.Value.StartsWith("yixian.sect.fate.",StringComparison.Ordinal)))
                {
                    int points=f.P.RewardPoints;f.Buy(fate.AbilityId);Require(points-f.P.RewardPoints==20,"native fate fee must be 20");fatePaid+=20;
                    long revision=f.A.Run.Revision;Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,fate.AbilityId,revision)).IsSuccess&&f.P.RewardPoints==points-20&&f.A.Run.Revision==revision,"duplicate fate charged or mutated revision");
                }
                Require(fatePaid==80,"four fates must cost 80");
                int expected=school==SwordRules.Lineage?1365:school==SevenStarRules.Lineage?1240:900;
                Require(start-f.P.RewardPoints==expected,"full collection wallet delta differs");
                rows.Add(new JObject{["school"]=school,["steps"]=paid,["lineage_total"]=820,["packs_paid"]=packsPaid,["fates_paid"]=fatePaid,["total_paid"]=expected});
            }
            foreach(string school in SectRegistry.Sects.Keys)
            {
                var boundary=New(0,school:school,buyCurrent:false);
                Require(boundary.P.SpendRewardPoints(boundary.P.RewardPoints-14).IsSuccess,"arrange lineage 14-point boundary");
                long revision=boundary.A.Run.Revision;
                Require(!boundary.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,new StableId(school),revision,default(StableId))).IsSuccess
                    &&boundary.P.RewardPoints==14&&boundary.A.Run.Revision==revision&&boundary.P.Lineages.Count==0,"lineage insufficient-funds rejection not atomic");
                boundary.System(RunDeveloperCommand.AddRewardPoints,1);boundary.Upgrade(new StableId(school));
                Require(boundary.P.RewardPoints==0,"exactly 15 points must buy first lineage rank");
            }
            foreach(string school in new[]{SwordRules.Lineage,SevenStarRules.Lineage,FiveElementRules.Lineage})
            {
                var f=New(3,school:school,buyCurrent:false);
                var fate=f.Economy.ShelfAbilities.First(a=>a.SourceId.Value==school&&a.AbilityId.Value.StartsWith("yixian.sect.fate.",StringComparison.Ordinal));
                Require(f.P.SpendRewardPoints(f.P.RewardPoints-19).IsSuccess,"arrange 19-point boundary");
                long revision=f.A.Run.Revision;var owned=f.P.PurchasedShelfAbilityIds.ToArray();
                Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,fate.AbilityId,revision)).IsSuccess&&f.P.RewardPoints==19&&f.A.Run.Revision==revision&&f.P.PurchasedShelfAbilityIds.SequenceEqual(owned),"19-point fate rejection not atomic");
                f.System(RunDeveloperCommand.AddRewardPoints,1);f.Buy(fate.AbilityId);Require(f.P.RewardPoints==0,"exactly 20 points must buy one fate");
            }
            var visualFixture=New(3,buyCurrent:false);var snapshot=RunAuthoritySnapshotProjector.Create(visualFixture.A,Slot);
            var visual=VisualSelfTests.RewardEconomyPrices(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),System.IO.Path.GetFileNameWithoutExtension(path),"reward-prices"),snapshot.Players.First(p=>p.Slot==Slot),snapshot,data,battles);
            return new JObject{["native_price_matrix"]=rows,["native_lineages_compared"]=15,["fourteen_fifteen_lineage_boundary_atomic"]=true,["nineteen_twenty_boundary_atomic"]=true,["free_gifts_and_five_library"]=true,["visual"]=visual};
        }
    }
}
