using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void DecisionTests()
    {
        foreach(var route in FiveBuildGuidance.Routes)
        {
            var available=FiveDeckRules.Candidates(1).Select(FiveDeckRules.CardId).ToList();
            for(int rank=2;rank<=5;rank++)
            {
                var candidates=FiveDeckRules.Candidates(rank).Select(FiveDeckRules.CardId).ToArray();
                var learned=FiveBuildGuidance.Suggest(route,candidates,2,rank);
                Check(learned.Count==2&&learned.Distinct().Count()==2&&learned.All(candidates.Contains),"learning route exact two at current rank "+route.Name);
                Check(learned.Select(id=>(string)SectRegistry.Cards[id.Value]["name"]).SequenceEqual(route.Learn[rank-2]),"learning trajectory uses authored choices, not candidate fallback");
                available.AddRange(learned);var copy=available.ToArray();
                var proposal=FiveBuildGuidance.Suggest(route,available,8,rank);
                Check(proposal.Count==8&&proposal.Distinct().Count()==8&&proposal.All(available.Contains),"eight are actually unlocked "+route.Name);
                Check(copy.SequenceEqual(available),"suggestions cannot mutate library");
                Check(proposal.Count(id=>(int?)SectRegistry.Cards[id.Value]["rules"]?["array"]>0||(int?)SectRegistry.Cards[id.Value]["rules"]?["activationArray"]>0)<=2,"recommendation cannot promise three simultaneous arrays");
                var draft=new FiveDeckDraft(available,Array.Empty<StableId>(),8);draft.Apply(proposal);Check(draft.Ready,"apply only fills draft");
                var before=draft.Selected.ToArray();bool rejected=false;
                try{draft.Apply(proposal.Take(7).ToArray());}catch(ArgumentException){rejected=true;}
                Check(rejected&&before.SequenceEqual(draft.Selected),"invalid proposal preserves pending selection");
            }
        }
        foreach(var route in SwordBuilds.StarRoutes)
        foreach(int rank in Enumerable.Range(1,5))
        {
            var owned=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==SevenStarRules.Lineage&&(int)p["rank"]<=rank).Select(SectRegistry.PackId).ToArray();
            var proposal=SwordBuilds.Recommend(route,rank,owned);
            Check(proposal.Selected.Count==Math.Min(3,owned.Length)&&proposal.Selected.All(owned.Contains),"star route respects rank and purchased shelf");
        }
        var rows=JObject.Parse(File.ReadAllText(Mod+"/content/statuses.json"))["operations"].OfType<JObject>().Where(s=>((string)s["id"]).StartsWith(SectDecisionHints.ReadoutPrefix)).ToArray();
        Check(rows.Length>10&&rows.All(s=>(int)s["fields"]["display"]==30&&(int)s["fields"]["maximumStacks"]==1&&!s["fields"]["modifiers"].Any()),"readouts hidden, one stack, no modifiers");
        RunCombatantSnapshot Actor(params (string id,int count,int flat)[] states)
        {
            var statuses=states.Select((s,i)=>new RunCombatStatusSnapshot(new StableId("readout."+i),new StableId(s.id),ActorId,Enumerable.Repeat(new CombatStatusStackState(0),s.count).ToArray(),flatAmount:s.flat)).ToArray();
            return new RunCombatantSnapshot(ActorId,new StableId("test.hero"),CombatantKind.Player,CombatSide.Player,FormationRow.Front,new PlayerSlotId(1),100,100,0,40,40,3,0,0,0,0,1,statuses,Array.Empty<RunAbilityCooldownSnapshot>());
        }
        var star=Actor((SevenStarRules.StatusPrefix+"plays",2,0),(SevenStarRules.StatusPrefix+"star",4,0),(SevenStarRules.StatusPrefix+"position3",1,0));
        var astral=SectRegistry.Cards.Values.First(c=>SectRegistry.Owner(c)==SevenStarRules.Lineage&&(string)c["name"]=="星弈·夹");
        Check(SectDecisionHints.Describe(astral,star,null).Contains("第3格 · 星位，每段攻击＋12"),"next slot is played count plus one, not last resolved slot");
        var wave=SectRegistry.Cards.Values.Single(c=>(string)c["name"]=="水灵·波澜");
        Check(SectDecisionHints.Describe(wave,Actor(("yixian.sect.status.five.last",1,0)),null).Contains("相生"),"metal links to water without permanent activation");
        Check(SectDecisionHints.Describe(wave,Actor(),null).Contains("仍需8斗气"),"unready wave cannot show zero cost");
        var deadwood=SectRegistry.Cards.Values.Single(c=>(string)c["name"]=="枯木逢春");
        Check(SectDecisionHints.Describe(deadwood,Actor((SectDecisionHints.ReadoutPrefix+"star.healingTotal",1,15000)),null).Contains("增伤＋3000"),"flat readout preserves values beyond icon stack cap");
        Console.WriteLine("PASS decision hints and nine route guidance: reachable unlocks/drafts, rank/ownership, hidden snapshot counters, next star slot and conditional costs");
    }
}
