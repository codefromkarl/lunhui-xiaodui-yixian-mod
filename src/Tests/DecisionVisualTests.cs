using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal static partial class VisualSelfTests
    {
        internal static JObject DecisionControls(string directory,CardTableViewModel sword,CardTableViewModel star)
        {
            Directory.CreateDirectory(directory);
            var root=new GameObject("DecisionControlsTest",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.SetActive(false);
            var cameraObject=new GameObject("DecisionControlsCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.07f,.06f);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;camera.aspect=1600f/900;
            var result=new JObject();
            try
            {
                var hud=Prefab<GameHUDWindow>().GetComponent<GameHUDWindow>();var font=Field<TMP_Text>(hud,"cardTableSummary").font;
                var candidates=FiveDeckRules.Candidates(2).Select(FiveDeckRules.CardId).ToArray();var draft=new FiveDeckDraft(candidates,Array.Empty<StableId>(),2);bool submitted=false;
                var picker=FiveCardPicker.Open(root.transform,"DepthLearning","五行突破 · 筑基","选择两张加入本局牌库；推荐只改变待选，确认后才解锁。","确认解锁与突破",draft,font,_=>submitted=true,2);
                Layer(root.transform);root.SetActive(true);Canvas.ForceUpdateCanvases();
                var recommend=picker.GetComponentsInChildren<Button>().Single(b=>b.name=="Recommend_0");recommend.onClick.Invoke();
                Require(draft.Ready&&!submitted,"recommendation submitted instead of drafting");
                Require(draft.Selected.Select(id=>(string)SectRegistry.Cards[id.Value]["name"]).SequenceEqual(FiveBuildGuidance.Routes[0].Learn[0]),"recommendation selected wrong rank cards");
                Capture(camera,texture,root,Path.Combine(directory,"five-learning-recommendation.png"));
                foreach(var text in picker.GetComponentsInChildren<Button>().SelectMany(b=>b.GetComponentsInChildren<TMP_Text>()))Require(!text.isTextOverflowing,"recommendation or picker button text overflows: "+text.text);
                var alternate=new RenderTexture(1280,960,24);
                try {camera.targetTexture=alternate;camera.aspect=1280f/960;Capture(camera,alternate,root,Path.Combine(directory,"five-learning-4x3.png"));}
                finally {camera.targetTexture=texture;camera.aspect=1600f/900;alternate.Release();UnityEngine.Object.DestroyImmediate(alternate);}
                picker.SetActive(false);UnityEngine.Object.DestroyImmediate(picker);
                var library=FiveDeckRules.Candidates(1).Select(FiveDeckRules.CardId).ToArray();var eight=new FiveDeckDraft(library,library,8);
                picker=FiveCardPicker.Open(root.transform,"DepthDeck","调整五行卡组","从已解锁牌库选择八张。保存保留手牌；下次抽牌使用新卡组。","保存八张卡组",eight,font,_=>submitted=true,3);
                Layer(picker.transform);picker.GetComponentsInChildren<Button>().Single(b=>b.name=="Recommend_2").onClick.Invoke();
                Require(eight.Ready&&!submitted&&eight.Selected.All(library.Contains),"deck recommendation changed ownership or submitted");
                Capture(camera,texture,root,Path.Combine(directory,"five-deck-transition.png"));picker.SetActive(false);UnityEngine.Object.DestroyImmediate(picker);
                foreach(var pair in new[]{(name:"sword",model:sword),(name:"star",model:star)})
                {
                    var panel=UnityEngine.Object.Instantiate(Field<CardTablePackPanel>(hud,"cardPackPrefab"),root.transform,false);
                    try
                    {
                        panel.Show(pair.model,Field<CardTableArtSet>(hud,"cardTableArt"),_=>submitted=true);
                        AccessTools.Method(typeof(CardTablePackPanel),"SelectPage").Invoke(panel,new object[]{true});Layer(panel.transform);
                        Capture(camera,texture,root,Path.Combine(directory,pair.name+"-recommendations.png"));
                        foreach(var image in panel.GetComponentsInChildren<Image>().Where(x=>new[]{"Route0","Route1","Route2"}.Contains(x.name)))
                        foreach(var tab in new[]{Field<Button>(panel,"packsTab"),Field<Button>(panel,"fusionTab")})
                            Require(!HudRect((RectTransform)root.transform,image.rectTransform).Overlaps(HudRect((RectTransform)root.transform,(RectTransform)tab.transform)),"route cards block native tab "+pair.name);
                        Require(panel.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("Use",StringComparison.Ordinal)&&b.gameObject.activeInHierarchy)==3,"three native proposal buttons missing "+pair.name);
                        foreach(var text in panel.GetComponentsInChildren<TMP_Text>())if(text.name=="RouteDetails")Require(!text.isTextOverflowing,"route explanation overflows "+pair.name);
                    }
                    finally{panel.gameObject.SetActive(false);UnityEngine.Object.DestroyImmediate(panel.gameObject);}
                }
                result["draft_only"]=!submitted;result["five_learning_exact_two"]=true;result["five_owned_eight"]=true;result["native_route_pages"]=2;
                return result;
            }
            finally{root.SetActive(false);UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
