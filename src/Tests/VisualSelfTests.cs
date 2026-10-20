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
                Require(entries.Count(e=>e.gameObject.activeSelf)==4,"actual panel not showing exactly 4 cards");
                var label=Field<TMP_Text>(panel,"detail");
                for(int i=0;i<4;i++)
                {
                    entries[i].Clicked();
                    Require(label.text==wanted.Cards[i].Detail&&label.text.Contains("消耗"),"actual preview click lost full description");
                    var actual= wanted.Cards[i].Title+"\n"+(string)SectRegistry.Cards[wanted.Cards[i].CardId]["description"];
                    Require(label.text==actual,"actual prefab re-appended source/adaptation text");
                }
                entries[0].Clicked();Capture(camera,texture,root,Path.Combine(directory,"card-pack-preview.png"));
                result["pack_preview"]="card-pack-preview.png";result["preview_clicks_checked"]=4;
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
                for(int i=0;i<2;i++)Require(labels[i].text.Contains("消耗")&&labels[i].text.Contains("×能量"),"choice dialog has no cost/formula");
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
            Canvas.ForceUpdateCanvases();
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))if(text.gameObject.activeInHierarchy)text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();camera.Render();var before=RenderTexture.active;
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=before;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
