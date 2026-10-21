using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Reincarnation.Domain;

namespace YixianSect
{
    // One compact selection travels through the existing pack-management gateway.
    // Ordinary/native pack IDs still use the stock encoder and command unchanged.
    [HarmonyPatch(typeof(CardTableRules),"EncodePacks")]
    internal static class FiveDeckEncodeSelection
    {
        private static bool Prefix(IReadOnlyList<StableId> packIds,ref StableId __result)
        {
            if(packIds==null||packIds.Count!=1||!FiveDeckRules.TrySelection(packIds[0],out var _))return true;
            __result=packIds[0];return false;
        }
    }

    [HarmonyPatch(typeof(BattleAuthorityService),"SubmitCardPackSelection")]
    internal static class FiveDeckBattleSelection
    {
        private static bool Prefix(BattleAuthorityService __instance,object __0,CombatActionIntent __1,ref RuleResult<BattleActionResultReceipt> __result)
        {
            if(!__1.ParameterId.Value.StartsWith(FiveDeckRules.SelectionPrefix,System.StringComparison.Ordinal))return true;
            var player=(PlayerRunState)AccessTools.Property(__0.GetType(),"RunPlayer").GetValue(__0);
            var battle=__instance.Battle;
            if(__instance.HasPendingChoice(player.Slot))
            {__result=RuleResult<BattleActionResultReceipt>.Failure(SessionErrorCode.InvalidPhase,battle.Revision);return false;}
            var tables=(IDictionary)AccessTools.Field(typeof(BattleAuthorityService),"cardTables").GetValue(__instance);
            if(SectRegistry.Rank(player,FiveElementRules.Lineage)==0||!tables.Contains(player.Slot))
            {__result=RuleResult<BattleActionResultReceipt>.Failure(SessionErrorCode.ItemUnavailable,battle.Revision);return false;}
            var sequence=AccessTools.Property(__0.GetType(),"LastAcceptedClientSequence");
            var action=new CombatActionSpec(CardTableRules.SetPacksActionId,CombatActionKind.UseAbility,0,0,0,CombatTargetKind.Self);
            var validation=CombatActionValidator.Validate(new CombatActionValidationContext(battle,__1,action,null,(long)sequence.GetValue(__0)));
            if(!validation.IsSuccess)
            {__result=RuleResult<BattleActionResultReceipt>.Failure(validation.ErrorCode,battle.Revision);return false;}
            if(!FiveDeckRules.TrySelection(__1.ParameterId,out var cards)||!FiveDeckRules.ValidSelection(player,cards)||(battle.Revision==long.MaxValue&&!System.Linq.Enumerable.SequenceEqual(FiveDeckRules.Cards(player),cards)))
            {__result=RuleResult<BattleActionResultReceipt>.Failure(SessionErrorCode.InvalidRequest,battle.Revision);return false;}
            long before=player.Revision;var result=FiveDeckRules.SetDeck(player,cards);
            if(!result.IsSuccess)
            {__result=RuleResult<BattleActionResultReceipt>.Failure(result.ErrorCode,battle.Revision);return false;}
            if(before!=player.Revision)AccessTools.Method(typeof(BattleState),"CommitChoiceChange").Invoke(battle,null);
            sequence.SetValue(__0,__1.ClientSequence);
            __result=RuleResult<BattleActionResultReceipt>.Success(null,battle.Revision);
            return false;
        }
    }
}
