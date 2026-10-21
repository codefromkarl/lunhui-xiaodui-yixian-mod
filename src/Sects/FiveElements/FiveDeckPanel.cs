using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace YixianSect
{
    internal static class FiveDeckPanel
    {
        private sealed class Context { internal bool Native;internal IReadOnlyList<StableId> Cards,Unlocked; }
        private sealed class View { internal Button Edit;internal Vector2[][] Original; }
        private static readonly ConditionalWeakTable<CardTableViewModel,Context> Contexts=new ConditionalWeakTable<CardTableViewModel,Context>();
        private static readonly ConditionalWeakTable<CardTablePackPanel,View> Views=new ConditionalWeakTable<CardTablePackPanel,View>();
        private static T Read<T>(object owner,string field)=>(T)AccessTools.Field(owner.GetType(),field).GetValue(owner);
        internal static void ConfigureModel(RunPlayerSnapshot player,CardTableViewModel model)
        {
            if(!player.Lineages.Any(l=>l.LineageId.Value==FiveElementRules.Lineage&&l.Rank>0))return;
            Contexts.Remove(model);Contexts.Add(model,new Context{Native=player.Lineages.Any(l=>l.LineageId==CardTableCatalog.LineageId),Cards=FiveDeckRules.Cards(player.PurchasedShelfAbilityIds),Unlocked=FiveDeckRules.Unlocked(player.PurchasedShelfAbilityIds)});
        }
        internal static bool PureFive(CardTableViewModel model)=>model!=null&&Contexts.TryGetValue(model,out var context)&&!context.Native;
        internal static void OpenEditor(CardTablePackPanel panel)
        {
            var model=Read<CardTableViewModel>(panel,"model");
            if(model==null||!Contexts.TryGetValue(model,out var context)||!model.CanManage||Read<bool>(panel,"waiting")||Read<bool>(panel,"fate"))return;
            var draft=new FiveDeckDraft(context.Unlocked,context.Cards,FiveDeckRules.Size);
            FiveCardPicker.Open(panel.transform,"FiveDeckEditor","调整五行卡组","从已解锁的 "+context.Unlocked.Count+" 张中选择八张。免费保存，保留手牌；下次抽牌使用新卡组。","保存八张卡组",draft,Read<TMP_Text>(panel,"title").font,cards=>Save(panel,cards));
        }
        internal static void Save(CardTablePackPanel panel,IReadOnlyList<StableId> cards)
        {
            var model=Read<CardTableViewModel>(panel,"model");
            if(model==null||!Contexts.TryGetValue(model,out var context)||!model.CanManage||Read<bool>(panel,"waiting")||Read<bool>(panel,"fate")||cards.Count!=8||cards.Distinct().Count()!=8||cards.Any(id=>!context.Unlocked.Contains(id)))return;
            AccessTools.Field(typeof(CardTablePackPanel),"waiting").SetValue(panel,true);
            AccessTools.Field(typeof(CardTablePackPanel),"failed").SetValue(panel,false);AccessTools.Field(typeof(CardTablePackPanel),"timedOut").SetValue(panel,false);
            AccessTools.Field(typeof(CardTablePackPanel),"submittedAt").SetValue(panel,Time.unscaledTime);
            AccessTools.Method(typeof(CardTablePackPanel),"Render").Invoke(panel,null);
            var submit=Read<Action<IReadOnlyList<string>>>(panel,"submit");
            if(submit==null){panel.Reject();return;}
            submit(new[]{FiveDeckRules.EncodeSelection(cards).Value});
        }
        internal static void Render(CardTablePackPanel panel)
        {
            var model=Read<CardTableViewModel>(panel,"model");var packs=Read<Button>(panel,"packsTab");var fusion=Read<Button>(panel,"fusionTab");
            if(model==null||!Contexts.TryGetValue(model,out var context))
            {
                if(Views.TryGetValue(panel,out var old)){old.Edit.gameObject.SetActive(false);Restore((RectTransform)packs.transform,old.Original[0]);Restore((RectTransform)fusion.transform,old.Original[1]);}
                return;
            }
            if(!Views.TryGetValue(panel,out var view))
            {
                view=new View{Original=new[]{Geometry((RectTransform)packs.transform),Geometry((RectTransform)fusion.transform)}};Views.Add(panel,view);
                view.Edit=UnityEngine.Object.Instantiate(packs,packs.transform.parent,false);view.Edit.name="FiveEditDeck";view.Edit.onClick=new Button.ButtonClickedEvent();view.Edit.onClick.AddListener(()=>OpenEditor(panel));
                view.Edit.GetComponentInChildren<TMP_Text>(true).text="调整八张";
            }
            bool fate=Read<bool>(panel,"fate");view.Edit.gameObject.SetActive(!fate);view.Edit.interactable=model.CanManage&&!Read<bool>(panel,"waiting");
            Restore((RectTransform)packs.transform,view.Original[0]);Restore((RectTransform)fusion.transform,view.Original[1]);
            if(fate)return;
            var a=(RectTransform)packs.transform;var b=(RectTransform)fusion.transform;
            float left=a.localPosition.x-a.rect.width*.5f,right=b.localPosition.x+b.rect.width*.5f;
            var tabs=context.Native?new[]{packs,view.Edit,fusion}:new[]{packs,view.Edit};float width=(right-left)/tabs.Length;
            for(int i=0;i<tabs.Length;i++){var t=(RectTransform)tabs[i].transform;t.anchorMin=new Vector2(.5f,t.anchorMin.y);t.anchorMax=new Vector2(.5f,t.anchorMax.y);t.pivot=new Vector2(.5f,t.pivot.y);t.sizeDelta=new Vector2(width-6,t.sizeDelta.y);t.anchoredPosition=new Vector2(left+width*(i+.5f),t.anchoredPosition.y);}
            if(context.Native)return;
            Read<TMP_Text>(panel,"title").text="五行自选卡包";
            Read<TMP_Text>(panel,"summary").text="当前八张 · 已解锁 "+context.Unlocked.Count+" 张\n点击「调整八张」免费组牌，保存保留手牌和本场状态。";
            fusion.gameObject.SetActive(false);Read<Button>(panel,"confirm").gameObject.SetActive(false);
        }
        private static Vector2[] Geometry(RectTransform r)=>new[]{r.anchorMin,r.anchorMax,r.pivot,r.anchoredPosition,r.sizeDelta};
        private static void Restore(RectTransform r,Vector2[] g){r.anchorMin=g[0];r.anchorMax=g[1];r.pivot=g[2];r.sizeDelta=g[4];r.anchoredPosition=g[3];}
    }
    [HarmonyPatch(typeof(CardTablePackPanel),"Render")]
    internal static class FiveDeckRender
    {private static void Postfix(CardTablePackPanel __instance)=>FiveDeckPanel.Render(__instance);}
    [HarmonyPatch(typeof(CardTablePackPanel),"CanConfirm")]
    internal static class FiveDeckConfirm
    {
        private static void Postfix(CardTablePackPanel __instance,ref bool __result)
        {
            var model=(CardTableViewModel)AccessTools.Field(typeof(CardTablePackPanel),"model").GetValue(__instance);
            bool fate=(bool)AccessTools.Field(typeof(CardTablePackPanel),"fate").GetValue(__instance);
            if(FiveDeckPanel.PureFive(model)&&!fate)__result=false;
        }
    }
}

namespace YixianSect
{
    // The shipped preview has four large card slots. Page those slots rather than
    // shrinking eight cards into unreadable thumbnails or silently hiding half.
    [HarmonyPatch(typeof(CardTablePackPanel),"Preview")]
    internal static class FiveDeckPreview
    {
        private sealed class Page
        {
            internal int Index;
            internal Button Previous,Next;
            internal UnityEngine.Vector2 Min,Max,OffsetMin,OffsetMax;
            internal float Width;
        }
        private static readonly ConditionalWeakTable<CardTablePackPanel,Page> Pages = new ConditionalWeakTable<CardTablePackPanel,Page>();
        private static T Read<T>(object owner,string field)=>(T)AccessTools.Field(owner.GetType(),field).GetValue(owner);
        private static bool Prefix(CardTablePackPanel __instance,CardTablePackViewModel pack)
        {
            if(pack.Id!=FiveDeckRules.PackId.Value)
            {
                if(Pages.TryGetValue(__instance,out var old))
                {
                    old.Previous.gameObject.SetActive(false);old.Next.gameObject.SetActive(false);
                    var rect=Read<TMP_Text>(__instance,"previewRecipes").rectTransform;
                    rect.anchorMin=old.Min;rect.anchorMax=old.Max;rect.offsetMin=old.OffsetMin;rect.offsetMax=old.OffsetMax;
                }
                return true;
            }
            var panel=__instance;
            if(!Pages.TryGetValue(panel,out var page))
            {
                page=new Page();Pages.Add(panel,page);
                var label=Read<TMP_Text>(panel,"previewRecipes");var rect=label.rectTransform;
                page.Min=rect.anchorMin;page.Max=rect.anchorMax;page.OffsetMin=rect.offsetMin;page.OffsetMax=rect.offsetMax;page.Width=rect.rect.width;
                Button Create(string name,string text,float left,float right,int target)
                {
                    var button=UnityEngine.Object.Instantiate(Read<Button>(panel,"packsTab"),rect.parent,false);button.name=name;
                    button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{page.Index=target;var current=Read<CardTableViewModel>(panel,"model").Packs.First(p=>p.Id==FiveDeckRules.PackId.Value);AccessTools.Method(typeof(CardTablePackPanel),"Preview").Invoke(panel,new object[]{current});});
                    var r=(UnityEngine.RectTransform)button.transform;r.anchorMin=page.Min;r.anchorMax=page.Max;r.offsetMin=page.OffsetMin;r.offsetMax=page.OffsetMax;
                    float width=rect.rect.width;r.offsetMin+=new UnityEngine.Vector2(width*left,0);r.offsetMax-=new UnityEngine.Vector2(width*(1-right),0);
                    button.GetComponentInChildren<TMP_Text>(true).text=text;return button;
                }
                page.Previous=Create("FiveDeckPreviousPage","上一页",0,.27f,0);page.Next=Create("FiveDeckNextPage","下一页",.73f,1,1);
                // Keep the page counter in the existing recipe row between buttons.

            }
            var counter=Read<TMP_Text>(panel,"previewRecipes").rectTransform;counter.anchorMin=page.Min;counter.anchorMax=page.Max;counter.offsetMin=page.OffsetMin+new Vector2(page.Width*.29f,0);counter.offsetMax=page.OffsetMax-new Vector2(page.Width*.29f,0);
            page.Previous.gameObject.SetActive(true);page.Next.gameObject.SetActive(true);
            page.Previous.interactable=page.Index>0;page.Next.interactable=page.Index<1;
            AccessTools.Field(typeof(CardTablePackPanel),"previewPackId").SetValue(panel,pack.Id);
            Read<TMP_Text>(panel,"detailTitle").text=pack.Title;
            Read<TMP_Text>(panel,"previewCount").text=pack.CountLine;
            Read<TMP_Text>(panel,"previewRecipes").text="第"+(page.Index+1)+"/2页 · 共8张";
            var entries=Read<CardTableListEntry[]>(panel,"previewCards");var art=Read<CardTableArtSet>(panel,"art");
            void Show(int index)
            {
                AccessTools.Method(typeof(CardTablePackPanel),"ShowCard").Invoke(panel,new object[]{pack,index,true});
                for(int i=0;i<entries.Length;i++)entries[i].SetFocused(i+page.Index*4==index);
            }
            for(int i=0;i<entries.Length;i++)
            {
                int index=i+page.Index*4;bool visible=i<4&&index<pack.Cards.Count;entries[i].gameObject.SetActive(visible);if(!visible)continue;
                var card=pack.Cards[index];entries[i].Present(card.Title,null,art?.Find(card.ArtKey),false);
                entries[i].Previewed=()=>Show(index);
                entries[i].Clicked=()=>
                {
                    if(Read<bool>(panel,"fate")&&!Read<bool>(panel,"waiting"))
                    {
                        AccessTools.Field(typeof(CardTablePackPanel),"fateCardId").SetValue(panel,card.CardId);
                        AccessTools.Method(typeof(CardTablePackPanel),"Render").Invoke(panel,null);
                    }
                    Show(index);
                };
            }
            Show(page.Index*4);return false;
        }
    }
}
