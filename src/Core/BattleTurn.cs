using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    // Shared native primitives for new schools. SwordRules retains its proven v0.3.2 arithmetic.
    public sealed class BattleTurn
    {
        public readonly ModBattleContext Context;
        public readonly CodeBattlePlan Plan;
        public readonly ModCombatantSnapshot Actor;
        public readonly string Sect;
        private int energy;
        public BattleTurn(ModBattleContext context, CodeBattlePlan plan, string sect, ModCombatantSnapshot actor = null)
        {
            Context = context; Plan = plan; Sect = sect;
            Actor = actor ?? context.Units.FirstOrDefault(u => u.RuntimeId == context.ActorId);
            if (Actor == null || Actor.IsDowned || SectRegistry.Rank(Actor, sect) == 0) throw new InvalidOperationException("卡牌需要对应门派血统。");
            energy = Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
        }
        public string Key(string key) => SectRegistry.StateKey(Sect, Actor.RuntimeId, key);
        public int Get(string key) => Plan.GetInt(Key(key));
        public void Set(string key, int value) => Plan.SetState(Key(key), Math.Max(0, value));
        public void Add(string key, int amount) => Set(key, Get(key) + amount);
        public int Value(JObject rules, string key)
            => Sect == FiveElementRules.Lineage && rules[key + "Scaling"] is JArray
                ? FiveCultivation.Value(this, rules, key)
                : ((int?)rules[key] ?? 0) + FixedPointMath.ScaleByBasisPointsFloor(AttributeScaling.Read((CombatScalingAttribute)((int?)rules[key + "Attr"] ?? 40), Actor.Attributes.GetValueOrDefault()), (int?)rules[key + "Bp"] ?? 0);
        public void Damage(int amount, int hits = 1, StableId target = default)
        {
            var targets = target.IsEmpty ? Context.Action?.Targets ?? Array.Empty<StableId>() : new[] { target };
            if (amount <= 0 || targets.Count == 0) return;
            var values = new Dictionary<string, object> { ["effects"] = 1, ["e0.primitive"] = 10, ["e0.target"] = 10, ["e0.flat"] = amount, ["e0.damage"] = 10, ["e0.shape"] = 10 };
            for (int i = 0; i < hits; i++) Plan.Use("effect.skill", targets, values, caster: Actor.RuntimeId);
        }
        public void Shield(int amount) { if (amount > 0) Plan.Shield(Actor.RuntimeId, amount); }
        public void Heal(int amount)
        {
            if(amount<=0)return;
            if(OriginalHealthRules.HasMaximum(Plan, Actor.RuntimeId))OriginalHealthRules.Queue(Plan, Actor.RuntimeId, Actor.RuntimeId, "heal", amount);
            else Plan.Heal(Actor.RuntimeId, amount);
        }
        public void AdjustMaximumHealth(StableId target,int delta,bool changeCurrent)
        {
            if(!Context.Units.Any(u=>u.RuntimeId==target))throw new InvalidOperationException("最大生命调整目标无效。");
            OriginalHealthRules.Queue(Plan,Actor.RuntimeId,target,"maximum",delta,changeCurrent);
        }
        public void MaxHealth(int delta,StableId target=default,bool changeCurrent=false)
            =>AdjustMaximumHealth(target.IsEmpty?Actor.RuntimeId:target,delta,changeCurrent);
        public void DirectDamage(int amount,StableId target=default)
        {
            if(target.IsEmpty)target=Context.Action?.Targets.FirstOrDefault()??default;
            if(target.IsEmpty||amount<=0)return;
            OriginalHealthRules.Queue(Plan,Actor.RuntimeId,target,"loss",amount);
        }
        public void NonAttackDamage(int amount,StableId target=default)
        {
            if(target.IsEmpty)target=Context.Action?.Targets.FirstOrDefault()??default;
            if(!target.IsEmpty && amount>0)OriginalHealthRules.Queue(Plan,Actor.RuntimeId,target,"nonAttack",amount);
        }
        public void EnemyStatus(string statusId,int stacks,int rounds=0)
        {
            if(stacks<=0)return;
            foreach(var target in Context.Action?.Targets??Array.Empty<StableId>())
                Plan.Status(Actor.RuntimeId,target,new StableId(statusId),stacks,rounds);
        }
        public void Qi(int amount)
        {
            var slot = Actor.EnergyPools.Primary.Kind == EnergyPoolKind.Fighting ? EnergyPoolSlot.Primary : Actor.EnergyPools.Secondary.Kind == EnergyPoolKind.Fighting ? EnergyPoolSlot.Secondary : EnergyPoolSlot.None;
            int gain = Math.Min(amount, Math.Max(0, Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting) - energy));
            if (slot == EnergyPoolSlot.None || gain <= 0) return;
            energy += gain; Plan.EnergyRestore(Actor.RuntimeId, slot, gain);
        }
        // The draw bridge keeps this in the same authoritative command transaction.
        public void Draw() { var key = "cards." + Actor.RuntimeId.Value + ".drawPending"; Plan.SetState(key, Plan.GetInt(key) + 1); }
        public void Basic(JObject rules, int extraDamage = 0)
        {
            Damage(Value(rules, "damage") + extraDamage, (int?)rules["hits"] ?? 1);
            Shield(Value(rules, "shield")); Heal(Value(rules, "heal")); Qi(Value(rules, "qi"));
        }
        public void View(params string[] names)
        {
            foreach (var name in names)
            {
                var id = new StableId("yixian.sect.status." + SectRegistry.Prefix(Sect) + "." + name);
                Plan.ClearStatus(Actor.RuntimeId, id);
                if (Get(name) > 0) Plan.Status(Actor.RuntimeId, Actor.RuntimeId, id, Get(name));
            }
        }
        public static void Reset(ModBattleContext context, CodeBattlePlan plan, string sect)
        {
            foreach (var actor in context.Units.Where(u => SectRegistry.Rank(u, sect) > 0))
            {
                string prefix = SectRegistry.StateKey(sect, actor.RuntimeId, "");
                foreach (var entry in context.State.Entries.Where(e => e.PackageId == SectRegistry.Package && e.Key.StartsWith(prefix, StringComparison.Ordinal))) plan.RemoveState(entry.Key);
                plan.RemoveState("cards." + actor.RuntimeId.Value + ".drawPending");
            }
        }
    }
}
