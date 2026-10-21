using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static void SwordReplacementTests()
    {
        var stats = new StatBlock(20, 30, 20, 10);
        var actor = Unit(attributes: stats);
        ModBattleHit Hit(int health = 5, int shield = 0, bool direct = true, StableId target = default)
            => new(ActorId, target.IsEmpty ? EnemyId : target, health, shield, DamageType.Physical, direct, false);
        var shaped = Unit(attributes: stats);
        var shape = Run(SwordRules.Play, Id(Card(11)), State(("intent", 2)), actor: shaped);
        Check(Damage(shape) == 24 + 16 + 6 && S(shape, "spentIntent") == 2, "shape damage is muscle and consumes old intent once ");
        Check(S(shape, "intent") == 0, "shape hit reward cannot fund its own damage ");
        var result = Run(SwordRules.Hits, state: shape.State, actor: shaped, hits: new[] { Hit(), Hit() });
        Check(S(result, "intent") == 3, "shape gives one level-correct reward per cast ");
        Check(S(Run(SwordRules.Hits, state: result.State, actor: shaped, hits: new[] { Hit() }), "intent") == 3, "duplicate hit callback cannot farm intent ");
        foreach (var invalid in new[] { Hit(0, 30), Hit(direct: false), Hit(target: ActorId) })
            Check(S(Run(SwordRules.Hits, state: shape.State, actor: shaped, hits: new[] { invalid }), "intent") == 0, "shape requires direct enemy health hit ");

        var guarded = Unit(attributes: stats);
        var reflection = Run(SwordRules.Play, Id(Card(8)), actor: guarded);
        Check(Damage(reflection) == 33 + 20, "reflection damage is muscle ");
        var shieldHit = Run(SwordRules.Hits, state: reflection.State, actor: guarded, hits: new[] { Hit() });
        int delayed = 21 + 10;
        Check(S(shieldHit, "nextShield") == delayed && Effect(shieldHit, ModBattleEffectKind.Shield) == 0, "reflection queues body-scaled shield, never immediate ");
        Check(S(Run(SwordRules.Hits, state: shieldHit.State, actor: guarded, hits: new[] { Hit() }), "nextShield") == delayed, "reflection duplicate callback cannot stack ");
        var second = Run(SwordRules.Play, Id(Card(8)), shieldHit.State, actor: guarded);
        var secondHit = Run(SwordRules.Hits, state: second.State, actor: guarded, hits: new[] { Hit() });
        Check(S(secondHit, "nextShield") == delayed * 2, "separate successful reflection casts accumulate ");
        var next = Run(SwordRules.Round, state: secondHit.State, round: 2, actor: guarded);
        Check(Effect(next, ModBattleEffectKind.Shield) == delayed * 2 && S(next, "nextShield") == 0, "next round pays accumulated shield and clears queue ");
        Check(Effect(Run(SwordRules.Round, state: next.State, round: 2, actor: guarded), ModBattleEffectKind.Shield) == 0, "round replay cannot repeat shield ");
        Check(S(Run(SwordRules.Hits, state: reflection.State, actor: guarded, hits: new[] { Hit(0, 100) }), "nextShield") == 0, "shield-only reflection does not queue shield ");
        var other = Unit(slot: 2, attributes: stats);
        Check(Effect(Run(SwordRules.Round, state: shieldHit.State, round: 2, actor: other), ModBattleEffectKind.Shield) == 0, "queued reflection shield cannot transfer to teammate ");

        var flurry = Unit(attributes: stats);
        var flow = Run(SwordRules.Play, Id(Card(37)), State(("intent", 2), ("power", 1)), actor: flurry);
        Check(flow.Operations.Count == 4 && Damage(flow) == (6 + 12 + 6 + 3) * (4), "flow has separate native operations and shared intent bonus each segment ");
        Check(S(flow, "spentIntent") == 2 && S(flow, "intent") == 0, "flow consumes intent once for entire card ");
        foreach (int id in new[] { 8, 11, 37 }) Check(SwordRules.ResourceCost(Card(id), 0, 0, false) == 4, "new original face one lingqi equals four qi " + id);
        Check(new[] { "yixian_sword_intent2", "yixian_sword_intent3", "yixian_sword_intent4" }.All(k => SwordRules.Packs.Single(p => (string)p["key"] == k)["cards"].Count() == 4), "replacement packs restored to four");
        Console.WriteLine("PASS three sword replacements: original fees, muscle/body/reaction formulas, direct health-hit gates, delayed stack/consume/isolation and four native segments");
    }
}
