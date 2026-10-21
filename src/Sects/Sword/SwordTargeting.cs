using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // Immutable targeting projection. Neither camera coordinates nor active views participate
    // in authority decisions. Normal battles have formation slots, not persistent grid cells.
    public static class SwordTargeting
    {
        public sealed class Unit
        {
            public StableId Id;
            public CombatSide Side;
            public FormationRow Row;
            public CombatantKind Kind;
            public bool Downed, Positioned, AreaImmune;
            public int X, Y;
        }

        public sealed class Layout
        {
            public readonly Unit[] Units;
            public readonly bool Tactical;
            public Layout(IEnumerable<Unit> units, bool tactical = false)
            {
                Units = units.ToArray(); Tactical = tactical;
                if (!tactical) ProjectSlots(Units);
            }
        }

        private static readonly ConditionalWeakTable<ModBattleContext, Layout> Contexts = new ConditionalWeakTable<ModBattleContext, Layout>();
        public static Layout For(ModBattleContext context)
            => Contexts.TryGetValue(context, out var layout) ? layout : new Layout(context.Units.Select(u => new Unit {
                Id = u.RuntimeId, Side = u.Side, Row = u.Row, Kind = u.Kind, Downed = u.IsDowned }));

        public static void Attach(ModBattleContext context, BattleState battle)
        {
            var units = context.Units.Select(u => new Unit { Id = u.RuntimeId, Side = u.Side, Row = u.Row,
                Kind = u.Kind, Downed = u.IsDowned }).ToArray();
            foreach (var unit in units)
            {
                unit.AreaImmune = battle.Geometry?.IsAreaImmune(unit.Id) ?? false;
                if (battle.Geometry is MarchState march)
                    unit.Positioned = march.TryGetPosition(unit.Id, out unit.X, out unit.Y);
                else if (battle.Geometry is TowerDefenseState tower && tower.TryGetTileOf(unit.Id, out int tile))
                { unit.Positioned = true; unit.X = tower.Map.X(tile) * 1000; unit.Y = tower.Map.Y(tile) * 1000; }
            }
            Contexts.Add(context, new Layout(units, battle.Geometry != null));
        }

        public static Layout For(RunBattleSnapshot battle)
        {
            var units = battle.Combatants.Select(u => new Unit { Id = u.RuntimeId, Side = u.Side, Row = u.Row,
                Kind = u.Kind, Downed = u.IsDowned }).ToArray();
            foreach (var unit in units)
            {
                if (battle.March != null)
                {
                    foreach (var u in battle.March.Units.Where(u => u.RuntimeId == unit.Id && !u.Downed))
                    { unit.Positioned = true; unit.X = u.X; unit.Y = u.Y; }
                    foreach (var u in battle.March.Enemies.Where(u => u.RuntimeId == unit.Id))
                    { unit.Positioned = true; unit.X = u.X; unit.Y = u.Y; }
                }
                else if (battle.TowerDefense != null)
                {
                    var tower = battle.TowerDefense;
                    int tile = tower.Units.Where(u => u.RuntimeId == unit.Id && !u.Downed).Select(u => u.Tile)
                        .Concat(tower.Enemies.Where(u => u.RuntimeId == unit.Id).Select(u => u.Tile)).DefaultIfEmpty(-1).First();
                    if (tile >= 0) { unit.Positioned = true; unit.X = tile % tower.Width * 1000; unit.Y = tile / tower.Width * 1000; }
                    unit.AreaImmune = tower.Enemies.Any(u => u.RuntimeId == unit.Id && u.IsObject);
                }
            }
            return new Layout(units, battle.March != null || battle.TowerDefense != null);
        }

        private static void ProjectSlots(Unit[] units)
        {
            // Same source order, crowd split and normalized vertical distribution as
            // BattleSpawnCoordinator / BattleCombatantView.Apply. Downed slots stay occupied.
            foreach (var group in units.GroupBy(u => new { u.Side, u.Row }))
            {
                var row = group.ToArray(); int columns = row.Length <= 3 ? 1 : 2;
                int lanes = (row.Length + columns - 1) / columns;
                for (int i = 0; i < row.Length; i++)
                {
                    var u = row[i]; int depth = (int)u.Row / 10 - 1;
                    u.Positioned = u.Row != FormationRow.None;
                    u.X = (u.Side == CombatSide.Player ? -1 : 1) * (2000 + depth * 1000)
                        + (columns == 1 ? 0 : i % columns == 0 ? -208 : 208);
                    u.Y = lanes == 1 ? 0 : -1000 + 2000 * (i / columns) / (lanes - 1);
                }
            }
        }

        public static bool Enabled(JObject card, bool linked)
        {
            var area = card?["area"] as JObject;
            return area != null && (!SwordRules.Bool(area, "linked") || linked);
        }

        // Returns secondaries only, in deterministic geometric order (ID breaks ties).
        // For splash, the caller separately requires an actual primary health hit.
        public static IReadOnlyList<StableId> Select(Layout layout, StableId actorId, StableId primaryId, JObject card, bool linked)
        {
            if (!Enabled(card, linked)) return Array.Empty<StableId>();
            var actor = layout.Units.FirstOrDefault(u => u.Id == actorId);
            var center = layout.Units.FirstOrDefault(u => u.Id == primaryId);
            if (actor == null || center == null || center.Side == actor.Side || !center.Positioned) return Array.Empty<StableId>();
            var area = (JObject)card["area"]; string shape = SwordRules.Text(area, "shape");
            long dx = (long)center.X - actor.X, dy = (long)center.Y - actor.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            var candidates = layout.Units.Where(u => u.Id != primaryId && u.Side == center.Side
                && !u.Downed && u.Kind != CombatantKind.AuxiliaryTarget && u.Positioned && !u.AreaImmune);
            bool InArea(Unit u)
            {
                long x = (long)u.X - center.X, y = (long)u.Y - center.Y;
                if (shape == "row") return !layout.Tactical ? u.Row == center.Row
                    : actor.Positioned && length > 0 && Math.Abs(x * dx + y * dy) <= 250 * length;
                if (shape == "line") return actor.Positioned && length > 0 && x * dx + y * dy > 0
                    && Math.Abs(x * dy - y * dx) <= 250 * length;
                if (shape == "splash" || shape == "radius") return Math.Abs(x) + Math.Abs(y) <= 1000;
                throw new ArgumentException("未知剑宗攻击形状：" + shape);
            }
            return candidates.Where(InArea).OrderBy(u => Math.Abs((long)u.X - center.X) + Math.Abs((long)u.Y - center.Y))
                .ThenBy(u => u.Id).Take(SwordRules.Int(area, "maxTargets", int.MaxValue)).Select(u => u.Id).ToArray();
        }
    }

    [HarmonyPatch(typeof(BattleAuthorityService), "CreateModContext")]
    internal static class SwordGeometryBridge
    {
        private static void Postfix(BattleAuthorityService __instance, ModBattleContext __result)
            => SwordTargeting.Attach(__result, __instance.Battle);
    }
}
