using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject LineageStats()
        {
            int[] budget = { 8, 18, 44, 84, 132 }, prices = { 15, 55, 130, 240, 380 };
            int[][] sword = { new[] {6,0,2,0}, new[] {9,3,6,0}, new[] {22,6,16,0}, new[] {42,12,30,0}, new[] {66,18,48,0} };
            int[][] star = { new[] {0,2,0,6}, new[] {0,4,3,11}, new[] {0,9,7,28}, new[] {0,17,13,54}, new[] {0,27,21,84} };
            int[] main = { 6, 12, 30, 58, 92 }, support = { 2, 6, 14, 26, 40 };
            var rows = new JArray();
            foreach (string id in new[] { "lineage.BL01", "lineage.BL02", "lineage.BL03", "lineage.BL04", "lineage.BL14" })
            {
                Require(data.TryGetLineage(new StableId(id), out var native), "native budget reference missing");
                for (int rank = 1; rank <= 5; rank++)
                {
                    int sum = native.Steps[rank - 1].Passive.Values.Where(v => new[] { "muscleFlat", "reactionFlat", "constitutionFlat", "energyFlat" }.Contains(v.Key)).Sum(v => v.IntValue);
                    Require(sum == budget[rank - 1], "native common attribute budget changed: " + id + "/" + rank);
                }
            }
            foreach (string school in new[] { SwordRules.Lineage, SevenStarRules.Lineage, FiveElementRules.Lineage })
            foreach (int primary in school == FiveElementRules.Lineage ? new[] { 40, 20, 30 } : new[] { 40 })
            {
                var f = New(0, school: school, buyCurrent: false);
                if (school == FiveElementRules.Lineage)
                    Require(f.Send(RunCommand.PurchaseAttributePoint(Slot, primary == 40 ? ExchangeAttribute.Energy : primary == 20 ? ExchangeAttribute.Reaction : ExchangeAttribute.Constitution, 60, f.A.Run.Revision)).IsSuccess, "arrange automatic primary");
                var before = AutoPresentation(f); var observed = new JArray();
                for (int rank = 1; rank <= 5; rank++)
                {
                    int i = rank - 1, points = f.P.RewardPoints;
                    f.Upgrade(new StableId(school));
                    Require(points - f.P.RewardPoints == prices[i], "stat alignment changed price");
                    int[] a = school == SwordRules.Lineage ? sword[i] : school == SevenStarRules.Lineage ? star[i]
                        : new[] { 0, primary == 20 ? main[i] : 0, primary == 30 ? main[i] : primary == 40 ? support[i] : 0, primary == 40 ? main[i] : support[i] };
                    var expected = new StatBlock(before.Muscle + a[0], before.Reaction + a[1], before.Constitution + a[2], before.Energy + a[3]);
                    var actual = AutoPresentation(f);
                    Require(actual == expected && a.Sum() == budget[i], "rank must replace, not accumulate, exact attribute budget: " + school + "/" + rank + " got " + actual + " expected " + expected);
                    int poolBase = (school == SwordRules.Lineage ? new[] {12,22,36,54,78} : school == SevenStarRules.Lineage ? new[] {9,18,28,40,54} : new[] {8,15,24,34,46})[i];
                    var snapshot = RunAuthoritySnapshotProjector.Create(f.A, Slot);
                    var player = snapshot.Players.First(p => p.Slot == Slot);
                    int maximum = EnergyPoolRules.CalculateMaximum(EnergyPoolKind.Fighting, poolBase, expected);
                    Require(player.DerivedStats.MaximumMagic == maximum, "actual native pool differs from aligned attributes: " + school + "/" + rank);
                    for (int n = 0; n < 3; n++) Require(AutoPresentation(f) == actual, "repeated projection accumulates attributes");
                    observed.Add(new JObject { ["rank"] = rank, ["attributes"] = actual.ToString(), ["budget"] = a.Sum(), ["pool_base"] = poolBase, ["actual_maximum"] = maximum });
                }
                var view = RunAuthoritySnapshotProjector.Create(f.A, Slot);
                var visual = primary == 40 ? VisualSelfTests.LineageStatLadder(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path), "lineage-stats", school), school, view.Players.First(p => p.Slot == Slot), view, data, battles) : null;
                rows.Add(new JObject { ["school"] = school, ["primary"] = primary, ["ranks"] = observed, ["visual"] = visual });
            }
            return new JObject { ["native_reference_budgets"] = new JArray(budget), ["native_purchase_projections"] = rows };
        }
    }
}
