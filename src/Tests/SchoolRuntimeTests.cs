using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private static StableId SchoolCard(string key) => new StableId(SectRegistry.CardPrefix + key);
        private static ModStateSnapshot SchoolState(Fixture fixture) => (ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService), "modState").GetValue(fixture.B);
        private static int SchoolValue(Fixture fixture, string school, string key) => SectRegistry.Read(SchoolState(fixture), SectRegistry.StateKey(school, fixture.Actor.RuntimeId, key));
        private static void RequireFiveStatuses(Fixture fixture, int mask)
        {
            var keys = new[] { "metal", "water", "wood", "fire", "earth" };
            for (int i = 0; i < keys.Length; i++)
                Require(fixture.Actor.Statuses.Any(s => s.StatusId.Value == "yixian.sect.status.five." + keys[i] && s.StackCount == 1) == ((mask & (1 << i)) != 0), "five status mismatch: " + keys[i]);
            Require(!fixture.Actor.Statuses.Any(s => s.StatusId.Value == "yixian.sect.status.five.elements"), "legacy count-only badge remains");
        }
        private Fixture SchoolFixture(string school, bool visual = false,int rank=1)
        {
            var fixture = New(rank,school: school, buyCurrent: true);
            Require(fixture.P.ActiveCardPackIds.Count >= 2 && SectProgression.Owned(fixture.P).All(id => SectRegistry.Owner(SectRegistry.FindPack(id)) == school), "wrong starter pool");
            Require(fixture.P.SetActiveCardPacks(fixture.P.ActiveCardPackIds.Reverse().ToArray()).IsSuccess, "school active selection failed");
            fixture.StartBattle(); Require(fixture.Table.Hand.Count == 3, "school opening hand");
            Require(fixture.Table.Hand.All(h => SectRegistry.Cards.TryGetValue(h.CardId.Value, out var card) && SectRegistry.Owner(card) == school), "foreign card drawn");
            var snapshot = RunAuthoritySnapshotProjector.Create(fixture.A,Slot); var player = snapshot.Players.First(p => p.Slot == Slot);
            var model = BattleUIPresentationMapper.BuildCardTable(snapshot, player, data, battles);
            Require(model.Packs.Count == 12 && model.Packs.All(p => p.Cards.Count == 4 && p.CardDetails.Contains("消耗")), "school actual pack projection/details");
            if (visual) VisualSelfTests.Run(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileNameWithoutExtension(path), school), model, player, snapshot, data, battles, model.Packs[0].Id, false);
            ToughEnemy(fixture.Enemy);
            return fixture;
        }
        private JObject FiveElementLoop()
        {
            var school = FiveElementRules.Lineage; var f = SchoolFixture(school, true);
            var metal = SchoolCard("yixian_five_metal"); var water = SchoolCard("yixian_five_water");
            f.Prepare(metal, water); f.Play(metal); int qi = f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
            f.Play(water);
            Require(SchoolValue(f, school, "elements") == 2 && SchoolValue(f, school, "last") == 2, "five element sequence did not commit");
            Require(f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting) >= qi, "linked qi not restored");
            RequireFiveStatuses(f, 3);
            MemoryRoundTrip(f, school, "elements", 2);
            foreach (var card in SectRegistry.Cards.Where(c => SectRegistry.Owner(c.Value) == school && (int)c.Value["rank"]==1))
            {
                var single = SchoolFixture(school); var id = new StableId(card.Key); single.Prepare(id);
                int shieldBefore = single.Actor.Shield; var first = single.Play(id);
                int firstShield = single.Actor.Shield - shieldBefore;
                int element = (int?)card.Value["element"] ?? 0;
                if (element == 0) continue;
                RequireFiveStatuses(single, 1 << (element - 1));
                single.Prepare(id); int shield = single.Actor.Shield;
                int energy = single.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting); var second = single.Play(id);
                Require(SchoolValue(single, school, "elements") == 1, "same element counted twice");
                RequireFiveStatuses(single, 1 << (element - 1));
                if (element == 1 || element == 4)
                    Require(second.Targets.Sum(t => t.ResolvedDamage) > first.Targets.Sum(t => t.ResolvedDamage), "same element damage bonus missing");
                else if (element == 2)
                    Require(single.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting) > energy - 2, "same element qi bonus missing");
                else
                    Require(single.Actor.Shield - shield > firstShield, "same element shield bonus missing");
                var converge = SchoolCard("yixian_five_return"); single.Prepare(converge); single.Play(converge);
                Require(SchoolValue(single, school, "active") == 0, "convergence did not consume mask");
                RequireFiveStatuses(single, 0);
            }
            return new JObject { ["starter_cards"] = 8, ["executed_cards"] = 8, ["sequence_elements"] = 2, ["same_element_bonuses"] = 5, ["element_statuses_checked"] = true, ["memory_save_restore"] = true };
        }
        private JObject SevenStarLoop()
        {
            var school = SevenStarRules.Lineage; var f = SchoolFixture(school, true);
            var gather = SchoolCard("yixian_star_gather"); var divine = SchoolCard("yixian_star_divine"); var spark = SchoolCard("yixian_star_spark"); var fate = SchoolCard("yixian_star_fate");
            f.Prepare(gather, divine, spark, fate); f.Play(gather); f.Play(divine);
            int hand = f.Table.Hand.Count; f.Play(spark);
            Require(SchoolValue(f, school, "plays") == 3 && f.Table.Hand.Count == hand, "third-card draw not committed exactly once");
            f.Play(fate); Require(SchoolValue(f, school, "omen") == 1, "omen not consumed");
            Require(SectRegistry.Read(SchoolState(f), "cards." + f.Actor.RuntimeId.Value + ".drawPending") == 0, "draw request not drained");
            f.NextRound(); Require(SchoolValue(f, school, "plays") == 0 && SchoolValue(f, school, "omen") == 1, "round reset erased resource or retained position");
            MemoryRoundTrip(f, school, "omen", 1);
            foreach (var card in SectRegistry.Cards.Where(c => SectRegistry.Owner(c.Value) == school && (int)c.Value["rank"]==1))
            {
                var single = SchoolFixture(school); single.Prepare(new StableId(card.Key)); single.Play(new StableId(card.Key));
            }
            return new JObject { ["starter_cards"] = 8, ["executed_cards"] = 8, ["third_card_draw"] = true, ["omen_consumed"] = true, ["memory_save_restore"] = true };
        }
        private void MemoryRoundTrip(Fixture f, string school, string key, int expected)
        {
            var actorId = f.Actor.RuntimeId;
            Require(f.A.TryGetPlayerBattlefieldId(Slot, out var field), "save roundtrip battlefield missing");
            f.System(RunDeveloperCommand.WinBattle);
            // TickBattlefield advances turns only. CompleteBattlefield is the native settlement
            // command that merges terminal vitals and ModState into the run before saving.
            Require(f.A.Ledger.ExecuteSystem(RunSystemAction.CompleteBattlefield(field)).IsSuccess, "native battlefield settlement failed");
            Require(SectRegistry.Read(f.A.Run.ModState, SectRegistry.StateKey(school, actorId, key)) == expected, "school state not merged by settlement");
            var mapper = new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("school-selftest", data.CatalogVersion, data.GameplayContentHash, SessionMode.Solo), data, new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data), battles);
            Require(mapper.TryCapture(f.A.Run, out var saved, out var error), "memory save capture: " + error);
            Require(mapper.TryRestore(saved, out var restored, out error), "memory save restore: " + error);
            Require(restored.Players[0].Lineages.Any(l => l.LineageId.Value == school), "restored school missing");
            Require(SectRegistry.Read(restored.ModState, SectRegistry.StateKey(school, actorId, key)) == expected, "restored school state missing");
        }
        private JObject SingleSchoolGate()
        {
            foreach (var school in SectRegistry.Sects.Keys)
            {
                var f = New(school: school, buyCurrent: false);
                foreach (var other in SectRegistry.Sects.Keys.Where(id => id != school))
                {
                    int points = f.P.RewardPoints; long revision = f.A.Run.Revision;
                    var result = f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot, new StableId(other), revision));
                    Require(!result.IsSuccess && f.P.RewardPoints == points && f.A.Run.Revision == revision, "second school accepted or charged");
                }
                f.Upgrade(CardTableCatalog.LineageId);
                Require(f.P.Lineages.Count == 2, "native lineage combination lost");
            }
            return new JObject { ["schools"] = SectRegistry.Sects.Count, ["rejections_atomic"] = true, ["native_combination"] = true };
        }
    }
}
