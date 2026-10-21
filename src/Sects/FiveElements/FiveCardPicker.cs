using System;
using System.Collections.Generic;
using System.Linq;
using Reincarnation.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    // Native charcoal/gold palette, readable two-column card list, full effects
    // below. Learning two and equipping eight differ in count, not interaction.
    internal static class FiveCardPicker
    {
        private static readonly Color Back=new Color(.09f,.085f,.075f,.99f),Paper=new Color(.94f,.90f,.79f),Gold=new Color(.72f,.55f,.27f),Idle=new Color(.20f,.18f,.14f);
        internal static RectTransform Box(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.offsetMin=Vector2.zero;r.offsetMax=Vector2.zero;return r;
        }
        internal static TMP_Text Text(string name,Transform parent,TMP_FontAsset font,string text,int size,Vector2 min,Vector2 max)
        {
            var r=Box(name,parent,min,max);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=text;t.fontSize=size;t.color=Paper;t.alignment=TextAlignmentOptions.TopLeft;t.raycastTarget=false;t.enableWordWrapping=true;return t;
        }
        internal static Button Button(string name,Transform parent,TMP_FontAsset font,string title,Vector2 min,Vector2 max,Action click)
        {
            var r=Box(name,parent,min,max);var image=r.gameObject.AddComponent<Image>();image.color=Idle;
            var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>click());
            Text("Label",r,font,title,22,new Vector2(.04f,.08f),new Vector2(.96f,.92f)).alignment=TextAlignmentOptions.Center;return button;
        }
        internal static GameObject Open(Transform parent,string name,string title,string instruction,string action,FiveDeckDraft draft,TMP_FontAsset font,Action<IReadOnlyList<StableId>> submit,int rank=1)
        {
            var previous=parent.Find(name);if(previous!=null){previous.gameObject.SetActive(false);UnityEngine.Object.Destroy(previous.gameObject);}
            var root=Box(name,parent,Vector2.zero,Vector2.one);root.SetAsLastSibling();root.gameObject.AddComponent<Image>().color=Back;
            Text("Title",root,font,title,30,new Vector2(.04f,.92f),new Vector2(.96f,.985f));
            var progress=Text("Progress",root,font,"",22,new Vector2(.04f,.86f),new Vector2(.96f,.915f));
            Text("Instruction",root,font,instruction,20,new Vector2(.04f,.77f),new Vector2(.96f,.855f));
            var detail=Scroll(root,"Details",new Vector2(.04f,.115f),new Vector2(.96f,.275f),out var detailContent);
            var detailText=Text("CardDetail",detailContent,font,"点击卡牌查看完整效果。",20,new Vector2(0,1),Vector2.one);
            detailText.rectTransform.pivot=new Vector2(.5f,1);detailContent.sizeDelta=new Vector2(0,160);detailText.rectTransform.sizeDelta=new Vector2(0,160);
            Scroll(root,"Candidates",new Vector2(.04f,.30f),new Vector2(.96f,.685f),out var content);content.sizeDelta=new Vector2(0,((draft.Candidates.Count+1)/2)*100);
            var rows=new List<(Button button,StableId card)>();Button confirm=null;string feedback="";
            Action refresh=()=>
            {
                progress.text="已选 "+draft.Selected.Count+" / "+draft.Required+" 张"+(feedback.Length>0?" · "+feedback:"");
                foreach(var row in rows)row.button.targetGraphic.color=draft.Selected.Contains(row.card)?Gold:Idle;
                if(confirm!=null)confirm.interactable=draft.Ready;
            };
            for(int i=0;i<FiveBuildGuidance.Routes.Length;i++)
            {
                var route=FiveBuildGuidance.Routes[i];
                Button("Recommend_"+i,root,font,route.Name,new Vector2(.04f+i*.31f,.70f),new Vector2(.335f+i*.31f,.76f),()=>
                {
                    var proposal=FiveBuildGuidance.Suggest(route,draft.Candidates,draft.Required,rank);
                    draft.Apply(proposal);feedback=route.Name+" · 待确认";
                    detailText.text=FiveBuildGuidance.Explain(route,draft.Candidates,proposal,draft.Required,rank);
                    float height=Math.Max(160,detailText.GetPreferredValues(detailText.text,detail.viewport.rect.width-16,0).y+16);
                    detailContent.sizeDelta=new Vector2(0,height);detailText.rectTransform.sizeDelta=new Vector2(0,height);detail.verticalNormalizedPosition=1;refresh();
                });
            }
            for(int i=0;i<draft.Candidates.Count;i++)
            {
                var card=draft.Candidates[i];var rule=SectRegistry.Cards[card.Value];
                var b=Button("Pick_"+(string)rule["key"],content,font,"",Vector2.zero,Vector2.one,()=>
                {
                    feedback=draft.Toggle(card)?"":"已选满，请先取消一张";
                    detailText.text=(string)rule["name"]+"\n"+SectDescriptions.CardDetail(rule);
                    float height=Math.Max(160,detailText.GetPreferredValues(detailText.text,detail.viewport.rect.width-16,0).y+16);
                    detailContent.sizeDelta=new Vector2(0,height);detailText.rectTransform.sizeDelta=new Vector2(0,height);detail.verticalNormalizedPosition=1;refresh();
                });
                var r=(RectTransform)b.transform;r.anchorMin=new Vector2(i%2*.5f,1);r.anchorMax=new Vector2((i%2+1)*.5f,1);r.pivot=new Vector2(.5f,1);r.anchoredPosition=new Vector2(0,-i/2*100);r.sizeDelta=new Vector2(-8,94);
                var label=b.GetComponentInChildren<TMP_Text>();label.text=(string)rule["name"]+" · "+SectCardRules.PrintedCost(rule)+"斗气\n"+SectDescriptions.CardSummary(rule);label.fontSize=20;label.alignment=TextAlignmentOptions.MidlineLeft;rows.Add((b,card));
            }
            Button("Cancel",root,font,"取消",new Vector2(.54f,.025f),new Vector2(.72f,.09f),()=>{root.gameObject.SetActive(false);UnityEngine.Object.Destroy(root.gameObject);});
            confirm=Button("ConfirmCards",root,font,action,new Vector2(.74f,.025f),new Vector2(.96f,.09f),()=>
            {
                if(!draft.Ready)return;var selected=draft.Selected.ToArray();root.gameObject.SetActive(false);UnityEngine.Object.Destroy(root.gameObject);submit(selected);
            });refresh();return root.gameObject;
        }
        private static ScrollRect Scroll(Transform parent,string name,Vector2 min,Vector2 max,out RectTransform content)
        {
            var root=Box(name,parent,min,max);root.gameObject.AddComponent<Image>().color=new Color(.12f,.11f,.095f);root.gameObject.AddComponent<RectMask2D>();
            content=Box("Content",root,new Vector2(0,1),Vector2.one);content.pivot=new Vector2(.5f,1);
            var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.viewport=root;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;return scroll;
        }
    }
}
