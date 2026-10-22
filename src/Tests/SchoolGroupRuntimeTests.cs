using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject SchoolGroups()
        {
            var results = new JArray();
            foreach (var card in SectRegistry.Cards.Values.Where(c => c["groupAttack"] != null))
            {
                string school = SectRegistry.Owner(card);
                var id = new StableId(SectRegistry.CardPrefix + (string)card["key"]);
                var f = SchoolFixture(school, rank: 5, required: id);
                for (int i = 0; i < 3; i++)
                {
                    var enemy = new CombatantState(new StableId("yixian.group." + i), f.Enemy.DefinitionId,
                        CombatantKind.Enemy, CombatSide.Enemy, i == 2 ? FormationRow.Back : f.Enemy.Row,
                        10000, 20, 0, 0, 0, f.B.Battle.Buffs);
                    Require(f.B.Battle.AddCombatant(enemy).IsSuccess, "group fixture insert");
                }
                foreach (var enemy in f.B.Battle.Combatants.Where(u => u.Side == CombatSide.Enemy)) ToughEnemy(enemy);
                // Arrange resources only in a private memory fixture. Actual attacks
                // still enter through native Prepare / encoded player play commands.
                var values = school == SevenStarRules.Lineage
                    ? new[] { ("omen", 10), ("plays", 2), ("positions", 36), ("star", 2) }
                    : new[] { ("active", 31), ("waterForce", 5), ("attack", 2), ("lostShield", 36) };
                var state = SchoolState(f).Apply(SectRegistry.Package, values.Select(v => new ModStateEntry(SectRegistry.Package,
                    SectRegistry.StateKey(school, f.Actor.RuntimeId, v.Item1), ModValueKind.Integer, v.Item2)).ToArray());
                AccessTools.Field(typeof(BattleAuthorityService), "modState").SetValue(f.B, state);
                f.Prepare(id);
                var snapshot = RunAuthoritySnapshotProjector.Create(f.A, Slot);
                var affected = SchoolGroupAttack.Targets(SwordTargeting.For(snapshot.Battle), f.Actor.RuntimeId, f.Enemy.RuntimeId, card, true);
                Require(affected.Length >= 3, "group fixture needs at least three affected enemies");
                var before = f.B.Battle.Combatants.ToDictionary(u => u.RuntimeId, u => u.CurrentHealth);
                int omen = SchoolValue(f, school, "omen");
                f.Play(id);
                foreach (var enemy in f.B.Battle.Combatants.Where(u => u.Side == CombatSide.Enemy))
                    Require((enemy.CurrentHealth < before[enemy.RuntimeId]) == affected.Contains(enemy.RuntimeId),
                        "native group command damaged wrong target: " + card["name"] + "/" + enemy.RuntimeId);
                if (school == SevenStarRules.Lineage)
                {
                    int expected = (int?)card["rules"]?["chanceRepeats"] ?? ((int?)card["rules"]?["randomMax"] > 0
                        ? (int?)card["rules"]?["randomRepeats"] ?? 1 : 0);
                    Require(omen - SchoolValue(f, school, "omen") == expected, "native omen spent per target instead of wave");
                    Require(SchoolValue(f, school, "plays") == 3, "native group advanced card count several times");
                }
                results.Add(new JObject { ["card"] = id.Value, ["formal_command"] = true,
                    ["damaged_targets"] = new JArray(affected.Select(x => x.Value)),
                    ["losses"] = new JArray(f.B.Battle.Combatants.Where(u => affected.Contains(u.RuntimeId))
                        .Select(u => new JObject { ["target"] = u.RuntimeId.Value, ["health_loss"] = before[u.RuntimeId] - u.CurrentHealth })) });
            }
            return new JObject { ["cards"] = results, ["physical_mouse_or_vfx_render_test"] = false };
        }
    }
}
