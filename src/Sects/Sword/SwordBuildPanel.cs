using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal sealed class SwordBuildContext { public int Rank; public string School; }

    [HarmonyPatch(typeof(BattleUIPresentationMapper),"BuildCardTable")]
    internal static class SwordBuildPresentation
    {
        internal static readonly ConditionalWeakTable<CardTableViewModel,SwordBuildContext> Contexts=new ConditionalWeakTable<CardTableViewModel,SwordBuildContext>();
        private static void Postfix(RunPlayerSnapshot player,CardTableViewModel __result)
        {
            if(__result==null||player==null||player.Lineages.Any(l=>l.LineageId==CardTableCatalog.LineageId))return;
            var schools=player.Lineages.Where(l=>l.Rank>0&&l.LineageId.Value!=FiveElementRules.Lineage&&SwordBuilds.RoutesFor(l.LineageId.Value).Count>0).ToArray();
            if(schools.Length==0)return;
            var school=schools[0];
            Contexts.Remove(__result);Contexts.Add(__result,new SwordBuildContext{Rank=school.Rank,School=school.LineageId.Value});
            foreach(var card in __result.Hand)
            {
                if(card.Action==null||!SwordRules.Cards.TryGetValue(card.CardId,out var rule))continue;
                card.Detail="本次费用："+card.Action.CostSummary+"\n"+card.Detail;
                if(SwordRules.Text((Newtonsoft.Json.Linq.JObject)rule["rules"],"onHit")=="thunder")
                    card.Detail+="\n击伤追加单独结算，不计入主攻击伤害预告。";
            }
        }
    }

    [HarmonyPatch(typeof(CardTablePackPanel),"Render")]
    internal static class SwordBuildPanel
    {
        private sealed class View { public GameObject Root; public TMP_Text[] Text; public Button[] Buttons; }
        private static readonly ConditionalWeakTable<CardTablePackPanel,View> Views=new ConditionalWeakTable<CardTablePackPanel,View>();
        private static T Read<T>(object owner,string field)=>(T)AccessTools.Field(owner.GetType(),field).GetValue(owner);
        private static readonly Color Gold=new Color(1f,.94f,.80f),Muted=new Color(.70f,.68f,.63f);
        private static void Postfix(CardTablePackPanel __instance)
        {
            var model=Read<CardTableViewModel>(__instance,"model");
            if(model==null||Read<bool>(__instance,"fate")||!SwordBuildPresentation.Contexts.TryGetValue(model,out var context))
            {
                if(Views.TryGetValue(__instance,out var previous))previous.Root.SetActive(false);
                return;
            }
            Read<TMP_Text>(__instance,"fusionTabLabel").text="推荐构筑";
            var selected=Read<HashSet<string>>(__instance,"selected");
            var stats=SwordBuilds.PoolSummary(context.School,selected.Select(id=>new StableId(id)));
            var summary=Read<TMP_Text>(__instance,"summary");
            int line=summary.text.IndexOf('\n');
            if(line>=0)summary.text="已选 "+selected.Count+" 包（"+Math.Min(3,model.Packs.Count(p=>!p.IsLocked))+"–8） · "+stats+"\n"+summary.text.Substring(line+1);
            bool showing=Read<bool>(__instance,"showingFusion");
            if(!Views.TryGetValue(__instance,out var view))
            {
                if(!showing)return;
                view=Create(__instance,context.School);Views.Add(__instance,view);
            }
            view.Root.SetActive(showing);
            if(!showing)return;
            Read<CardTableFusionBrowser>(__instance,"fusionBrowser").gameObject.SetActive(false);
            var owned=model.Packs.Where(p=>!p.IsLocked).Select(p=>new StableId(p.Id)).ToArray();
            var routes=SwordBuilds.RoutesFor(context.School);
            for(int i=0;i<routes.Count;i++)
            {
                var proposal=SwordBuilds.Recommend(routes[i],context.Rank,owned);
                var pool=SwordBuilds.PoolSummary(context.School,proposal.Selected);
                string missing=proposal.Missing.Count==0?"本境界核心包已齐备":
                    "待购核心：\n"+string.Join("\n",proposal.Missing.Select(p=>p["name"]+"（"+p["price"]+"点）"));
                view.Text[i].text="<size=26><color=#FFF0CC>"+proposal.Route.Name+"</color></size>\n"+
                    "<color=#B3ADA1>"+(proposal.IsTransition?"当前为过渡配置":"当前境界配置")+"</color>\n\n"+
                    proposal.Route.Play+"\n\n<size=20><color=#FFF0CC>已购包推荐</color></size>\n"+SwordBuilds.Names(proposal.Selected)+
                    "\n\n"+pool+"\n"+SwordBuilds.Diagnostics(proposal,model.Packs.Where(p=>p.IsActive).Select(p=>new StableId(p.Id)))+"\n\n<color=#B3ADA1>"+missing+(proposal.Missing.Count==0?"\n\n"+proposal.Route.Milestone:"")+"</color>";
                var viewport=view.Text[i].transform.parent as RectTransform;
                float height=Math.Max(viewport.rect.height,view.Text[i].GetPreferredValues(view.Text[i].text,Math.Max(100,viewport.rect.width),0).y+8);
                view.Text[i].rectTransform.sizeDelta=new Vector2(0,height);
                var button=view.Buttons[i];button.onClick=new Button.ButtonClickedEvent();
                button.interactable=model.CanManage&&!Read<bool>(__instance,"waiting")&&proposal.Selected.Count>=Math.Min(3,owned.Length);
                button.onClick.AddListener(()=>{
                    selected.Clear();foreach(var id in proposal.Selected)selected.Add(id.Value);
                    AccessTools.Method(typeof(CardTablePackPanel),"SelectPage").Invoke(__instance,new object[]{false});
                });
            }
        }
        private static View Create(CardTablePackPanel panel,string school)
        {
            var browser=Read<CardTableFusionBrowser>(panel,"fusionBrowser");var source=(RectTransform)browser.transform;
            var root=new GameObject("SwordRecommendedBuilds",typeof(RectTransform));var rect=(RectTransform)root.transform;
            rect.SetParent(source.parent,false);rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;rect.anchoredPosition=source.anchoredPosition;rect.sizeDelta=source.sizeDelta;
            var view=new View{Root=root,Text=new TMP_Text[3],Buttons=new Button[3]};
            var template=Read<TMP_Text>(panel,"detail");
            for(int i=0;i<3;i++)
            {
                var card=new GameObject("Route"+i,typeof(RectTransform),typeof(Image));var c=(RectTransform)card.transform;c.SetParent(rect,false);
                c.anchorMin=new Vector2(i/3f,0.07f);c.anchorMax=new Vector2((i+1)/3f,.88f);c.offsetMin=new Vector2(i==0?0:10,0);c.offsetMax=new Vector2(i==2?0:-10,0);
                card.GetComponent<Image>().color=new Color(.12f,.13f,.15f,1f);
                var viewport=new GameObject("RouteViewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));var v=(RectTransform)viewport.transform;v.SetParent(c,false);
                v.anchorMin=Vector2.zero;v.anchorMax=Vector2.one;v.offsetMin=new Vector2(18,72);v.offsetMax=new Vector2(-18,-18);viewport.GetComponent<Image>().color=Color.clear;
                var text=UnityEngine.Object.Instantiate(template,v,false);text.name="RouteDetails";text.fontSize=20;text.enableAutoSizing=false;text.alignment=TextAlignmentOptions.TopLeft;text.color=new Color(.84f,.82f,.77f);text.overflowMode=TextOverflowModes.Overflow;text.raycastTarget=false;
                text.rectTransform.anchorMin=new Vector2(0,1);text.rectTransform.anchorMax=Vector2.one;text.rectTransform.pivot=new Vector2(.5f,1);text.rectTransform.anchoredPosition=Vector2.zero;text.rectTransform.sizeDelta=Vector2.zero;view.Text[i]=text;
                var scroll=viewport.GetComponent<ScrollRect>();scroll.viewport=v;scroll.content=text.rectTransform;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
                var button=UnityEngine.Object.Instantiate(Read<Button>(panel,"confirm"),c,false);button.name="Use"+SwordBuilds.RoutesFor(school)[i].Key;button.gameObject.SetActive(true);
                var b=(RectTransform)button.transform;b.anchorMin=new Vector2(0,0);b.anchorMax=new Vector2(1,0);b.pivot=new Vector2(.5f,0);b.offsetMin=new Vector2(18,16);b.offsetMax=new Vector2(-18,64);
                var label=button.GetComponentInChildren<TMP_Text>(true);label.text="套用到待选";label.fontSize=20;label.color=Gold;view.Buttons[i]=button;
            }
            var footer=UnityEngine.Object.Instantiate(template,rect,false);footer.name="RecommendationNote";footer.text="仅套用已购包，回到卡包页后点击保存生效。回气／摸牌统计含条件触发；当前手牌不变。";footer.fontSize=18;footer.enableAutoSizing=false;footer.color=Muted;footer.raycastTarget=false;
            footer.rectTransform.anchorMin=Vector2.zero;footer.rectTransform.anchorMax=new Vector2(1,.06f);footer.rectTransform.offsetMin=Vector2.zero;footer.rectTransform.offsetMax=Vector2.zero;
            return view;
        }
    }
}
