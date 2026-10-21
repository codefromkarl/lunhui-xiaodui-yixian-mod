using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace YixianSect
{
    // Only called by the explicit runtime self-test. Real game prefabs, isolated canvas/camera,
    // no live window manager, intent sink, input, run host, save host or permission changes.
    internal static partial class VisualSelfTests
    {
        private static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        private static T Field<T>(object value,string key) => (T)AccessTools.Field(value.GetType(),key).GetValue(value);
        private static void Layer(Transform root){root.gameObject.layer=30;foreach(Transform child in root)Layer(child);}
        private static GameObject Prefab<T>() where T:Component
        {
            return Resources.FindObjectsOfTypeAll<ProjectContentManifest>()
                .Where(m=>m.UIWindowRegistry!=null).SelectMany(m=>m.UIWindowRegistry.Registrations)
                .Select(r=>r.WindowPrefab).FirstOrDefault(p=>p!=null&&p.GetComponent<T>()!=null);
        }
        internal static JObject OpeningHandHud(CardTableViewModel model,System.Collections.Generic.IReadOnlyList<ActionCardViewModel> actions,string school,string directory)
        {
            // Inactive parent prevents the cloned HUD from binding live input callbacks.
            var root=new GameObject("YixianOpeningHandHudTest",typeof(RectTransform));root.SetActive(false);
            try
            {
                var prefab=Prefab<GameHUDWindow>();Require(prefab!=null,"opening HUD prefab missing");
                var hud=UnityEngine.Object.Instantiate(prefab,root.transform,false).GetComponent<GameHUDWindow>();
                var summary=Field<TMP_Text>(hud,"cardTableSummary");var rect=summary.rectTransform;
                var original=new[]{rect.anchorMin,rect.anchorMax,rect.pivot,rect.anchoredPosition,rect.sizeDelta};
                var layoutRects=new[]{"cardDrawButton","cardPackButton","cardFateButton"}.Select(n=>(RectTransform)Field<Button>(hud,n).transform)
                    .Concat(new[]{"cardDrawButton","cardPackButton","cardFateButton"}.Select(n=>Field<Button>(hud,n).GetComponentInChildren<TMP_Text>(true).rectTransform))
                    .Concat(new[]{Field<RectTransform>(hud,"cardHandContent"),(RectTransform)Field<Button>(hud,"endTurnButton").transform.parent}).ToArray();
                var layoutOriginal=layoutRects.Select(RectValues).ToArray();
                var nativeControls=layoutRects.Take(3).Select(r=>new UiRectSnapshot(r)).ToArray();int count=model.Hand.Count;
                float font=summary.fontSize;var alignment=summary.alignment;var overflow=summary.overflowMode;
                hud.SetBattleActive(true);hud.SetCardTable(model);hud.SetActions(actions);
                var abilities=Field<System.Collections.Generic.List<ActionCardViewModel>>(hud,"abilityActions");
                Require(!abilities.Any(a=>SectRegistry.Cards.ContainsKey(a.CommandActionId)),"actual HUD puts sect hand in Abilities tab");
                var hand=Field<System.Collections.Generic.List<CardTableHandCard>>(hud,"handCards");
                Require(hand.Count(c=>c.gameObject.activeSelf)==count,"actual HUD hand count differs");
                var expected=actions.Where(a=>a.ActionKind==CombatActionKind.UseAbility&&!BattleUIPresentationMapper.IsCardTableCommand(a)&&a.CommandActionId!=BattleAuthorityService.GeneLockActionId.Value);
                Require(abilities.Select(a=>a.StableId).SequenceEqual(expected.Select(a=>a.StableId)),"HUD lost unrelated abilities");
                var result=new JObject{["school"]=school,["cards_in_card_tab"]=count,["sect_cards_in_abilities"]=0,["actual_hud_prefab"]=true};
                CaptureOpeningHud(root,hud,hand,directory,result,nativeControls);
                var held=hand[0];var heldModel=held.Model;string sectId=heldModel.CardId;
                try
                {
                    heldModel.CardId="ability.lineage.BL08.card.test";
                    held.Present(heldModel,Field<CardTableArtSet>(hud,"cardTableArt"),true);
                    var nativeArt=Field<Image>(Field<CardTableHandCard>(hud,"cardHandPrefab"),"art");
                    Require(RectValues(Field<Image>(held,"art").rectTransform).SequenceEqual(RectValues(nativeArt.rectTransform))&&Field<Image>(held,"art").preserveAspect==nativeArt.preserveAspect,"illustration layout leaked into pooled native card");
                }
                finally{heldModel.CardId=sectId;}
                hud.SetCardTable(new CardTableViewModel());
                Require(original.SequenceEqual(new[]{rect.anchorMin,rect.anchorMax,rect.pivot,rect.anchoredPosition,rect.sizeDelta})&&summary.fontSize==font&&summary.alignment==alignment&&summary.overflowMode==overflow,"sect header layout leaked into non-sect HUD");
                Require(hud.GetComponentsInChildren<SectHandHoverFilter>(true).All(f=>!f.Active),"hand hover exclusion leaked into native HUD");
                result["native_layout_restored"]=true;
                for(int i=0;i<layoutRects.Length;i++)Require(RectValues(layoutRects[i]).SequenceEqual(layoutOriginal[i]),"card control/hand layout leaked into native HUD");
                result["native_card_art_restored"]=true;
                return result;
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CaptureOpeningHud(GameObject root,GameHUDWindow hud,System.Collections.Generic.List<CardTableHandCard> hand,string directory,JObject result,UiRectSnapshot[] nativeControls)
        {
            // Render the shipped HUD without opening a live window or binding an
            // intent sink. Disabled HUD and raycasters keep this synchronous
            // capture isolated from the player's input and run.
            hud.enabled=false;
            var nativeScaler=UnityEngine.Object.FindObjectsOfType<CanvasScaler>().FirstOrDefault(c=>c.gameObject.scene.IsValid()&&c.isActiveAndEnabled&&c.GetComponent<Canvas>()?.isRootCanvas==true);
            Require(nativeScaler!=null,"native root CanvasScaler missing");
            var referenceResolution=nativeScaler.referenceResolution;
            var canvas=root.AddComponent<Canvas>();var scaler=root.AddComponent<CanvasScaler>();
            result["native_canvas_reference"]=new JObject{["name"]=nativeScaler.name,["x"]=referenceResolution.x,["y"]=referenceResolution.y};
            var cameraObject=new GameObject("YixianOpeningHudCamera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.07f,.06f);
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;camera.aspect=1600f/900;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=referenceResolution;scaler.matchWidthOrHeight=nativeScaler.matchWidthOrHeight;
            try
            {
                foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))raycaster.enabled=false;
                hud.SetBattleActive(true);hud.SetBattleContext(1,1,1);hud.SetInteractionAvailability(true,true,false,"");hud.SelectTab(GameHudTab.Cards);
                Layer(root.transform);root.SetActive(true);
                Require(Field<Button>(hud,"cardTabButton").gameObject.activeInHierarchy,"Cards tab button not visible in battle HUD");
                Require(Field<GameObject>(hud,"cardPage").activeInHierarchy&&hand.Count(c=>c.gameObject.activeInHierarchy)==(int)result["cards_in_card_tab"],"hand cards not visible in rendered Cards tab");
                Require(!Field<GameObject>(hud,"abilityPage").activeInHierarchy,"ability page overlaps card page");
                Capture(camera,texture,root,Path.Combine(directory,"opening-cards.png"));
                result["native_controls"]=JArray.FromObject(nativeControls.Select(r=>RectEvidence(r.BoundsIn((RectTransform)root.transform))));
                File.WriteAllText(Path.Combine(directory,"native-controls.json"),result["native_controls"].ToString());
                CheckHandHeader(root,hud,directory,result,nativeControls:nativeControls);
                var viewports=new JArray();float originalScale=canvas.scaleFactor;scaler.enabled=false;
                foreach(var resolution in new[]{new Vector2Int(1280,720),new Vector2Int(1280,960),new Vector2Int(2560,1080)})
                {
                    var alternate=new RenderTexture(resolution.x,resolution.y,24);
                    try
                    {
                        camera.targetTexture=alternate;camera.aspect=resolution.x/(float)resolution.y;
                        canvas.scaleFactor=Mathf.Pow(resolution.x/referenceResolution.x,1-scaler.matchWidthOrHeight)*Mathf.Pow(resolution.y/referenceResolution.y,scaler.matchWidthOrHeight);
                        camera.Render();
                        string suffix=resolution.x+"x"+resolution.y;
                        Capture(camera,alternate,root,Path.Combine(directory,"opening-cards-"+suffix+".png"));
                        Require(Mathf.Abs(((RectTransform)root.transform).rect.width/((RectTransform)root.transform).rect.height-resolution.x/(float)resolution.y)<.01f,"HUD canvas aspect did not follow camera viewport");
                        Require(Mathf.Abs(canvas.pixelRect.width-resolution.x)<1&&Mathf.Abs(canvas.pixelRect.height-resolution.y)<1,"viewport fixture did not resize its actual canvas");
                        var check=new JObject{["pixels"]=suffix};
                        CheckHandHeader(root,hud,directory,check,"header-layout-"+suffix+".json",nativeControls);viewports.Add(check);
                        if(resolution.x==1280&&resolution.y==960){
                            hud.SetInteractionAvailability(true,false,false,"",true);
                            Capture(camera,alternate,root,Path.Combine(directory,"cancel-turn-"+suffix+".png"));
                            CheckHandHeader(root,hud,directory,new JObject(),"cancel-turn-"+suffix+".json",nativeControls);
                            hud.SetInteractionAvailability(true,true,false,"");
                        }
                    }
                    finally{camera.targetTexture=texture;camera.aspect=1600f/900;alternate.Release();UnityEngine.Object.DestroyImmediate(alternate);}
                }
                canvas.scaleFactor=originalScale;scaler.enabled=true;result["additional_viewports"]=viewports;
                hud.SelectTab(GameHudTab.Ability);
                Require(Field<GameObject>(hud,"abilityPage").activeInHierarchy&&!Field<GameObject>(hud,"cardPage").activeInHierarchy,"ability tab did not hide card page");
                Capture(camera,texture,root,Path.Combine(directory,"opening-abilities.png"));
                result["rendered_cards"]="opening-cards.png";result["rendered_abilities"]="opening-abilities.png";
                result["scope"]="shipped HUD, native canvas reference, full turn bar, native geometry, scoped GraphicRaycaster and PointerClick; isolated camera, no live intent sink or campaign";
            }
            finally
            {
                root.SetActive(false);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        private static Rect HudRect(RectTransform root,RectTransform item)
        {
            var corners=new Vector3[4];item.GetWorldCorners(corners);
            var points=corners.Select(p=>root.InverseTransformPoint(p)).ToArray();
            return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
        }
        private static Vector2[] RectValues(RectTransform rect)=>new[]{rect.anchorMin,rect.anchorMax,rect.pivot,rect.anchoredPosition,rect.sizeDelta};
        private static JObject RectEvidence(Rect rect)=>new JObject{["x"]=rect.x,["y"]=rect.y,["width"]=rect.width,["height"]=rect.height};
        private static Rect HudTextRect(RectTransform root,TMP_Text text)
        {
            var bounds=text.textBounds;
            var a=root.InverseTransformPoint(text.transform.TransformPoint(bounds.min));
            var b=root.InverseTransformPoint(text.transform.TransformPoint(bounds.max));
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        }
        private static void CheckHandHeader(GameObject root,GameHUDWindow hud,string directory,JObject result,string filename="header-layout.json",UiRectSnapshot[] nativeControls=null)
        {
            var canvasRect=(RectTransform)root.transform;var summary=Field<TMP_Text>(hud,"cardTableSummary");
            var header=HudRect(canvasRect,summary.rectTransform);
            var tabs=new JArray();bool overlap=false;float tabTop=float.MinValue;
            foreach(string name in new[]{"actionTabButton","abilityTabButton","inventoryTabButton","customTabButton","cardTabButton"})
            {
                var button=Field<Button>(hud,name);if(button==null||!button.gameObject.activeInHierarchy)continue;
                var bounds=HudRect(canvasRect,(RectTransform)button.transform);
                overlap|=header.Overlaps(bounds);tabTop=Math.Max(tabTop,bounds.yMax);
                tabs.Add(new JObject{["name"]=name,["bounds"]=RectEvidence(bounds)});
            }
            var guides=Field<Transform>(hud,"battleView").Find("FormationGuides");
            Require(guides!=null,"formation guide fixture missing");
            var ink=HudTextRect(canvasRect,summary);var guideEvidence=new JArray();bool guideOverlap=false;
            foreach(var label in guides.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy&&!string.IsNullOrEmpty(t.text)))
            {
                var bounds=HudTextRect(canvasRect,label);guideOverlap|=ink.Overlaps(bounds);
                guideEvidence.Add(new JObject{["text"]=label.text,["ink"]=RectEvidence(bounds)});
            }
            Require(guideEvidence.Count>0,"formation labels were not rendered");
            var evidence=new JObject{["header"]=RectEvidence(header),["header_ink"]=RectEvidence(ink),["canvas"]=RectEvidence(canvasRect.rect),["tabs"]=tabs,["formation_labels"]=guideEvidence,["text"]=summary.text,["font_size"]=summary.fontSize,["overflow"]=summary.isTextOverflowing,["overlaps_tabs"]=overlap,["overlaps_formation_text"]=guideOverlap};
            var controls=new JArray();bool clippedControl=false,controlOverHand=false;
            var held=Field<System.Collections.Generic.List<CardTableHandCard>>(hud,"handCards").Where(c=>c.gameObject.activeInHierarchy).ToArray();
            evidence["hand_content"]=RectEvidence(HudRect(canvasRect,Field<RectTransform>(hud,"cardHandContent")));
            evidence["hand_cards"]=new JArray(held.Select(c=>new JObject{
                ["card"]=c.Model.CardId,["bounds"]=RectEvidence(HudRect(canvasRect,(RectTransform)c.transform)),
                ["art"]=RectEvidence(HudRect(canvasRect,Field<Image>(c,"art").rectTransform)),
                ["visual"]=RectEvidence(HudRect(canvasRect,Field<RectTransform>(c,"visual")))}));
            var illustrations=new JArray();
            foreach(var card in held)
            {
                var image=Field<Image>(card,"art");var source=Field<CardTableArtSet>(hud,"cardTableArt").Find(card.Model.ArtKey);
                Require(source!=null&&image.sprite!=null&&image.sprite!=source&&image.sprite.texture==source.texture,"card does not use a display-only illustration crop");
                var full=source.rect;var crop=image.sprite.rect;var area=image.rectTransform.rect;var cardArea=((RectTransform)card.transform).rect;
                var illustration=new JObject{["card"]=card.Model.CardId,["image_area"]=RectEvidence(area),["card_area"]=RectEvidence(cardArea),["crop"]=RectEvidence(crop),["source"]=RectEvidence(full)};
                illustrations.Add(illustration);
                Require(crop.xMin>=full.x+full.width*74/308f-.01f&&crop.xMax<=full.x+full.width*282/308f+.01f&&crop.yMin>=full.y+full.height*224/508f-.01f&&crop.yMax<=full.y+full.height*474/508f+.01f,"illustration includes original printed name/cost/rules");
                Require(area.width>=cardArea.width*.9f&&area.height>=cardArea.height*.5f&&Mathf.Abs(crop.width/crop.height-area.width/area.height)<.01f,"illustration does not fill the upper card without distortion: "+illustration);
                Require(Field<TMP_Text>(card,"cost").text==(card.Model.Action.CommandCost?.Energy??0).ToString(),"new illustration hid or changed current energy cost");
            }
            evidence["illustrations"]=illustrations;evidence["illustrations_fill_upper_card"]=true;evidence["original_printed_values_excluded"]=true;
            foreach(string name in new[]{"cardDrawButton","cardPackButton","cardFateButton"})
            {
                var button=Field<Button>(hud,name);Require(button!=null&&button.gameObject.activeInHierarchy,"card control missing: "+name);
                var bounds=HudRect(canvasRect,(RectTransform)button.transform);
                bool visible=canvasRect.rect.Contains(bounds.min)&&canvasRect.rect.Contains(bounds.max);
                clippedControl|=!visible;controlOverHand|=held.Any(c=>bounds.Overlaps(HudRect(canvasRect,(RectTransform)c.transform)));
                var label=button.GetComponentInChildren<TMP_Text>(true);
                Require(!label.isTextOverflowing,"card control label clipped: "+name+"; label="+label.rectTransform.rect+"; button="+((RectTransform)button.transform).rect+"; preferred="+label.GetPreferredValues(label.text,label.rectTransform.rect.width,0)+"; font="+label.fontSize+"; margin="+label.margin+"; parentLayout="+string.Join(",",button.GetComponents<Component>().Select(c=>c.GetType().Name)));
                var table=Field<CardTableViewModel>(hud,"cardTable");
                if(name=="cardDrawButton")Require(button.IsInteractable()==table.DrawAction.IsAvailable,"draw control availability differs from its formal command");
                controls.Add(new JObject{["name"]=name,["bounds"]=RectEvidence(bounds),["visible"]=visible,["text"]=label.text,["interactable"]=button.IsInteractable(),["text_overflow"]=label.isTextOverflowing});
            }
            var endTurn=Field<Button>(hud,"endTurnButton");Require(endTurn.gameObject.activeInHierarchy,"end turn missing from HUD fixture");
            var turnBounds=HudRect(canvasRect,(RectTransform)endTurn.transform);
            evidence["end_turn"]=RectEvidence(turnBounds);
            Require(canvasRect.rect.Contains(turnBounds.min)&&canvasRect.rect.Contains(turnBounds.max),"end turn control clipped by viewport");
            bool turnOverlap=controls.OfType<JObject>().Any(c=>{var b=c["bounds"];return turnBounds.Overlaps(new Rect((float)b["x"],(float)b["y"],(float)b["width"],(float)b["height"]));});
            evidence["controls_overlap_end_turn"]=turnOverlap;
            if(nativeControls!=null){
                var original=nativeControls.Select(r=>r.BoundsIn(canvasRect)).ToArray();
                evidence["native_controls"]=new JArray(original.Select(RectEvidence));
                bool nativeSafe=original.All(r=>canvasRect.rect.Contains(r.min)&&canvasRect.rect.Contains(r.max)&&!r.Overlaps(turnBounds));
                // When the original controls fit, normal viewports must retain their positions/sizes.
                if(nativeSafe)for(int i=0;i<original.Length;i++){
                    var actual=HudRect(canvasRect,nativeControls[i].Rect);
                    Require(Mathf.Abs(original[i].x-actual.x)<.1f&&Mathf.Abs(original[i].width-actual.width)<.1f,"safe native card-control horizontal placement was changed; expected="+original[i]+" actual="+actual+" canvas="+canvasRect.rect+" turn="+turnBounds);
                    Require(actual.yMax<=original[i].yMax+.1f&&actual.height>=original[i].height-.1f,"native control row moved upward or shrank");
                    if(i==0)Require(Mathf.Abs(original[i].yMax-actual.yMax)<.1f,"safe native control column top changed");
                }
                evidence["safe_native_layout_preserved"]=nativeSafe;
            }
            evidence["turn_hierarchy"]=new JArray(endTurn.GetComponentsInParent<RectTransform>().Select(t=>new JObject{["name"]=t.name,["rect"]=t.rect.ToString(),["anchorMin"]=t.anchorMin.ToString(),["anchorMax"]=t.anchorMax.ToString(),["size"]=t.sizeDelta.ToString(),["position"]=t.anchoredPosition.ToString(),["scale"]=t.localScale.ToString()}));
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"geometry-"+filename),evidence.ToString());
            evidence["pointer_hits"]=CheckCardControlPointers(root,hud);
            evidence["controls"]=controls;evidence["clipped_controls"]=clippedControl;evidence["controls_overlap_hand"]=controlOverHand;
            result["header_layout"]=evidence;Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,filename),evidence.ToString());
            Require(summary.gameObject.activeInHierarchy&&summary.fontSize>=13&&!summary.isTextOverflowing,"hand summary hidden, unreadable or clipped");
            Require(!overlap&&header.yMin>=tabTop+7,"hand summary overlaps bottom tabs or has no separate header row");
            Require(!guideOverlap,"hand summary overlaps formation text");
            Require(canvasRect.rect.Contains(header.min)&&canvasRect.rect.Contains(header.max),"hand summary outside canvas");
            Require(!clippedControl&&!controlOverHand,"draw/pack controls clipped or overlapping hand");
            Require(!turnOverlap,"draw/pack controls overlap end turn");
            Require(held.All(c=>{var r=HudRect(canvasRect,(RectTransform)c.transform);return canvasRect.rect.Contains(r.min)&&canvasRect.rect.Contains(r.max);}),"hand card clipped by viewport");
        }
        private static JArray CheckCardControlPointers(GameObject root,GameHUDWindow hud)
        {
            var canvas=root.GetComponent<Canvas>();var raycaster=root.GetComponent<GraphicRaycaster>()??root.AddComponent<GraphicRaycaster>();raycaster.enabled=false;
            var result=new JArray();
            var hand=Field<RectTransform>(hud,"cardHandContent");var handCenter=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,hand.TransformPoint(hand.rect.center));
            foreach(var filter in root.GetComponentsInChildren<SectHandHoverFilter>(true))Require(filter.IsRaycastLocationValid(handCenter,canvas.worldCamera),"hand hover no longer accepts the fan center");
            foreach(string name in new[]{"cardDrawButton","cardPackButton","cardFateButton","endTurnButton"}){
                var button=Field<Button>(hud,name);var rect=(RectTransform)button.transform;
                var point=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.TransformPoint(rect.rect.center));
                var pointer=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
                var hits=new List<RaycastResult>();raycaster.Raycast(pointer,hits);
                var top=hits.OrderByDescending(h=>h.sortingOrder).ThenByDescending(h=>h.depth).FirstOrDefault();
                Require(top.gameObject!=null&&top.gameObject.GetComponentInParent<Button>()==button,"HUD control covered at pointer: "+name+"; top="+top.gameObject?.name+" point="+point+" rect="+HudRect((RectTransform)root.transform,rect)+" camera="+canvas.worldCamera.pixelRect+" screen="+Screen.width+"x"+Screen.height);
                bool clicked=false;
                UnityEngine.Events.UnityAction receive=()=>clicked=true;
                button.onClick.AddListener(receive);var scale=button.transform.localScale;
                try{if(button.IsInteractable()){ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);Require(clicked,"HUD pointer click not received: "+name);}}
                finally{button.onClick.RemoveListener(receive);button.transform.localScale=scale;}
                result.Add(new JObject{["control"]=name,["top_hit"]=top.gameObject.name,["click_received"]=clicked,["interactable"]=button.IsInteractable()});
            }
            return result;
        }
        internal static JObject Run(string directory,CardTableViewModel model,RunPlayerSnapshot player,RunAuthoritySnapshot snapshot,Reincarnation.Runtime.Managers.DataManager data,BattleAuthorityCatalog battles,string packId="yixian.sect.pack.yixian_sword_cloud2",bool foundation=true)
        {
            var root=new GameObject("YixianIsolatedVisualTest",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var cameraObject=new GameObject("YixianIsolatedVisualCamera");
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.07f,.06f);
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            var result=new JObject{["directory"]=directory};GameObject panelObject=null,shopObject=null;
            try
            {
                var hud=Prefab<GameHUDWindow>();Require(hud!=null,"HUD prefab missing from loaded manifest");
                var panelPrefab=Field<CardTablePackPanel>(hud.GetComponent<GameHUDWindow>(),"cardPackPrefab");Require(panelPrefab!=null,"pack panel prefab missing");
                var panel=UnityEngine.Object.Instantiate(panelPrefab,root.transform,false);panelObject=panel.gameObject;
                panel.Show(model,Field<CardTableArtSet>(hud.GetComponent<GameHUDWindow>(),"cardTableArt"),_=>{});Layer(root.transform);
                var wanted=model.Packs.First(p=>p.Id==packId);
                AccessTools.Method(typeof(CardTablePackPanel),"Preview").Invoke(panel,new object[]{wanted});
                var entries=Field<CardTableListEntry[]>(panel,"previewCards");
                int visibleCards=Math.Min(4,wanted.Cards.Count);
                Require(entries.Count(e=>e.gameObject.activeSelf)==visibleCards,"actual panel card count differs from selected pack");
                var label=Field<TMP_Text>(panel,"detail");
                for(int i=0;i<visibleCards;i++)
                {
                    entries[i].Clicked();
                    Require(label.text==wanted.Cards[i].Detail&&label.text.Contains("消耗"),"actual preview click lost full description");
                    var actual= wanted.Cards[i].Title+"\n"+(string)SectRegistry.Cards[wanted.Cards[i].CardId]["description"];
                    Require(label.text==actual,"actual prefab re-appended source/adaptation text");
                }
                entries[0].Clicked();Capture(camera,texture,root,Path.Combine(directory,"card-pack-preview.png"));
                if(wanted.Cards.Count==8)
                {
                    var next=panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="FiveDeckNextPage");
                    Require(next.interactable,"eight-card preview has no second page");next.onClick.Invoke();
                    for(int i=0;i<4;i++){entries[i].Clicked();Require(label.text==wanted.Cards[i+4].Detail,"second page preview uses wrong card");}
                    Capture(camera,texture,root,Path.Combine(directory,"card-pack-preview-page2.png"));
                    result["second_page"]="card-pack-preview-page2.png";
                }
                result["pack_preview"]="card-pack-preview.png";result["preview_clicks_checked"]=wanted.Cards.Count;
                UnityEngine.Object.DestroyImmediate(panelObject);panelObject=null;
                if(!foundation){result["scope"]="actual shipped pack prefab and preview callbacks; no physical mouse test";return result;}
                var shopPrefab=Prefab<ShopWindow>();Require(shopPrefab!=null,"shop prefab missing from loaded manifest");
                shopObject=UnityEngine.Object.Instantiate(shopPrefab,root.transform,false);var shop=shopObject.GetComponent<ShopWindow>();
                shop.SetContext(player,snapshot,data,battles);
                bool opened=(bool)AccessTools.Method(typeof(ShopWindow),"TryOpenFreePick").Invoke(shop,new object[]{new StableId(SwordRules.Lineage),2});
                Require(opened,"actual foundation choice dialog failed to open");
                Layer(root.transform);
                var labels=Field<TMP_Text[]>(shop,"freePickDescriptions");var options=Field<GameObject[]>(shop,"freePickOptionRoots");
                Require(options.Count(o=>o.activeSelf)==2,"foundation dialog does not have 2 options");
                for(int i=0;i<2;i++)Require(labels[i].text.Contains("消耗")&&labels[i].text.Contains("×筋肉"),"Sword foundation choice dialog has no cost/muscle formula");
                Capture(camera,texture,root,Path.Combine(directory,"foundation-choice.png"));
                result["foundation_choice"]="foundation-choice.png";result["choice_options_checked"]=2;
                result["scope"]="actual shipped prefabs, isolated camera; preview callbacks, not physical mouse hit testing";
                return result;
            }
            finally
            {
                if(panelObject!=null)UnityEngine.Object.DestroyImmediate(panelObject);
                if(shopObject!=null)UnityEngine.Object.DestroyImmediate(shopObject);
                UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        private static void Capture(Camera camera,RenderTexture texture,GameObject root,string path)
        {
            Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
            // This synchronous camera capture occurs inside Update; run the
            // production layout pass that normally follows in LateUpdate.
            foreach(var layout in root.GetComponentsInChildren<SectHandHeaderLayout>(true))layout.Apply();
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))if(text.gameObject.activeInHierarchy)text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();camera.Render();var before=RenderTexture.active;
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=before;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
