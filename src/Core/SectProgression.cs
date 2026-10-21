using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Reincarnation.Domain;

namespace YixianSect
{
    public static class SectProgression
    {
        // Only the shared native card-engine gate uses a maximum. Pack eligibility never does.
        public static int Rank(PlayerRunState player) => player.Lineages.Where(l => SectRegistry.IsSect(l.LineageId.Value)).Select(l => l.Rank).DefaultIfEmpty().Max();
        public static bool Native(PlayerRunState player) => player.Lineages.Any(l => l.LineageId == CardTableCatalog.LineageId);
        public static IReadOnlyList<StableId> GiftCandidates(StableId lineageId, int rank, PlayerSlotId slot, IReadOnlyList<StableId> owned)
            => SectRegistry.IsSect(lineageId.Value) && lineageId.Value != FiveElementRules.Lineage && rank == 2 && slot.IsValid
                ? SectRegistry.Packs.Where(p => SectRegistry.Owner(p) == lineageId.Value && (int)p["rank"] == 2).Select(SectRegistry.PackId).Where(id => owned == null || !owned.Contains(id)).ToArray()
                : System.Array.Empty<StableId>();
        private static readonly FieldInfo Purchased = AccessTools.Field(typeof(PlayerRunState), "purchasedShelfAbilityIds");
        private static readonly FieldInfo Known = AccessTools.Field(typeof(PlayerRunState), "knownCardPackIds");
        public static bool Refresh(PlayerRunState player)
        {
            if (Rank(player) == 0) return true;
            if (FiveDeckRules.Refresh(player)) return Native(player);
            var purchased = (List<StableId>)Purchased.GetValue(player);
            foreach (var gift in SectRegistry.Packs.Where(p => (int)p["rank"] == 1 && SectRegistry.Rank(player, SectRegistry.Owner(p)) > 0).Select(SectRegistry.PackId))
                if (!purchased.Contains(gift)) purchased.Add(gift);
            if (Native(player)) return true;
            var owned = Owned(player);
            var active = (List<StableId>)AccessTools.Field(typeof(PlayerRunState), "activeCardPackIds").GetValue(player);
            var known = (HashSet<StableId>)Known.GetValue(player);
            active.RemoveAll(id => !owned.Contains(id));
            // Unlocking is not equipping. Keep existing selections (including old saves)
            // and only fill the native minimum below. BL08/mixed players delegate above.
            foreach (var id in owned) known.Add(id);
            foreach (var id in owned)
                if (active.Count < System.Math.Min(3, owned.Count) && !active.Contains(id)) active.Add(id);
            return false;
        }
        public static List<StableId> Owned(PlayerRunState player) => SectRegistry.Rank(player, FiveElementRules.Lineage) > 0
            ? new List<StableId> { FiveDeckRules.PackId } : SectRegistry.Packs
            .Where(p => (int)p["rank"] <= SectRegistry.Rank(player, SectRegistry.Owner(p)))
            .Select(SectRegistry.PackId).Where(player.HasPurchasedShelfAbility).ToList();
        public static RuleResult SetActive(PlayerRunState player, IReadOnlyList<StableId> packIds)
        {
            bool five = SectRegistry.Rank(player, FiveElementRules.Lineage) > 0;
            var owned = five && Native(player)
                ? CardTableCatalog.Packs.Where(p => !FiveDeckRules.IsRetired(p.UnlockAbilityId) && player.HasPurchasedShelfAbility(p.UnlockAbilityId)).Select(p => p.UnlockAbilityId).Concat(new[] { FiveDeckRules.PackId }).Distinct().ToList()
                : Owned(player);
            if (packIds == null || packIds.Count > 8 || packIds.Count < (five && !Native(player) ? 1 : System.Math.Min(3, owned.Count)) || packIds.Distinct().Count() != packIds.Count || packIds.Any(id => !owned.Contains(id)) || (five && !packIds.Contains(FiveDeckRules.PackId)))
                return RuleResult.Failure(SessionErrorCode.InvalidRequest, player.Revision);
            var active = (List<StableId>)AccessTools.Field(typeof(PlayerRunState), "activeCardPackIds").GetValue(player);
            if (active.SequenceEqual(packIds)) return RuleResult.Success(player.Revision);
            AccessTools.Method(typeof(RevisionedState), "EnsureRevisionCanAdvance").Invoke(player, null);
            var known=(HashSet<StableId>)Known.GetValue(player);foreach(var id in packIds)known.Add(id);
            active.Clear(); active.AddRange(packIds);
            return RuleResult.Success((long)AccessTools.Method(typeof(RevisionedState), "CommitRevision").Invoke(player, null));
        }
    }

    // Enforce before spending points or advancing revision, including non-shop grant paths.
    [HarmonyPatch(typeof(PlayerRunState), "PurchaseLineage")]
    internal static class SectPurchaseExclusivity
    {
        private static bool Prefix(PlayerRunState __instance, StableId lineageId, ref RuleResult __result)
        {
            if (SectRegistry.CanAcquire(__instance, lineageId.Value)) return true;
            __result = RuleResult.Failure(SessionErrorCode.ItemUnavailable, __instance.Revision); return false;
        }
    }
    [HarmonyPatch(typeof(PlayerRunState), "AddLineage")]
    internal static class SectGrantExclusivity
    {
        private static bool Prefix(PlayerRunState __instance, StableId lineageId, ref RuleResult __result)
        {
            if (SectRegistry.CanAcquire(__instance, lineageId.Value)) return true;
            __result = RuleResult.Failure(SessionErrorCode.ItemUnavailable, __instance.Revision); return false;
        }
    }
}
