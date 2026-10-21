using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static void RoundDrawTests()
    {
        foreach (var school in SectRegistry.Sects.Keys)
        {
            var actor = Unit(school: school);
            string draw = "cards." + ActorId.Value + ".drawPending";
            string round = "cards." + ActorId.Value + ".drawRound";
            var first = Run(SectDrawRules.Round, actor: actor);
            Check(first.Effects.Count == 0 && first.Operations.Count == 0
                && SectRegistry.Read(first.State, draw) == 0, "opening draws no extra cards and grants no shield: " + school);
            var second = Run(SectDrawRules.Round, state: first.State, round: 2, actor: actor);
            Check(second.Effects.Count == 0 && second.Operations.Count == 0
                && SectRegistry.Read(second.State, draw) == 2, "later round only queues two cards: " + school);
            var repeat = Run(SectDrawRules.Round, state: second.State, round: 2, actor: actor);
            Check(SectRegistry.Read(repeat.State, draw) == 2 && repeat.Effects.Count == 0, "duplicate round queues no more cards: " + school);
            var drained = second.State.Apply(SectRegistry.Package, new[] { new ModStateEntry(SectRegistry.Package, draw, ModValueKind.Removed) });
            var third = Run(SectDrawRules.Round, state: drained, round: 3, actor: actor);
            Check(SectRegistry.Read(third.State, draw) == 2 && SectRegistry.Read(third.State, round) == 3, "next round queues two after previous draw drained: " + school);
            var pending = first.State.Apply(SectRegistry.Package, new[] { new ModStateEntry(SectRegistry.Package, draw, ModValueKind.Integer, 1) });
            var combined = Run(SectDrawRules.Round, state: pending, round: 2, actor: actor);
            Check(SectRegistry.Read(combined.State, draw) == 3, "round draw preserves another queued card: " + school);
            var reset = Run(SectDrawRules.Start, state: second.State, actor: actor);
            Check(SectRegistry.Read(reset.State, round) == 0 && SectRegistry.Read(reset.State, draw) == 0, "new battle resets draw state: " + school);
        }
        var native = Run(SectDrawRules.Round, actor: Unit(sect: false), round: 2);
        Check(native.Effects.Count == 0 && native.State.Entries.Count == 0, "native-only lineage keeps original draws");
        var ally = Unit(slot: 2);
        var multi = Run(SectDrawRules.Round, round: 2, extra: new[] { ally });
        Check(multi.Effects.Count == 0 && SectRegistry.Read(multi.State, "cards." + ActorId.Value + ".drawPending") == 2
            && SectRegistry.Read(multi.State, "cards." + ally.RuntimeId.Value + ".drawPending") == 2, "each player gets independent draws without combat bonuses");

        // Removing the universal bonus must restore single-target card behavior even in a group.
        var enemy = new CombatantState(new StableId("test.extra.enemy"), new StableId("enemy.test"), CombatantKind.Enemy,
            CombatSide.Enemy, FormationRow.Back, 300, 20, 0, 0, 0, BuffCatalog.Empty);
        var extra = (ModCombatantSnapshot)PrivateCtor(typeof(ModCombatantSnapshot), enemy, ModUnitLoadout.None, null, null, null, null);
        foreach (var item in new (string school, string source, Action<ModBattleContext, CodeBattlePlan> play)[] {
            (SwordRules.Lineage, "1000032", SwordRules.Play),
            (FiveElementRules.Lineage, "7000004", FiveElementRules.Play),
            (SevenStarRules.Lineage, "4000003", SevenStarRules.Play) })
        {
            var card = SectRegistry.Cards.Values.Single(c => (string)c["sourceId"] == item.source);
            var plan = Run(item.play, Id(card), actor: Unit(school: item.school), extra: new[] { extra });
            Check(!plan.Effects.Any(e => e.Kind == ModBattleEffectKind.Damage)
                && plan.Operations.All(o => o.Targets.All(id => id == EnemyId)), "single-target attacks have no universal splash: " + item.school);
        }
        Console.WriteLine("PASS automatic draws: first/later rounds, duplicate guard, queued effects, actor isolation and no universal splash/shield");
    }
}
