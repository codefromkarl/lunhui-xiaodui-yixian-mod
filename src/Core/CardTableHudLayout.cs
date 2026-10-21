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
        private Button endTurn;
        private UiRectSnapshot[] controlRects,labelRects;
        private UiRectSnapshot handRect,turnGroupRect;
        private RectTransform safeFrame;
        private SectHandHoverFilter hoverFilter;
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
                endTurn=(Button)AccessTools.Field(typeof(GameHUDWindow),"endTurnButton").GetValue(owner);
                turnGroupRect=new UiRectSnapshot((RectTransform)endTurn.transform.parent);
                safeFrame=owner.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r=>r.name=="SafeFrame16x9");
                controlRects=controls.Select(b=>new UiRectSnapshot((RectTransform)b.transform)).ToArray();
                labelRects=controls.Select(b=>new UiRectSnapshot(b.GetComponentInChildren<TMP_Text>(true).rectTransform)).ToArray();
                handRect=new UiRectSnapshot((RectTransform)AccessTools.Field(typeof(GameHUDWindow),"cardHandContent").GetValue(owner));
                var handArea=owner.GetComponentInChildren<CardTableHandArea>(true);
                if(handArea!=null){
                    hoverFilter=handArea.GetComponent<SectHandHoverFilter>()??handArea.gameObject.AddComponent<SectHandHoverFilter>();
                    hoverFilter.Excluded=controls.Select(b=>(RectTransform)b.transform).Concat(new[]{(RectTransform)endTurn.transform}).ToArray();
                }
            }
            tabs=buttons;enabled=true;if(hoverFilter!=null)hoverFilter.Active=true;Apply();
        }
        private void Restore()
        {
            if(!ownsLayout||summary==null)return;
            var rect=summary.rectTransform;rect.anchorMin=anchorMin;rect.anchorMax=anchorMax;
            rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;
            summary.fontSize=fontSize;summary.enableAutoSizing=autoSize;summary.enableWordWrapping=wrap;
            summary.raycastTarget=raycast;summary.alignment=alignment;summary.overflowMode=overflow;
            foreach(var rectState in controlRects.Concat(labelRects))rectState.Restore();
            handRect.Restore();turnGroupRect.Restore();if(hoverFilter!=null)hoverFilter.Active=false;
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
            var viewport=Bounds(safeFrame!=null?safeFrame:(RectTransform)canvas.rootCanvas.transform,parent);
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
            // The native 316-unit TurnCommit group ends flush with BottomBar at
            // the authored 1920 reference. Its fixed left offset (1596) clips in
            // a narrower safe frame; right anchoring preserves the reference pose.
            var turn=turnGroupRect.Rect;var min=turn.anchorMin;var max=turn.anchorMax;var pivot=turn.pivot;var position=turn.anchoredPosition;
            min.x=max.x=pivot.x=1;position.x=0;
            turn.anchorMin=min;turn.anchorMax=max;turn.pivot=pivot;turn.anchoredPosition=position;
            var original=handRect.BoundsIn(space);
            var native=controlRects.Select(r=>r.BoundsIn(space)).ToArray();
            var fitted=native.ToArray();
            for(int i=0;i<fitted.Length;i++){
                var text=controls[i].GetComponentInChildren<TMP_Text>(true);
                float height=Mathf.Max(native[i].height,Mathf.Ceil(text.GetPreferredValues(text.text,native[i].width-8,0).y+4));
                float top=i==0?native[i].yMax:Mathf.Min(native[i].yMax,fitted[i-1].yMin-6);
                fitted[i]=new Rect(native[i].xMin,top-height,native[i].width,height);
            }
            var group=Rect.MinMaxRect(fitted.Min(r=>r.xMin),fitted.Min(r=>r.yMin),fitted.Max(r=>r.xMax),fitted.Max(r=>r.yMax));
            float tabBottom=tabs.Where(b=>b!=null&&b.gameObject.activeSelf).Min(b=>Bounds((RectTransform)b.transform,space).yMin);
            var allowed=Rect.MinMaxRect(viewport.xMin+20,viewport.yMin+20,viewport.xMax-20,Mathf.Min(viewport.yMax-20,tabBottom-8));
            Rect? blocker=endTurn!=null&&endTurn.gameObject.activeInHierarchy?Bounds((RectTransform)endTurn.transform,space):(Rect?)null;
            var shift=NearestControlShift(group,allowed,blocker);
            for(int i=0;i<controls.Length;i++)
            {
                // Keep the native card-area placement. Only grow a row enough
                // for its current label; never shrink text or globally dock controls.
                var wantedControl=fitted[i];wantedControl.position+=shift;
                if(shift.sqrMagnitude<.0001f&&Vector2.Distance(fitted[i].position,native[i].position)<.05f&&Vector2.Distance(fitted[i].size,native[i].size)<.05f)controlRects[i].Restore();
                else UiRectSnapshot.Place((RectTransform)controls[i].transform,space,wantedControl);
                labelRects[i].Restore();
                var label=controls[i].GetComponentInChildren<TMP_Text>(true).rectTransform;
                label.anchorMin=Vector2.zero;label.anchorMax=Vector2.one;label.offsetMin=new Vector2(4,2);label.offsetMax=new Vector2(-4,-2);
            }
            group.position+=shift;
            float left=group.xMin;

            float handLeft=Mathf.Max(original.xMin,viewport.xMin+20),handRight=Mathf.Min(original.xMax,viewport.xMax-20);
            if(group.yMin<original.yMax&&group.yMax>original.yMin)handRight=Mathf.Min(handRight,left-16);
            var wanted=Rect.MinMaxRect(handLeft,original.yMin,handRight,original.yMax);
            var current=Bounds(handRect.Rect,space);
            if(Vector2.Distance(current.position,wanted.position)<.05f&&Vector2.Distance(current.size,wanted.size)<.05f)return;
            if(Vector2.Distance(wanted.position,original.position)<.05f&&Vector2.Distance(wanted.size,original.size)<.05f)handRect.Restore();
            else UiRectSnapshot.Place(handRect.Rect,space,wanted);
            // Native placement uses the available hand width. Run it once after
            // resizing so a full ten-card fan leaves the controls unobstructed.
            applying=true;
            try{AccessTools.Method(typeof(GameHUDWindow),"RenderCardTable").Invoke(hud,null);}
            finally{applying=false;}
        }
        internal static Vector2 NearestControlShift(Rect box,Rect allowed,Rect? blocker)
        {
            float x=Mathf.Clamp(box.xMin,allowed.xMin,allowed.xMax-box.width);
            float y=Mathf.Clamp(box.yMin,allowed.yMin,allowed.yMax-box.height);
            var xs=new[]{x,allowed.xMin,allowed.xMax-box.width}.ToList();
            var ys=new[]{y,allowed.yMin,allowed.yMax-box.height}.ToList();
            if(blocker.HasValue){var b=blocker.Value;xs.Add(b.xMin-16-box.width);xs.Add(b.xMax+16);ys.Add(b.yMin-16-box.height);ys.Add(b.yMax+16);}
            // Supported HUD viewports have room to the left of the fixed turn controls.
            // Choose the least translation; a safe native box has zero cost.
            return (from xx in xs from yy in ys
                    let candidate=new Rect(xx,yy,box.width,box.height)
                    where candidate.xMin>=allowed.xMin-.01f&&candidate.yMin>=allowed.yMin-.01f&&candidate.xMax<=allowed.xMax+.01f&&candidate.yMax<=allowed.yMax+.01f
                    where !blocker.HasValue||!OverlapsWithGap(candidate,blocker.Value,16)
                    let offset=candidate.position-box.position
                    orderby offset.sqrMagnitude
                    select offset).First();
        }
        private static bool OverlapsWithGap(Rect a,Rect b,float gap)
            =>a.xMin<b.xMax+gap&&a.xMax>b.xMin-gap&&a.yMin<b.yMax+gap&&a.yMax>b.yMin-gap;
        private void LateUpdate(){Apply();}
        private void OnRectTransformDimensionsChange(){Apply();}
    }

    // The native transparent HandArea extends beyond the fan. When controls move
    // inward on narrow frames it must not consume clicks intended for those buttons.
    internal sealed class SectHandHoverFilter : MonoBehaviour,ICanvasRaycastFilter
    {
        internal bool Active;
        internal RectTransform[] Excluded;
        public bool IsRaycastLocationValid(Vector2 point,Camera eventCamera)
            =>!Active||Excluded.All(r=>!r.gameObject.activeInHierarchy||!RectTransformUtility.RectangleContainsScreenPoint(r,point,eventCamera));
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
