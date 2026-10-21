using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    public static class SevenStarRules
    {
        public const string Lineage = "yixian.sect.lineage.star";
        public const string StatusPrefix = "yixian.sect.status.star.";
        private static readonly string[] Views = { "plays", "slot", "star", "omen", "hexFormation", "repeat", "astralLinks", "calm" };
        public static int Number(JObject r, string k) => (int?)r[k] ?? 0;
        public static bool Flag(JObject r, string k) => (bool?)r[k] == true;
        public static int Slot(int count) => count <= 0 ? 0 : (count - 1) % 8 + 1;
        public static bool IsPosition(int count) => Slot(count) == 3 || Slot(count) == 6;
        public static void Gain(BattleTurn turn, string key, int amount) => turn.Set(key, Math.Min(9999, turn.Get(key) + amount));
        public static bool CanPlay(JObject card, StableId actor, ModStateSnapshot state)
            => !Flag((JObject)card["rules"], "persistent") || SectRegistry.Read(state, SectRegistry.StateKey(Lineage, actor, "persistent" + (string)card["sourceId"])) == 0;
        private sealed class Turn
        {
            public readonly BattleTurn T;
            public CodeBattlePlan P => T.Plan;
            public ModCombatantSnapshot U => T.Actor;
            public int Energy;
            public int OmenUsed;
            private readonly HashSet<string> plannedGuards = new HashSet<string>();
            public Turn(ModBattleContext c, CodeBattlePlan p, ModCombatantSnapshot actor = null)
            {
                T = new BattleTurn(c, p, Lineage, actor); Energy = U.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                if (T.Get("initialized") == 0) { T.Set("initialized", 1); T.Set("positions", (1 << 2) | (1 << 5)); }
            }
            public int Get(string key) => T.Get(key);
            public void Set(string key, int value) => T.Set(key, value);
            public void Add(string key, int amount) => T.Add(key, amount);
            public StableId Target => T.Context.Units.FirstOrDefault(u => !u.IsDowned && u.Side != U.Side && (T.Context.Action?.Targets?.Contains(u.RuntimeId) ?? false))?.RuntimeId
                ?? T.Context.Units.FirstOrDefault(u => !u.IsDowned && u.Side != U.Side)?.RuntimeId ?? default(StableId);
            public ModCombatantSnapshot Enemy => T.Context.Units.FirstOrDefault(u => u.RuntimeId == Target);
            public bool Position => (Get("positions") & (1 << (Math.Max(1, Get("slot")) - 1))) != 0;
            public int StarBonus => Position ? Get("star") * 3 : 0;
            public void Next() { Add("plays", 1); Set("slot", Slot(Get("plays"))); }
            public void Star(int n) => Gain(T, "star", n);
            public void Omen(int n)
            {
                int before = Get("omen"); Gain(T, "omen", n);
                int gained = Get("omen") - before;
                if (gained > 0 && Get("hexFormation") > 0) T.NonAttackDamage(gained * Get("hexFormation") * 3, Target);
            }
            public int Random(int min, int max)
            {
                if (Get("omen") > 0) { Add("omen", -1); Add("omenUsed", 1); OmenUsed++; return max; }
                return P.Random(min, max);
            }
            public bool Chance(int percent) => Random(1, 100) > 100 - percent;
            public void Mark(int distance)
            {
                int slot = (Get("slot") - 1 + distance) % 8;
                if ((Get("positions") & (1 << slot)) != 0) Qi(4);
                else Set("positions", Get("positions") | (1 << slot));
            }
            public void Qi(int amount)
            {
                int gained = Math.Min(amount, Math.Max(0, U.EnergyPools.GetMaximum(EnergyPoolKind.Fighting) - Energy));
                T.Qi(gained); Energy += gained;
                if (gained > 0 && Get("calm") > 0)
                {
                    int total = Get("calmRemainder") + gained;
                    Set("calmRemainder", total % 4);
                    H(total / 4 * Get("calm") * 3);
                }
            }
            public void S(int amount) { T.Shield(amount); }
            public void Max(int amount, bool current = false)
            {
                T.MaxHealth(amount, changeCurrent: current);
            }
            public void H(int amount, bool all = false, StableId selected = default)
            {
                if (amount <= 0) return;
                SevenStarHealing.Heal(T, amount, all, selected);
            }
            public void Attack(int amount, int hits = 1, StableId target = default)
            {
                if (amount > 0) T.Damage(amount + StarBonus, hits, target);
            }
            public void Debuff(string key, int n, int rounds = 0)
            {
                if (n <= 0 || Target.IsEmpty) return;
                P.Status(U.RuntimeId, Target, new StableId(StatusPrefix + key), n, rounds);
            }
            public void Guard(int layers)
            {
                // One native instance per layer; distinct ids prevent native interception from clearing all layers.
                int remaining = layers;
                for (int i = 0; i < 32 && remaining > 0; i++)
                {
                    string key = StatusPrefix + "guard" + i;
                    if (U.Statuses.Any(s => s.StatusId.Value == key) || plannedGuards.Contains(key)) continue;
                    P.Status(U.RuntimeId, U.RuntimeId, new StableId(key), 1); plannedGuards.Add(key); remaining--;
                }
            }
            public void View()
            {
                T.View(Views);
                for (int i = 1; i <= 8; i++)
                {
                    var id = new StableId(StatusPrefix + "position" + i); P.ClearStatus(U.RuntimeId, id);
                    if ((Get("positions") & (1 << (i - 1))) != 0) P.Status(U.RuntimeId, U.RuntimeId, id, 1);
                }
            }
        }
        public static void Register(ICodeModContext context)
        {
            foreach (var card in SectRegistry.Cards.Where(c => SectRegistry.Owner(c.Value) == Lineage)) context.OnAction(card.Key, Play);
            context.OnBattle("battle_start", Start); context.OnBattle("round_start", Round);
            context.OnBattle("action_resolved", Resolved);
        }
        public static void Start(ModBattleContext c, CodeBattlePlan p)
        {
            foreach (var actor in c.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            {
                string marker = SectRegistry.StateKey(Lineage, actor.RuntimeId, "battleKey");
                if (!string.IsNullOrEmpty(c.BattleKey) && p.GetText(marker) == c.BattleKey) continue;
                string prefix = SectRegistry.StateKey(Lineage, actor.RuntimeId, "");
                foreach (var e in c.State.Entries.Where(e => e.PackageId == SectRegistry.Package && e.Key.StartsWith(prefix, StringComparison.Ordinal))) p.RemoveState(e.Key);
                p.SetState(marker, c.BattleKey);
                var t = new Turn(c, p, actor); SevenStarFateRules.Start(t.T); t.View();
            }
        }
        public static void Round(ModBattleContext c, CodeBattlePlan p)
        {
            foreach (var actor in c.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            { var t = new Turn(c, p, actor); t.View(); }
        }
        public static void Play(ModBattleContext c, CodeBattlePlan p)
        {
            if (!SectRegistry.Cards.TryGetValue(c.ActionId.Value, out var card) || SectRegistry.Owner(card) != Lineage) return;
            var t = new Turn(c, p);
            if (Flag((JObject)card["rules"], "selectedAllyHeal")) SevenStarHealing.SelectedAlly(t.T);
            t.Next();
            bool repeat = t.Get("repeat") > 0; if (repeat) t.Set("repeat", 0);
            Apply(t, card);
            if (repeat) Apply(t, card);
            t.View();
        }
        private static StableId LightningTarget(Turn t, HashSet<StableId> used = null)
        {
            var enemies = t.T.Context.Units.Where(u => !u.IsDowned && u.Side != t.U.Side && u.Kind != CombatantKind.AuxiliaryTarget)
                .OrderBy(u => u.RuntimeId.Value, StringComparer.Ordinal).Select(u => u.RuntimeId).ToArray();
            if (enemies.Length == 0) return default;
            if (used != null && used.Count == 0 && enemies.Contains(t.Target)) { used.Add(t.Target); return t.Target; }
            var fresh = used == null ? enemies : enemies.Where(id => !used.Contains(id)).ToArray();
            var pool = fresh.Length > 0 ? fresh : enemies;
            var target = pool[pool.Length == 1 ? 0 : t.P.Random(0, pool.Length - 1)];
            used?.Add(target); return target;
        }
        private static void Apply(Turn t, JObject card)
        {
            var r = (JObject)card["rules"]; string id = (string)card["sourceId"];
            var healTarget = Flag(r, "selectedAllyHeal") ? SevenStarHealing.SelectedAlly(t.T) : default;
            if (Flag(r, "persistent") && t.Get("persistent" + id) > 0) return;
            if (Flag(r, "persistent")) t.Set("persistent" + id, 1);
            string used = "used" + id; bool post = t.Get(used) > 0;
            t.Add(used, 1);
            bool postCard = Number(r, "postAttack") > 0 || Number(r, "postHeal") > 0 || Number(r, "postGuard") > 0 || Flag(r, "postAgain");
            if (!post && postCard) SevenStarFateRules.FirstPostCard(t.T);
            bool astral = ((string)card["name"]).StartsWith("星弈", StringComparison.Ordinal);
            if (astral && t.Get("astralLinks") > 0)
            {
                t.Add("astralLinks", -1);
                if (t.Position) t.Star(1);
                else { t.Set("positions", t.Get("positions") | (1 << (t.Get("slot") - 1))); t.H(15); }
            }
            if (Number(r, "calmPerQi") > 0) t.Set("calm", Number(r, "calmPerQi"));
            int damage = checked(t.T.Value(r, "damage") + (Number(r, "healingStep") > 0 ? t.Get("healingTotal") / Number(r, "healingStep") * Number(r, "damagePerHealing") : 0));
            if (damage > 0) t.Attack(damage, Math.Max(1, Number(r, "hits")));
            if (Number(r, "randomMax") > 0)
            {
                int repeats = Math.Max(1, Number(r, "randomRepeats"));
                var targets = Flag(r, "distinctLightning") ? new HashSet<StableId>() : null;
                for (int i = 0; i < repeats; i++)
                {
                    int before = t.OmenUsed; int rolled = t.Random(Number(r, "randomMin"), Number(r, "randomMax"));
                    int attr = AttributeScaling.Read(repeats > 1 ? CombatScalingAttribute.Reaction : CombatScalingAttribute.Energy, t.U.Attributes.GetValueOrDefault());
                    if (repeats > 1) attr = attr * 4000 / 10000;
                    var target = targets == null ? default : LightningTarget(t, targets);
                    if (targets == null || !target.IsEmpty) t.Attack(rolled * 3 + attr, target: target);
                    if (t.OmenUsed > before && Number(r, "qiOnOmen") > 0) t.Qi(Number(r, "qiOnOmen") * 4);
                }
            }
            for (int i = 0; i < Number(r, "chanceRepeats"); i++)
            {
                if (!t.Chance(Number(r, "attackChance"))) continue;
                bool distributed = Flag(r, "distributedLightning");
                var target = distributed ? LightningTarget(t) : default;
                if (!distributed || !target.IsEmpty)
                    t.Attack(Number(r, "chanceAttack") * 3 + AttributeScaling.Read(CombatScalingAttribute.Reaction, t.U.Attributes.GetValueOrDefault()) * 4000 / 10000, target: target);
            }
            t.S(t.T.Value(r, "shield")); t.Qi(t.T.Value(r, "qi"));
            if (Number(r, "maxHealth") > 0) t.Max(Number(r, "maxHealth") * 3);
            t.H(t.T.Value(r, "heal"), selected: healTarget);
            if (Number(r, "randomHealMax") > 0)
                t.H(t.Random(Number(r, "randomHealMin"), Number(r, "randomHealMax")) * 3
                    + AttributeScaling.Read(CombatScalingAttribute.Constitution, t.U.Attributes.GetValueOrDefault()) / 2, selected: healTarget);
            t.Star(Number(r, "starGain")); t.Omen(Number(r, "omenGain"));
            if (Number(r, "markNext") > 0) t.Mark(Number(r, "markNext"));
            if (t.Position)
            {
                if (Number(r, "positionAttack") > 0) t.Attack(Number(r, "positionAttack") * 3);
                t.S(Number(r, "positionDefense") * 3); t.Qi(Number(r, "positionQi") * 4);
                if (Flag(r, "positionAgain")) t.T.Draw();
                if (Number(r, "positionWeak") > 0) t.Debuff("weak", Number(r, "positionWeak"));
                if (Flag(r, "positionSkip")) t.Debuff("skip", 1, 1);
                if (Number(r, "positionAgainStars") > 0 && t.Get("star") >= Number(r, "positionAgainStars")) t.T.Draw();
                if (Number(r, "positionMaxHeal") > 0)
                {
                    int n = (Number(r, "positionMaxHeal") + t.Get("star") * Number(r, "maxHealPerStar")) * 3;
                    t.Max(n); t.H(n);
                }
            }
            if (post)
            {
                if (Number(r, "postAttack") > 0) t.Attack(Number(r, "postAttack") * 3 + (damage == 0 ? AttributeScaling.Read(CombatScalingAttribute.Energy, t.U.Attributes.GetValueOrDefault()) : 0));
                if (Number(r, "postHeal") > 0) t.H(Number(r, "postHeal") * 3 + AttributeScaling.Read(CombatScalingAttribute.Constitution, t.U.Attributes.GetValueOrDefault()) / 2, selected: healTarget);
                if (Flag(r, "postAgain")) t.T.Draw();
                if (Number(r, "postGuard") > 0) t.Guard(Number(r, "postGuard"));
            }
            if (Number(r, "weakChance") > 0 && t.Chance(Number(r, "weakChance"))) t.Debuff("weak", 1);
            int supportOmen = t.Get("omen");
            if (Number(r, "shieldPerOmen") > 0)
            {
                int amount = supportOmen * Number(r, "shieldPerOmen") * 3;
                if (Flag(r,"groupOmenSupport")) { if (amount > 0) foreach (var ally in SevenStarHealing.Allies(t.T)) t.P.Shield(ally.RuntimeId, amount); }
                else t.S(amount);
            }
            if (Number(r, "healPerOmen") > 0) t.H(supportOmen * Number(r, "healPerOmen") * 3, Flag(r,"groupOmenSupport"));
            if (Flag(r, "rankRefundOmen")) t.Omen(Math.Max(0, SevenStarFateRules.Rank(t.U) - 2));
            if (Number(r, "hexFormation") > 0) t.Set("hexFormation", Number(r, "hexFormation"));
            if (Flag(r, "consumeAllOmen"))
            {
                int n = t.Get("omen"); t.Set("omen", 0); t.Star(n); t.Qi(n * 4); t.H(n * 3);
            }
            if (Number(r, "astralLinks") > 0) t.Add("astralLinks", Number(r, "astralLinks"));
            if (Flag(r, "again") || Number(r, "againAtOmen") > 0 && t.Get("omen") >= Number(r, "againAtOmen")) t.T.Draw();
            if (Flag(r, "repeatNext")) t.Set("repeat", 1);
            if (Flag(r, "allPositions"))
            {
                if (t.Get("slot") == 1) t.Star(Number(r, "firstSlotStar"));
                t.Set("positions", 255);
            }
            if (Number(r, "compareQiFlaw") > 0 && t.Enemy != null && t.Energy > t.Enemy.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)) t.Debuff("flaw", Number(r, "compareQiFlaw"));
            if (Number(r, "bonusChance") > 0 && t.Chance(Number(r, "bonusChance"))) t.Attack(Number(r, "bonusAttack") * 3, Number(r, "bonusHits"));
            if (t.Get("slot") % 2 == 1)
            {
                t.Star(Number(r, "oddStar"));
            }
            else
            {
                int n = Math.Min(t.Get("star"), Number(r, "evenStarGuard")); t.Add("star", -n); t.Guard(n);
            }
        }
        public static void Resolved(ModBattleContext c, CodeBattlePlan p)
        {
            var actor = c.Units.FirstOrDefault(u => u.RuntimeId == c.ActorId);
            if (actor == null || actor.IsDowned || SectRegistry.Rank(actor, Lineage) == 0) return;
            if (SectRegistry.Cards.TryGetValue(c.ActionId.Value, out var card) && SectRegistry.Owner(card) == Lineage) return;
            if (!SectRegistry.Cards.ContainsKey(c.ActionId.Value) && !CardTableCatalog.TryGetCard(c.ActionId, out var _)) return;
            var t = new Turn(c, p); t.Next(); t.View();
        }
    }
}
