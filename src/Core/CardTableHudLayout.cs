using System;
using System.Linq;
using HarmonyLib;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    [HarmonyPatch(typeof(GameHUDWindow),"RenderCardTable")]
    internal static class SectHandHeaderPatch
    {
        private static T Read<T>(object owner,string field)=>(T)AccessTools.Field(owner.GetType(),field).GetValue(owner);
        private static void Postfix(GameHUDWindow __instance)
        {
            var model=Read<CardTableViewModel>(__instance,"cardTable");
            bool sect=model!=null&&model.IsVisible&&
                (model.Hand.Any(c=>SectRegistry.Cards.ContainsKey(c.CardId))||
                 model.Packs.Any(p=>p.Id!=null&&p.Id.StartsWith(SectRegistry.Package+".pack.",StringComparison.Ordinal)));
            var layout=__instance.GetComponent<SectHandHeaderLayout>();
            if(layout==null)
            {
                if(!sect)return;
                layout=__instance.gameObject.AddComponent<SectHandHeaderLayout>();
            }
            layout.Configure(__instance,Read<TMP_Text>(__instance,"cardTableSummary"),
                new[]{"actionTabButton","abilityTabButton","inventoryTabButton","customTabButton","cardTabButton"}
                    .Select(name=>Read<Button>(__instance,name)).ToArray(),sect);
        }
    }

    // Keep the complete hand summary in its own row above the actual tab bounds.
    // Re-evaluate after layout and resizing, including the hand's control column.
    internal sealed class SectHandHeaderLayout : MonoBehaviour
    {
        private TMP_Text summary;
        private Button[] tabs;
        private readonly Vector3[] corners=new Vector3[4];
        private bool ownsLayout;
        private Vector2 anchorMin,anchorMax,pivot,position,size;
        private float fontSize;
        private bool autoSize,wrap,raycast;
        private TextAlignmentOptions alignment;
        private TextOverflowModes overflow;
        private GameHUDWindow hud;
        private Button[] controls;
        private UiRectSnapshot[] controlRects,labelRects;
        private UiRectSnapshot handRect;
        private bool applying;

        internal void Configure(GameHUDWindow owner,TMP_Text target,Button[] buttons,bool sect)
        {
            if(!sect){Restore();enabled=false;return;}
            if(target==null)return;
            if(!ownsLayout)
            {
                summary=target;var rect=summary.rectTransform;
                anchorMin=rect.anchorMin;anchorMax=rect.anchorMax;pivot=rect.pivot;
                position=rect.anchoredPosition;size=rect.sizeDelta;fontSize=summary.fontSize;
                autoSize=summary.enableAutoSizing;wrap=summary.enableWordWrapping;raycast=summary.raycastTarget;
                alignment=summary.alignment;overflow=summary.overflowMode;ownsLayout=true;
                hud=owner;
                controls=new[]{"cardDrawButton","cardPackButton","cardFateButton"}.Select(name=>(Button)AccessTools.Field(typeof(GameHUDWindow),name).GetValue(owner)).ToArray();
                controlRects=controls.Select(b=>new UiRectSnapshot((RectTransform)b.transform)).ToArray();
                labelRects=controls.Select(b=>new UiRectSnapshot(b.GetComponentInChildren<TMP_Text>(true).rectTransform)).ToArray();
                handRect=new UiRectSnapshot((RectTransform)AccessTools.Field(typeof(GameHUDWindow),"cardHandContent").GetValue(owner));
            }
            tabs=buttons;enabled=true;Apply();
        }
        private void Restore()
        {
            if(!ownsLayout||summary==null)return;
            var rect=summary.rectTransform;rect.anchorMin=anchorMin;rect.anchorMax=anchorMax;
            rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;
            summary.fontSize=fontSize;summary.enableAutoSizing=autoSize;summary.enableWordWrapping=wrap;
            summary.raycastTarget=raycast;summary.alignment=alignment;summary.overflowMode=overflow;
            foreach(var rectState in controlRects.Concat(labelRects))rectState.Restore();
            handRect.Restore();
            ownsLayout=false;
        }
        private Rect Bounds(RectTransform item,RectTransform parent)
        {
            item.GetWorldCorners(corners);
            float left=float.MaxValue,bottom=float.MaxValue,right=float.MinValue,top=float.MinValue;
            foreach(var corner in corners)
            {
                var p=parent.InverseTransformPoint(corner);
                left=Mathf.Min(left,p.x);bottom=Mathf.Min(bottom,p.y);
                right=Mathf.Max(right,p.x);top=Mathf.Max(top,p.y);
            }
            return Rect.MinMaxRect(left,bottom,right,top);
        }
        internal void Apply()
        {
            if(!ownsLayout||summary==null||tabs==null||applying)return;
            var parent=summary.rectTransform.parent as RectTransform;
            var canvas=GetComponentInParent<Canvas>();
            if(parent==null||canvas==null)return;
            float left=float.MaxValue,top=float.MinValue;
            foreach(var button in tabs)
            {
                if(button==null||!button.gameObject.activeSelf)continue;
                var bounds=Bounds((RectTransform)button.transform,parent);
                left=Mathf.Min(left,bounds.xMin);top=Mathf.Max(top,bounds.yMax);
            }
            if(left==float.MaxValue)return;
            var viewport=Bounds((RectTransform)canvas.rootCanvas.transform,parent);
            if(viewport.width<=0||viewport.height<=0)return;
            ApplyControls(parent,viewport);
            float right=viewport.xMax-20f;left=Mathf.Max(left,viewport.xMin+20f);
            float width=right-left;if(width<=0)return;
            summary.enableAutoSizing=false;summary.fontSize=15;summary.enableWordWrapping=true;
            summary.alignment=TextAlignmentOptions.BottomLeft;summary.overflowMode=TextOverflowModes.Overflow;
            summary.raycastTarget=false;
            float height=Mathf.Max(24,summary.GetPreferredValues(summary.text,width,0).y+4);
            // A narrow viewport can turn the long elemental summary into three
            // lines and push its ink into the formation labels. Use spare space
            // to the left before growing the header beyond two text lines.
            float maximumWidth=viewport.width-40;
            while(height>48&&width<maximumWidth)
            {
                width=Mathf.Min(maximumWidth,width+64);left=right-width;
                height=Mathf.Max(24,summary.GetPreferredValues(summary.text,width,0).y+4);
            }
            var rect=summary.rectTransform;rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;
            rect.anchoredPosition=new Vector2(left-parent.rect.xMin,top+8-parent.rect.yMin);
            rect.sizeDelta=new Vector2(width,height);
        }
        private void ApplyControls(RectTransform space,Rect viewport)
        {
            var original=handRect.BoundsIn(space);
            float tabBottom=tabs.Where(b=>b!=null&&b.gameObject.activeSelf).Min(b=>Bounds((RectTransform)b.transform,space).yMin);
            float right=viewport.xMax-20,left=right-106;
            float top=Mathf.Min(original.yMax-14,tabBottom-16);
            top=Mathf.Max(top,viewport.yMin+20+162);
            for(int i=0;i<controls.Length;i++)
            {
                UiRectSnapshot.Place((RectTransform)controls[i].transform,space,new Rect(left,top-50-i*56,106,50));
                var label=controls[i].GetComponentInChildren<TMP_Text>(true).rectTransform;
                label.anchorMin=Vector2.zero;label.anchorMax=Vector2.one;label.offsetMin=new Vector2(4,2);label.offsetMax=new Vector2(-4,-2);
            }
            float handLeft=Mathf.Max(original.xMin,viewport.xMin+20),handRight=Mathf.Min(original.xMax,left-16);
            var wanted=Rect.MinMaxRect(handLeft,original.yMin,handRight,original.yMax);
            var current=Bounds(handRect.Rect,space);
            if(Vector2.Distance(current.position,wanted.position)<.05f&&Vector2.Distance(current.size,wanted.size)<.05f)return;
            UiRectSnapshot.Place(handRect.Rect,space,wanted);
            // Native placement uses the available hand width. Run it once after
            // resizing so a full ten-card fan leaves the controls unobstructed.
            applying=true;
            try{AccessTools.Method(typeof(GameHUDWindow),"RenderCardTable").Invoke(hud,null);}
            finally{applying=false;}
        }
        private void LateUpdate(){Apply();}
        private void OnRectTransformDimensionsChange(){Apply();}
    }

    internal sealed class UiRectSnapshot
    {
        internal readonly RectTransform Rect;
        private readonly Vector2 min,max,pivot,position,size;
        internal UiRectSnapshot(RectTransform rect)
        {Rect=rect;min=rect.anchorMin;max=rect.anchorMax;pivot=rect.pivot;position=rect.anchoredPosition;size=rect.sizeDelta;}
        internal void Restore()
        {Rect.anchorMin=min;Rect.anchorMax=max;Rect.pivot=pivot;Rect.anchoredPosition=position;Rect.sizeDelta=size;}
        internal Rect BoundsIn(RectTransform space)
        {
            var parent=(RectTransform)Rect.parent;
            var low=parent.rect.min+Vector2.Scale(parent.rect.size,min);
            var high=parent.rect.min+Vector2.Scale(parent.rect.size,max);
            var dimensions=high-low+size;var origin=low+Vector2.Scale(high-low,pivot)+position-Vector2.Scale(dimensions,pivot);
            var a=space.InverseTransformPoint(parent.TransformPoint(origin));
            var b=space.InverseTransformPoint(parent.TransformPoint(origin+dimensions));
            return UnityEngine.Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        internal static void Place(RectTransform rect,RectTransform space,Rect bounds)
        {
            var parent=(RectTransform)rect.parent;
            var a=parent.InverseTransformPoint(space.TransformPoint(bounds.min));
            var b=parent.InverseTransformPoint(space.TransformPoint(bounds.max));
            rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;
            rect.anchoredPosition=(Vector2)a-parent.rect.min;rect.sizeDelta=b-a;
        }
    }
}
