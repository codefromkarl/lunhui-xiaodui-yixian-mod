using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Content;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public static class SectDescriptions
    {
        public static string Rank(int rank)
        {
            var lines=new List<string>();
            if(rank==1)lines.Add("入宗赠送两个炼气包，共 8 张宗门牌。\n仅首回合起手 3 张；主动摸牌耗 1 行动力、不耗斗气；出牌 0 行动力，仅按单卡灵气费用消耗斗气，无额外基础费。\n连云看自己上一张打出的卡；剑意整张消耗、逐段生效；再次行动每次额外摸 1 张，手牌最多 10 张。");
            if(rank==2)lines.Add("突破筑基时，从两个筑基卡组中免费选一个；另一包可购买。此后突破不再赠包。");
            lines.Add(rank==1?"本境界自动获得的卡组（每包 4 张）：":"本境界新增可购卡组（每包 4 张；低阶未拥有包持续出售）：");
            foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==SwordRules.Lineage && SwordRules.Int(p,"rank")==rank))
            {
                var names=((JArray)pack["cards"]).Values<string>().Select(k=>SwordRules.Text(SectRegistry.Cards[SectRegistry.CardPrefix+k],"name"));
                lines.Add("【"+pack["name"]+"】 "+(rank==1?"入宗赠送":pack["price"]+" 奖励点")+"\n"+string.Join("、",names));
            }
            return string.Join("\n",lines);
        }
        public static string RankFor(string sect,int rank)
        {
            if(sect==SwordRules.Lineage)return Rank(rank);
            var packs=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==sect && (int)p["rank"]==rank);
            var lines=new List<string>();
            if(rank==1)lines.Add("入宗赠送两个炼气包，共8张牌；仅首回合起手3张，主动摸牌1行动力。出牌0行动力，单卡灵气费用×4斗气，无基础费。");
            else if(rank==2)lines.Add("突破筑基从两个筑基包中免费选一，另一包可购买；后续境界不再赠包。");
            lines.Add(SectRegistry.Summary(sect));
            lines.Add(rank==1?"本境界赠送卡组（每包4张）：":"本境界新增可购卡组（每包4张）：");
            lines.AddRange(packs.Select(p=>"【"+p["name"]+"】 "+(rank==1?"赠送":p["price"]+"奖励点")+"\n"+string.Join("、",((JArray)p["cards"]).Values<string>().Select(k=>(string)SectRegistry.Cards[SectRegistry.CardPrefix+k]["name"]))));
            foreach(var fate in SectRegistry.Fates.Values.Where(f=>SectRegistry.Owner(f)==sect && (int)f["rank"]==rank))
                lines.Add("本境界开放仙命：【"+fate["name"]+"】40奖励点；独立被动，不占手牌。");
            return string.Join("\n",lines);
        }
        public static string SectForCode(string code)=>SectRegistry.Sects.Values.Where(s=>(string)s["code"]==code).Select(s=>(string)s["id"]).FirstOrDefault();
        public static string PackDetail(JObject pack) => SwordRules.Text(pack,"description");
        public static string PackSummary(JObject pack) => "4 张 · "+SwordRules.Text(pack,"role");
    }

    [HarmonyPatch(typeof(ProgressionPassiveNarrator),"Describe",new[]{typeof(RuntimeContentOperation)})]
    internal static class SectPassiveDescription
    {
        private static bool Prefix(RuntimeContentOperation __0,ref string __result)
        {
            if(__0==null||!__0.TryGetValue("lineageCode",out var code)||SectDescriptions.SectForCode(code.StringValue)==null||!__0.TryGetValue("cardTablePackKey",out var key))return true;
            var pack=SectRegistry.Packs.SingleOrDefault(p=>SwordRules.Text(p,"key")==key.StringValue);
            if(pack==null)return true;
            __result=SectDescriptions.PackDetail(pack);return false;
        }
        private static void Postfix(RuntimeContentOperation __0,ref string __result)
        {
            if(__0==null || !__0.TryGetValue("lineageCode",out var code) || SectDescriptions.SectForCode(code.StringValue)==null || __0.TryGetValue("cardTablePackKey",out var _))return;
            if(__0.TryGetValue("step",out var step))__result=SectDescriptions.RankFor(SectDescriptions.SectForCode(code.StringValue),step.IntValue+1)+"\n"+__result;
        }
    }
    [HarmonyPatch(typeof(ProgressionPassiveNarrator),"DescribeUpgradeDetails")]
    internal static class SectUpgradeDescription
    {
        private static void Postfix(RuntimeContentOperation operation,ref string __result)
        {
            if(operation!=null && operation.TryGetValue("lineageCode",out var code) && SectDescriptions.SectForCode(code.StringValue)!=null && operation.TryGetValue("step",out var step))
                __result=SectDescriptions.RankFor(SectDescriptions.SectForCode(code.StringValue),step.IntValue+1)+"\n"+__result;
        }
    }
    [HarmonyPatch(typeof(ProgressionPassiveNarrator),"DescribeLadderSummary")]
    internal static class SectLadderSummary
    {
        private static void Postfix(RuntimeContentOperation operation,ref string __result)
        {
            if(operation==null || !operation.TryGetValue("lineageCode",out var code) || SectDescriptions.SectForCode(code.StringValue)==null || !operation.TryGetValue("step",out var step))return;
            __result+="\n卡组："+string.Join("、",SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==SectDescriptions.SectForCode(code.StringValue) && SwordRules.Int(p,"rank")==step.IntValue+1).Select(p=>SwordRules.Text(p,"name")));
        }
    }
    [HarmonyPatch(typeof(RunUIPresentationMapper),"DescribeAbilityOperation")]
    internal static class SectAbilityDescription
    {
        private static bool Prefix(StableId abilityId,ref string __result)
        {
            if(SectRegistry.Fates.TryGetValue(abilityId.Value,out var fate)){__result=(string)fate["description"]+"\n本局购买一次，不占手牌；境界成长按能力说明生效。";return false;}
            if(SectRegistry.Cards.TryGetValue(abilityId.Value,out var card)){__result=SwordRules.Text(card,"description");return false;}
            var pack=SectRegistry.FindPack(abilityId);
            if(pack==null)return true;
            __result=SectDescriptions.PackDetail(pack);return false;
        }
    }
    [HarmonyPatch(typeof(CardTableContentNarrator),"CardSummary")]
    internal static class SectCardSummary
    {
        private static bool Prefix(CardTableCard card,ref string __result)
        {if(card==null||!SectRegistry.Cards.TryGetValue(card.AbilityId.Value,out var row))return true;__result=SingleLine(SwordRules.Text(row,"description"));return false;}
        private static string SingleLine(string text)=>text.Split('\n')[0];
    }
    [HarmonyPatch(typeof(CardTableContentNarrator),"CardDetail")]
    internal static class SectCardDetail
    {
        private static bool Prefix(CardTableCard card,ref string __result)
        {if(card==null||!SectRegistry.Cards.TryGetValue(card.AbilityId.Value,out var row))return true;__result=SwordRules.Text(row,"description");return false;}
    }
}

namespace YixianSect
{
    [HarmonyPatch(typeof(BattleUIPresentationMapper),"BuildCardTable")]
    internal static class SectCardTablePresentation
    {
        private static void Postfix(RunPlayerSnapshot player,ref Reincarnation.UI.ViewModels.CardTableViewModel __result)
        {
            if(player==null||__result==null)return;
            var held=player.Lineages.Where(l=>SectRegistry.IsSect(l.LineageId.Value)).ToList();
            int rank=held.Select(l=>l.Rank).DefaultIfEmpty().Max();
            string sect=held.Select(l=>l.LineageId.Value).FirstOrDefault();
            bool native=player.Lineages.Any(l=>l.LineageId==CardTableRules.LineageId);
            var all=new HashSet<string>(SectRegistry.Packs.Select(p=>SectRegistry.PackId(p).Value));
            var own=new HashSet<string>(SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==sect).Select(p=>SectRegistry.PackId(p).Value));
            if(rank==0){__result.Packs=__result.Packs.Where(p=>!all.Contains(p.Id)).ToArray();return;}
            __result.Packs=__result.Packs.Where(p=>!all.Contains(p.Id)||own.Contains(p.Id)).ToArray();
            if(!native)
            {
                __result.Packs=__result.Packs.Where(p=>own.Contains(p.Id))
                    .OrderBy(p=>SwordRules.Int(SectRegistry.FindPack(new StableId(p.Id)),"rank"))
                    .ThenBy(p=>SwordRules.Int(SectRegistry.FindPack(new StableId(p.Id)),"index")).ToArray();
                __result.Recipes=Array.Empty<Reincarnation.UI.ViewModels.CardTableRecipeViewModel>();
                __result.Summary="手牌 "+__result.Hand.Count+"/10 · 4斗气＝1灵气 · "+SectRegistry.Summary(sect);
            }
            var names=new[]{"炼气","筑基","金丹","元婴","化神"};
            foreach(var pack in __result.Packs.Where(p=>own.Contains(p.Id)))
            {
                var row=SectRegistry.Packs.Single(p=>SectRegistry.PackId(p).Value==pack.Id);
                int required=SwordRules.Int(row,"rank");
                pack.UnlockLabel=pack.IsLocked?(rank<required?"需要"+names[required-1]:"未购买"):names[required-1]+"卡组";
                pack.CountLine=pack.Cards.Count+" 张 · "+names[required-1];
                pack.Detail=SectDescriptions.PackSummary(row);
                pack.RecipeLine=SwordRules.Text(row,"role");
                foreach(var card in pack.Cards)
                    if(SectRegistry.Cards.TryGetValue(card.CardId,out var c))card.Detail=card.Title+"\n"+SwordRules.Text(c,"description");
                pack.CardDetails=SectDescriptions.PackDetail(row);
            }
        }
    }
}

namespace YixianSect
{
    // Card-table panels use a separate built-in art set, not the ability icon resolver.
    // Route only this package's keys through the game's cached mod-image loader.
    [HarmonyPatch(typeof(Reincarnation.UI.Nodes.CardTableArtSet),"Find",new[]{typeof(string)})]
    internal static class SectCardArt
    {
        private static void Postfix(string key,ref UnityEngine.Sprite __result)
        {
            if(SectRegistry.Cards.ContainsKey(SectRegistry.CardPrefix+key))
                __result=Reincarnation.UI.UIContentIconLibrary.GetOrRequest("icon."+SectRegistry.CardPrefix+key,null);
        }
    }
    [HarmonyPatch(typeof(Reincarnation.UI.Nodes.CardTableArtSet),"FindPack")]
    internal static class SectPackArt
    {
        private static void Postfix(string key,ref UnityEngine.Sprite __result)
        {
            var pack=SectRegistry.Packs.SingleOrDefault(p=>SwordRules.Text(p,"key")==key);
            if(pack!=null)__result=Reincarnation.UI.UIContentIconLibrary.GetOrRequest("icon."+SectRegistry.PackId(pack).Value,null);
        }
    }
}

namespace YixianSect
{
    // Full details are for the scrolling detail area, never the narrow shop list subtitle.
    [HarmonyPatch(typeof(RunUIPresentationMapper),"BuildAbilityFunctionalOneLiner")]
    internal static class SectPackOneLiner
    {
        private static bool Prefix(RuntimeAbilityDefinition ability,ref string __result)
        {
            if(ability!=null && SectRegistry.Fates.TryGetValue(ability.StableId.Value,out var fate)){__result=((string)fate["description"]).Split('\n')[0];return false;}
            var pack=ability==null?null:SectRegistry.FindPack(ability.StableId);
            if(pack==null)return true;
            __result=SectDescriptions.PackSummary(pack);return false;
        }
    }
}
