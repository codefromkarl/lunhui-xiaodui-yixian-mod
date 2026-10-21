using System;
using System.Linq;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.Flow;
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
        internal static JObject FiveEditorControls(CardTableViewModel model)
        {
            var root=new GameObject("FiveEditorTest",typeof(RectTransform),typeof(Canvas));
            try
            {
                var hud=Prefab<GameHUDWindow>();var panel=UnityEngine.Object.Instantiate(Field<CardTablePackPanel>(hud.GetComponent<GameHUDWindow>(),"cardPackPrefab"),root.transform,false);
                foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))raycaster.enabled=false;
                int submits=0;System.Collections.Generic.IReadOnlyList<string> submitted=null;
                panel.Show(model,Field<CardTableArtSet>(hud.GetComponent<GameHUDWindow>(),"cardTableArt"),ids=>{submits++;submitted=ids;});
                var edit=panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="FiveEditDeck");Require(edit.interactable,"battle edit button unavailable");edit.onClick.Invoke();
                var overlay=panel.transform.Find("FiveDeckEditor");var buttons=overlay.GetComponentsInChildren<Button>(true);
                Require(buttons.Count(b=>b.name.StartsWith("Pick_",StringComparison.Ordinal))==16,"editor must show sixteen unlocked cards");
                var current=model.Packs.Single(p=>p.Id==FiveDeckRules.PackId.Value).Cards.Select(c=>new StableId(c.CardId)).ToArray();
                var confirm=buttons.Single(b=>b.name=="ConfirmCards");Require(confirm.interactable,"existing eight not selected");
                buttons.Single(b=>b.name=="Pick_"+SectRegistry.Cards[current[0].Value]["key"]).onClick.Invoke();Require(!confirm.interactable,"seven-card draft may be saved");
                var add=buttons.First(b=>b.name.StartsWith("Pick_",StringComparison.Ordinal)&&!current.Any(id=>b.name=="Pick_"+SectRegistry.Cards[id.Value]["key"]));add.onClick.Invoke();
                Require(confirm.interactable&&submits==0,"draft changed authority before save");confirm.onClick.Invoke();
                Require(submits==1&&submitted.Count==1&&FiveDeckRules.TrySelection(new StableId(submitted[0]),out var saved)&&saved.Count==8,"editor did not submit exactly one native-compatible eight-card answer");
                return new JObject{["unlocked_rows"]=16,["exact_eight_required"]=true,["staged_only"]=true,["native_callback_submits"]=1,["physical_mouse_test"]=false};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        internal static JObject FiveUpgradeControls(string directory,RunPlayerSnapshot player,RunAuthoritySnapshot snapshot,DataManager data,BattleAuthorityCatalog battles)
        {
            if(Application.isBatchMode)return new JObject{["status"]="not_run",["reason"]="headless probe; run the graphical suite for upgrade-panel acceptance"};
            var root=new GameObject("FiveUpgradeVisualTest",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var cameraObject=new GameObject("FiveUpgradeCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);
            try
            {
                var source=Prefab<ShopWindow>();Require(source!=null,"five upgrade shop prefab missing");
                var shopObject=UnityEngine.Object.Instantiate(source,root.transform,false);var shop=shopObject.GetComponent<ShopWindow>();shop.SetContext(player,snapshot,data,battles);
                var card=RunUIPresentationMapper.BuildLineages(snapshot,player,data).Single(c=>c.StableId==FiveElementRules.Lineage);
                AccessTools.Field(typeof(ShopWindow),"currentProgressionCard").SetValue(shop,card);
                AccessTools.Field(typeof(ShopWindow),"currentLadderStep").SetValue(shop,new ExchangeLadderStepViewModel(2,"筑基",55,"","",false,true));
                Require((bool)AccessTools.Method(typeof(ShopWindow),"TryOpenFreePick").Invoke(shop,new object[]{new StableId(FiveElementRules.Lineage),2}),"five replacement panel did not open");
                var overlay=shop.transform.Find("FiveDeckUpgrade");Require(overlay!=null,"replacement overlay missing");
                var buttons=overlay.GetComponentsInChildren<Button>(true);
                Require(buttons.Count(b=>b.name.StartsWith("Pick_",StringComparison.Ordinal))==8,"wrong current/candidate card inventory");
                var confirm=buttons.Single(b=>b.name=="ConfirmCards");Require(!confirm.interactable,"incomplete replacement can be submitted");
                foreach(var b in buttons.Where(b=>b.name.StartsWith("Pick_",StringComparison.Ordinal)).Take(2))b.onClick.Invoke();
                Require(confirm.interactable,"two unlocks do not enable review");
                Require(overlay.GetComponentsInChildren<TMP_Text>().Single(t=>t.name=="CardDetail").text.Contains("消耗"),"replacement card detail missing effects/cost");
                Layer(root.transform);Capture(camera,texture,root,Path.Combine(directory,"five-upgrade.png"));
                foreach(var text in overlay.GetComponentsInChildren<TMP_Text>().Where(t=>t.name=="Label"||t.name=="Title"||t.name=="Progress"))Require(!text.isTextOverflowing,"five upgrade label overflow: "+text.text);
                buttons.Single(b=>b.name=="Cancel").onClick.Invoke();
                return new JObject{["new_candidates"]=8,["two_unlock_review"]=true,["cancel_no_submission"]=true,["image"]="five-upgrade.png",["physical_mouse_test"]=false};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
