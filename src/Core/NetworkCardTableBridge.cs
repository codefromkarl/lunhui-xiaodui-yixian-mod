using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.Network;

namespace YixianSect
{
    // The stock wire validator only accepts cp~ pack selections. Admit exactly
    // our eight-card shape; ownership, phase, revision and sequence still belong
    // to the native admission/authority path and FiveDeckBattleSelection.
    [HarmonyPatch(typeof(NetworkRunCommandPolicy),"IsValidBattleActionPayload")]
    internal static class FiveDeckNetworkPayload
    {
        private static bool Prefix(StableId actionId,StableId targetRuntimeId,StableId parameterId,int actionKindValue,bool booleanValue,int targetRowValue,int inventorySlot,ref bool __result)
        {
            if(actionId!=CardTableRules.SetPacksActionId||!parameterId.Value.StartsWith(FiveDeckRules.SelectionPrefix,System.StringComparison.Ordinal))return true;
            __result=actionKindValue==(int)CombatActionKind.UseAbility&&!booleanValue&&targetRuntimeId.IsEmpty&&targetRowValue==(int)FormationRow.None&&inventorySlot==-1&&FiveDeckRules.TrySelection(parameterId,out var _);
            return false;
        }
    }
}
