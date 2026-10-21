using System;
using System.Linq;
using Reincarnation.Gameplay.Combat;
using UnityEngine;

namespace MuyifengAppearance
{
    internal sealed partial class RuntimeVerification
    {
        private static bool OpaqueAt(SpriteRenderer renderer,Vector3 world)
        {
            var sprite=renderer.sprite;var local=renderer.transform.InverseTransformPoint(world);
            if(renderer.flipX)local.x=-local.x;if(renderer.flipY)local.y=-local.y;
            float x=local.x*sprite.pixelsPerUnit+sprite.pivot.x,y=local.y*sprite.pixelsPerUnit+sprite.pivot.y;
            if(x<0||y<0||x>=sprite.rect.width||y>=sprite.rect.height)return false;
            return sprite.texture.GetPixel((int)(sprite.rect.x+x),(int)(sprite.rect.y+y)).a>=.5f;
        }
        private int VisibleOverlap(SpriteRenderer first,SpriteRenderer second,Vector3 junction)
        {
            var screen=camera.WorldToScreenPoint(junction);int count=0;
            for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++){
                var world=camera.ScreenToWorldPoint(screen+new Vector3(x*.2f,y*.2f,0));
                if(OpaqueAt(first,world)&&OpaqueAt(second,world))count++;
            }
            return count;
        }
        private void AssertVisibleRig(BattleCombatantView actor,AppearanceArt art,string phase)
        {
            var rig=art.AtlasDefinition["rig"];if((bool?)rig["calibrated"]!=true)return;
            var renderers=actor.GetComponentsInChildren<SpriteRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            SpriteRenderer Part(string key)=>renderers.Single(r=>r.sprite==art.Parts[key]);
            var head=Part("Body/Head");var torso=Part("Cloth/Body");
            var state=actor.GetComponent<SkinState>();var neck=state.Joints.Single(j=>j.Name=="neck").Target;
            int overlap=VisibleOverlap(head,torso,neck);
            Require(overlap>=4,"head/collar visible pixels do not overlap: "+art.Prefix+" / "+phase+" / "+overlap);
            int count=(int?)report["rig_visible_overlap_checks"]??0;report["rig_visible_overlap_checks"]=count+1;
            report["rig_min_visible_neck_overlap_samples"]=Math.Min((int?)report["rig_min_visible_neck_overlap_samples"]??25,overlap);
            foreach(string side in new[]{"Left","Right"}){
                var leg=rig["leg"+side];var ankle=state.Joints.Single(j=>j.Name==side+" ankle").Target;
                Require(VisibleOverlap(Part((string)leg["foot"]),Part((string)leg["pant"]),ankle)>=4,"visible trouser/boot gap: "+art.Prefix+" / "+side+" / "+phase);
                string handKey="Hand/"+((side=="Left"||rig["offhand"]!=null)?"Grip_":"Open_")+(side=="Left"?"L":"R");
                var wrist=state.Joints.Single(j=>j.Name==side+" wrist").Target;
                Require(VisibleOverlap(Part(handKey),Part("Cloth/"+side+"Lower"),wrist)>=4,"visible sleeve/hand gap: "+art.Prefix+" / "+side+" / "+phase);
            }
            if(phase!="DEATH")foreach(string side in new[]{"Left","Right"}){
                var leg=rig["leg"+side];string key=(string)leg["foot"];var foot=Part(key);
                var toe=foot.transform.TransformPoint(art.SourcePoint(key,leg["toe"]));var heel=foot.transform.TransformPoint(art.SourcePoint(key,leg["heel"]));
                Require(OpaqueAt(foot,toe)&&OpaqueAt(foot,heel),"foot landmarks miss visible toe/heel pixels: "+art.Prefix+" / "+side+" / "+phase);
                Require(Vector3.Dot(toe-heel,foot.transform.parent.TransformVector(Vector3.left))>0,"foot is reversed relative to its animated foot bone: "+art.Prefix+" / "+side+" / "+phase);
                if(phase=="IDLE"||phase=="MIRROR"||phase=="RESTORE")
                    Require(Vector3.Dot(toe-heel,head.transform.TransformVector(Vector3.left))>0,"standing foot points inward/backwards: "+art.Prefix+" / "+side+" / "+phase);
            }
            if(rig["back"]!=null){
                var rear=Part("HairBack");var body=Rows(actor).First(r=>r.PartType=="Body"&&r.Structure=="Body"&&r.renderer.gameObject.activeInHierarchy).renderer;
                Require(rear.transform.parent==body.transform,"shoulder mantle is still attached to the head");
            }
        }
    }
}
