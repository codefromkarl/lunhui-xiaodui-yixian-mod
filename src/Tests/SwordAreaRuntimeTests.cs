using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject SwordAreas()
        {
            var results = new JArray();
            foreach (int source in new[] { 2, 9, 14, 46 })
            {
                var f = New(3, seed: (ulong)(73600 + source));
                foreach (var pack in SwordRules.Packs.Where(p => SwordRules.Int(p, "rank") <= 3 && !f.P.HasPurchasedShelfAbility(PackId(p))))
                    f.Buy(PackId(pack));
                f.System(RunDeveloperCommand.SetAttribute, (int)ExchangeAttribute.Reaction, 1000);
                f.StartBattle();
                // Arrange a regular formation, not a screen-space fake. Only this private
                // fixture is changed. Native AddCombatant establishes the canonical order.
                foreach (var row in new[] { FormationRow.Front, FormationRow.Middle, FormationRow.Back })
                {
                    int count = f.B.Battle.Combatants.Count(u => u.Side == CombatSide.Enemy && u.Row == row);
                    int wanted = Math.Max(3, count % 2 == 0 ? count + 1 : count);
                    for (int i = count; i < wanted; i++)
                    {
                        var enemy = new CombatantState(new StableId("yixian.area." + (int)row + "." + i), f.Enemy.DefinitionId,
                            CombatantKind.Enemy, CombatSide.Enemy, row, 10000, 20, 0, 0, 0, f.B.Battle.Buffs);
                        Require(f.B.Battle.AddCombatant(enemy).IsSuccess, "area fixture insert");
                    }
                }
                foreach (var enemy in f.B.Battle.Combatants.Where(u => u.Side == CombatSide.Enemy)) ToughEnemy(enemy);
                var front = f.B.Battle.Combatants.Where(u => u.Side == CombatSide.Enemy && u.Row == FormationRow.Front).ToArray();
                var primary = front[front.Length / 2];
                var cardId = Card(source); var card = SwordRules.Cards[cardId.Value];
                f.Prepare(cardId);
                // Linked condition is authoritative ModState, exactly as after a cloud card.
                if (source == 2 || source == 14)
                {
                    var state = SchoolState(f).Apply(SectRegistry.Package, new[] {
                        new ModStateEntry(SectRegistry.Package, SwordRules.StateKey(f.Actor.RuntimeId, "cloud"), ModValueKind.Integer, 1) });
                    AccessTools.Field(typeof(BattleAuthorityService), "modState").SetValue(f.B, state);
                }
                var request = new ModActionRequest(cardId.Value, cardId, new[] { primary.RuntimeId }, primary.RuntimeId,
                    primary.Row, Array.Empty<BattleEffectValue>(), true);
                var nativeContext = (ModBattleContext)AccessTools.Method(typeof(BattleAuthorityService), "CreateModContext").Invoke(f.B,
                    new object[] { ModBattleHook.ScriptedAction, f.Actor.RuntimeId, cardId, f.Actor.RuntimeId, default(StableId), null, null, request, null });
                var affected = SwordTargeting.Select(SwordTargeting.For(nativeContext), f.Actor.RuntimeId, primary.RuntimeId, card, true);
                Require(affected.Count > 0, "area fixture must have secondary targets: " + source);
                var snapshot = RunAuthoritySnapshotProjector.Create(f.A, Slot);
                Require(affected.SequenceEqual(SwordTargeting.Select(SwordTargeting.For(snapshot.Battle), f.Actor.RuntimeId,
                    primary.RuntimeId, card, true)), "authority and UI snapshot geometry differ");
                var before = f.B.Battle.Combatants.ToDictionary(u => u.RuntimeId, u => u.CurrentHealth);
                int shield = f.Actor.Shield;
                var held = f.Table.Hand.First(h => h.CardId == cardId);
                var played = f.Action(cardId, primary.RuntimeId, CardTableRules.EncodePlay(held.InstanceId));
                Require(played.IsSuccess, "formal area play: " + played.ErrorCode);
                foreach (var unit in f.B.Battle.Combatants.Where(u => u.Side == CombatSide.Enemy))
                {
                    bool shouldHit = unit.RuntimeId == primary.RuntimeId || affected.Contains(unit.RuntimeId);
                    Require((unit.CurrentHealth < before[unit.RuntimeId]) == shouldHit, "formal area changed wrong target: " + unit.RuntimeId);
                }
                if (source != 46) Require(f.Actor.Shield == shield, "area attack granted unrelated shield");
                Require(f.State("attacks") == 1 && f.State("intent") == 0, "area counted as several cards/intent payments");
                Require(SwordRules.ReadText(SchoolState(f), f.Actor.RuntimeId, "areaTargets") == "", "pending area scope leaked after action");
                results.Add(new JObject { ["card"] = cardId.Value, ["primary"] = primary.RuntimeId.Value,
                    ["secondary_targets"] = new JArray(affected.Select(id => id.Value)), ["formal_command"] = true,
                    ["snapshot_preview_matches"] = true, ["damage_observed"] = true });
            }
            return new JObject { ["cards"] = results, ["physical_mouse_or_vfx_render_test"] = false };
        }
    }
}
