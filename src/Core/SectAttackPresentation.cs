using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Presentation.Combat;

namespace YixianSect
{
    // Reuse the shipped paced receipt player: each real step retains its ordinal,
    // hit/miss, target and numbers. No timers or synthetic attacks on authority.
    public static class SectAttackPresentation
    {
        public static string Style(JObject card)
        {
            var rules=(JObject)card["rules"];
            bool attacks=new[]{"damage","damageBp","cloudDamage","randomMax","chanceAttack","postAttack"}.Any(k=>(int?)rules[k]>0);
            if(!attacks)
            {
                if(new[]{"heal","healBp","conditionalHeal","randomHealMax","postHeal","healPerOmen"}.Any(k=>(int?)rules[k]>0))return "Heal";
                if(new[]{"shield","shieldBp","cloudShield","shieldPerOmen"}.Any(k=>(int?)rules[k]>0))return "Shield";
                return "Buff";
            }
            if(SectRegistry.Owner(card)==SwordRules.Lineage)return "Blade";
            if(SectRegistry.Owner(card)==SevenStarRules.Lineage)return (int?)card["rules"]?["randomMax"]>0||(int?)card["rules"]?["attackChance"]>0?"Lightning":"Magic";
            return new[]{"Magic","Pierce","Water","Wind","Fire","Blunt"}[(int?)card["element"]??0];
        }
        // Water/Wind/Buff use native recipes on the registered shared carrier. The
        // registered magic carrier accepts StyleOverride in CombatVfxVisual.Begin.
        public static string Key(string style)=>style=="Blade"?"vfx.combat.weapon.blade":style=="Pierce"?"vfx.combat.weapon.spear":style=="Blunt"?"vfx.combat.weapon.unarmed":style=="Water"||style=="Wind"||style=="Buff"?"vfx.combat.magic":"vfx.combat."+style.ToLowerInvariant();
        public static void Bind(CombatFeedbackPresenter presenter)
        {
            var owner=typeof(CombatFeedbackPresenter);var type=owner.GetNestedType("ActionBinding",System.Reflection.BindingFlags.NonPublic);
            var dictionary=(IDictionary)AccessTools.Field(owner,"actionById").GetValue(presenter);
            foreach(var pair in SectRegistry.Cards)
            {
                var binding=Activator.CreateInstance(type,true);string style=Style(pair.Value);
                void Set(string key,object value)=>AccessTools.Field(type,key).SetValue(binding,value);
                Set("actionId",pair.Key);Set("vfxStableKey",Key(style));Set("sfxStableKey",style=="Blade"||style=="Pierce"?"sfx.combat.weapon.sword":"");
                Set("visualStyle",Enum.Parse(AccessTools.Field(type,"visualStyle").FieldType,style));
                Set("lifetime",.24f);Set("scale",.85f);dictionary[pair.Key]=binding;
            }
        }
    }

    internal sealed class SectCastBeat
    {
        internal long Sequence=-1;
        internal readonly HashSet<string> Casts=new HashSet<string>();
        internal bool Repeated;
    }
    [HarmonyPatch(typeof(CombatFeedbackPresenter),"Present")]
    internal static class SectCastSequence
    {
        internal static readonly ConditionalWeakTable<CombatFeedbackPresenter,SectCastBeat> Beats=new ConditionalWeakTable<CombatFeedbackPresenter,SectCastBeat>();
        private static void Prefix(CombatFeedbackPresenter __instance,BattlePresentationResult result)
        {
            var state=Beats.GetValue(__instance,_=>new SectCastBeat());
            if(result.Kind==BattlePresentationResultKind.Rebuild||result.Kind==BattlePresentationResultKind.Cleared){state.Casts.Clear();state.Sequence=-1;state.Repeated=false;return;}
            if(result.Kind!=BattlePresentationResultKind.ActionResolved)return;
            state.Repeated=false;
            if(!SectRegistry.Cards.ContainsKey(result.ContentId.Value)||result.ServerSequence<=0)return;
            if(state.Sequence!=result.ServerSequence){state.Casts.Clear();state.Sequence=result.ServerSequence;}
            state.Repeated=!state.Casts.Add(result.SourceActorRuntimeId.Value+"/"+result.ContentId.Value);
        }
    }
    [HarmonyPatch(typeof(CombatFeedbackPresenter),"PlayAtSource")]
    internal static class SectSingleCast
    {
        private static bool Prefix(CombatFeedbackPresenter __instance,StableId contentId,bool isCast,ref bool __result)
        {
            if(!isCast||!SectRegistry.Cards.ContainsKey(contentId.Value)||!SectCastSequence.Beats.TryGetValue(__instance,out var beat)||!beat.Repeated)return true;
            __result=false;return false;
        }
    }
    [HarmonyPatch(typeof(CombatFeedbackPresenter),"Resolve")]
    internal static class SectVolleyApproach
    {
        private static bool Prefix(BattlePresentationResult action,ref AttackApproachPlan __result)
        {
            if(!SectRegistry.Cards.TryGetValue(action.ContentId.Value,out var card))return true;
            var r=(JObject)card["rules"];
            if(!new[]{"hits","randomRepeats","chanceRepeats"}.Any(k=>(int?)r[k]>1)&&(bool?)r["extraQiHit"]!=true)return true;
            __result=new AttackApproachPlan(AttackApproach.Step);return false;
        }
    }
    [HarmonyPatch(typeof(CombatFeedbackPresenter),"RebuildBindings")]
    internal static class SectAttackBindings
    { private static void Postfix(CombatFeedbackPresenter __instance)=>SectAttackPresentation.Bind(__instance); }
}
