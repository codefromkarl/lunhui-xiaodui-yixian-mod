using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        // Real commands in private memory runs: exercise queue draining, repeated effects,
        // maximum-health refresh, shield interception and card replay under Mono detours.
        private JObject NativeCrashBoundaryStress()
        {
            var rows = new JArray();
            foreach (string school in new[] { SwordRules.Lineage, FiveElementRules.Lineage, SevenStarRules.Lineage })
            {
                var f = New(5, school: school, buyCurrent: false);
                foreach (var pack in SectRegistry.Packs.Where(p => SectRegistry.Owner(p) == school))
                    if (school!=FiveElementRules.Lineage&&!f.P.HasPurchasedShelfAbility(PackId(pack))) f.Buy(PackId(pack));
                if (school == SwordRules.Lineage)
                    foreach (string fate in new[] { FateRules.Rhyme, FateRules.CloudGuard, FateRules.FormationGuard, FateRules.CloudSea }) f.Buy(new StableId(fate));
                else
                    foreach (var fate in SectRegistry.Fates.Where(p => SectRegistry.Owner(p.Value) == school)) f.Buy(new StableId(fate.Key));
                f.StartBattle(); ToughEnemy(f.Enemy);
                if (school == FiveElementRules.Lineage)
                    foreach (int source in new[] { 7000001, 7000003, 7000006, 7000009, 7000011 })
                    { var seal = SchoolSource(school, source); f.Prepare(seal); f.Play(seal); }
                var sequence = school == SwordRules.Lineage
                    ? new[] { Card(65), Card(25), Card(36), Card(64), Card(45) }
                    : school == FiveElementRules.Lineage
                    ? new[] { SchoolSource(school, 7000057), SchoolSource(school, 7000100), SchoolSource(school, 7000112), SchoolSource(school, 7000113) }
                    : new[] { SchoolCard("yixian_star_storm"), SchoolCard("yixian_star_refuge"), SchoolCard("yixian_star_luminary"), SchoolCard("yixian_star_rebirth") };
                int played = 0;
                for (int cycle = 0; cycle < 12; cycle++)
                    foreach (var id in sequence)
                    {
                        f.Prepare(id);
                        f.System(RunDeveloperCommand.SetVitals, int.MinValue, f.Actor.MaximumMagic, 1000);
                        f.Play(id); played++;
                        var state = SchoolState(f);
                        Require(!state.Entries.Any(e => e.PackageId == SectRegistry.Package && e.Key == OriginalHealthRules.PendingKey), "life queue replay/leak after " + id);
                        Require(SectRegistry.Read(state, "cards." + f.Actor.RuntimeId.Value + ".drawPending") == 0, "draw queue leak after " + id);
                        Require(f.Actor.CurrentHealth >= 0 && f.Actor.CurrentHealth <= f.Actor.CurrentMaximumHealth && f.Actor.CurrentMaximumHealth >= 1, "invalid native vitals after " + id);
                        Require(f.Actor.Statuses.Count(s => s.StatusId == OriginalHealthRules.MaximumStatus) <= 1, "maximum-health status multiplied");
                        Require(f.Actor.Statuses.Count(s => s.StatusId.Value.StartsWith("yixian.sect.status.star.guard", StringComparison.Ordinal)) <= 32, "guard instance growth unbounded");
                    }
                rows.Add(new JObject { ["school"] = school, ["cycles"] = 12, ["cards_executed"] = played, ["native_queues_drained"] = true, ["vitals_valid"] = true });
            }
            return new JObject { ["schools"] = rows, ["scope"] = "bounded sequential command stress, not a long-duration soak" };
        }

        private JObject NativeOpeningReset()
        {
            foreach (string school in new[] { FiveElementRules.Lineage, SevenStarRules.Lineage })
            {
                var f = SchoolFixture(school);
                var actor = f.Actor.RuntimeId;
                var field = AccessTools.Field(typeof(BattleAuthorityService), "modState");
                var old = (ModStateSnapshot)field.GetValue(f.B);
                var entries = new[]
                {
                    new ModStateEntry(SectRegistry.Package, SectRegistry.StateKey(school, actor, "persistentCount"), ModValueKind.Integer, 2),
                    new ModStateEntry(SectRegistry.Package, SectRegistry.StateKey(school, actor, "consumed.old"), ModValueKind.Integer, 1),
                    new ModStateEntry(SectRegistry.Package, "cards." + actor.Value + ".drawPending", ModValueKind.Integer, 3),
                    new ModStateEntry(SectRegistry.Package, "other.actor.sentinel", ModValueKind.Integer, 7)
                };
                field.SetValue(f.B, old.Apply(SectRegistry.Package, entries));
                // Native initialization uses Dictionary.Add: its real precondition is no table
                // for this slot. Remove only this private fixture's table before recreating it.
                ((System.Collections.IDictionary)AccessTools.Field(typeof(BattleAuthorityService), "cardTables").GetValue(f.B)).Remove(Slot);
                // Invoke the native lifecycle boundary, including the actual Harmony prefix.
                AccessTools.Method(typeof(BattleAuthorityService), "InitializeCardTablesOrThrow").Invoke(f.B, new object[] { (PlayerSlotId?)Slot });
                Require(f.Table.Hand.Count == 3, "opening hand affected by stale consumed state");
                var fresh = SchoolState(f);
                Require(SectRegistry.Read(fresh, SectRegistry.StateKey(school, actor, "persistentCount")) == 0
                    && SectRegistry.Read(fresh, SectRegistry.StateKey(school, actor, "consumed.old")) == 0
                    && SectRegistry.Read(fresh, "cards." + actor.Value + ".drawPending") == 0, "opening retained previous battle's private state");
                Require(SectRegistry.Read(fresh, "other.actor.sentinel") == 7, "opening reset removed another actor's state");
            }
            return new JObject { ["schools"] = 2, ["actual_initialization_prefix"] = true, ["stale_state_removed_before_draw"] = true, ["other_actor_preserved"] = true };
        }
    }
}
