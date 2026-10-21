using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject AutomaticRoundDraw()
        {
            var rows = new JArray();
            foreach (var school in SectRegistry.Sects.Keys)
            {
                var f = New(school: school, buyCurrent: false);
                f.StartBattle();
                Require(f.Table.Hand.Count == 3, "round draw first hand: " + school);
                foreach (var enemy in f.B.Battle.Combatants.Where(u => u.Side == CombatSide.Enemy)) ToughEnemy(enemy);
                Require(f.Actor.SetVitals(f.Actor.CurrentHealth, 100000).IsSuccess, "protect round draw fixture");
                var opening = f.Table.Hand.Select(h => h.InstanceId).ToArray();
                f.NextRound();
                Require(f.Table.Hand.Count == 5 && opening.All(id => f.Table.Hand.Any(h => h.InstanceId == id)), "round two must retain three and add two: " + school);
                int ap = f.Actor.ActionPoints;
                int qi = f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                var retained = f.Table.Hand.Select(h => h.InstanceId).ToArray();
                AccessTools.Method(typeof(BattleAuthorityService), "RunModHook").Invoke(f.B, new object[] { ModBattleHook.RoundStart, default(StableId), default(StableId) });
                Require(f.Table.Hand.Select(h => h.InstanceId).SequenceEqual(retained)
                    && f.Actor.ActionPoints == ap && f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting) == qi, "duplicate round hook changed hand or charged resources: " + school);
                f.NextRound();
                Require(f.Table.Hand.Count == 7, "round three must add two: " + school);
                f.System(RunDeveloperCommand.SetVitals, int.MinValue, int.MinValue, 10);
                for (int i = 0; i < 2; i++) Require(f.Action(CardTableRules.DrawActionId).IsSuccess, "draw to nine");
                f.NextRound();
                Require(f.Table.Hand.Count == 10, "round draw must cap at ten: " + school);
                f.NextRound();
                Require(f.Table.Hand.Count == 10, "full hand must remain capped: " + school);

                rows.Add(new JObject { ["school"] = school, ["opening_hand"] = 3, ["round_two_hand"] = 5, ["round_three_hand"] = 7, ["hand_cap"] = 10 });
            }
            return new JObject { ["schools"] = rows };
        }
    }
}
