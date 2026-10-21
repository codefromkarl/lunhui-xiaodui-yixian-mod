using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.Runtime.Birth;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MuyifengAppearance
{
    internal sealed class CreationPicker : MonoBehaviour
    {
        private BirthWindow window;
        private Button open,random;
        private GameObject popup;
        private BirthDraftModel draft;
        private sealed class NativeDraft { internal BirthAppearance Appearance=BirthAppearance.Default; }
        private static readonly ConditionalWeakTable<BirthDraftModel,NativeDraft> nativeDrafts=new ConditionalWeakTable<BirthDraftModel,NativeDraft>();
        internal static void Render(BirthWindow window)
        {
            var picker=window.GetComponent<CreationPicker>()??window.gameObject.AddComponent<CreationPicker>();
            picker.window=window;picker.Refresh();
        }
        private void Refresh()
        {
            var flow=Entry.Read<Reincarnation.Runtime.Scenes.IBirthSceneFlow>(window,"flow");draft=flow?.Draft;
            if(draft==null||!flow.IsReady){if(open!=null)open.gameObject.SetActive(false);return;}
            if(AppearanceChoices.NativeValid(draft.Appearance))nativeDrafts.GetOrCreateValue(draft).Appearance=draft.Appearance;
            random=Entry.Read<Button>(window,"randomAppearanceButton");
            if(random==null)return;
            if(open==null)
            {
                open=Instantiate(random,random.transform.parent,false);open.name="AppearanceChoiceOpen";
                open.onClick=new Button.ButtonClickedEvent();open.onClick.AddListener(Open);
                foreach(var b in open.GetComponentsInChildren<Behaviour>(true))if(b.GetType().Name=="LocalizedLabel")b.enabled=false;
            }
            bool info=draft.CurrentTab==BirthDraftTab.Info;
            open.gameObject.SetActive(info);if(!info)Close();
            Label(open,"造型："+(AppearanceChoices.Find(draft.Appearance)?.Name??"原生自定义"));
            Align();
            bool native=AppearanceChoices.Find(draft.Appearance)==null;
            foreach(var b in Entry.Read<Button[]>(window,"appearancePreviousButtons").Concat(Entry.Read<Button[]>(window,"appearanceNextButtons")))if(b!=null)b.interactable=native;
            if(!native)foreach(var text in Entry.Read<TMP_Text[]>(window,"appearancePartValues"))if(text!=null)text.text="随造型";
        }
        private void LateUpdate()=>Align();
        private void Align()
        {
            if(open==null||random==null)return;
            var r=(RectTransform)random.transform;var o=(RectTransform)open.transform;
            o.anchorMin=r.anchorMin;o.anchorMax=r.anchorMax;o.pivot=r.pivot;
            o.anchoredPosition=r.anchoredPosition+new Vector2(r.rect.width+16,0);
        }
        private static void Label(Button button,string value)
        {
            var texts=button.GetComponentsInChildren<TMP_Text>(true);
            for(int i=0;i<texts.Length;i++){texts[i].text=i==0?value:"";texts[i].raycastTarget=false;}
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);go.layer=parent.gameObject.layer;
            var r=(RectTransform)go.transform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        private void Open()=>OpenGroup(null);
        private void OpenGroup(string requestedGroup)
        {
            Close();var root=Rect("AppearanceChoiceMenu",window.transform,Vector2.zero,Vector2.one);popup=root.gameObject;root.SetAsLastSibling();
            var canvas=popup.AddComponent<Canvas>();canvas.overrideSorting=true;
            canvas.sortingOrder=window.GetComponentsInParent<Canvas>(true).Select(c=>c.sortingOrder).DefaultIfEmpty(0).Max()+1;
            canvas.worldCamera=window.GetComponentInParent<Canvas>()?.worldCamera;popup.AddComponent<GraphicRaycaster>();
            popup.AddComponent<Image>().color=new Color(0,0,0,.64f);
            float height=Mathf.Min(730,((RectTransform)window.transform).rect.height-80);
            var panel=Rect("Choices",root,new Vector2(.5f,.5f),new Vector2(.5f,.5f));panel.sizeDelta=new Vector2(740,height);
            panel.gameObject.AddComponent<Image>().color=new Color(.10f,.13f,.17f,1);
            var heading=Rect("Title",panel,new Vector2(0,1),Vector2.one);heading.pivot=new Vector2(.5f,1);heading.sizeDelta=new Vector2(-40,54);heading.anchoredPosition=new Vector2(0,-12);
            var title=heading.gameObject.AddComponent<TextMeshProUGUI>();title.font=open.GetComponentInChildren<TMP_Text>(true).font;title.fontSize=26;title.text="选择角色造型";title.alignment=TextAlignmentOptions.Center;title.raycastTarget=false;
            AddOption(panel,"AppearanceChoice.native","原生自定义",null,0);
            var groups=AppearanceChoices.All.Select(c=>c.Group??"其他").Distinct().OrderBy(g=>g=="云灵剑宗"?0:g=="七星阁"?1:g=="五行道盟"?2:3).ThenBy(g=>g,StringComparer.Ordinal).ToArray();
            string selected=requestedGroup??AppearanceChoices.Find(draft.Appearance)?.Group??groups.FirstOrDefault();
            if(!groups.Contains(selected))selected=groups.FirstOrDefault();
            for(int i=0;i<groups.Length;i++)
            {
                string group=groups[i];var tab=Instantiate(random,panel,false);tab.name="AppearanceGroup."+group;
                tab.onClick=new Button.ButtonClickedEvent();tab.onClick.AddListener(()=>OpenGroup(group));Label(tab,(selected==group?"✓ ":"")+group);
                var r=(RectTransform)tab.transform;Position(r,132,44);r.sizeDelta=new Vector2(660f/groups.Length-8,44);r.anchoredPosition=new Vector2(-330+(i+.5f)*660/groups.Length,-132);
                var layout=tab.GetComponent<LayoutElement>();if(layout!=null)layout.minWidth=layout.preferredWidth=r.sizeDelta.x;
            }
            var scroll=Rect("CharacterScroll",panel,Vector2.zero,Vector2.one);scroll.offsetMin=new Vector2(24,82);scroll.offsetMax=new Vector2(-24,-194);
            scroll.gameObject.AddComponent<Image>().color=new Color(0,0,0,.04f);scroll.gameObject.AddComponent<RectMask2D>();
            var content=Rect("CharacterOptions",scroll,new Vector2(0,1),Vector2.one);content.pivot=new Vector2(.5f,1);
            var choices=AppearanceChoices.All.Where(c=>(c.Group??"其他")==selected).OrderBy(c=>SkinCatalog.SourceOrder(c.Id)).ThenBy(c=>c.Name,StringComparer.Ordinal).ToArray();content.sizeDelta=new Vector2(0,12+54*choices.Length);
            var scroller=scroll.gameObject.AddComponent<ScrollRect>();scroller.viewport=scroll;scroller.content=content;scroller.horizontal=false;scroller.vertical=true;scroller.movementType=ScrollRect.MovementType.Clamped;
            int index=0;foreach(var choice in choices)AddOption(content,"AppearanceChoice."+choice.Id,choice.Name,choice,index++,6);
            var cancel=Instantiate(random,panel,false);cancel.name="AppearanceChoiceCancel";cancel.onClick=new Button.ButtonClickedEvent();cancel.onClick.AddListener(Close);Label(cancel,"返回");Position((RectTransform)cancel.transform,panel.rect.height-64,52);
        }
        private void AddOption(RectTransform parent,string name,string text,AppearanceChoice choice,int index,float top=74)
        {
            var button=Instantiate(random,parent,false);button.name=name;button.onClick=new Button.ButtonClickedEvent();
            bool selected=choice==null?AppearanceChoices.Find(draft.Appearance)==null:choice.Token==draft.Appearance;
            Label(button,(selected?"✓  ":"")+text);button.onClick.AddListener(()=>{
                var appearance=choice?.Token??nativeDrafts.GetOrCreateValue(draft).Appearance;
                if(draft.TrySetAppearance(appearance,out var error))Close();else Entry.Context.Log(error);
            });Position((RectTransform)button.transform,top+index*54,46);
        }
        private static void Position(RectTransform r,float top,float height)
        {var fitter=r.GetComponent<ContentSizeFitter>();if(fitter!=null)fitter.enabled=false;
            var layout=r.GetComponent<LayoutElement>();if(layout!=null){layout.minWidth=layout.preferredWidth=540;layout.minHeight=layout.preferredHeight=height;}
            foreach(var b in r.GetComponentsInChildren<Behaviour>(true))if(b.GetType().Name=="LocalizedLabel")b.enabled=false;
            r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.pivot=new Vector2(.5f,1);r.localScale=Vector3.one;r.sizeDelta=new Vector2(540,height);r.anchoredPosition=new Vector2(0,-top);}
        private void Close(){if(popup!=null){popup.SetActive(false);Destroy(popup);popup=null;}}
        private void OnDisable()=>Close();
    }
    [HarmonyPatch(typeof(BirthWindow),"RefreshView")]
    internal static class CreationAppearanceMenu
    {private static void Postfix(BirthWindow __instance)=>CreationPicker.Render(__instance);}
}
