using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static ModCombatantSnapshot AreaEnemy(string id, FormationRow row, bool dead = false, CombatantKind kind = CombatantKind.Enemy)
    {
        var e = new CombatantState(new StableId(id), new StableId("enemy.test"), kind, CombatSide.Enemy,
            row, 300, 20, 0, 0, 0, BuffCatalog.Empty);
        if (dead) e.SetVitals(0, 0);
        return (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot), e, ModUnitLoadout.None, null, null, null, null);
    }
    static ModBattlePlan AreaRun(JObject card, ModCombatantSnapshot[] units, ModStateSnapshot state = null, bool preview = false)
    {
        var action = Id(card);
        var request = new ModActionRequest(action.Value, action, new[] { EnemyId }, EnemyId, FormationRow.Front, Array.Empty<BattleEffectValue>(), preview);
        var context = new ModBattleContext(ModBattleHook.ScriptedAction, 1, 1, 1, ActorId, action, units,
            state ?? ModStateSnapshot.Empty, new DeterministicRngState(17, 3, 0), ActorId, default, null, null, request, "test.battle");
        var plan = (CodeBattlePlan)PrivateCtor(typeof(CodeBattlePlan), SectRegistry.Package, context);
        SwordRules.Play(context, plan);
        return (ModBattlePlan)Method(typeof(CodeBattlePlan), "Build").Invoke(plan, null);
    }
    static int AreaDamage(ModBattlePlan plan, StableId id) => plan.Operations.Where(o => o.Targets.Contains(id))
        .Sum(o => o.Values.First(v => v.Key == "e0.flat").IntegerValue);
    static SwordTargeting.Unit AreaPoint(string id, int x, int y, bool dead = false, CombatSide side = CombatSide.Enemy,
        CombatantKind kind = CombatantKind.Enemy, bool immune = false) => new() {
            Id = new StableId(id), Side = side, Kind = kind, Row = FormationRow.Front,
            Positioned = true, X = x, Y = y, Downed = dead, AreaImmune = immune };

    static void SwordAreaTests()
    {
        var cloud = State(("cloud", 1), ("intent", 3), ("power", 2));
        var actor = Unit(); var primary = Enemy();
        var middle = AreaEnemy("test.middle", FormationRow.Middle);
        var back = AreaEnemy("test.back", FormationRow.Back);
        var units = new[] { actor, primary, middle, back };
        var plain = AreaRun(Card(2), units);
        Check(plain.Operations.Count == 1 && plain.Operations[0].Targets.SequenceEqual(new[] { EnemyId }), "flying thrust unlinked stays single");
        var line = AreaRun(Card(2), units, cloud);
        int full = AreaDamage(line, EnemyId);
        Check(line.Operations.Count == 2 && AreaDamage(line, middle.RuntimeId) == full / 2 && AreaDamage(line, back.RuntimeId) == 0,
            "flying thrust linked pierces nearest one behind, half damage floor");
        Check(S(line, "intent") == 0 && S(line, "attacks") == 1 && S(line, "cloud") == 2, "area consumes intent/counts cloud and attack once");
        var side = AreaEnemy("test.side", FormationRow.Front);
        var rowUnits = new[] { actor, primary, side, back };
        var row = AreaRun(Card(14), rowUnits, cloud);
        Check(AreaDamage(row, side.RuntimeId) == AreaDamage(row, EnemyId) / 2 && AreaDamage(row, back.RuntimeId) == 0,
            "no edge linked hits only same formation");
        Check(AreaRun(Card(14), rowUnits).Operations.Count == 0, "no edge unlinked has neither main nor secondary attack");
        var sea = Unit(rank: 3, abilities: new[] { FateRules.CloudSea });
        var seaRow = AreaRun(Card(14), new[] { sea, primary, side });
        Check(AreaDamage(seaRow, side.RuntimeId) > 0, "cloud sea enables area without previous cloud");

        var mirror = AreaRun(Card(46), units);
        int mirrorFull = AreaDamage(mirror, EnemyId);
        Check(mirror.Operations.Count == 2 && AreaDamage(mirror, middle.RuntimeId) == mirrorFull * 3000 / 10000
            && AreaDamage(mirror, back.RuntimeId) == 0, "mirror hits adjacent formation slot, not distant row");
        var mirrorSingle = AreaRun(Card(46), new[] { actor, primary });
        Check(AreaDamage(mirrorSingle, EnemyId) == mirrorFull
            && Effect(mirrorSingle, ModBattleEffectKind.Shield) == Effect(mirror, ModBattleEffectKind.Shield), "mirror primary formula and shield unchanged");
        var preview = AreaRun(Card(46), units, cloud, true);
        var actual = AreaRun(Card(46), units, cloud);
        Check(preview.Operations.Select(o => (o.Targets[0], o.Values.First(v => v.Key == "e0.flat").IntegerValue))
            .SequenceEqual(actual.Operations.Select(o => (o.Targets[0], o.Values.First(v => v.Key == "e0.flat").IntegerValue))), "preview and execution use identical geometry/damage planning");
        Check(SwordRules.ReadInt(cloud, ActorId, "intent") == 3, "area planning does not mutate input");

        var thunder = AreaRun(Card(9), units);
        Check(thunder.Operations.Count == 1 && !thunder.Effects.Any(e => e.Kind == ModBattleEffectKind.Damage), "thunder does not splash before health-hit hook");
        var shield = Run(SwordRules.Hits, state: thunder.State,
            hits: new[] { new ModBattleHit(ActorId, EnemyId, 0, 5, DamageType.Physical, true, false) }, extra: new[] { middle, back });
        Check(shield.Effects.Count == 0 && S(shield, "hitHandled") == 0, "shield-only main hit never triggers thunder splash");
        var payoff = Run(SwordRules.Hits, state: thunder.State,
            hits: new[] { new ModBattleHit(ActorId, EnemyId, 5, 0, DamageType.Physical, true, false) }, extra: new[] { middle, back });
        var damage = payoff.Effects.Where(e => e.Kind == ModBattleEffectKind.Damage).ToArray();
        Check(damage.Length == 2 && damage[0].Target == EnemyId && damage[1].Target == middle.RuntimeId
            && damage[1].Amount == damage[0].Amount * 4000 / 10000, "thunder splash is forty percent of payoff, not main attack");
        var repeated = Run(SwordRules.Hits, state: payoff.State,
            hits: new[] { new ModBattleHit(ActorId, EnemyId, 5, 0, DamageType.Physical, true, false),
                new ModBattleHit(ActorId, middle.RuntimeId, 5, 0, DamageType.Physical, false, false) }, extra: new[] { middle });
        Check(repeated.Effects.Count == 0, "thunder payoff/splash never recursively retriggers");
        var onlySide = Run(SwordRules.Hits, state: thunder.State,
            hits: new[] { new ModBattleHit(ActorId, middle.RuntimeId, 5, 0, DamageType.Physical, true, false) }, extra: new[] { middle });
        Check(onlySide.Effects.Count == 0 && S(onlySide, "hitHandled") == 0, "secondary health damage cannot activate primary hit reward");
        var indirect = Run(SwordRules.Hits, state: thunder.State,
            hits: new[] { new ModBattleHit(ActorId, EnemyId, 5, 0, DamageType.Physical, false, false) });
        Check(indirect.Effects.Count == 0, "non-attack damage cannot start thunder payoff");

        var points = new SwordTargeting.Layout(new[] {
            AreaPoint(ActorId.Value, -2000, 0, side: CombatSide.Player, kind: CombatantKind.Player),
            AreaPoint(EnemyId.Value, 0, 0), AreaPoint("near.b", 1000, 0), AreaPoint("near.a", 0, 1000),
            AreaPoint("near.c", 0, -1000), AreaPoint("diagonal", 1000, 1000), AreaPoint("far", 3000, 0), AreaPoint("before", -1500, 0),
            AreaPoint("dead", 0, 1, dead: true), AreaPoint("ally", 1, 0, side: CombatSide.Player),
            AreaPoint("auxiliary", 2, 0, kind: CombatantKind.AuxiliaryTarget), AreaPoint("immune", 3, 0, immune: true) }, true);
        var splash = SwordTargeting.Select(points, ActorId, EnemyId, Card(9), false);
        Check(splash.Select(id => id.Value).SequenceEqual(new[] { "near.a", "near.b" }), "splash nearest two, deterministic tie, excludes diagonal/dead/ally/auxiliary/immune");
        Check(SwordTargeting.Select(points, ActorId, EnemyId, Card(46), false).Count == 3, "radius one is local, not global");
        var ray = SwordTargeting.Select(points, ActorId, EnemyId, Card(2), true);
        Check(ray.Count == 1 && ray[0].Value == "near.b", "tactical ray follows caster direction and nearest behind target");
        var single = SwordTargeting.Select(points, ActorId, EnemyId, Card(32), true);
        Check(single.Count == 0, "unmodified three peaks never gains area");
        var raw = new[] {
            new CombatantState(ActorId, new StableId("player.test"), CombatantKind.Player, CombatSide.Player,
                FormationRow.Front, 300, 20, 0, 0, 0, BuffCatalog.Empty, new PlayerSlotId(1)),
            new CombatantState(EnemyId, new StableId("enemy.test"), CombatantKind.Enemy, CombatSide.Enemy,
                FormationRow.Front, 300, 20, 0, 0, 0, BuffCatalog.Empty),
            new CombatantState(new StableId("test.middle"), new StableId("enemy.test"), CombatantKind.Enemy, CombatSide.Enemy,
                FormationRow.Middle, 300, 20, 0, 0, 0, BuffCatalog.Empty) };
        var battle = new BattleState(new StableId("test.geometry"), 1, BuffCatalog.Empty, BattlePhase.Player, 1);
        foreach (var unit in raw) Check(battle.AddCombatant(unit).IsSuccess, "geometry fixture unit");
        var ordered = battle.Combatants.Select(u => (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot), u, ModUnitLoadout.None, null, null, null, null)).ToArray();
        var nativeContext = new ModBattleContext(ModBattleHook.ScriptedAction, 1, 1, 1, ActorId, Id(Card(2)), ordered,
            ModStateSnapshot.Empty, new DeterministicRngState(17, 3, 0));
        SwordTargeting.Attach(nativeContext, battle);
        var projected = new RunBattleSnapshot(battle.BattleId, 1, BattlePhase.Player, 1, battle.Revision, 0, 0,
            battle.Combatants.Select(u => (RunCombatantSnapshot)PrivateCtor(typeof(RunCombatantSnapshot), u, 0)).ToArray(),
            Array.Empty<RunBattleActionSnapshot>(), Array.Empty<PlayerSlotId>(), Array.Empty<RunEnemyIntentSnapshot>());
        foreach (var card in SwordRules.Cards.Values.Where(c => c["area"] != null))
            Check(SwordTargeting.Select(SwordTargeting.For(nativeContext), ActorId, EnemyId, card, true)
                .SequenceEqual(SwordTargeting.Select(SwordTargeting.For(projected), ActorId, EnemyId, card, true)), "native context and UI snapshot same affected set " + card["name"]);
        var replay = Run(SwordRules.Play, Id(Card(64)), mirror.State, extra: new[] { middle, back });
        Check(AreaDamage(replay, middle.RuntimeId) > 0 && AreaDamage(replay, back.RuntimeId) == 0,
            "repeated mirror retains its radius instead of reverting to single/global damage");
        foreach (var card in SwordRules.Cards.Values.Where(c => c["area"] != null))
            Check(SwordRules.ResourceCost(card, 0, 0, false) == ((int)card["rules"]["cost"]) * 4, "area preserves printed cost " + card["name"]);
        var clear = Run(SwordRules.Resolved, Id(Card(9)), thunder.State);
        Check(SwordRules.ReadText(clear.State, ActorId, "primaryTarget") == "" && SwordRules.ReadText(clear.State, ActorId, "areaTargets") == "", "action cleanup removes pending area scope");
        Console.WriteLine("PASS four sword areas: linked line/row, radius, health-only thunder, primary rewards, preview equality and unchanged costs");
    }
}
