using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void FiveCultivationTests()
    {
        FiveCultivation.Validate();
        Check(FiveCultivation.SelectPrimary(new StatBlock(999, 20, 10, 100)) == 40, "muscle does not select Five primary");
        Check(FiveCultivation.SelectPrimary(new StatBlock(0, 101, 100, 100)) == 20, "highest reaction selected");
        Check(FiveCultivation.SelectPrimary(new StatBlock(0, 100, 101, 100)) == 30, "highest constitution selected");
        Check(FiveCultivation.SelectPrimary(new StatBlock(0, 100, 100, 100), 20) == 20, "ties keep previous primary");
        Check(FiveCultivation.SelectPrimary(new StatBlock(0, 100, 100, 100), 10) == 40, "no valid preference ties use stable energy first");
        var metal = new JObject { ["damage"] = 18, ["damageScaling"] = new JArray(new JObject { ["attribute"] = 20, ["bp"] = 7000 }, new JObject { ["attribute"] = 40, ["bp"] = 3000 }) };
        var fire = new JObject { ["damage"] = 18, ["damageScaling"] = new JArray(new JObject { ["attribute"] = 40, ["bp"] = 7000 }, new JObject { ["attribute"] = 20, ["bp"] = 3000 }) };
        var stats = new StatBlock(10, 20, 10, 100);
        Check(FiveCultivation.Bonus(fire, "damage", stats, 40) == 76, "matching native multi-attribute sum is not taxed");
        Check(FiveCultivation.Bonus(metal, "damage", stats, 40) == 70, "off-stat card receives seventy percent fallback");
        Check(FiveCultivation.Bonus(metal, "damage", new StatBlock(0, 100, 20, 20), 20) == 76, "specialist metal beats off-stat fallback");
        Check(FiveCultivation.Bonus(metal, "damage", new StatBlock(0, 60, 20, 100), 40) == 72, "investing secondary raises actual native result");
        Check(FiveCultivation.Bonus(fire, "damage", new StatBlock(0, 200, 0, 0), 40) == 60, "locked primary does not suppress later real secondary gains");
        Check(FiveCultivation.Bonus(metal, "damage", new StatBlock(0, 1, 0, 1), 30) == 1, "combine fractional native terms before floor");
        Check(FiveCultivation.Bonus(new JObject { ["shield"] = 27 }, "shield", stats, 40) == 0, "fixed effects never gain phantom attributes");
        var cards = SectRegistry.Cards.Values.Where(c => SectRegistry.Owner(c) == FiveElementRules.Lineage).ToArray();
        foreach (var card in cards)
        foreach (var scaling in ((JObject)card["rules"]).Properties().Where(p => p.Name.EndsWith("Scaling")))
        foreach (int primary in new[] { 40, 20, 30 })
        {
            string key = scaling.Name[..^7]; int sum = ((JArray)scaling.Value).Sum(t => (int)t["bp"]);
            Check(FiveCultivation.Bonus((JObject)card["rules"], key, new StatBlock(20, 20, 20, 20), primary) == 20 * sum / 10000, "balanced attributes retain full native budget " + card["name"] + "/" + key);
            for (int value = 0; value <= 120; value += 20)
            {
                var x = new StatBlock(0, value, value / 2, 100);
                int native = ((JArray)scaling.Value).Sum(t => AttributeScaling.Read((CombatScalingAttribute)(int)t["attribute"], x) * (int)t["bp"]) / 10000;
                Check(FiveCultivation.Bonus((JObject)card["rules"], key, x, primary) >= native, "fallback never reduces native scaling " + card["name"]);
            }
        }
        var actor = Unit(school: FiveElementRules.Lineage, rank: 5, attributes: stats);
        var start = Run(FiveElementRules.Start, actor: actor);
        string stateKey = SectRegistry.StateKey(FiveElementRules.Lineage, ActorId, FiveCultivation.BattleKey);
        Check(SectRegistry.Read(start.State, stateKey) == 40, "battle snapshots auto primary");
        Check(SchoolState(start, FiveElementRules.Lineage, "active") == 0, "auto primary grants no activation");
        var changed = Unit(school: FiveElementRules.Lineage, rank: 5, attributes: new StatBlock(10, 200, 10, 100));
        int locked = 0;
        Run((context, plan) => locked = FiveCultivation.Read(new BattleTurn(context, plan, FiveElementRules.Lineage)), state: start.State, actor: changed);
        Check(locked == 40, "temporary stats and deck changes cannot switch primary in battle");
        Check(SectRegistry.Read(Run(FiveElementRules.Start, state: start.State, actor: changed).State, stateKey) == 20, "next battle selects updated persistent primary");

        var owner = SchoolPlayer(FiveElementRules.Lineage, 3);
        var baseMods = new List<StatModifier> { new(new StableId("test.external"), new StatBlock(0, 89, 0, 90)) };
        var current = baseMods.ToList(); FiveCultivation.AppendGrowth(null, owner, current);
        Check(current.Count == 2 && current.Last().Attributes.Energy == 30 && current.Last().Attributes.Constitution == 14 && current.Last().Attributes.Reaction == 0, "44-point budget splits 30 energy / 14 body");
        owner.RecalculateDerivedStats(DerivedStatCalculator.Calculate(owner.Birth.FinalBirthStats, current, owner.EnergyPools.Primary.Kind));
        var switched = new List<StatModifier> { new(new StableId("test.external"), new StatBlock(0, 91, 0, 90)) };
        FiveCultivation.AppendGrowth(null, owner, switched);
        Check(switched.Last().Attributes.Reaction == 30 && switched.Last().Attributes.Energy == 14 && switched.Last().Attributes.Constitution == 0, "reaction exceeds external energy by ONE: old primary/support growth cannot self-lock energy");
        int points = owner.RewardPoints; long revision = owner.Revision;
        var again = new List<StatModifier> { switched.First() }; FiveCultivation.AppendGrowth(null, owner, again);
        Check(again.Last().Attributes.Reaction == 30 && owner.Revision == revision && owner.RewardPoints == points, "repeated projection is pure and non-cumulative");
        Check(!owner.PurchasedShelfAbilityIds.Any(id => id.Value.StartsWith(FiveCultivation.Prefix)), "automatic growth creates no purchase tokens");
        for (int rank = 1; rank <= 5; rank++)
        foreach (int primary in new[] { 40, 20, 30 })
        {
            var p = SchoolPlayer(FiveElementRules.Lineage, rank); var m = FiveCultivation.GrowthModifier(p, primary);
            int[] main = {6,12,30,58,92}, support = {2,6,14,26,40};
            var expected = new StatBlock(0, primary == 20 ? main[rank-1] : 0, primary == 30 ? main[rank-1] : primary == 40 ? support[rank-1] : 0, primary == 40 ? main[rank-1] : support[rank-1]);
            Check(m.Attributes == expected, "exact primary/support native growth budget " + rank + "/" + primary);
            var derived = DerivedStatCalculator.Calculate(new StatBlock(5,5,5,5), new[] {m}, EnergyPoolKind.Fighting);
            Check(derived.MaximumMagic == 2 * (5 + expected.Energy) + 5, "native Five attribute capacity without duplicate base " + rank + "/" + primary);
        }
        var offhand = Player(native: true, sect: false);
        Check(offhand.AddLineage(new StableId(FiveElementRules.Lineage)).IsSuccess, "native primary plus Five offhand");
        ((List<LineageRankState>)typeof(PlayerRunState).GetField("lineages", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(offhand))[1] = new LineageRankState(new StableId(FiveElementRules.Lineage), 3);
        foreach (bool separatePool in new[] { false, true })
        {
            if (separatePool) offhand.SetEnergyPools(new EnergyPoolSet(offhand.EnergyPools.Primary, new EnergyPoolState(EnergyPoolKind.Fighting, 1, 30), offhand.EnergyPools.Style));
            var modifier = FiveCultivation.GrowthModifier(offhand, 20);
            Check(modifier.Attributes.Reaction == 30 && modifier.Attributes.Energy == 14 && modifier.MaximumMagicFlat == 0, "native offhand attribute budget and no duplicate pools " + separatePool);
        }
        var other = SchoolPlayer(SevenStarRules.Lineage, 3); var foreignMods = new List<StatModifier>();
        FiveCultivation.AppendGrowth(null, other, foreignMods); Check(foreignMods.Count == 0, "non-Five receives no projection");
        Console.WriteLine("PASS automatic Five: highest external attribute, no self-lock, ties, pure multi-scaling vs 70% fallback, battle lock and non-cumulative growth");
    }
}
