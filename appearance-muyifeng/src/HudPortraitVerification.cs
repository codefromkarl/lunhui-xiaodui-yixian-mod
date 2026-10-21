using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Reincarnation.Config;
using Reincarnation.Runtime.Content;
using Reincarnation.Domain;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace MuyifengAppearance
{
    internal sealed partial class RuntimeVerification
    {
        private void VerifyHudPortrait()
        {
            var root=new GameObject("IsolatedHudPortraitTest",typeof(RectTransform));root.SetActive(false);
            GameHUDWindow hud=null;Camera shot=null;RenderTexture target=null;
            try
            {
                var prefab=Resources.FindObjectsOfTypeAll<ProjectContentManifest>()
                    .Where(m=>m.UIWindowRegistry!=null).SelectMany(m=>m.UIWindowRegistry.Registrations)
                    .Select(r=>r.WindowPrefab).First(p=>p!=null&&p.GetComponent<GameHUDWindow>()!=null);
                hud=Instantiate(prefab,root.transform,false).GetComponent<GameHUDWindow>();hud.enabled=false;
                var go=new GameObject("HudPortraitShot");shot=go.AddComponent<Camera>();shot.enabled=false;
                shot.transform.position=new Vector3(20000,0,-20);shot.cullingMask=1<<30;
                shot.clearFlags=CameraClearFlags.SolidColor;shot.backgroundColor=new Color(.08f,.10f,.13f);
                var surface=root.AddComponent<Canvas>();surface.renderMode=RenderMode.ScreenSpaceCamera;surface.worldCamera=shot;surface.planeDistance=10;
                var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
                foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))raycaster.enabled=false;
                Layer(root.transform);root.SetActive(true);Canvas.ForceUpdateCanvases();
                var refresh=AccessTools.Method(typeof(GameHUDWindow),"RefreshPlayerPortrait");
                Action<BirthAppearance> look=a=>refresh.Invoke(hud,new object[]{new CombatantCardViewModel("hud-test","头像测试",null,100,100,0,10,10,"","",false,true,true,appearance:a)});
                look(BirthAppearance.Default);
                var rig=Entry.Read<GameObject>(hud,"playerPortraitRig");
                Require(rig!=null,"native HUD portrait rig missing");
                var portraitCamera=rig.GetComponentInChildren<Camera>(true);
                var position=portraitCamera.transform.localPosition;float size=portraitCamera.orthographicSize,aspect=portraitCamera.aspect;
                var raw=Entry.Read<RawImage>(hud,"playerPortraitRenderImage");var rect=raw.rectTransform;
                var originalRect=new[]{rect.anchorMin,rect.anchorMax,rect.pivot,rect.anchoredPosition,rect.sizeDelta};
                foreach(var resolution in new[]{new Vector2Int(1600,900),new Vector2Int(1280,960),new Vector2Int(2560,1080)})
                {
                    target=new RenderTexture(resolution.x,resolution.y,24);shot.targetTexture=target;shot.aspect=resolution.x/(float)resolution.y;
                    scaler.enabled=false;surface.scaleFactor=resolution.y/1080f;Canvas.ForceUpdateCanvases();
                    look(MuyifengChoice.Token);
                    SaveHudPortrait(raw,Path.Combine(output,"hud-portrait-"+resolution.x+"x"+resolution.y+".png"));
                    shot.Render();SaveHudTexture(target,Path.Combine(output,"hud-frame-"+resolution.x+"x"+resolution.y+".png"));
                    Case("hud_head_framing_"+resolution.x+"x"+resolution.y,()=>{
                        var head=rig.GetComponentsInChildren<SpriteRenderer>(true).First(r=>r.enabled&&r.sprite!=null&&r.sprite.name.EndsWith("/Body/Head",StringComparison.Ordinal));
                        var min=portraitCamera.WorldToViewportPoint(head.bounds.min);var max=portraitCamera.WorldToViewportPoint(head.bounds.max);
                        Require(max.y-min.y>=.60f,"HUD still renders a tiny full-body figure instead of a head portrait: "+(max.y-min.y));
                        Require(min.x>=0&&max.x<=1&&min.y>=0&&max.y<=1,"HUD clips face/hair");
                        Require(Mathf.Abs(portraitCamera.aspect-rect.rect.width/rect.rect.height)<.001f,"portrait projection would stretch the face");
                        Require(originalRect.SequenceEqual(new[]{rect.anchorMin,rect.anchorMax,rect.pivot,rect.anchoredPosition,rect.sizeDelta}),"portrait fix resized native HUD frame");
                    });
                    var selectedPosition=portraitCamera.transform.localPosition;float selectedSize=portraitCamera.orthographicSize;
                    for(int i=0;i<10;i++)look(MuyifengChoice.Token);
                    Case("hud_repeat_no_zoom_drift_"+resolution.x,()=>Require(Vector3.Distance(selectedPosition,portraitCamera.transform.localPosition)<.00001f&&Mathf.Abs(selectedSize-portraitCamera.orthographicSize)<.00001f,"repeated refresh accumulates zoom"));
                    look(BirthAppearance.Default);
                    Case("hud_native_camera_restored_"+resolution.x,()=>Require(Vector3.Distance(position,portraitCamera.transform.localPosition)<.00001f&&Mathf.Abs(size-portraitCamera.orthographicSize)<.00001f&&Mathf.Abs(aspect-portraitCamera.aspect)<.00001f,"native character inherited portrait zoom"));
                    look(MuyifengChoice.Token);refresh.Invoke(hud,new object[]{null});
                    Case("hud_empty_restores_"+resolution.x,()=>Require(!raw.enabled&&Vector3.Distance(position,portraitCamera.transform.localPosition)<.00001f&&Mathf.Abs(size-portraitCamera.orthographicSize)<.00001f,"empty HUD retained skin camera state"));
                    shot.targetTexture=null;target.Release();DestroyImmediate(target);target=null;
                }
                target=new RenderTexture(1600,900,24);shot.targetTexture=target;shot.aspect=1600f/900;
                surface.scaleFactor=900f/1080;Canvas.ForceUpdateCanvases();
                foreach(var choice in AppearanceChoices.All)
                {
                    look(choice.Token);
                    shot.Render();SaveHudTexture(target,Path.Combine(output,"hud-character-"+choice.Id+".png"));
                    Case("hud_character_head_"+choice.Id,()=>{
                        var head=rig.GetComponentsInChildren<SpriteRenderer>(true).First(r=>r.enabled&&r.sprite!=null&&r.sprite.name.EndsWith("/Body/Head",StringComparison.Ordinal));
                        var min=portraitCamera.WorldToViewportPoint(head.bounds.min);var max=portraitCamera.WorldToViewportPoint(head.bounds.max);
                        Require(min.x>=0&&max.x<=1&&min.y>=0&&max.y<=1,"character portrait clips face/hair: "+choice.Name);
                        Require(max.y-min.y>.5f,"character head remains too small: "+choice.Name);
                    });
                }
                Case("hud_world_model_unaffected",()=>Require(HasSkin(hero)&&HasSword(hero)&&hero.GetComponentsInChildren<HudPortraitCameraState>(true).Length==0,"portrait zoom modified world actor"));
            }
            finally
            {
                if(hud!=null)AccessTools.Method(typeof(GameHUDWindow),"ReleasePlayerPortraitRig").Invoke(hud,null);
                if(shot!=null){shot.targetTexture=null;DestroyImmediate(shot.gameObject);}
                if(target!=null){target.Release();DestroyImmediate(target);}
                DestroyImmediate(root);
            }
        }
        private static void SaveHudPortrait(RawImage image,string path)=>SaveHudTexture((RenderTexture)image.texture,path);
        private static void SaveHudTexture(RenderTexture target,string path)
        {
            var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally{RenderTexture.active=previous;DestroyImmediate(texture);}
        }
    }
}
