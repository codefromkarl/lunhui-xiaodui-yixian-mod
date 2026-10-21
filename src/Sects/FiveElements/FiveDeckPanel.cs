using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal static class FiveDeckPanel
    {
        internal sealed class Context { internal int Rank;internal IReadOnlyList<CardTablePackViewModel> All; }
        private sealed class View { internal bool Cards;internal int Element=-1;internal Button Toggle;internal GameObject Filters;internal RectTransform Scroll;internal Vector2 OriginalOffset; }
        private static readonly ConditionalWeakTable<CardTableViewModel,Context> Contexts=new ConditionalWeakTable<CardTableViewModel,Context>();
        private static readonly ConditionalWeakTable<CardTablePackPanel,View> Views=new ConditionalWeakTable<CardTablePackPanel,View>();
        private static readonly System.Reflection.PropertyInfo PlaybackWaiting=AccessTools.Property(typeof(CardTablePackPanel).Assembly.GetTypes().Single(t=>t.Name=="BattlePlaybackAvailability"),"IsWaiting");
        internal static T Read<T>(object o,string f)=>(T)AccessTools.Field(o.GetType(),f).GetValue(o);
        internal static bool CardsMode(CardTablePackPanel p)=>Views.TryGetValue(p,out var v)&&v.Cards;
        internal static void ConfigureModel(RunPlayerSnapshot player,CardTableViewModel model)
        {
            var lineage=player.Lineages.FirstOrDefault(l=>l.LineageId.Value==FiveElementRules.Lineage);
            if(lineage.Rank<=0||model==null)return;
            Contexts.Remove(model);Contexts.Add(model,new Context{Rank=lineage.Rank,All=model.Packs});
            foreach(var entry in model.Packs.Where(p=>FiveCardSelection.Is(new StableId(p.Id))))
            {
                var e=FiveCardSelection.Find(new StableId(entry.Id));var c=SectRegistry.Cards[SectRegistry.CardPrefix+(string)e["cardKey"]];
                entry.Title=(string)c["name"];entry.CountLine=new[]{"中性","金","水","木","火","土"}[(int?)c["element"]??0]+" · "+new[]{"炼气","筑基","金丹","元婴","化神"}[(int)e["rank"]-1];
                entry.Detail=(string)c["summary"];entry.CardDetails=(string)c["description"];
                entry.UnlockLabel=entry.IsLocked?(lineage.Rank<(int)e["rank"]?"境界未到":(int?)c["element"]==2?"水系单卡 · "+e["price"]+"点（商店购买）":"需拥有原卡包"):"已拥有 · 可自由选入";
            }
        }
        private static void Render(CardTablePackPanel p)=>AccessTools.Method(typeof(CardTablePackPanel),"Render").Invoke(p,null);
        private static void Page(CardTablePackPanel p)=>AccessTools.Method(typeof(CardTablePackPanel),"SelectPage").Invoke(p,new object[]{false});
        private static void SetMode(CardTablePackPanel p,View v,bool cards)
        {
            var model=Read<CardTableViewModel>(p,"model");var selected=Read<HashSet<string>>(p,"selected");
            if(cards)
            {
                var keys=selected.SelectMany(id=>SectRegistry.FindPack(new StableId(id))?["cards"]?.Values<string>()??Enumerable.Empty<string>()).ToHashSet();
                selected.Clear();foreach(var e in FiveCardSelection.Entries.Where(e=>keys.Contains((string)e["cardKey"])))selected.Add(FiveCardSelection.Id(e).Value);
            }
            else
            {
                selected.Clear();var packs=model.Packs.Where(x=>!FiveCardSelection.Is(new StableId(x.Id))&&!x.IsLocked).ToArray();
                foreach(var e in packs.Where(x=>x.IsActive))selected.Add(e.Id);
                if(selected.Count==0)foreach(var e in packs.Take(Math.Min(3,packs.Length)))selected.Add(e.Id);
            }
            v.Cards=cards;Page(p);
        }
        internal static void Before(CardTablePackPanel p,out IReadOnlyList<CardTablePackViewModel> saved)
        {
            saved=null;var model=Read<CardTableViewModel>(p,"model");
            if(model==null||!Contexts.TryGetValue(model,out var context))
            {
                if(Views.TryGetValue(p,out var stale)){stale.Toggle.gameObject.SetActive(false);stale.Filters.SetActive(false);stale.Scroll.offsetMax=stale.OriginalOffset;}
                return;
            }
            if(!Views.TryGetValue(p,out var v))
            {
                v=new View{Cards=model.Packs.Any(x=>x.IsActive&&FiveCardSelection.Is(new StableId(x.Id)))};
                Views.Add(p,v);Create(p,v);
            }
            bool fate=Read<bool>(p,"fate");v.Toggle.gameObject.SetActive(!fate);v.Filters.SetActive(!fate&&v.Cards&&!Read<bool>(p,"showingFusion"));
            v.Scroll.offsetMax=v.OriginalOffset+(v.Filters.activeSelf?new Vector2(0,-48):Vector2.zero);
            saved=model.Packs;
            model.Packs=saved.Where(x=>fate||FiveCardSelection.Is(new StableId(x.Id))==v.Cards).Where(x=>fate||!v.Cards||v.Element<0||
                (int?)SectRegistry.Cards[SectRegistry.CardPrefix+(string)FiveCardSelection.Find(new StableId(x.Id))["cardKey"]]["element"]==v.Element).ToArray();
        }
        internal static void After(CardTablePackPanel p,IReadOnlyList<CardTablePackViewModel> saved)
        {
            var model=Read<CardTableViewModel>(p,"model");if(saved!=null)model.Packs=saved;
            if(model==null||!Views.TryGetValue(p,out var v)||!Contexts.TryGetValue(model,out var context))return;
            v.Toggle.GetComponentInChildren<TMP_Text>(true).text=v.Cards?"返回整包":"自由选牌";
            v.Toggle.interactable=model.CanManage&&!Read<bool>(p,"waiting");
            foreach(var button in v.Filters.GetComponentsInChildren<Button>(true))button.interactable=v.Toggle.interactable;
            if(!v.Cards||Read<bool>(p,"fate")||Read<bool>(p,"showingFusion"))return;
            var selected=Read<HashSet<string>>(p,"selected");
            Read<TMP_Text>(p,"title").text="五行自由选牌";
            Read<TMP_Text>(p,"summary").text="已选 "+selected.Count+" 张 · 本境界 "+FiveCardSelection.Minimum(context.Rank)+"–"+FiveCardSelection.Maximum+" 张\n可混选已购牌；筑基起开战纯水的水势结算×"+FiveCardSelection.WaterMasteryMultiplier+"。保存保留当前手牌。";
            Read<TMP_Text>(p,"packsTabLabel").text="逐卡选择";
        }
        internal static bool? CanConfirm(CardTablePackPanel p)
        {
            var m=Read<CardTableViewModel>(p,"model");if(m==null||!Views.TryGetValue(p,out var v)||!v.Cards||Read<bool>(p,"fate"))return null;
            if(!Contexts.TryGetValue(m,out var c))return false;
            var selected=Read<HashSet<string>>(p,"selected");
            return m.CanManage&&!Read<bool>(p,"waiting")&&!Read<bool>(p,"showingFusion")&&!(bool)PlaybackWaiting.GetValue(null)&&
                selected.Count>=FiveCardSelection.Minimum(c.Rank)&&selected.Count<=FiveCardSelection.Maximum&&
                selected.All(id=>FiveCardSelection.Is(new StableId(id))&&c.All.Any(x=>x.Id==id&&!x.IsLocked));
        }
        private static void Create(CardTablePackPanel p,View v)
        {
            var packs=Read<Button>(p,"packsTab");var fusion=Read<Button>(p,"fusionTab");
            var toggle=UnityEngine.Object.Instantiate(packs,packs.transform.parent,false);toggle.name="FiveFreeCards";toggle.onClick=new Button.ButtonClickedEvent();
            v.Toggle=toggle;toggle.onClick.AddListener(()=>SetMode(p,v,!v.Cards));
            var a=(RectTransform)packs.transform;var b=(RectTransform)fusion.transform;
            float left=a.localPosition.x-a.rect.width*.5f,right=b.localPosition.x+b.rect.width*.5f,width=(right-left)/3;
            var tabs=new[]{packs,toggle,fusion};
            for(int i=0;i<3;i++){var t=(RectTransform)tabs[i].transform;t.anchorMin=new Vector2(.5f,t.anchorMin.y);t.anchorMax=new Vector2(.5f,t.anchorMax.y);t.pivot=new Vector2(.5f,t.pivot.y);t.sizeDelta=new Vector2(width-6,t.sizeDelta.y);t.anchoredPosition=new Vector2(left+width*(i+.5f),t.anchoredPosition.y);}
            // Returning to recommendations returns to whole-pack staging first.
            fusion.onClick.AddListener(()=>{if(v.Cards){SetMode(p,v,false);AccessTools.Method(typeof(CardTablePackPanel),"SelectPage").Invoke(p,new object[]{true});}});
            v.Scroll=Read<RectTransform>(p,"content").GetComponentInParent<ScrollRect>().GetComponent<RectTransform>();v.OriginalOffset=v.Scroll.offsetMax;
            var bar=new GameObject("FiveElementFilters",typeof(RectTransform));v.Filters=bar;var rect=(RectTransform)bar.transform;rect.SetParent(v.Scroll,false);rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(0,42);rect.anchoredPosition=new Vector2(0,4);
            var labels=new[]{"全","金","水","木","火","土","中","纯水推荐"};
            for(int i=0;i<labels.Length;i++)
            {
                int filter=i==0?-1:i==6?0:i;var button=UnityEngine.Object.Instantiate(packs,rect,false);button.name=i==7?"FiveWaterPreset":"FiveFilter"+filter;button.onClick=new Button.ButtonClickedEvent();
                var t=(RectTransform)button.transform;t.anchorMin=new Vector2(i<7?i*.09f:.63f,0);t.anchorMax=new Vector2(i<7?(i+1)*.09f:1,1);t.offsetMin=new Vector2(1,0);t.offsetMax=new Vector2(-1,0);button.GetComponentInChildren<TMP_Text>(true).text=labels[i];button.GetComponentInChildren<TMP_Text>(true).fontSize=16;
                if(i<7)button.onClick.AddListener(()=>{v.Element=filter;Render(p);});
                else button.onClick.AddListener(()=>{var m=Read<CardTableViewModel>(p,"model");var selected=Read<HashSet<string>>(p,"selected");selected.Clear();foreach(var e in m.Packs.Where(x=>!x.IsLocked&&FiveCardSelection.Is(new StableId(x.Id))&&
                    (int?)SectRegistry.Cards[SectRegistry.CardPrefix+(string)FiveCardSelection.Find(new StableId(x.Id))["cardKey"]]["element"]==2))selected.Add(e.Id);v.Element=2;Render(p);});
            }
        }
    }
    [HarmonyPatch(typeof(CardTablePackPanel),"Render")]
    internal static class FiveDeckRender
    {
        [HarmonyPriority(Priority.First)]private static void Prefix(CardTablePackPanel __instance,out IReadOnlyList<CardTablePackViewModel> __state)=>FiveDeckPanel.Before(__instance,out __state);
        [HarmonyPriority(Priority.Last)]private static void Postfix(CardTablePackPanel __instance,IReadOnlyList<CardTablePackViewModel> __state)=>FiveDeckPanel.After(__instance,__state);
    }
    [HarmonyPatch(typeof(CardTablePackPanel),"CanConfirm")]
    internal static class FiveDeckConfirm
    {
        private static void Postfix(CardTablePackPanel __instance,ref bool __result){var value=FiveDeckPanel.CanConfirm(__instance);if(value.HasValue)__result=value.Value;}
    }
}
