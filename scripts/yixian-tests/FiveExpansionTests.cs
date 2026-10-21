using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void FiveExpansionTests()
    {
        const string school = FiveElementRules.Lineage;
        var cards = SectRegistry.Cards.Values.Where(c => SectRegistry.Owner(c) == school).ToList();
        var packs = SectRegistry.Packs.Where(p => SectRegistry.Owner(p) == school).ToList();
        var source = JArray.Parse(File.ReadAllText("data/sources/original-correction/five-elements-alliance-cards.json")).OfType<JObject>().ToDictionary(c => (string)c["id"]);
        var costs = JObject.Parse(File.ReadAllText("data/sources/original-correction/original-costs.json"))["cards"];
        var realms = new[] { "炼气", "筑基", "金丹", "元婴", "化神" };
        var actor = Unit(school: school, rank: 5, energy: 0);
        JObject CardByOriginal(int id) => cards.Single(c => (int)c["rules"]["originalRule"] == id);
        StableId CardId(int id) => new(SectRegistry.CardPrefix + (string)CardByOriginal(id)["key"]);
        ModStateSnapshot Saved(params (string, int)[] values) => new(values.Select(v => new ModStateEntry(SectRegistry.Package, SectRegistry.StateKey(school, ActorId, v.Item1), ModValueKind.Integer, v.Item2)).ToArray());
        // A completed command drains life requests. Preserve requests between Play/Hits/Resolved,
        // but never replay a previous command's queue when starting the next card.
        ModStateSnapshot Drained(ModStateSnapshot state) => state?.Apply(SectRegistry.Package, new[] { new ModStateEntry(SectRegistry.Package, OriginalHealthRules.PendingKey, ModValueKind.Removed) });
        ModBattlePlan Play(int id, ModStateSnapshot state = null, ModCombatantSnapshot unit = null, int round = 1)
            => Run(FiveElementRules.Play, CardId(id), Drained(state), round, actor: unit ?? actor);
        ModBattlePlan Resolve(int id, ModBattlePlan played, ModCombatantSnapshot unit = null, int round = 1)
            => Run(FiveElementRules.Resolved, CardId(id), played.State, round, actor: unit ?? actor);
        ModBattlePlan Cycle(int id, ModStateSnapshot state = null, ModCombatantSnapshot unit = null, int round = 1) => Resolve(id, Play(id, state, unit, round), unit, round);
        ModBattlePlan Hit(int id, ModBattlePlan played, ModBattleHit[] hits, ModCombatantSnapshot unit = null)
            => Run(FiveElementRules.Hits, CardId(id), played.State, actor: unit ?? actor, hits: hits);
        ModBattleHit Health(int hp, int shield = 0) => new(ActorId, EnemyId, hp, shield, DamageType.Physical, true, false);
        int Read(ModBattlePlan plan, string key) => SchoolState(plan, school, key);
        int Draw(ModBattlePlan plan) => SectRegistry.Read(plan.State, "cards." + ActorId.Value + ".drawPending");
        JArray Requests(ModBattlePlan plan)
        {
            var entry = plan.State.Entries.FirstOrDefault(e => e.PackageId == SectRegistry.Package && e.Key == OriginalHealthRules.PendingKey);
            return string.IsNullOrEmpty(entry?.Text) ? new JArray() : JArray.Parse(entry.Text);
        }
        int Requested(ModBattlePlan plan, string kind, StableId target) => Requests(plan).OfType<JObject>().Where(q => (string)q["kind"] == kind && (string)q["target"] == target.Value).Sum(q => (int)q["amount"]);
        int Qi(ModBattlePlan plan) => plan.Effects.Where(e => e.Kind == ModBattleEffectKind.Energy).Sum(e => e.Energy.Amount);
        bool Ready(int last, int element, int mask = 0)
        {
            bool answer = false;
            Run((context, plan) => answer = FiveElementRules.ElementReady(new BattleTurn(context, plan, school), element), state: Saved(("last", last), ("active", mask)), actor: actor);
            return answer;
        }

        Check(cards.Count == 48 && packs.Count == 12, "five original 48-card / 12-pack inventory");
        Check(cards.Select(c => (string)c["sourceId"]).Distinct().Count() == 48, "five unique original sources");
        Check(packs.All(p => p["cards"].Count() == 4) && packs.SelectMany(p => p["cards"].Values<string>()).Distinct().Count() == 48, "five unique four-card pack coverage");
        foreach (int rank in Enumerable.Range(1, 5))
        {
            Check(cards.Count(c => (int)c["rank"] == rank) == (rank < 4 ? 8 : 12), "five original realm card count " + rank);
            Check(packs.Count(p => (int)p["rank"] == rank) == (rank < 4 ? 2 : 3), "five original realm pack count " + rank);
        }
        Check(packs.Select(p => (int)p["index"]).SequenceEqual(new[] { 60, 61 }.Concat(Enumerable.Range(80, 10))), "five current namespaced shelf slot ordering");
        foreach (var card in cards)
        {
            string sid = (string)card["sourceId"]; var original = source[sid];
            Check(((string)card["name"]).Replace('·', '•') == (string)original["name"], "five real original name " + sid);
            Check(realms[(int)card["rank"] - 1] == (string)original["realm"], "five real original realm " + sid);
            Check((string)card["originalText"] == (string)original["original"] && (string)card["sourceUrl"] == (string)original["url"], "five exact first-level text and URL " + sid);
            Check((int)card["rules"]["cost"] == (int)costs[sid]["cost"], "five image-proven original qi cost " + sid);
            Check(SectCardRules.PrintedCost(card) == 4 * (int)costs[sid]["cost"], "five zero-base qi conversion " + sid);
            Check(((string)card["description"]).Contains("消耗：") && !((string)card["description"]).Contains("适配说明：") && !string.IsNullOrWhiteSpace((string)card["summary"]), "five player copy separates effects from source evidence " + sid);
            foreach (string invented in new[] { "edgeGain", "flameGain", "growthGain", "wardGain", "converge" }) Check(card["rules"][invented] == null, "five removes invented rule " + invented + "/" + sid);
        }
        Check((int)CardByOriginal(19)["rules"]["cost"] == 2, "five waves has two original qi fee");
        Check(FiveElementRules.PrintedCost(CardByOriginal(19), Saved(), ActorId) == 8, "five waves unprepared pays eight qi");
        foreach (var state in new[] { Saved(("last", 1)), Saved(("last", 2)), Saved(("active", 2)), Saved(("nextActivate", 1)) })
            Check(FiveElementRules.PrintedCost(CardByOriginal(19), state, ActorId) == 0, "five waves actual elemental fee exemption");

        for (int previous = 1; previous <= 5; previous++)
        for (int next = 1; next <= 5; next++)
        {
            bool expected = previous == next || next == previous % 5 + 1;
            Check(FiveElementRules.Links(previous, next) == expected && Ready(previous, next) == expected, "five same/generating condition " + previous + "/" + next);
            Check(Ready(previous, next, 1 << (next - 1)), "five permanent activation bypasses previous element " + previous + "/" + next);
        }
        Check(!Ready(0, 1) && !Ready(0, 0) && !FiveElementRules.Links(0, 0) && !FiveElementRules.Links(6, 6), "five no invented neutral link");
        var ordinary = Cycle(2);
        Check(Read(ordinary, "active") == 0 && Read(ordinary, "sharp") == 0 && Read(ordinary, "last") == 1, "five ordinary needle neither activates nor gains unprepared sharp");
        Check(Read(Cycle(2, ordinary.State), "sharp") == 2, "five same-element needle grants real sharp");
        Check(Read(Cycle(2, Saved(("last", 5))), "sharp") == 2, "five earth generates needle condition");
        Check(Read(Cycle(2, Saved(("active", 1))), "sharp") == 2, "five active metal grants needle condition");
        ModStateSnapshot seals = null;
        foreach (int id in new[] { 1, 6, 3, 9, 11 }) seals = Cycle(id, seals).State;
        Check(SectRegistry.Read(seals, SectRegistry.StateKey(school, ActorId, "active")) == 31, "five five real seals activate all five");
        var neutral = Cycle(94, Saved(("active", 31), ("last", 1)));
        Check(Read(neutral, "last") == 0 && Read(neutral, "active") == 31, "five neutral card breaks sequence but retains real activation");
        var drawn = Run(FiveElementRules.Resolved, CardTableRules.DrawActionId, Saved(("last", 1)), actor: actor);
        Check(Read(drawn, "last") == 1, "five draw does not break sequence");
        var nativeBreak = Run(FiveElementRules.Resolved, CardTableCatalog.Cards.First().AbilityId, Saved(("last", 1), ("active", 1)), actor: actor);
        Check(Read(nativeBreak, "last") == 0 && Read(nativeBreak, "active") == 1, "five native card breaks sequence not activation");
        for (int mask = 0; mask < 32; mask++)
        {
            var visible = Play(2, Saved(("active", mask)));
            Check(Read(visible, "active") == mask, "five ordinary card preserves exact activation mask " + mask);
            foreach (int bit in Enumerable.Range(0, 5))
            {
                string sid = "yixian.sect.status.five." + new[] { "metal", "water", "wood", "fire", "earth" }[bit];
                Check(visible.Effects.Any(e => e.Kind == ModBattleEffectKind.Status && e.Content.Value == sid && e.Amount == 1) == ((mask & (1 << bit)) != 0), "five five separate element badges " + mask + "/" + bit);
                Check(visible.Effects.Any(e => e.Kind == ModBattleEffectKind.ClearStatus && e.Content.Value == sid), "five clears stale badge " + mask + "/" + bit);
            }
        }

        var blocked = Hit(2, Play(2, Saved(("sharp", 5), ("last", 1))), new[] { Health(0, 12) });
        Check(Read(blocked, "sharp") == 5 && Requested(blocked, "loss", EnemyId) == 0, "five sharp does not trigger against shield-only hit");
        Check(Read(Resolve(2, blocked), "sharp") == 7, "five needle gains sharp only after its attack");
        var pierced = Hit(2, Play(2, Saved(("sharp", 5), ("last", 1))), new[] { Health(9) });
        Check(Read(pierced, "sharp") == 0 && Requested(pierced, "loss", EnemyId) == 15, "five sharp consumes on real health hit and adds direct life loss");
        var duplicateHit = Hit(2, pierced, new[] { Health(9) });
        Check(Requests(duplicateHit).Count == Requests(pierced).Count && Read(duplicateHit, "sharp") == 0, "five outgoing hit handler does not repeat supplements");
        var returnBlade = Hit(99, Play(99, Saved(("sharp", 5), ("active", 1))), new[] { Health(9), Health(9) });
        Check(Read(returnBlade, "sharp") == 2 && Requested(returnBlade, "loss", EnemyId) == 24, "five return blade ceil sixty percent per actual attack");
        var noReturn = Hit(99, Play(99, Saved(("sharp", 5))), new[] { Health(9), Health(9) });
        Check(Read(noReturn, "sharp") == 0 && Requested(noReturn, "loss", EnemyId) == 15, "five blade return requires metal condition");
        var shuttle = Resolve(34, Hit(34, Play(34, Saved(("sharp", 7), ("active", 1))), new[] { Health(9) }));
        Check(Read(shuttle, "sharp") == 7 && Requested(shuttle, "loss", EnemyId) == 0 && Draw(shuttle) == 1, "five shuttle excludes sharp and adapts again as draw");
        var cauldron = Hit(42, Play(42, Saved(("sharp", 4), ("active", 1))), new[] { Health(10) });
        Check(Requested(cauldron, "loss", EnemyId) == 34, "five cauldron doubles actual life loss including sharp");
        var edgeHit = Hit(26, Play(26, Saved(("active", 1))), new[] { Health(12) });
        Check(Read(edgeHit, "sharp") == 2, "five original edge gains sharp per six adapted health damage");
        var rose = Hit(27, Play(27, Saved(("active", 4))), new[] { Health(9), Health(18), Health(0, 6) });
        Check(Effect(rose, ModBattleEffectKind.Heal) == 9, "five rose heals per actual health damage, not shield damage");
        var heart = Hit(39, Play(39, Saved(("active", 8))), new[] { Health(6) });
        Check(Requested(heart, "maximum", EnemyId) == -12 && Requests(heart).OfType<JObject>().All(q => !(bool)q["current"]), "five heart burn reduces actual maximum not ordinary damage");
        var spring = Cycle(59, Saved(("active", 2)));
        Check(Read(spring, "nextWaterCharges") == 1, "five fountain reserves next-attack conversion");
        var fountainAttack = Hit(4, Play(4, spring.State), new[] { Health(30), Health(30) });
        Check(Read(fountainAttack, "waterForce") == 2 && Read(fountainAttack, "nextWaterCharges") == 0, "five fountain consumes one attack, not every segment");
        var growShield = Hit(28, Play(28, Saved(("active", 4))), new[] { Health(0, 10), Health(5) });
        Check(growShield.Effects.Any(e => e.Kind == ModBattleEffectKind.Shield && e.Target == EnemyId && e.Amount == -10), "five break-shield adaptation subtracts actual shield hit only");

        var metalArray = Cycle(16);
        Check(Read(metalArray, "sharp") == 4 && Read(metalArray, "metalArray") == 1, "five original metal array immediate and persistent effects");
        Check(Read(Cycle(2, metalArray.State), "sharp") == 7, "five metal array triggers after subsequent metal card");
        var waterArray = Cycle(29);
        Check(Read(waterArray, "waterArray") == 1 && Qi(Resolve(7, Play(7, waterArray.State))) == 4, "five water array restores one original qi");
        var woodArray = Cycle(36);
        Check(Requested(woodArray, "maximum", ActorId) == 30 && Read(woodArray, "woodArray") == 2, "five wood array increases real maximum and installs healing");
        Check(Effect(Cycle(4, woodArray.State), ModBattleEffectKind.Heal) == 15 + 7 + 6, "five wood array heals after wood condition without invented regeneration");
        var fireArray = Cycle(31);
        Check(Read(fireArray, "fireArray") == 2 && Requested(Cycle(21, fireArray.State), "maximum", EnemyId) == -15, "five fire array reduces enemy life and cap per fire card");
        var earthArray = Cycle(23);
        Check(Read(earthArray, "earthArray") == 2 && Effect(Cycle(40, earthArray.State), ModBattleEffectKind.Shield) == 6, "five earth array gives shield after earth card");
        var twoArrays = Cycle(23, metalArray.State);
        Check(Read(twoArrays, "persistentCount") == 2 && !FiveElementRules.CanPlay(CardByOriginal(16), twoArrays.State, ActorId) && !FiveElementRules.CanPlay(CardByOriginal(29), twoArrays.State, ActorId), "five persistent cards consumed once with two-card cap");
        try { Play(29, twoArrays.State); throw new Exception("third persistent card accepted"); } catch (InvalidOperationException) { }
        var sealArray = Cycle(94);
        var sealDamage = Play(1, sealArray.State);
        Check(sealDamage.Effects.Any(e => e.Kind == ModBattleEffectKind.Damage && e.Amount == 6) && Read(sealDamage, "activationCount") == 1, "five seal heart triggers only actual active print");
        Check(!Play(2, sealArray.State).Effects.Any(e => e.Kind == ModBattleEffectKind.Damage), "five ordinary metal card is not an activation event");
        var tide = Cycle(7, Saved(("active", 2)));
        var end = Run(FiveElementRules.End, state: tide.State, actor: actor);
        Check(Read(end, "waterForce") == 1 && end.Effects.Any(e => e.Kind == ModBattleEffectKind.Damage && e.Amount == 3), "five water force damage occurs at own adapted round end");
        Check(!Run(FiveElementRules.End, state: end.State, actor: actor).Effects.Any(e => e.Kind == ModBattleEffectKind.Damage), "five duplicate round-end callback no extra water damage");
        var refunds = Run(FiveElementRules.Hits, state: Saved(("shieldReturn", 2)), actor: actor, hits: new[] { new ModBattleHit(EnemyId, ActorId, 0, 8, DamageType.Physical, true, false), new ModBattleHit(EnemyId, ActorId, 0, 4, DamageType.Physical, true, false), new ModBattleHit(EnemyId, ActorId, 0, 3, DamageType.Physical, true, false) });
        Check(Read(refunds, "lostShield") == 15 && Read(refunds, "shieldReturn") == 0 && Effect(refunds, ModBattleEffectKind.Shield) == 12, "five eight-desolations returns exactly next two actual shield losses");
        Check(Effect(Play(40, Saved(("active", 16), ("lostShield", 36))), ModBattleEffectKind.Shield) - Effect(Play(40), ModBattleEffectKind.Shield) == 6, "five cliff scales from real lost shield only");
        Check(Damage(Play(33, Saved(("active", 16), ("lostShield", 36)))) - Damage(Play(33)) == 6, "five quicksand scales from real lost shield only");
        Check(Damage(Play(66, Saved(("deckElements", 31)))) - Damage(Play(66, Saved(("active", 31)))) == 60, "five mixed strike uses enabled-deck elements, not activation");
        Check(FiveElementRules.ReduceIncoming(100, DamageType.Physical, 1, 3, 0) == 60 && FiveElementRules.ReduceIncoming(100, DamageType.True, 1, 3, 0) == 100, "five dive adapted damage types");
        Check(FiveElementRules.ReduceIncoming(100, DamageType.Physical, 1, 3, 3) == 45 && FiveElementRules.ReduceIncoming(10, DamageType.True, 1, 0, 3) == 1, "five iron reduction after dive and minimum one");
        Check(FiveElementRules.ReduceIncoming(100, DamageType.Physical, 3, 3, 3) == 100, "five damage reduction expiry exact");
        var dive = Play(20, Saved(("active", 2)));
        Check(Read(dive, "diveUntil") == 3 && dive.Effects.Any(e => e.Kind == ModBattleEffectKind.Status && e.Content.Value == "yixian.sect.status.five.dive"), "five dive installs actual damage-rule status");
        Check(Read(Play(20, dive.State), "diveUntil") == 5, "five repeated timed effect extends remaining duration");
        var burnEarth = Play(113, Saved(("active", 16)), Unit(school: school, rank: 5, shield: 27));
        Check(Requested(burnEarth, "maximum", EnemyId) == -36, "five burn-out earth clause independent of fire condition");
        Check(Requested(Play(113, Saved(("active", 1), ("sharp", 6))), "maximum", EnemyId) == -36, "five burn-out metal clause independent of fire condition");
        Check(Requested(Play(113, Saved(("active", 31), ("sharp", 6), ("attack", 2)), Unit(school: school, rank: 5, shield: 27)), "maximum", EnemyId) == -57, "five burn-out all three independent elemental clauses");
        Check(Damage(Play(115)) == 0 && Damage(Play(115, Saved(("active", 6), ("waterForce", 2), ("attack", 2)), Unit(school: school, rank: 5, energy: 8))) == 45, "five rain independently executes water and wood attacks with multi-attribute/main cultivation scaling");
        Check(Draw(Cycle(116)) == 0 && Draw(Cycle(116, Saved(("last", 2)))) == 1, "five sands does not self-prime its metal/water conditions");
        var drain = Cycle(112, Saved(("active", 4), ("attack", 2)));
        Check(Read(drain, "drainPlays") == 3 && Requested(drain, "loss", EnemyId) == 0, "five branches starts next-three-cards drain without consuming itself");
        var drained = Cycle(2, drain.State);
        Check(Read(drained, "drainPlays") == 2 && Requested(drained, "loss", EnemyId) == 9, "five branches life drain from real next card");
        var replayResolved = Resolve(2, drained);
        Check(Read(replayResolved, "drainPlays") == 2 && Requests(replayResolved).Count == Requests(drained).Count, "five duplicate resolved card does not replay drain");
        var nativeDrain = Run(FiveElementRules.Resolved, CardTableCatalog.Cards.First().AbilityId, Drained(drain.State), actor: actor);
        Check(Read(nativeDrain, "drainPlays") == 2 && Requested(nativeDrain, "loss", EnemyId) == 9, "five branches also drains on native cards");
        var agility = Cycle(100, Saved(("active", 2), ("waterForce", 5), ("agility", 8)), Unit(school: school, rank: 5, energy: 24));
        Check(Draw(agility) == 2 && Read(agility, "agility") == 0, "five wind-wave cap twelve agility and preserved remainder convert to draws");

        var allFates = new[] { FiveFateRules.Mark, FiveFateRules.Herbs, FiveFateRules.WaterMark, FiveFateRules.Explosion };
        foreach (int rank in Enumerable.Range(1, 5))
        {
            var unit = Unit(school: school, rank: rank, energy: 0, abilities: allFates);
            var start = Run(FiveElementRules.Start, actor: unit);
            Check(Read(start, "nextActivate") == (rank >= 2 ? 1 : 0), "five real mark ownership/rank gate " + rank);
            Check(Read(start, "active") == (rank >= 3 ? 2 : 0) && Qi(start) == (rank >= 3 ? 4 : 0), "five real water mark fixed original values " + rank);
            Check(Requested(start, "maximum", ActorId) == (rank >= 3 ? -12 : 0) && Requested(start, "maximum", EnemyId) == (rank >= 3 ? -9 : 0), "five explosion real downside plus activation payoff " + rank);
        }
        var herbs = Unit(school: school, rank: 2, abilities: new[] { FiveFateRules.Herbs });
        var herbsNeutral = Cycle(94, unit: herbs);
        Check(Read(herbsNeutral, "active") == 4 && Requested(herbsNeutral, "maximum", ActorId) == 6 && Requests(herbsNeutral).OfType<JObject>().All(q => !(bool)q["current"]), "five hundred-herbs neutral activation adds cap not invented heal");
        var markUnit = Unit(school: school, rank: 2, abilities: new[] { FiveFateRules.Mark });
        var markStart = Run(FiveElementRules.Start, actor: markUnit);
        var markedNeedle = Cycle(2, markStart.State, markUnit);
        Check(Read(markedNeedle, "active") == 1 && Read(markedNeedle, "nextActivate") == 0 && Read(markedNeedle, "sharp") == 2, "five mark activates before first elemental card condition");
        var herbsTwice = Cycle(3, Cycle(3, unit: herbs).State, herbs);
        Check(Read(herbsTwice, "activationCount") == 2 && Requested(herbsTwice, "maximum", ActorId) == 6, "five repeated explicit activation still triggers herbs each time");
        Check(!FiveFateRules.Has(Unit(school: school, rank: 5), FiveFateRules.Mark) && !FiveFateRules.Has(Unit(school: SevenStarRules.Lineage, rank: 5, abilities: allFates), FiveFateRules.Mark), "five fates require actual owned school and ability");
        Check(FiveFateRules.Required("unknown") == 0, "five unknown fate rejected");
        var saved = Saved(("active", 31), ("sharp", 5), ("waterForce", 7), ("deckElements", 31), ("lostShield", 36));
        var restored = new ModStateSnapshot(JsonConvert.DeserializeObject<ModStateEntry[]>(JsonConvert.SerializeObject(saved.Entries)));
        Check(Damage(Play(33, saved)) == Damage(Play(33, restored)), "five exact state serialization preserves real mechanics");
        var reset = Run(FiveElementRules.Start, state: saved, actor: actor);
        Check(Read(reset, "active") == 0 && Read(reset, "sharp") == 0 && Read(reset, "waterForce") == 0 && Read(reset, "lostShield") == 0 && Read(reset, "deckElements") == 31, "five new battle clears combat state while retaining fresh deck mask");
        var other = Unit(school: school, rank: 5, slot: 2);
        Check(Damage(Play(33, saved, other)) == Damage(Play(33, unit: other)), "five all counters isolated by actor");
        foreach (int stat in new[] { 0, 10, 50 })
        {
            var unit = Unit(school: school, rank: 5, energy: 0, attributes: new StatBlock(stat, stat, stat, stat));
            Check(Damage(Play(1, unit: unit)) == 18 + stat, "five original single attack uses energy100 " + stat);
            Check(Damage(Play(4, unit: unit)) == (9 + stat * 4000 / 10000) * 2, "five original multi attack uses reaction40 " + stat);
            Check(Effect(Play(11, unit: unit), ModBattleEffectKind.Shield) == 12 + stat * 7000 / 10000, "five original shield energy70 " + stat);
            Check(Effect(Play(3, unit: unit), ModBattleEffectKind.Heal) == 12 + stat * 5000 / 10000 && Qi(Play(3, unit: unit)) == 4 + stat * 2000 / 10000, "five original healing/qi formulas " + stat);
        }
        foreach (var card in cards)
        foreach (int mask in new[] { 0, 31 })
        {
            int id = (int)card["rules"]["originalRule"];
            var played = Play(id, Saved(("active", mask), ("deckElements", 31), ("sharp", 5), ("attack", 2), ("waterForce", 3), ("lostShield", 36)));
            var hit = Hit(id, played, played.Operations.Count > 0 ? new[] { Health(18, 6), Health(9) } : Array.Empty<ModBattleHit>());
            var complete = Resolve(id, hit);
            foreach (var operation in played.Operations)
                Check(AuthoredSkillRules.TryCreateEffects(new BattleEffectOperation(operation.HandlerKey, CombatTargetKind.EnemySingle, default, operation.Values), out var effects, out var error) && effects.Count == 1, "five original native operation " + id + ": " + error);
            Check(Read(complete, "active") >= 0 && Read(complete, "active") <= 31 && Read(complete, "sharp") >= 0, "five original legal state output " + id + "/" + mask);
            foreach (JObject request in Requests(complete)) Check((string)request["target"] == ActorId.Value || (string)request["target"] == EnemyId.Value, "five original life adjustment targets actual unit " + id);
        }
        Console.WriteLine("PASS five original correction: 48 source/realm/cost-faithful cards, seals vs conditions, real sharp/attack/water/arrays, independent elemental clauses, four real fates, actor state and native operations");
    }
}
