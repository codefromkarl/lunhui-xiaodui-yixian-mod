using System;
using System.Linq;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;

namespace YixianSect
{
    // Keep the native targeting/confirmation flow and rings. Only replace the affected set
    // for the four authored cards; no access to the user's authority or mutable save state.
    [HarmonyPatch(typeof(GameHUDWindow), "CollectAffectedTargets")]
    internal static class SwordAreaPreview
    {
        private static readonly System.Reflection.FieldInfo Hand = AccessTools.Field(typeof(GameHUDWindow), "cardTable");
        private static readonly System.Reflection.FieldInfo Snapshot = AccessTools.Field(typeof(GameHUDWindow), "battlefieldSnapshot");
        private static readonly System.Reflection.MethodInfo Camera = AccessTools.Method(typeof(GameHUDWindow), "ResolveBattleCamera");
        private static readonly System.Reflection.MethodInfo AddRing = AccessTools.Method(typeof(GameHUDWindow), "AddRingPoint");
        private static bool Prefix(GameHUDWindow __instance, ActionCardViewModel action, CombatantCardViewModel hovered)
        {
            var hand = (CardTableViewModel)Hand.GetValue(__instance);
            string id = hand?.Hand.FirstOrDefault(c => c.Action.StableId == action?.StableId)?.CardId ?? action?.CommandActionId;
            if (id == null || !SwordRules.Cards.TryGetValue(id, out var card) || card["area"] == null || hovered == null) return true;
            var snapshot = (RunAuthoritySnapshot)Snapshot.GetValue(__instance);
            var battle = snapshot?.Battle;
            var actor = battle?.Combatants.FirstOrDefault(u => u.Kind == CombatantKind.Player && u.OwnerSlot == snapshot.ViewerSlot && !u.IsDowned);
            if (actor == null) return true;
            var owner = snapshot.Players.FirstOrDefault(p => p.Slot == actor.OwnerSlot);
            bool linked = actor.Statuses.Any(s => s.StatusId.Value == "yixian.sect.status.cloud" && s.Stacks.Count > 0)
                || (owner != null && owner.Lineages.Any(l => l.LineageId.Value == SwordRules.Lineage && l.Rank >= FateRules.Required(FateRules.CloudSea))
                    && owner.HasPurchasedShelfAbility(new StableId(FateRules.CloudSea)));
            var primary = new StableId(hovered.RuntimeId);
            // Keep the native projected guard/taunt resolution for the primary.
            var resolved = action.AttackDamagePreviews.FirstOrDefault(p => p.TargetId == primary)?.ResolvedTargetId ?? primary;
            if (battle.Combatants.Any(u => u.RuntimeId == resolved && u.Side != actor.Side && !u.IsDowned)) primary = resolved;
            var ids = SwordTargeting.Select(SwordTargeting.For(battle), actor.RuntimeId, primary, card, linked);
            var camera = Camera.Invoke(__instance, null);
            // Unlinked No Edge does not attack at all. Flying Thrust still attacks its primary.
            bool attacks = !(SwordRules.Int((Newtonsoft.Json.Linq.JObject)card["rules"], "damage") == 0
                && SwordRules.Int((Newtonsoft.Json.Linq.JObject)card["rules"], "cloudDamage") > 0 && !linked);
            if (attacks) AddRing.Invoke(__instance, new object[] { primary.Value, camera });
            foreach (var target in ids) AddRing.Invoke(__instance, new object[] { target.Value, camera });
            return false;
        }
    }
}
