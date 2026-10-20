using System;
using System.IO;
using System.Linq;
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
        internal static JObject SwordRecommendations(string directory,CardTableViewModel model)
        {
            var root=new GameObject("SwordBuildVisualTest",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var camObject=new GameObject("SwordBuildCamera");var camera=camObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.03f,.05f);
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            GameObject instance=null;
            try
            {
                var hud=Prefab<GameHUDWindow>();Require(hud!=null,"recommendation HUD prefab");
                var source=Field<CardTablePackPanel>(hud.GetComponent<GameHUDWindow>(),"cardPackPrefab");
                var panel=UnityEngine.Object.Instantiate(source,root.transform,false);instance=panel.gameObject;
                int submits=0;System.Collections.Generic.IReadOnlyList<string> submitted=null;
                panel.Show(model,Field<CardTableArtSet>(hud.GetComponent<GameHUDWindow>(),"cardTableArt"),ids=>{submits++;submitted=ids;});
                Require(Field<TMP_Text>(panel,"fusionTabLabel").text=="推荐构筑","recommendation tab not shown");
                Field<Button>(panel,"fusionTab").onClick.Invoke();Layer(root.transform);
                Capture(camera,texture,root,Path.Combine(directory,"recommended-builds.png"));
                var labels=panel.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.name=="RouteDetails").ToArray();
                Require(labels.Length==3,"three route panels missing");
                foreach(var label in labels)Require(label.preferredHeight<=label.rectTransform.rect.height+2,"route text overflows: "+label.preferredHeight+" / "+label.rectTransform.rect.height);
                foreach(var route in SwordBuilds.Routes)
                {
                    Field<Button>(panel,"fusionTab").onClick.Invoke();
                    var button=panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Use"+route.Key);button.onClick.Invoke();
                    Require(submits==0,"recommendation submitted immediately");
                    var selected=Field<System.Collections.Generic.HashSet<string>>(panel,"selected");
                    var expected=SwordBuilds.Recommend(route,5,model.Packs.Where(p=>!p.IsLocked).Select(p=>new StableId(p.Id))).Selected;
                    Require(selected.SetEquals(expected.Select(id=>id.Value)),"button picked wrong proposal");
                }
                Layer(root.transform);Capture(camera,texture,root,Path.Combine(directory,"pending-build.png"));
                Field<Button>(panel,"confirm").onClick.Invoke();Require(submits==1&&submitted.Count==3,"native confirmation did not submit exactly once");
                return new JObject{["routes"]=3,["staging_callbacks"]=3,["native_confirm_submits"]=1,["screenshots"]=new JArray("recommended-builds.png","pending-build.png"),["physical_mouse_test"]=false};
            }
            finally
            {
                if(instance!=null)UnityEngine.Object.DestroyImmediate(instance);UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(camObject);texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
