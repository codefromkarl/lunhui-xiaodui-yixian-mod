using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    public static class FiveElementRules
    {
        public const string Lineage = "yixian.sect.lineage.five";
        private const string StatusPrefix = "yixian.sect.status.five.";
        private static readonly string[] ElementViews = { "metal", "water", "wood", "fire", "earth" };
        private static readonly string[] Arrays = { "", "metalArray", "waterArray", "woodArray", "fireArray", "earthArray" };
        public static bool Generates(int previous, int next) => previous >= 1 && previous <= 5 && next == previous % 5 + 1;
        public static bool Links(int previous, int next) => previous >= 1 && previous <= 5 && (previous == next || Generates(previous, next));
        public static int ActiveCount(int mask) => Enumerable.Range(0, 5).Count(i => (mask & (1 << i)) != 0);
        public static bool CanPlay(JObject card, ModStateSnapshot state, StableId actor)
        {
            if (SectRegistry.Owner(card) != Lineage) return true;
            var rules = (JObject)card["rules"]; if (I(rules, "array") == 0 && I(rules, "activationArray") == 0) return true;
            return SectRegistry.Read(state, SectRegistry.StateKey(Lineage, actor, "consumed." + (string)card["key"])) == 0
                && SectRegistry.Read(state, SectRegistry.StateKey(Lineage, actor, "persistentCount")) < 2;
        }
        public static int PrintedCost(JObject card, ModStateSnapshot state, StableId actor)
        {
            var r = (JObject)card["rules"]; int element = I(card, "element");
            int active = SectRegistry.Read(state, SectRegistry.StateKey(Lineage, actor, "active"));
            int last = SectRegistry.Read(state, SectRegistry.StateKey(Lineage, actor, "last"));
            int next = SectRegistry.Read(state, SectRegistry.StateKey(Lineage, actor, "nextActivate"));
            bool ready = element > 0 && ((active & (1 << (element - 1))) != 0 || Links(last, element) || next > 0);
            return B(r, "freeWhenElement") && ready ? 0 : SectCardRules.PrintedCost(card);
        }
        public static bool ElementReady(BattleTurn t, int element) => element > 0 && ((t.Get("active") & (1 << (element - 1))) != 0 || Links(t.Get("last"), element));
        private static int I(JObject r, string key) => (int?)r[key] ?? 0;
        private static bool B(JObject r, string key) => (bool?)r[key] == true;
        private static void Add(BattleTurn t, string key, int amount) => t.Set(key, (int)Math.Min(9999L, (long)t.Get(key) + amount));
        private static ModCombatantSnapshot Enemy(BattleTurn t)
            => (t.Context.Action?.Targets ?? Array.Empty<StableId>()).Select(id => t.Context.Units.FirstOrDefault(u => u.RuntimeId == id && u.Side != t.Actor.Side && !u.IsDowned)).FirstOrDefault(u => u != null)
                ?? t.Context.Units.FirstOrDefault(u => u.Side != t.Actor.Side && !u.IsDowned);
        public static void LifeLoss(BattleTurn t, int amount, bool current = true)
        {
            var enemy = Enemy(t); if (enemy != null && amount > 0) t.AdjustMaximumHealth(enemy.RuntimeId, -amount, current);
        }
        private static void NonAttackDamage(BattleTurn t, int amount)
        {
            var enemy = Enemy(t); if (enemy != null && amount > 0) t.Plan.Damage(t.Actor.RuntimeId, enemy.RuntimeId, amount, DamageType.Physical);
        }
        public static void Activate(BattleTurn t, int element)
        {
            if (element < 1 || element > 5) return;
            t.Set("active", t.Get("active") | (1 << (element - 1))); Add(t, "activationCount", 1);
            if (element == 3) Add(t, "woodActivations", 1);
            FiveFateRules.Activated(t, element);
            NonAttackDamage(t, t.Get("activationArray") * 3);
        }
        private static void Heal(BattleTurn t, int amount)
        {
            if (amount <= 0) return; t.Heal(amount); t.Set("healed", 1);
        }
        private static void View(BattleTurn t)
        {
            t.View("last", "nextActivate", "sharp", "attack", "waterForce", "metalArray", "waterArray", "woodArray", "fireArray", "earthArray", "activationArray", "shieldReturn", "agility");
            t.Plan.ClearStatus(t.Actor.RuntimeId, new StableId(StatusPrefix + "elements"));
            for (int i = 0; i < 5; i++)
            {
                var id = new StableId(StatusPrefix + ElementViews[i]); t.Plan.ClearStatus(t.Actor.RuntimeId, id);
                if ((t.Get("active") & (1 << i)) != 0) t.Plan.Status(t.Actor.RuntimeId, t.Actor.RuntimeId, id, 1);
            }
        }
        public static void Register(ICodeModContext context)
        {
            foreach (var card in SectRegistry.Cards.Where(c => SectRegistry.Owner(c.Value) == Lineage)) context.OnAction(card.Key, Play);
            context.OnBattle("battle_start", Start); context.OnBattle("round_start", Round);
            context.OnBattle("round_end", End); context.OnBattle("damage_dealt", Hits); context.OnBattle("action_resolved", Resolved);
            context.AddDamageRule(IncomingDamage);
        }
        public static int ReduceIncoming(int amount, DamageType type, int round, int diveUntil, int ironUntil)
        {
            if (amount <= 0) return amount;
            if (round < diveUntil && (type == DamageType.Physical || type == DamageType.Magic)) amount = Math.Max(1, (int)((long)amount * 6000 / 10000));
            if (round < ironUntil) amount = Math.Max(1, amount - 15);
            return amount;
        }
        public static int IncomingDamage(ModDamageQuery query)
        {
            if (query.Target == null || query.Battle == null) return query.Amount;
            int Until(string key) => query.Target.Statuses.Where(s => s.StatusId.Value == StatusPrefix + key).Select(s => s.FlatAmount).DefaultIfEmpty().Max();
            return ReduceIncoming(query.Amount, query.Type, query.Battle.Round, Until("dive"), Until("iron"));
        }
        public static void Start(ModBattleContext context, CodeBattlePlan plan)
        {
            var deckMasks = context.Units.ToDictionary(u => u.RuntimeId, u => plan.GetInt(SectRegistry.StateKey(Lineage, u.RuntimeId, "deckElements")));
            var mastery = context.Units.ToDictionary(u=>u.RuntimeId,u=>plan.GetInt(SectRegistry.StateKey(Lineage,u.RuntimeId,"waterMastery")));
            BattleTurn.Reset(context, plan, Lineage);
            foreach (var actor in context.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            {
                var t = new BattleTurn(context, plan, Lineage, actor);
                foreach (var key in new[] { "dive", "iron" }) plan.ClearStatus(actor.RuntimeId, new StableId(StatusPrefix + key));
                t.Set("deckElements", deckMasks[actor.RuntimeId]);
                if(deckMasks[actor.RuntimeId]==2&&mastery[actor.RuntimeId]>0&&SectRegistry.Rank(actor,Lineage)>=FiveCardSelection.WaterMasteryMinimumRank)t.Set("waterMastery",1);
                FiveFateRules.Start(t); View(t);
            }
        }
        private static void Attack(BattleTurn t, int damage, int hits, bool pierce)
        {
            var enemy = Enemy(t); if (enemy == null || damage <= 0) return;
            // A true authored attack preserves direct-hit records while bypassing native shield.
            for (int hit = 0; hit < hits; hit++)
            {
                bool bypass = pierce && hit == 0;
                t.Plan.Use("effect.skill", new[] { enemy.RuntimeId }, new Dictionary<string, object> { ["effects"] = 1, ["e0.primitive"] = 10, ["e0.target"] = 10, ["e0.flat"] = damage, ["e0.damage"] = bypass ? 40 : 10, ["e0.shape"] = 10 }, caster: t.Actor.RuntimeId);
            }
        }
        public static void Play(ModBattleContext context, CodeBattlePlan plan)
        {
            if (!SectRegistry.Cards.TryGetValue(context.ActionId.Value, out var card) || SectRegistry.Owner(card) != Lineage) return;
            if (!CanPlay(card, context.State, context.ActorId)) throw new InvalidOperationException("本场已使用此持续牌，或持续牌已达2张上限。");
            var t = new BattleTurn(context, plan, Lineage); var r = (JObject)card["rules"]; int element = I(card, "element");
            if (I(r, "array") > 0 || I(r, "activationArray") > 0) { t.Set("consumed." + (string)card["key"], 1); Add(t, "persistentCount", 1); }
            if (element > 0 && t.Get("nextActivate") > 0) { t.Set("nextActivate", t.Get("nextActivate") - 1); Activate(t, element); }
            if (I(r, "activate") > 0) Activate(t, I(r, "activate"));
            if(element!=2&&t.Get("waterMastery")>0)t.Set("waterMastery",0);
            bool ready = ElementReady(t, element); int id = I(r, "originalRule");
            t.Set("pendingMetalReady", ElementReady(t, 1) ? 1 : 0); t.Set("pendingWaterReady", ElementReady(t, 2) ? 1 : 0);
            // Post-attack gains are committed at ActionResolved, after actual health-hit processing.
            plan.SetState(t.Key("pendingCard"), (string)card["key"]); t.Set("pendingReady", ready ? 1 : 0); t.Set("hitHandled", 0);
            t.Set("pendingArray", element > 0 ? t.Get(Arrays[element]) : 0);
            int qiGain = t.Value(r, "qi"); t.Qi(qiGain);
            int qi = Math.Min(t.Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting), t.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting) + qiGain) / 4;
            if (I(r, "maxHealth") > 0) t.MaxHealth(I(r, "maxHealth") * 3);
            Heal(t, t.Value(r, "heal")); t.Shield(t.Value(r, "shield"));
            Add(t, "sharp", I(r, "sharp")); Add(t, "attack", I(r, "attack")); Add(t, "waterForce", I(r, "water"));
            if (I(r, "lifeLoss") > 0) LifeLoss(t, I(r, "lifeLoss") * 3);
            if (I(r, "enemyMaxLoss") > 0) LifeLoss(t, I(r, "enemyMaxLoss") * 3, false);
            if (I(r, "directDamage") > 0) NonAttackDamage(t, I(r, "directDamage"));
            if (ready)
            {
                if (I(r, "conditionalLifeLoss") > 0) LifeLoss(t, (I(r, "conditionalLifeLoss") + I(r, "lifeLossPerAttack") * t.Get("attack")) * 3);
                if (I(r, "conditionalMaxHealth") > 0) { int gain = (I(r, "conditionalMaxHealth") + I(r, "maxHealthPerWater") * t.Get("waterForce")) * 3; t.MaxHealth(gain); Heal(t, gain); }
                if (I(r, "conditionalWaterPerQi") > 0) Add(t, "waterForce", qi / I(r, "conditionalWaterPerQi"));
                if (I(r, "conditionalAttackPerQi") > 0) Add(t, "attack", qi / I(r, "conditionalAttackPerQi"));
                if (I(r, "conditionalReductionBp") > 0 || I(r, "conditionalReductionFlat") > 0)
                {
                    string kind = I(r, "conditionalReductionBp") > 0 ? "dive" : "iron";
                    int until = Math.Max(context.Round, t.Get(kind + "Until")) + I(r, "duration"); t.Set(kind + "Until", until); plan.ClearStatus(t.Actor.RuntimeId, new StableId(StatusPrefix + kind)); plan.Status(t.Actor.RuntimeId, t.Actor.RuntimeId, new StableId(StatusPrefix + kind), 1, flat: until);
                }
                if (I(r, "conditionalShieldReturn") > 0) Add(t, "shieldReturn", I(r, "conditionalShieldReturn"));
                if (I(r, "conditionalSpring") > 0) { t.Set("springUntil", Math.Max(context.Round, t.Get("springUntil")) + I(r, "duration")); t.Set("springAmount", I(r, "conditionalSpring") * 3); }
                if (I(r, "conditionalAgilityPerResource") > 0)
                {
                    int agility = Math.Min(I(r, "agilityCap"), (t.Get("waterForce") + qi) * I(r, "conditionalAgilityPerResource")) + t.Get("agility");
                    for (int i = 0; i < agility / 10; i++) t.Draw(); t.Set("agility", agility % 10);
                }
            }
            if (id == 113)
            {
                int bonus = ready ? t.Get("attack") * I(r, "conditionalLifeLossPerAttack") : 0;
                if (ElementReady(t, 5)) bonus += t.Actor.Shield / (I(r, "earthLossPerShield") * 3);
                if (ElementReady(t, 1)) bonus += t.Get("sharp") / I(r, "metalLossPerSharp");
                LifeLoss(t, bonus * 3);
            }
            int damage = t.Value(r, "damage") + (I(r, "damage") > 0 ? t.Get("attack") * 3 : 0);
            if (ready && I(r, "damagePerLostShield") > 0) damage += t.Get("lostShield") / (I(r, "damagePerLostShield") * 3) * 3;
            if (ready && I(r, "shieldPerLostShield") > 0) t.Shield(t.Get("lostShield") / (I(r, "shieldPerLostShield") * 3) * 3);
            if (I(r, "damagePerDeckElement") > 0) damage += ActiveCount(t.Get("deckElements")) * I(r, "damagePerDeckElement") * 3;
            bool waterAttack = id == 115 && ElementReady(t, 2), woodAttack = id == 115 && ElementReady(t, 3);
            bool willAttack = damage > 0 || waterAttack || woodAttack;
            bool pierce = t.Get("pierceNext") > 0 && willAttack;
            if (pierce) t.Set("pierceNext", t.Get("pierceNext") - 1);
            if (willAttack && t.Get("nextWaterCharges") > 0)
            {
                t.Set("pendingWaterPerHealth", t.Get("nextWaterPerHealth")); t.Set("nextWaterCharges", t.Get("nextWaterCharges") - 1);
                if (t.Get("nextWaterCharges") == 0) t.Set("nextWaterPerHealth", 0);
            }
            else t.Set("pendingWaterPerHealth", 0);
            Attack(t, damage, I(r, "hits") > 0 ? I(r, "hits") : 1, pierce && damage > 0);
            bool firstSpecial = damage <= 0;
            if (waterAttack)
            {
                Attack(t, (I(r, "waterAttack") + t.Get("waterForce") * I(r, "waterDamagePerWater") + t.Get("attack")) * 3 + t.Value(r, "specialDamage"), 1, pierce && firstSpecial);
                firstSpecial = false;
            }
            if (woodAttack) Attack(t, (I(r, "woodAttack") + qi * I(r, "woodDamagePerQi") + t.Get("attack")) * 3 + t.Value(r, "specialDamage"), 1, pierce && firstSpecial);
            t.Set("last", element); t.Set("elements", ActiveCount(t.Get("active"))); View(t);
        }
        public static void Hits(ModBattleContext context, CodeBattlePlan plan)
        {
            foreach (var actor in context.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            {
                var t = new BattleTurn(context, plan, Lineage, actor);
                foreach (var incoming in context.Hits.Where(h => h.Target == actor.RuntimeId && h.ShieldDamage > 0))
                {
                    Add(t, "lostShield", incoming.ShieldDamage);
                    if (t.Get("shieldReturn") > 0) { t.Shield(incoming.ShieldDamage); t.Set("shieldReturn", t.Get("shieldReturn") - 1); }
                }
                string key = plan.GetText(t.Key("pendingCard"));
                if (key.Length == 0 || t.Get("hitHandled") > 0 || !SectRegistry.Cards.TryGetValue(SectRegistry.CardPrefix + key, out var card)) { View(t); continue; }
                var r = (JObject)card["rules"]; bool ready = t.Get("pendingReady") > 0;
                var hits = context.Hits.Where(h => h.Source == actor.RuntimeId && h.Direct && context.Units.FirstOrDefault(u => u.RuntimeId == h.Target)?.Side != actor.Side).ToList();
                if (hits.Count == 0) { View(t); continue; }
                t.Set("hitHandled", 1);
                int sharp = t.Get("sharp"); int index = 0;
                foreach (var hit in hits)
                {
                    if (B(r, "breakShield") && hit.ShieldDamage > 0) plan.Shield(hit.Target, -hit.ShieldDamage);
                    if (t.Get("springUntil") > context.Round && t.Get("springAmount") > 0) { t.MaxHealth(t.Get("springAmount")); Heal(t, t.Get("springAmount")); }
                    if (hit.HealthDamage <= 0) { index++; continue; }
                    int extra = 0;
                    if (!B(r, "noSharp") && sharp > 0)
                    {
                        extra += sharp * 3; sharp = ready && I(r, "sharpReturnBp") > 0 ? (sharp * I(r, "sharpReturnBp") + 9999) / 10000 : 0;
                    }
                    if (ready && B(r, "doubleHealth")) extra += hit.HealthDamage + extra;
                    if (extra > 0) t.DirectDamage(extra, hit.Target);
                    int health = hit.HealthDamage + extra;
                    if (ready && I(r, "sharpPerHealth") > 0) sharp += health / (I(r, "sharpPerHealth") * 3);
                    if (ready && I(r, "healPerHealth") > 0) Heal(t, health / (I(r, "healPerHealth") * 3) * 3);
                    if (ready && I(r, "maxLossPerHealth") > 0) t.AdjustMaximumHealth(hit.Target, -health / (I(r, "maxLossPerHealth") * 3) * I(r, "maxLossMultiplier") * 3, false);
                    if (index == 0 && t.Get("pendingWaterPerHealth") > 0) Add(t, "waterForce", health / (t.Get("pendingWaterPerHealth") * 3));
                    index++;
                }
                t.Set("sharp", sharp); View(t);
            }
        }
        public static void Resolved(ModBattleContext context, CodeBattlePlan plan)
        {
            var actor = context.Units.FirstOrDefault(u => u.RuntimeId == context.ActorId);
            if (actor == null || actor.IsDowned || SectRegistry.Rank(actor, Lineage) == 0) return;
            bool own = SectRegistry.Cards.TryGetValue(context.ActionId.Value, out var card) && SectRegistry.Owner(card) == Lineage;
            if (!own && !CardTableCatalog.TryGetCard(context.ActionId, out var _)) return;
            var t = new BattleTurn(context, plan, Lineage, actor);
            if (own && plan.GetText(t.Key("pendingCard")) != (string)card["key"]) return;
            int element = own ? I(card, "element") : 0;
            if (element == 0) t.Set("last", 0);
            if (!own || plan.GetText(t.Key("pendingCard")) == (string)card["key"])
                if (element == 0) FiveFateRules.Neutral(t);
            int previousDrain = t.Get("drainPlays");
            if (previousDrain > 0)
            {
                int amount = (t.Get("drainBase") + t.Get("attack") * t.Get("drainPerAttack")) * 3; var enemy = Enemy(t);
                if (enemy != null) { t.DirectDamage(amount, enemy.RuntimeId); Heal(t, Math.Min(amount, enemy.CurrentHealth)); } t.Set("drainPlays", previousDrain - 1);
            }
            if (own && plan.GetText(t.Key("pendingCard")) == (string)card["key"])
            {
                var r = (JObject)card["rules"]; bool ready = t.Get("pendingReady") > 0;
                if (ready)
                {
                    Add(t, "sharp", I(r, "conditionalSharp")); Add(t, "attack", I(r, "conditionalAttack")); Add(t, "waterForce", I(r, "conditionalWater"));
                    Heal(t, t.Value(r, "conditionalHeal") + (I(r, "healPerAttack") > 0 ? t.Get("attack") * I(r, "healPerAttack") * 3 : 0));
                    if (I(r, "conditionalSharpPerShield") > 0) Add(t, "sharp", actor.Shield / (I(r, "conditionalSharpPerShield") * 3));
                    if (B(r, "conditionalPierceNext")) t.Set("pierceNext", t.Get("pierceNext") + 1);
                    if (I(r, "conditionalNextWaterPerHealth") > 0) { t.Set("nextWaterPerHealth", I(r, "conditionalNextWaterPerHealth")); Add(t, "nextWaterCharges", 1); }
                    if (B(r, "conditionalAgain") || B(r, "conditionalAgainIfHealed") && t.Get("healed") > 0) t.Draw();
                    if (I(r, "capAtHealthMinus") > 0)
                    {
                        var enemy = Enemy(t); if (enemy != null) t.AdjustMaximumHealth(enemy.RuntimeId, Math.Min(0, enemy.CurrentHealth - I(r, "capAtHealthMinus") * 3 - enemy.CurrentMaximumHealth), false);
                    }
                }
                if (I(r, "conditionalAgainEither") > 0 && (t.Get("pendingMetalReady") > 0 || t.Get("pendingWaterReady") > 0)) t.Draw();
                if (ready && I(r, "conditionalDrainPlays") > 0) { Add(t, "drainPlays", I(r, "conditionalDrainPlays")); t.Set("drainBase", I(r, "drainBase")); t.Set("drainPerAttack", I(r, "drainPerAttack")); }
                int array = t.Get("pendingArray");
                if (element == 1) Add(t, "sharp", array); else if (element == 2) t.Qi(array * 4); else if (element == 3) Heal(t, array * 3); else if (element == 4) LifeLoss(t, array * 3); else if (element == 5) t.Shield(array * 3);
                if (I(r, "array") > 0) Add(t, Arrays[I(r, "array")], I(r, "arrayGain"));
                if (I(r, "activationArray") > 0) Add(t, "activationArray", I(r, "activationArray"));
                plan.SetState(t.Key("pendingCard"), "");
            }
            t.Set("elements", ActiveCount(t.Get("active"))); View(t);
        }
        public static void Round(ModBattleContext context, CodeBattlePlan plan)
        {
            foreach (var actor in context.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            {
                var t = new BattleTurn(context, plan, Lineage, actor); if (t.Get("roundTick") == context.Round) continue; t.Set("roundTick", context.Round);
                foreach (string kind in new[] { "dive", "iron" }) if (context.Round >= t.Get(kind + "Until")) plan.ClearStatus(actor.RuntimeId, new StableId(StatusPrefix + kind));
                View(t);
            }
        }
        public static void End(ModBattleContext context, CodeBattlePlan plan)
        {
            foreach (var actor in context.Units.Where(u => !u.IsDowned && SectRegistry.Rank(u, Lineage) > 0))
            {
                var t = new BattleTurn(context, plan, Lineage, actor); if (t.Get("endTick") == context.Round) continue; t.Set("endTick", context.Round);
                NonAttackDamage(t, t.Get("waterForce") * 3 * (t.Get("waterMastery")>0&&t.Get("deckElements")==2&&SectRegistry.Rank(actor,Lineage)>=FiveCardSelection.WaterMasteryMinimumRank?FiveCardSelection.WaterMasteryMultiplier:1)); View(t);
            }
        }
    }
}
