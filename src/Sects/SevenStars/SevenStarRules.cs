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
        private static readonly string[] Views = { "plays", "slot", "star", "omen", "hexFormation", "repeat", "astralLinks", "soulCharges", "injuryRounds" };
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
            public int Energy, Health, Maximum, Shield;
            public int OmenUsed;
            private readonly Dictionary<string, int> applied = new Dictionary<string, int>();
            private readonly HashSet<string> plannedGuards = new HashSet<string>();
            public Turn(ModBattleContext c, CodeBattlePlan p, ModCombatantSnapshot actor = null)
            {
                T = new BattleTurn(c, p, Lineage, actor); Energy = U.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
                Health = U.CurrentHealth; Maximum = U.CurrentMaximumHealth; Shield = U.Shield;
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
            }
            public void S(int amount) { T.Shield(amount); Shield += Math.Max(0, amount); }
            public void Max(int amount, bool current = false)
            {
                T.MaxHealth(amount, changeCurrent: current); Maximum = Math.Max(1, Maximum + amount);
                if (current) Health = Math.Max(0, Math.Min(Maximum, Health + amount));
            }
            public void H(int amount)
            {
                if (amount <= 0) return;
                int extra = SevenStarFateRules.Rejuvenation(U);
                if (extra > 0) Max(extra);
                T.Heal(amount); int actual = Math.Min(amount, Math.Max(0, Maximum - Health)); Health += actual;
                Add("healed", actual);
            }
            public void Attack(int amount, int hits = 1)
            {
                if (amount > 0) T.Damage(amount + StarBonus, hits);
            }
            public int Stacks(string key)
            {
                int existing = Enemy?.Statuses.Where(s => s.StatusId.Value == StatusPrefix + key).Sum(s => s.StackCount) ?? 0;
                return existing + (applied.TryGetValue(key, out int n) ? n : 0);
            }
            public int Negatives => (Enemy?.Statuses.Where(s => s.StatusId.Value.StartsWith(StatusPrefix, StringComparison.Ordinal) && new[] { "internal", "external", "weak", "flaw", "decrease" }.Contains(s.StatusId.Value.Substring(StatusPrefix.Length))).Sum(s => s.StackCount) ?? 0) + applied.Values.Sum();
            public void Debuff(string key, int n, int rounds = 0)
            {
                if (n <= 0 || Target.IsEmpty) return;
                P.Status(U.RuntimeId, Target, new StableId(StatusPrefix + key), n, rounds);
                applied[key] = (applied.TryGetValue(key, out int old) ? old : 0) + n;
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
                Set("injuryRounds", Math.Max(0, Get("injuryUntil") - T.Context.Round));
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
            context.OnBattle("action_resolved", Resolved); context.OnBattle("damage_dealt", Hits);
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
            var t = new Turn(c, p); t.Next();
            bool repeat = t.Get("repeat") > 0; if (repeat) t.Set("repeat", 0);
            Apply(t, card);
            if (repeat) Apply(t, card);
            t.View();
        }
        private static void Apply(Turn t, JObject card)
        {
            var r = (JObject)card["rules"]; string id = (string)card["sourceId"];
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
            // White Snake's chance comes before its attack in the original text.
            if (Number(r, "flawChance") > 0 && t.Chance(Number(r, "flawChance"))) t.Debuff("flaw", Number(r, "flaw"));
            if (Number(r, "injuryRounds") > 0) t.Set("injuryUntil", t.T.Context.Round + Number(r, "injuryRounds"));
            int damage = t.T.Value(r, "damage");
            if (damage > 0) t.Attack(damage, Math.Max(1, Number(r, "hits")));
            if (Number(r, "randomMax") > 0)
            {
                int repeats = Math.Max(1, Number(r, "randomRepeats"));
                for (int i = 0; i < repeats; i++)
                {
                    int before = t.OmenUsed; int rolled = t.Random(Number(r, "randomMin"), Number(r, "randomMax"));
                    int attr = AttributeScaling.Read(repeats > 1 ? CombatScalingAttribute.Reaction : CombatScalingAttribute.Energy, t.U.Attributes.GetValueOrDefault());
                    if (repeats > 1) attr = attr * 4000 / 10000;
                    t.Attack(rolled * 3 + attr);
                    if (t.OmenUsed > before && Number(r, "qiOnOmen") > 0) t.Qi(Number(r, "qiOnOmen") * 4);
                }
            }
            if (Number(r, "chanceRepeats") > 0)
                for (int i = 0; i < Number(r, "chanceRepeats"); i++) if (t.Chance(Number(r, "attackChance"))) t.Attack(Number(r, "chanceAttack") * 3 + AttributeScaling.Read(CombatScalingAttribute.Reaction, t.U.Attributes.GetValueOrDefault()) * 4000 / 10000);
            t.S(t.T.Value(r, "shield")); t.Qi(t.T.Value(r, "qi"));
            if (Number(r, "maxHealth") > 0) t.Max(Number(r, "maxHealth") * 3);
            t.H(t.T.Value(r, "heal"));
            t.Star(Number(r, "starGain")); t.Omen(Number(r, "omenGain"));
            if (Number(r, "negativeOmen") > 0 && t.Negatives > 0) t.Omen(Number(r, "negativeOmen"));
            if (Number(r, "markNext") > 0) t.Mark(Number(r, "markNext"));
            if (t.Position)
            {
                if (Number(r, "positionAttack") > 0) t.Attack(Number(r, "positionAttack") * 3);
                t.S(Number(r, "positionDefense") * 3); t.Qi(Number(r, "positionQi") * 4);
                if (Flag(r, "positionAgain")) t.T.Draw();
                if (Number(r, "positionWeak") > 0) t.Debuff("weak", Number(r, "positionWeak"), 1);
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
                if (Number(r, "postHeal") > 0) t.H(Number(r, "postHeal") * 3 + AttributeScaling.Read(CombatScalingAttribute.Constitution, t.U.Attributes.GetValueOrDefault()) / 2);
                if (Flag(r, "postAgain")) t.T.Draw();
                if (Number(r, "postGuard") > 0) t.Guard(Number(r, "postGuard"));
            }
            if (Number(r, "weakChance") > 0 && t.Chance(Number(r, "weakChance"))) t.Debuff("weak", 1, 1);
            if (Number(r, "internal") > 0) t.Debuff("internal", Number(r, "internal"));
            if (Flag(r, "triggerInternal")) t.T.DirectDamage(t.Stacks("internal") * 3, t.Target);
            if (Number(r, "shieldPerOmen") > 0) t.S(t.Get("omen") * Number(r, "shieldPerOmen") * 3);
            if (Number(r, "healPerOmen") > 0) t.H(t.Get("omen") * Number(r, "healPerOmen") * 3);
            if (Number(r, "refundOmen") > 0) t.Omen(Math.Min(t.Get("omenUsed"), Number(r, "refundOmen")));
            if (Number(r, "hexFormation") > 0) t.Set("hexFormation", Number(r, "hexFormation"));
            if (Number(r, "enemyMaxHealth") > 0) t.T.AdjustMaximumHealth(t.Target, -Number(r, "enemyMaxHealth") * 3, Number(r, "enemyLoseHealth") == Number(r, "enemyMaxHealth"));
            else if (Number(r, "enemyLoseHealth") > 0) t.T.DirectDamage(Number(r, "enemyLoseHealth") * 3, t.Target);
            if (Flag(r, "consumeAllOmen"))
            {
                int n = t.Get("omen"); t.Set("omen", 0); t.Star(n); t.Qi(n * 4); t.H(n * 3);
            }
            if (Number(r, "soulCharges") > 0) t.Add("soulCharges", Number(r, "soulCharges"));
            if (Number(r, "astralLinks") > 0) t.Add("astralLinks", Number(r, "astralLinks"));
            if (Flag(r, "again") || Number(r, "againAtOmen") > 0 && t.Get("omen") >= Number(r, "againAtOmen")) t.T.Draw();
            if (Number(r, "againChance") > 0 && t.Chance(Number(r, "againChance"))) t.T.Draw();
            if (Flag(r, "repeatNext")) t.Set("repeat", 1);
            if (Flag(r, "allPositions"))
            {
                if (t.Get("slot") == 1) t.Star(Number(r, "firstSlotStar"));
                t.Set("positions", 255);
            }
            if (Number(r, "highestGain") > 0)
            {
                int qi = t.Energy / 4, omen = t.Get("omen"), star = t.Get("star"), n = Number(r, "highestGain");
                if (qi >= omen && qi >= star) t.Qi(n * 4); else if (omen >= star) t.Omen(n); else t.Star(n);
            }
            if (Number(r, "compareQiFlaw") > 0 && t.Enemy != null && t.Energy > t.Enemy.EnergyPools.GetCurrent(EnergyPoolKind.Fighting)) t.Debuff("flaw", Number(r, "compareQiFlaw"));
            if (Number(r, "defenseByNegative") > 0)
            {
                int defense = Math.Min(Number(r, "defenseCap"), 1 + t.Negatives * Number(r, "defenseByNegative"));
                t.S(defense * 3); if (t.Shield >= Number(r, "againAtDefense") * 3) t.T.Draw();
            }
            if (Number(r, "injuryRounds") > 0) t.Set("injuryUntil", t.T.Context.Round + Number(r, "injuryRounds"));
            if (Number(r, "bonusChance") > 0 && t.Chance(Number(r, "bonusChance"))) t.Attack(Number(r, "bonusAttack") * 3, Number(r, "bonusHits"));
            if (Number(r, "decreaseChance") > 0 && t.Chance(Number(r, "decreaseChance"))) t.Debuff("decrease", 1);
            if (t.Get("slot") % 2 == 1)
            {
                t.Star(Number(r, "oddStar")); if (Number(r, "oddInternal") > 0) t.Debuff("internal", Number(r, "oddInternal"));
            }
            else
            {
                int n = Math.Min(t.Get("star"), Number(r, "evenStarGuard")); t.Add("star", -n); t.Guard(n);
                if (Number(r, "evenExternal") > 0) t.Debuff("external", Number(r, "evenExternal"));
            }
            if (Number(r, "enemyQi") > 0 && t.Enemy != null)
            {
                var e = t.Enemy; var slot = e.EnergyPools.Primary.Kind == EnergyPoolKind.Fighting ? EnergyPoolSlot.Primary : e.EnergyPools.Secondary.Kind == EnergyPoolKind.Fighting ? EnergyPoolSlot.Secondary : EnergyPoolSlot.None;
                if (slot != EnergyPoolSlot.None) t.P.EnergyRestore(e.RuntimeId, slot, Math.Min(Number(r, "enemyQi") * 4, Math.Max(0, e.EnergyPools.GetMaximum(EnergyPoolKind.Fighting) - e.EnergyPools.GetCurrent(EnergyPoolKind.Fighting))));
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
        public static void Hits(ModBattleContext c, CodeBattlePlan p)
        {
            foreach (var actor in c.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            {
                var t = new Turn(c, p, actor);
                foreach (var hit in c.Hits.Where(h => !h.Direct && (h.Source == actor.RuntimeId || h.Target == actor.RuntimeId)))
                {
                    StableId target = hit.Source == actor.RuntimeId ? hit.Target : hit.Source;
                    if (t.Get("soulCharges") > 0) { t.Add("soulCharges", -1); p.Status(actor.RuntimeId, target, new StableId(StatusPrefix + "internal"), 1); }
                    if (hit.Source == actor.RuntimeId && t.Get("injuryUntil") > c.Round && SectRegistry.Cards.TryGetValue(c.ActionId.Value, out var card) && SectRegistry.Owner(card) == Lineage)
                        p.Status(actor.RuntimeId, hit.Target, new StableId(StatusPrefix + "internal"), 1);
                }
                t.View();
            }
        }
    }
}
