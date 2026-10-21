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
            if(rank==1)lines.Add("入宗赠送两个炼气包，共8张牌。\n"+RulesFor(SwordRules.Lineage));
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
            if(rank==1)lines.Add("入宗赠送两个炼气包，共8张牌。\n"+RulesFor(sect));
            else if(rank==2)lines.Add("突破筑基从两个筑基包中免费选一，另一包可购买；后续境界不再赠包。");
            lines.Add(SectRegistry.Summary(sect));
            if(sect==FiveElementRules.Lineage)
            {
                lines.Add("纯水试点：商店可逐张购买水牌，原包已拥有则免于重复购买；卡包管理可切换自由选牌。筑基起开战仅启用水牌时，回合末水势伤害×"+FiveCardSelection.WaterMasteryMultiplier+"；改成混池或使用其他五行牌后本场失效，混系仍按原规则。");
                foreach(var entry in FiveCardSelection.Entries.Where(e=>(int)e["rank"]==rank&&(int)e["price"]>0))lines.Add("水系单卡：【"+entry["name"]+"】"+entry["price"]+"奖励点。");
            }
            lines.Add(rank==1?"本境界赠送卡组（每包4张）：":"本境界新增可购卡组（每包4张）：");
            lines.AddRange(packs.Select(p=>"【"+p["name"]+"】 "+(rank==1?"赠送":p["price"]+"奖励点")+"\n"+string.Join("、",((JArray)p["cards"]).Values<string>().Select(k=>(string)SectRegistry.Cards[SectRegistry.CardPrefix+k]["name"]))));
            foreach(var fate in SectRegistry.Fates.Values.Where(f=>SectRegistry.Owner(f)==sect && (int)f["rank"]==rank))
                lines.Add("本境界开放仙命：【"+fate["name"]+"】40奖励点；独立被动，不占手牌。");
            return string.Join("\n",lines);
        }
        public static string SectForCode(string code)=>SectRegistry.Sects.Values.Where(s=>(string)s["code"]==code).Select(s=>(string)s["id"]).FirstOrDefault();
        public static string RulesFor(string sect)=>SectRegistry.Sects.TryGetValue(sect,out var row)?SwordRules.Text(row,"rulesDescription"):"";
        public static string CardSummary(JObject card)=>SwordRules.Text(card,"summary");
        public static string CardDetail(JObject card)=>SwordRules.Text(card,"description");
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
            if(pack==null)
            {
                var entry=FiveCardSelection.Entries.FirstOrDefault(e=>(string)e["key"]==key.StringValue);
                if(entry==null)return true;
                __result=SectDescriptions.CardDetail(SectRegistry.Cards[SectRegistry.CardPrefix+(string)entry["cardKey"]])+"\n单卡解锁；购买后可在自由选牌中选入，不自动改变当前卡池。";return false;
            }
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
            if(SectRegistry.Fates.TryGetValue(abilityId.Value,out var fate)){__result=(string)fate["description"]+"\n独立被动，本局限购一次，不占手牌。";return false;}
            if(SectRegistry.Cards.TryGetValue(abilityId.Value,out var card)){__result=SectDescriptions.CardDetail(card);return false;}
            var pack=SectRegistry.FindPack(abilityId);
            if(pack==null)
            {
                var entry=FiveCardSelection.Find(abilityId);
                if(entry==null)return true;
                __result=SectDescriptions.CardDetail(SectRegistry.Cards[SectRegistry.CardPrefix+(string)entry["cardKey"]])+"\n单卡解锁；购买后可在自由选牌中选入，不自动改变当前卡池。";return false;
            }
            __result=SectDescriptions.PackDetail(pack);return false;
        }
    }
    [HarmonyPatch(typeof(CardTableContentNarrator),"CardSummary")]
    internal static class SectCardSummary
    {
        private static bool Prefix(CardTableCard card,ref string __result)
        {if(card==null||!SectRegistry.Cards.TryGetValue(card.AbilityId.Value,out var row))return true;__result=SectDescriptions.CardSummary(row);return false;}
    }
    [HarmonyPatch(typeof(CardTableContentNarrator),"CardDetail")]
    internal static class SectCardDetail
    {
        private static bool Prefix(CardTableCard card,ref string __result)
        {if(card==null||!SectRegistry.Cards.TryGetValue(card.AbilityId.Value,out var row))return true;__result=SectDescriptions.CardDetail(row);return false;}
    }
}

namespace YixianSect
{
    // Native HUD tab splitting recognizes only BL08 ID prefixes. Canonical mod IDs
    // must use the same card route, including exclusion from custom ability bars.
    [HarmonyPatch(typeof(BattleUIPresentationMapper),"IsCardTableCommand")]
    internal static class SectCardCommand
    {
        private static void Postfix(Reincarnation.UI.ViewModels.ActionCardViewModel action,ref bool __result)
        {
            if(action!=null&&SectRegistry.Cards.ContainsKey(action.CommandActionId))__result=true;
        }
    }

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
            foreach(var e in FiveCardSelection.Entries){all.Add(FiveCardSelection.Id(e).Value);if(sect==FiveElementRules.Lineage)own.Add(FiveCardSelection.Id(e).Value);}
            if(rank==0){__result.Packs=__result.Packs.Where(p=>!all.Contains(p.Id)).ToArray();return;}
            __result.Packs=__result.Packs.Where(p=>!all.Contains(p.Id)||own.Contains(p.Id)).ToArray();
            if(!native)
            {
                __result.Packs=__result.Packs.Where(p=>own.Contains(p.Id))
                    .OrderBy(p=>SwordRules.Int((SectRegistry.FindPack(new StableId(p.Id))??FiveCardSelection.Find(new StableId(p.Id))),"rank"))
                    .ThenBy(p=>SwordRules.Int((SectRegistry.FindPack(new StableId(p.Id))??FiveCardSelection.Find(new StableId(p.Id))),"index")).ToArray();
                __result.Recipes=Array.Empty<Reincarnation.UI.ViewModels.CardTableRecipeViewModel>();
                __result.Summary="手牌 "+__result.Hand.Count+"/10 · 4斗气＝1灵气 · "+SectRegistry.Summary(sect);
            }
            var names=new[]{"炼气","筑基","金丹","元婴","化神"};
            foreach(var pack in __result.Packs.Where(p=>own.Contains(p.Id)&&!FiveCardSelection.Is(new StableId(p.Id))))
            {
                var row=SectRegistry.Packs.Single(p=>SectRegistry.PackId(p).Value==pack.Id);
                int required=SwordRules.Int(row,"rank");
                pack.UnlockLabel=pack.IsLocked?(rank<required?"需要"+names[required-1]:"未购买"):names[required-1]+"卡组";
                pack.CountLine=pack.Cards.Count+" 张 · "+names[required-1];
                pack.Detail=SectDescriptions.PackSummary(row);
                pack.RecipeLine=SwordRules.Text(row,"role");
                foreach(var card in pack.Cards)
                    if(SectRegistry.Cards.TryGetValue(card.CardId,out var c))card.Detail=card.Title+"\n"+SectDescriptions.CardDetail(c);
                pack.CardDetails=SectDescriptions.PackDetail(row);
            }
            FiveDeckPanel.ConfigureModel(player,__result);
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
            if(ability!=null && SectRegistry.Cards.TryGetValue(ability.StableId.Value,out var card)){__result=SectDescriptions.CardSummary(card);return false;}
            var pack=ability==null?null:SectRegistry.FindPack(ability.StableId);
            if(pack==null)return true;
            __result=SectDescriptions.PackSummary(pack);return false;
        }
    }
}
