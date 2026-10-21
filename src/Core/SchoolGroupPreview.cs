using System.Linq;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;

namespace YixianSect
{
    [HarmonyPatch(typeof(GameHUDWindow), "CollectAffectedTargets")]
    internal static class SchoolGroupPreview
    {
        private static readonly System.Reflection.FieldInfo Hand = AccessTools.Field(typeof(GameHUDWindow), "cardTable");
        private static readonly System.Reflection.FieldInfo Snapshot = AccessTools.Field(typeof(GameHUDWindow), "battlefieldSnapshot");
        private static readonly System.Reflection.MethodInfo Camera = AccessTools.Method(typeof(GameHUDWindow), "ResolveBattleCamera");
        private static readonly System.Reflection.MethodInfo AddRing = AccessTools.Method(typeof(GameHUDWindow), "AddRingPoint");
        private static bool Prefix(GameHUDWindow __instance, ActionCardViewModel action, CombatantCardViewModel hovered)
        {
            var hand = (CardTableViewModel)Hand.GetValue(__instance);
            string id = hand?.Hand.FirstOrDefault(c => c.Action.StableId == action?.StableId)?.CardId ?? action?.CommandActionId;
            if (id == null || !SectRegistry.Cards.TryGetValue(id, out var card) || card["groupAttack"] == null || hovered == null) return true;
            var snapshot = (RunAuthoritySnapshot)Snapshot.GetValue(__instance);
            var battle = snapshot?.Battle;
            var actor = battle?.Combatants.FirstOrDefault(u => u.Kind == CombatantKind.Player && u.OwnerSlot == snapshot.ViewerSlot && !u.IsDowned);
            if (actor == null) return true;
            var primary = new StableId(hovered.RuntimeId);
            var resolved = action.AttackDamagePreviews.FirstOrDefault(p => p.TargetId == primary)?.ResolvedTargetId ?? primary;
            if (battle.Combatants.Any(u => u.RuntimeId == resolved && u.Side != actor.Side && !u.IsDowned)) primary = resolved;
            var targets = SchoolGroupAttack.Targets(SwordTargeting.For(battle), actor.RuntimeId, primary, card,
                SchoolGroupAttack.PreviewEnabled(card, actor));
            var camera = Camera.Invoke(__instance, null);
            foreach (var target in targets) AddRing.Invoke(__instance, new object[] { target.Value, camera });
            return false;
        }
    }
}
