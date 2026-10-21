using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.ExternalBridges.Spum;
using Reincarnation.UI.Nodes;
using UnityEngine;

namespace MuyifengAppearance
{
    internal sealed class MuyifengEquipmentPart : MonoBehaviour { }

    internal sealed partial class SkinState
    {
        internal sealed class JointLink
        {
            internal string Name;
            internal Vector3 Actual,Target;
        }
        private sealed class Piece
        {
            internal SpriteRenderer Source,View;
            internal string Key;
        }
        private sealed class Arm
        {
            internal string Side;
            internal SpriteRenderer Source,Cloth;
            internal Piece Sleeve,LowerSleeve,Hand;
            internal Vector3 Palm,Shoulder,Wrist;
        }
        private sealed class Leg
        {
            internal string Side;internal SpriteRenderer Source,Pant;internal Piece Foot,Cloth;
        }
        private sealed class Group
        {
            internal SpriteRenderer Body,Head,Cloth;
            internal Piece Torso,Face,Hair,Sword,Offhand;
            internal Arm Left,Right;
            internal Leg LegLeft,LegRight;
        }
        private readonly Dictionary<Tuple<SpriteRenderer,string>,Piece> rigPieces=new Dictionary<Tuple<SpriteRenderer,string>,Piece>();
        private readonly List<Group> rigGroups=new List<Group>();
        private readonly List<JointLink> jointLinks=new List<JointLink>();
        private bool RigEnabled=>Art.AtlasDefinition?["rig"] is JObject;
        internal IReadOnlyList<JointLink> Joints=>jointLinks;
        internal Sprite NativeSprite(SpriteRenderer renderer)=>originals.TryGetValue(renderer,out var value)?value.Sprite:renderer.sprite;
        private MatchingElement[] RigRows()=>GetComponentsInChildren<SPUM_MatchingList>(true).SelectMany(x=>x.matchingTables).Where(x=>x!=null&&x.renderer!=null&&x.UnitType=="Unit").GroupBy(x=>x.renderer).Select(g=>g.First()).ToArray();
        internal void BeforeNativeLook()
        {
            if(!Selected||!RigEnabled)return;
            DisableRig();
            foreach(var item in originals)if(item.Key!=null){item.Key.sprite=item.Value.Sprite;item.Key.color=item.Value.Color;}
            suppressed.Clear();
        }
        internal void AfterNativeLook()
        {
            if(RigEnabled)foreach(var row in RigRows())
                if(!Art.Parts.Values.Contains(row.renderer.sprite))originals[row.renderer]=new Original{Sprite=row.renderer.sprite,Color=row.renderer.color};
            Apply();
        }
        private Piece PieceFor(SpriteRenderer source,string slot,string key,Transform parent=null,bool equipment=false)
        {
            var id=Tuple.Create(source,slot);
            if(!rigPieces.TryGetValue(id,out var piece))
            {
                var go=new GameObject("MuyifengBinding/"+slot);go.transform.SetParent(parent??source.transform,false);go.layer=source.gameObject.layer;
                var renderer=go.AddComponent<SpriteRenderer>();
                if(equipment)go.AddComponent<MuyifengEquipmentPart>();
                piece=new Piece{Source=source,View=renderer};rigPieces.Add(id,piece);
            }
            piece.Key=key;piece.View.sprite=Art.Parts[key];
            return piece;
        }
        private bool ApplyRig()
        {
            var rows=RigRows();bool changed=false;
            foreach(var piece in rigPieces.Values)
                if(Art.Parts.TryGetValue(piece.Key,out var current)){changed|=piece.View.sprite!=current;piece.View.sprite=current;}
                else piece.View.enabled=false;
            foreach(var row in rows)
            {
                var r=row.renderer;
                if(!originals.ContainsKey(r)||(r.sprite!=null&&!Art.Parts.Values.Contains(r.sprite)))
                    originals[r]=new Original{Sprite=r.sprite,Color=r.color};
                bool hide=row.PartType=="Eye"||row.PartType=="Hair"||row.PartType=="FaceHair"
                    ||row.PartType=="Body"&&(row.Structure=="Body"||row.Structure=="Head"||row.Structure=="Arm_L"||row.Structure=="Arm_R"||row.Structure=="Foot_L"||row.Structure=="Foot_R")
                    ||row.PartType=="Pant"&&(row.Structure=="Left"||row.Structure=="Right")
                    ||row.PartType=="Cloth"&&(row.Structure=="Body"||row.Structure=="Left"||row.Structure=="Right")
                    ||row.PartType=="Weapons";
                if(hide){changed|=r.sprite!=null;r.sprite=null;suppressed.Add(r);continue;}
                string key=row.PartType+"/"+row.Structure;
                if(Art.Parts.TryGetValue(key,out var sprite)){changed|=r.sprite!=sprite;r.sprite=sprite;}
            }
            foreach(var row in rows.Where(x=>x.PartType=="Body"&&x.Structure=="Body"))
            {
                if(rigGroups.Any(g=>g.Body==row.renderer))continue;
                var root=row.renderer.transform.parent;
                SpriteRenderer Find(string type,string part)=>rows.FirstOrDefault(x=>x.PartType==type&&x.Structure==part&&x.renderer.transform.IsChildOf(root))?.renderer;
                var g=new Group{Body=row.renderer,Head=Find("Body","Head"),Cloth=Find("Cloth","Body")};
                var left=Find("Body","Arm_L");var right=Find("Body","Arm_R");var lc=Find("Cloth","Left");var rc=Find("Cloth","Right");
                if(g.Head==null||g.Cloth==null||left==null||right==null||lc==null||rc==null)continue;
                g.Torso=PieceFor(g.Cloth,"torso","Cloth/Body");g.Face=PieceFor(g.Head,"head","Body/Head");
                g.Hair=PieceFor(g.Head,"rear-hair","HairBack",g.Face.View.transform);
                g.Left=new Arm{Side="Left",Source=left,Cloth=lc,Sleeve=PieceFor(left,"sleeve","Cloth/Left"),LowerSleeve=PieceFor(left,"lower-sleeve","Cloth/LeftLower"),Hand=PieceFor(left,"hand","Hand/Grip_R")};
                g.Right=new Arm{Side="Right",Source=right,Cloth=rc,Sleeve=PieceFor(right,"sleeve","Cloth/Right"),LowerSleeve=PieceFor(right,"lower-sleeve","Cloth/RightLower"),Hand=PieceFor(right,"hand","Hand/Open_L")};
                var skeleton=root.parent.parent;
                Leg MakeLeg(string side){var data=Art.AtlasDefinition["rig"]["leg"+side];var fk=((string)data["foot"]).Split('/');var pk=((string)data["pant"]).Split('/');
                    var foot=rows.First(x=>x.PartType==fk[0]&&x.Structure==fk[1]&&x.renderer.transform.IsChildOf(skeleton)).renderer;
                    var pant=rows.First(x=>x.PartType==pk[0]&&x.Structure==pk[1]&&x.renderer.transform.IsChildOf(skeleton)).renderer;
                    return new Leg{Side=side,Source=foot,Pant=pant,Foot=PieceFor(foot,"foot",(string)data["foot"]),Cloth=PieceFor(pant,"leg",(string)data["pant"])};}
                g.LegLeft=MakeLeg("Left");g.LegRight=MakeLeg("Right");
                // The sword belongs to the costume, not an equipment renderer. An
                // empty or disabled native weapon slot must not hide or rotate it.
                g.Sword=PieceFor(left,"sword","Weapon/Sword",equipment:true);
                rigGroups.Add(g);changed=true;
            }
            return SyncRigPose()||changed;
        }
        private static Vector3 V(JToken value)=>new Vector3((float)value[0],(float)value[1],0);
        private Vector3 Point(Piece piece,JToken sourcePoint)=>piece.View.transform.TransformPoint(Art.SourcePoint(piece.Key,sourcePoint));
        private static bool SetPose(Piece p,Vector3 position,Quaternion rotation,Vector3 scale)
        {
            var t=p.View.transform;bool changed=(t.localPosition-position).sqrMagnitude>1e-10f||Quaternion.Angle(t.localRotation,rotation)>.001f||(t.localScale-scale).sqrMagnitude>1e-10f;
            t.localPosition=position;t.localRotation=rotation;t.localScale=scale;return changed;
        }
        private bool Style(Piece p,SpriteRenderer source,Color? color=null,int? order=null)
        {
            var r=p.View;bool enabled=source.enabled;
            bool changed=r.enabled!=enabled;r.enabled=enabled;r.gameObject.layer=source.gameObject.layer;
            r.sharedMaterial=source.sharedMaterial;r.sortingLayerID=source.sortingLayerID;r.sortingOrder=order??source.sortingOrder;r.color=color??source.color;
            bool mirror=(bool?)Art.AtlasDefinition["parts"][p.Key]["mirrorX"]??false;changed|=r.flipX!=mirror;r.flipX=mirror;
            return changed;
        }
        private bool Anchor(Piece p,JToken marker,Vector3 world)
        {
            var target=p.View.transform.parent.InverseTransformPoint(world);target.z=0;
            return SetPose(p,target-Art.SourcePoint(p.Key,marker),Quaternion.identity,Vector3.one);
        }
        private bool Fit(Piece p,JToken first,JToken second,Vector3 worldFirst,Vector3 worldSecond)
        {
            var parent=p.View.transform.parent;var a=parent.InverseTransformPoint(worldFirst);var b=parent.InverseTransformPoint(worldSecond);a.z=b.z=0;
            var sa=Art.SourcePoint(p.Key,first);var sb=Art.SourcePoint(p.Key,second);
            float scale=(b-a).magnitude/(sb-sa).magnitude;
            var q=Quaternion.Euler(0,0,Vector2.SignedAngle(sb-sa,b-a));
            return SetPose(p,a-q*(sa*scale),q,new Vector3(scale,scale,1));
        }
        private void Link(string name,Vector3 actual,Vector3 target)=>jointLinks.Add(new JointLink{Name=name,Actual=actual,Target=target});
        private static Vector3 ReferenceVector(JToken figure,JToken from,JToken to,float spineLength)
        {
            float length=(V(figure["neck"])-V(figure["waist"])).magnitude;
            var vector=V(to)-V(from);vector.y=-vector.y;return vector*(spineLength/length);
        }
        // Solve in canonical body coordinates so mirroring does not reverse the bend.
        // A folded arm retains its fabric length; a long native reach extends it uniformly.
        internal static Vector3 SolveElbow(Vector3 shoulder,Vector3 palm,float upper,float lower,float side)
        {
            var delta=palm-shoulder;delta.z=0;float distance=delta.magnitude;
            var direction=distance>1e-6f?delta/distance:Vector3.down;
            float total=upper+lower;
            if(distance>total){float stretch=distance/total;upper*=stretch;lower*=stretch;}
            if(distance<Mathf.Abs(upper-lower)){upper=lower=total*.5f;}
            float along=distance>1e-6f?(upper*upper-lower*lower+distance*distance)/(2*distance):0;
            float height=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            return shoulder+direction*along+new Vector3(-direction.y,direction.x,0)*height*side;
        }
        internal bool SyncRigPose()
        {
            if(!Selected||!RigEnabled)return false;
            bool changed=false;jointLinks.Clear();ReadCostumePose();var rig=Art.AtlasDefinition["rig"];var body=rig["body"];var native=rig["native"];
            float spine=(V(native["neck"])-V(native["waist"])).magnitude;var figure=rig["referenceFigure"];
            var soleA=V(native["waist"])+ReferenceVector(figure,figure["waist"],figure["soleLeft"],spine);
            var soleB=V(native["waist"])+ReferenceVector(figure,figure["waist"],figure["soleRight"],spine);
            // Preserve the reference's body/leg ratio while keeping the native ground and shadow.
            var shift=new Vector3(-(soleA.x+soleB.x)*.5f,(float)native["groundY"]-Mathf.Min(soleA.y,soleB.y),0);
            foreach(var g in rigGroups)
            {
                if(g.Body==null)continue;
                changed|=Fit(g.Torso,body["frameNeck"]??body["neck"],body["waist"],g.Body.transform.TransformPoint(V(native["neck"])+shift),g.Body.transform.TransformPoint(V(native["waist"])+shift));
                changed|=Style(g.Torso,g.Cloth);
                var neck=Point(g.Torso,body["neck"]);
                g.Face.Key=FaceKey;g.Face.View.sprite=Art.Parts[FaceKey];
                changed|=Anchor(g.Face,FaceNeck,neck);changed|=Style(g.Face,g.Head);
                if(g.Body.gameObject.activeInHierarchy)Link("neck",Point(g.Face,FaceNeck),neck);
                var back=rig["back"] as JObject;var rearParent=back!=null?g.Body.transform:g.Face.View.transform;
                if(g.Hair.View.transform.parent!=rearParent){g.Hair.View.transform.SetParent(rearParent,false);changed=true;}
                if(back!=null){changed|=Fit(g.Hair,back["shoulderLeft"],back["shoulderRight"],Point(g.Torso,body["shoulderLeft"]),Point(g.Torso,body["shoulderRight"]));changed|=Style(g.Hair,g.Cloth,order:g.Cloth.sortingOrder-1);}
                else {changed|=SetPose(g.Hair,HairOffset,Quaternion.identity,Vector3.one);changed|=Style(g.Hair,g.Head,order:g.Cloth.sortingOrder-1);}
                foreach(var arm in new[]{g.Left,g.Right})
                {
                    arm.Shoulder=Point(g.Torso,body["shoulder"+arm.Side]);
                    var nativePalm=arm.Source.transform.TransformPoint(V(native["palmInArm"]));
                    var delta=g.Body.transform.InverseTransformPoint(nativePalm)-V(native["restPalm"+arm.Side]);delta.z=0;
                    var reference=arm.Shoulder+g.Body.transform.TransformVector(ReferenceVector(figure,figure["shoulder"+arm.Side],figure["palm"+arm.Side],spine));
                    arm.Palm=CostumePalm(g,arm,reference,delta*((float?)rig["motionScale"]??1),spine);
                    bool holding=arm==g.Left||rig["offhand"]!=null;
                    string key="Hand/"+(holding?"Grip_":"Open_")+(arm.Side=="Left"?"R":"L");arm.Hand.Key=key;arm.Hand.View.sprite=Art.Parts[key];
                    var handMarks=Art.AtlasDefinition["parts"][key]["landmarks"];
                    var shoulderLocal=g.Body.transform.InverseTransformPoint(arm.Shoulder);
                    var palmLocal=g.Body.transform.InverseTransformPoint(arm.Palm);
                    float upper=ReferenceVector(figure,figure["shoulder"+arm.Side],figure["elbow"+arm.Side],spine).magnitude;
                    float lower=ReferenceVector(figure,figure["elbow"+arm.Side],figure["palm"+arm.Side],spine).magnitude;
                    var elbow=g.Body.transform.TransformPoint(SolveElbow(shoulderLocal,palmLocal,upper,lower,arm.Side=="Left"?1:-1));
                    var direction=arm.Hand.View.transform.parent.InverseTransformVector(arm.Palm-elbow);direction.z=0;
                    var sourceDirection=Art.SourcePoint(key,handMarks["grip"])-Art.SourcePoint(key,handMarks["wrist"]);
                    var rotation=CostumeWrist(g,arm,sourceDirection,direction);
                    var palm=arm.Hand.View.transform.parent.InverseTransformPoint(arm.Palm);palm.z=0;
                    changed|=SetPose(arm.Hand,palm-rotation*Art.SourcePoint(key,handMarks["grip"]),rotation,Vector3.one);
                    // Atlas hand anatomy is opposite the native slot names; bones stay unchanged.
                    changed|=Style(arm.Hand,arm.Cloth,color:g.Head.color,order:arm.Cloth.sortingOrder+3);
                    arm.Wrist=Point(arm.Hand,handMarks["wrist"]);
                    var sleeve=rig["sleeve"+arm.Side];
                    changed|=Fit(arm.Sleeve,sleeve["shoulder"],sleeve["elbow"],arm.Shoulder,elbow);changed|=Style(arm.Sleeve,arm.Cloth);
                    var lowerElbow=sleeve["lowerElbow"]??sleeve["elbow"];
                    changed|=Fit(arm.LowerSleeve,lowerElbow,sleeve["cuff"],elbow,arm.Wrist);changed|=Style(arm.LowerSleeve,arm.Cloth,order:arm.Cloth.sortingOrder+1);
                    if(g.Body.gameObject.activeInHierarchy){Link(arm.Side+" shoulder",Point(arm.Sleeve,sleeve["shoulder"]),arm.Shoulder);Link(arm.Side+" elbow",Point(arm.LowerSleeve,lowerElbow),Point(arm.Sleeve,sleeve["elbow"]));Link(arm.Side+" wrist",Point(arm.LowerSleeve,sleeve["cuff"]),arm.Wrist);}
                }
                foreach(var leg in new[]{g.LegLeft,g.LegRight})
                {
                    var data=rig["leg"+leg.Side];var delta=g.Body.transform.InverseTransformPoint(leg.Source.transform.position)-V(native["restFoot"+leg.Side]);delta.z=0;
                    var sole=V(native["waist"])+shift+ReferenceVector(figure,figure["waist"],figure["sole"+leg.Side],spine)+delta;
                    changed|=Anchor(leg.Foot,data["sole"],g.Body.transform.TransformPoint(sole));changed|=Style(leg.Foot,leg.Source);
                    var hip=g.Body.transform.TransformPoint(V(native["waist"])+shift+ReferenceVector(figure,figure["waist"],figure["hip"+leg.Side],spine));
                    var ankle=Point(leg.Foot,data["ankle"]);
                    changed|=Fit(leg.Cloth,data["hip"],data["pantAnkle"],hip,ankle);changed|=Style(leg.Cloth,leg.Pant);
                    if(g.Body.gameObject.activeInHierarchy)Link(leg.Side+" ankle",Point(leg.Cloth,data["pantAnkle"]),ankle);
                }
                var sword=g.Sword;var swordData=rig["sword"];
                changed|=PoseProp(sword,g.Left,swordData);
                // Hand covers the hilt; body normalization still excludes the sword.
                changed|=Style(sword,g.Left.Source,order:g.Left.Cloth.sortingOrder+2);
                if(g.Body.gameObject.activeInHierarchy)Link("Left grip",Point(sword,swordData["grip"]),g.Left.Palm);
                if(rig["offhand"] is JObject offhand&&Art.Parts.ContainsKey("Weapon/Offhand"))
                {
                    if(g.Offhand==null)g.Offhand=PieceFor(g.Right.Source,"offhand","Weapon/Offhand",equipment:true);
                    changed|=PoseProp(g.Offhand,g.Right,offhand);
                    changed|=Style(g.Offhand,g.Right.Source,order:g.Right.Cloth.sortingOrder+2);
                    if(g.Body.gameObject.activeInHierarchy)Link("Right grip",Point(g.Offhand,offhand["grip"]),g.Right.Palm);
                }
                else if(g.Offhand!=null)g.Offhand.View.enabled=false;
            }
            return changed;
        }
        private bool DisableRig()
        {
            bool changed=false;ClearCostumeMotion();
            foreach(var piece in rigPieces.Values)if(piece.View!=null){changed|=piece.View.enabled;piece.View.enabled=false;}
            jointLinks.Clear();return changed;
        }
    }

    [HarmonyPatch(typeof(SpumBirthAppearancePreview),nameof(SpumBirthAppearancePreview.ApplyWeapon))]
    internal static class BoundWeaponRefresh
    {
        private static void Prefix(SpumBirthAppearancePreview __instance)=>AppearanceChoices.BeginCurrent(AppearanceChoices.Root(__instance));
        private static void Postfix(SpumBirthAppearancePreview __instance)=>AppearanceChoices.End(AppearanceChoices.Root(__instance));
    }
    [HarmonyPatch(typeof(SpumBirthAppearancePreview),nameof(SpumBirthAppearancePreview.IsEquipmentRenderer))]
    internal static class BoundSwordIsEquipment
    {
        private static void Postfix(SpriteRenderer renderer,ref bool __result)
        {if(renderer!=null&&renderer.GetComponent<MuyifengEquipmentPart>()!=null)__result=true;}
    }
    [HarmonyPatch(typeof(NodePartyPortrait),nameof(NodePartyPortrait.RenderPortrait))]
    internal static class BoundPortraitPose
    {
        private static void Prefix(NodePartyPortrait __instance)
        {
            var rig=Entry.Read<GameObject>(__instance,"rig");var binding=rig?.GetComponentInChildren<SkinState>(true);
            if(binding!=null&&binding.SyncRigPose())AccessTools.Field(typeof(NodePartyPortrait),"dirty").SetValue(__instance,true);
        }
    }
}
