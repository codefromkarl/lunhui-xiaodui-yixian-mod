using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.Runtime.Content;

namespace YixianSect
{
    // A derived projection, never a write to Birth/Purchased/Growth attributes.
    public static class SpiritConversion
    {
        public const string Ability = "yixian.sect.training.sword.spirit_conversion";
        public const int RateBp = 6000;
        private const string MarkerPrefix = "yixian.sect.spirit-conversion.";
        public const string Description = "持续被动：将筋肉、反应、体质各扣除60%，各项向下取整；实际扣除量合计转为能量，原有能量保留。持有期间持续按未转化的持久属性重算，后续加点、成长与装备属性同样参与转化，不重复扣减；不转换战斗临时属性。生命、气池、防御与暴击随转化后属性重算。购买即生效，本局限购一次，不占手牌。";
        public static bool Has(PlayerRunState player) => player != null
            && SectRegistry.Rank(player, SwordRules.Lineage) > 0
            && player.HasPurchasedShelfAbility(new StableId(Ability));
        public static bool IsMarker(StatModifier modifier) => modifier.SourceId.Value?.StartsWith(MarkerPrefix, StringComparison.Ordinal) == true;
        public static void AppendMarker(PlayerRunState player, List<StatModifier> modifiers)
        {
            if (Has(player) && !modifiers.Any(IsMarker))
                modifiers.Add(new StatModifier(new StableId(MarkerPrefix + player.Slot.Value), default));
        }
        public static StatBlock Convert(StatBlock original)
        {
            int muscle = checked((int)((long)original.Muscle * RateBp / 10000));
            int reaction = checked((int)((long)original.Reaction * RateBp / 10000));
            int constitution = checked((int)((long)original.Constitution * RateBp / 10000));
            return new StatBlock(original.Muscle - muscle, original.Reaction - reaction,
                original.Constitution - constitution, checked(original.Energy + muscle + reaction + constitution));
        }
        private static StatModifier WithoutAttributes(StatModifier m) => new StatModifier(m.SourceId, default,
            maximumHealthFlat: m.MaximumHealthFlat, maximumHealthBasisPoints: m.MaximumHealthBasisPoints,
            maximumMagicFlat: m.MaximumMagicFlat, maximumMagicBasisPoints: m.MaximumMagicBasisPoints,
            physicalDefenseFlat: m.PhysicalDefenseFlat, magicDefenseFlat: m.MagicDefenseFlat,
            mentalResistanceBasisPoints: m.MentalResistanceBasisPoints,
            criticalChanceBasisPoints: m.CriticalChanceBasisPoints, criticalDamageBasisPoints: m.CriticalDamageBasisPoints,
            shieldEfficiencyBasisPoints: m.ShieldEfficiencyBasisPoints, evasionRatingCenti: m.EvasionRatingCenti,
            summonStrength: m.SummonStrength, attributeBasisPoints: 0,
            physicalDefenseBasisPoints: m.PhysicalDefenseBasisPoints, magicDefenseBasisPoints: m.MagicDefenseBasisPoints,
            mentalResistanceCapBasisPoints: m.MentalResistanceCapBasisPoints, inspirationMaximumFlat: m.InspirationMaximumFlat);
        public static DerivedStats Rebuild(DerivedStats unconverted, IReadOnlyList<StatModifier> modifiers, EnergyPoolKind pool)
        {
            // The native first pass already applied flat AND percentage attribute
            // modifiers. Reuse those effective values exactly once, retaining every
            // non-attribute modifier. No marker remains, so this cannot recurse.
            var remaining = modifiers.Where(m => m.IsValid && !IsMarker(m)).Select(WithoutAttributes).ToArray();
            return DerivedStatCalculator.Calculate(Convert(unconverted.EffectiveAttributes), remaining, pool);
        }
        internal static void Refresh(RunAuthorityService authority, PlayerSlotId slot)
        {
            if (!authority.Run.TryGetPlayer(slot, out var player) || !Has(player)) return;
            var catalog = (BattleAuthorityCatalog)AccessTools.Field(typeof(RunAuthorityService), "battleCatalog").GetValue(authority);
            if (catalog == null) return; // Native non-battle catalog users have no derived-stat projection.
            var method = AccessTools.Method(typeof(BattleAuthorityService), "CalculatePlayerPresentationStats",
                new[] { typeof(RunState), typeof(BattleAuthorityCatalog), typeof(PlayerRunState), typeof(EnergyPoolSet).MakeByRefType() });
            object[] args = { authority.Run, catalog, player, default(EnergyPoolSet) };
            var stats = (DerivedStats)method.Invoke(null, args);
            var pools = (EnergyPoolSet)args[3];
            if (!player.ApplyMainEnergyPool(stats, pools).IsSuccess)
                throw new InvalidOperationException("灵剑转化后的属性／气池刷新失败。");
        }
    }
    [HarmonyPatch(typeof(BattleAuthorityService), "CreatePersistentStatModifiers")]
    internal static class SpiritConversionMarker
    {
        private static void Postfix(PlayerRunState runPlayer, List<StatModifier> __result)
            => SpiritConversion.AppendMarker(runPlayer, __result);
    }
    [HarmonyPatch(typeof(DerivedStatCalculator), "Calculate", new[] { typeof(StatBlock), typeof(IReadOnlyList<StatModifier>), typeof(EnergyPoolKind) })]
    internal static class SpiritConversionDerivedStats
    {
        private static void Postfix(IReadOnlyList<StatModifier> modifiers, EnergyPoolKind primaryPoolKind, ref DerivedStats __result)
        {
            if (modifiers != null && modifiers.Any(SpiritConversion.IsMarker))
                __result = SpiritConversion.Rebuild(__result, modifiers, primaryPoolKind);
        }
    }
    [HarmonyPatch(typeof(RunAuthorityService), "PurchaseShelfAbility")]
    internal static class SpiritConversionPurchase
    {
        private static void Postfix(RunAuthorityService __instance, PlayerSlotId playerSlot, StableId abilityId, RuleResult<ShelfAbilityPurchaseReceipt> __result)
        {
            if (__result.IsSuccess && abilityId.Value == SpiritConversion.Ability)
                SpiritConversion.Refresh(__instance, playerSlot);
        }
    }
    [HarmonyPatch(typeof(RunAuthorityService), "PurchaseAttributePoint")]
    internal static class SpiritConversionAttributePurchase
    {
        private static void Postfix(RunAuthorityService __instance, PlayerSlotId playerSlot,
            ref RuleResult<AttributePurchaseReceipt> __result)
        {
            if (!__result.TryGetValue(out var receipt) || !__instance.Run.TryGetPlayer(playerSlot, out var player)
                || !SpiritConversion.Has(player)) return;
            // Native attribute purchase adds directly to the last displayed stats.
            // Project full persistent sources again before returning the result.
            SpiritConversion.Refresh(__instance, playerSlot);
            var updated = new AttributePurchaseReceipt(receipt.Attribute, receipt.Points, receipt.PaidPrice,
                AttributeExchangeRules.GetValue(player.DerivedStats.EffectiveAttributes, receipt.Attribute), player.Revision);
            __result = RuleResult<AttributePurchaseReceipt>.Success(updated, __result.Revision, __result.Events);
        }
    }
    [HarmonyPatch(typeof(Reincarnation.UI.Flow.RunUIPresentationMapper), "DescribeAbilityOperation")]
    internal static class SpiritConversionDescription
    {
        private static void Postfix(StableId abilityId, ref string __result)
        {
            if (abilityId.Value == SpiritConversion.Ability) __result = SpiritConversion.Description;
        }
    }
    [HarmonyPatch(typeof(Reincarnation.UI.Flow.RunUIPresentationMapper), "BuildAbilityFunctionalOneLiner")]
    internal static class SpiritConversionOneLiner
    {
        private static void Postfix(RuntimeAbilityDefinition ability, ref string __result)
        {
            if (ability?.StableId.Value == SpiritConversion.Ability)
                __result = "持续将筋肉／反应／体质各60%转为能量，后续加点同样生效。";
        }
    }
}
