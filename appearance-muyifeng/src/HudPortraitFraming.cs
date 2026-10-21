using System;
using System.Linq;
using Reincarnation.UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace MuyifengAppearance
{
    // The HUD has its own offscreen rig. Never zoom the world/creation/party rigs.
    internal static class HudPortraitFraming
    {
        internal static void Restore(GameHUDWindow hud)
        {
            var rig=Entry.Read<GameObject>(hud,"playerPortraitRig");
            rig?.GetComponentInChildren<HudPortraitCameraState>(true)?.Restore();
        }

        internal static bool Apply(GameHUDWindow hud)
        {
            var rig=Entry.Read<GameObject>(hud,"playerPortraitRig");
            var skin=rig?.GetComponentsInChildren<SkinState>(true).FirstOrDefault(s=>s.Selected);
            var camera=rig?.GetComponentInChildren<Camera>(true);
            var image=Entry.Read<RawImage>(hud,"playerPortraitRenderImage");
            if(skin==null||camera==null||image==null)return false;
            skin.SyncRigPose();
            var head=skin.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r=>
                r.enabled&&r.gameObject.activeInHierarchy&&r.sprite!=null&&
                r.sprite.name.EndsWith("/Body/Head",StringComparison.Ordinal));
            if(head==null)return false;
            var state=camera.GetComponent<HudPortraitCameraState>()??camera.gameObject.AddComponent<HudPortraitCameraState>();
            state.Capture(camera);
            // Match projection to the actual display rectangle, not the native
            // 336x448 texture: RawImage otherwise stretches that texture to fit.
            var rect=image.rectTransform.rect;
            float aspect=rect.width>0&&rect.height>0?rect.width/rect.height:camera.aspect;
            var bounds=head.bounds;
            float halfHeight=Mathf.Max(bounds.size.y*.64f,bounds.size.x*.56f/aspect);
            var center=bounds.center-Vector3.up*bounds.size.y*.09f;
            camera.aspect=aspect;
            camera.orthographicSize=halfHeight;
            camera.transform.position=new Vector3(center.x,center.y,camera.transform.position.z);
            camera.Render();
            return true;
        }
    }

    internal sealed class HudPortraitCameraState : MonoBehaviour
    {
        private Camera camera;
        private Vector3 localPosition;
        private float size,aspect;
        internal void Capture(Camera value)
        {
            if(camera!=null)return;
            camera=value;localPosition=value.transform.localPosition;
            size=value.orthographicSize;aspect=value.aspect;
        }
        internal void Restore()
        {
            if(camera==null)return;
            camera.transform.localPosition=localPosition;
            camera.orthographicSize=size;camera.aspect=aspect;
        }
    }
}
