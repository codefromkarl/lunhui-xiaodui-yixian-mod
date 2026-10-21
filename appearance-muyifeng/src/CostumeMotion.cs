using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Presentation.Combat;
using Reincarnation.Runtime.Core;
using UnityEngine;

namespace MuyifengAppearance
{
    internal enum CostumePhase { Native, Idle, Attack, Damaged, Downed }

    // All coordinates are in the existing canonical body frame. Only our cutout
    // transforms move: native bones, motion clocks and gameplay receipts are read-only.
    internal sealed partial class SkinState
    {
        private static readonly FieldInfo AttackClock=AccessTools.Field(typeof(BattleCombatantView),"attackElapsed");
        private static readonly FieldInfo HitClock=AccessTools.Field(typeof(BattleCombatantView),"hitElapsed");
        private float cosmeticCast=-10f;
        private CostumePhase previewPhase=CostumePhase.Native;
        private float previewTime;
        private bool CostumeMotionEnabled=>Art.AtlasDefinition?["rig"]?["presentation"] is JObject;
        internal string MotionKind=>(string)Art.AtlasDefinition?["rig"]?["presentation"]?["kind"]??"native";
        internal string FaceState { get; private set; }="normal";
        internal float MotionProgress { get; private set; }=-1;
        internal void PreviewMotion(CostumePhase phase,float normalized=0){previewPhase=phase;previewTime=Mathf.Clamp01(normalized);}
        internal void BeginCostumeCast()
        {
            // Target-each/multi-hit playback must not restart a single cast.
            if(PresentationClock.Now-cosmeticCast>=.34f)cosmeticCast=PresentationClock.Now;
        }
        private CostumePhase ReadMotion(out float progress)
        {
            progress=previewTime;
            if(previewPhase!=CostumePhase.Native)return previewPhase;
            var actor=GetComponentInParent<BattleCombatantView>();
            if(actor==null)return CostumePhase.Idle;
            if(actor.LooksDowned)return CostumePhase.Downed;
            float hit=(float)HitClock.GetValue(actor);
            if(hit>=0&&hit<.20f)return CostumePhase.Damaged;
            float attack=(float)AttackClock.GetValue(actor);
            float cast=PresentationClock.Now-cosmeticCast;
            if(attack>=0&&attack<.34f){progress=attack/.34f;return CostumePhase.Attack;}
            if(cast>=0&&cast<.34f){progress=cast/.34f;return CostumePhase.Attack;}
            return CostumePhase.Idle;
        }
        private CostumePhase motionPhase;
        private void ReadCostumePose()
        {
            motionPhase=ReadMotion(out float t);
            if(!CostumeMotionEnabled)motionPhase=CostumePhase.Idle;
            MotionProgress=motionPhase==CostumePhase.Attack?t:-1;
            FaceState=motionPhase==CostumePhase.Downed?"downed":motionPhase==CostumePhase.Damaged?"damaged":"normal";
        }
        private string FaceKey=>FaceState=="normal"||!Art.Parts.ContainsKey("Face/"+FaceState)?"Body/Head":"Face/"+FaceState;
        private JToken FaceNeck=>FaceKey=="Body/Head"?Art.AtlasDefinition["rig"]["head"]["neck"]:Art.AtlasDefinition["parts"][FaceKey]["landmarks"]["neck"];
        private static float Pulse(float t,float peak)
        {
            if(t<=0||t>=1)return 0;
            float x=t<peak?t/peak:(1-t)/(1-peak);
            return x*x*(3-2*x);
        }
        private Vector3 CostumePalm(Group g,Arm arm,Vector3 reference,Vector3 nativeDelta,float spine)
        {
            if(!CostumeMotionEnabled)return reference+g.Body.transform.TransformVector(nativeDelta);
            var baseline=g.Body.transform.InverseTransformPoint(reference);bool left=arm==g.Left;
            string kind=MotionKind;
            // Fine motion remains native outside attacks. Heavy bodies take a
            // shorter arm swing; casters carry their prop below the face, not on it.
            if(kind=="cast"&&left)baseline+=new Vector3(-.12f,-.20f,0)*spine;
            float inherit=kind=="heavy"?.55f:kind=="cast"?.38f:1f;
            if(motionPhase!=CostumePhase.Attack)return g.Body.transform.TransformPoint(baseline+nativeDelta*inherit);
            float t=MotionProgress;float reach=Pulse(t,kind=="heavy"?.58f:.38f);
            float wind=Pulse(Mathf.Clamp01(t/.36f),.6f)*(1-reach);
            Vector3 offset;
            if(kind=="cast")offset=left?new Vector3(-.72f,.42f,0):new Vector3(.18f,.48f,0);
            else if(kind=="heavy")offset=left?new Vector3(-.95f,.18f,0):new Vector3(.12f,.2f,0);
            else if(kind=="dual"){
                float beat=Pulse(t,left?.32f:.60f);
                offset=left?new Vector3(-.72f,.4f,0):new Vector3(.4f,.55f,0);
                return g.Body.transform.TransformPoint(baseline+offset*spine*beat);
            }
            else offset=left?new Vector3(-.7f,.28f,0):new Vector3(.08f,.16f,0);
            var pull=left?new Vector3(.18f,.18f,0):Vector3.zero;
            return g.Body.transform.TransformPoint(baseline+(offset*reach+pull*wind)*spine);
        }
        private Quaternion CostumeWrist(Group g,Arm arm,Vector3 sourceDirection,Vector3 forearmDirection)
        {
            bool left=arm==g.Left;var rig=Art.AtlasDefinition["rig"];
            bool holding=left||rig["offhand"]!=null;
            if(!CostumeMotionEnabled||!holding||(motionPhase!=CostumePhase.Attack&&MotionKind!="cast"))
                return Quaternion.Euler(0,0,Vector2.SignedAngle(sourceDirection,forearmDirection));
            var data=left?rig["sword"]:rig["offhand"];
            // The offhand is created after the first arm pass; its source points
            // are already available in the art set on that first frame.
            string key=left?"Weapon/Sword":"Weapon/Offhand";
            var axis=Art.SourcePoint(key,data["emitter"]??new JArray(Art.AtlasDefinition["parts"][key]["rect"][2].Value<float>()*.5f,0))-
                     Art.SourcePoint(key,data["grip"]);
            float offset=(float)data["angleDegrees"]-RestHandAngle(arm);
            var handAxis=Quaternion.Euler(0,0,offset)*axis;
            var resting=Quaternion.Euler(0,0,(float)data["angleDegrees"])*axis;
            Vector3 target=resting;
            float t=MotionProgress;
            if(MotionKind=="cast"){
                bool hanging=(bool?)rig["presentation"]["hanging"]??false;
                target=hanging?Vector3.down:Vector3.up;
                if(!hanging&&motionPhase==CostumePhase.Attack)target=Quaternion.Euler(0,0,-10*Pulse(t,.38f))*target;
            }
            else if(motionPhase==CostumePhase.Attack&&MotionKind!="heavy"){
                float rest=Vector2.SignedAngle(Vector2.right,resting);
                float strike=MotionKind=="dual"?(left?.32f:.60f):.38f;
                float wind=strike*.45f,follow=Mathf.Min(.86f,strike+.24f);
                float angle=t<wind?Mathf.LerpAngle(rest,65,t/wind):t<strike?Mathf.LerpAngle(65,180,(t-wind)/(strike-wind)):
                    t<follow?Mathf.LerpAngle(180,240,(t-strike)/(follow-strike)):Mathf.LerpAngle(240,rest,(t-follow)/(1-follow));
                target=Quaternion.Euler(0,0,angle)*Vector3.right;
            }
            else if(motionPhase==CostumePhase.Attack)target=Quaternion.Euler(0,0,-12*Pulse(t,.58f))*resting;
            var local=arm.Hand.View.transform.parent.InverseTransformVector(g.Body.transform.TransformVector(target));
            return Quaternion.Euler(0,0,Vector2.SignedAngle(handAxis,local));
        }
        private float RestHandAngle(Arm arm)
        {
            var rig=Art.AtlasDefinition["rig"];var f=rig["referenceFigure"];string s=arm.Side;
            var direction=V(f["palm"+s])-V(f["elbow"+s]);direction.y=-direction.y;
            var marks=Art.AtlasDefinition["parts"][arm.Hand.Key]["landmarks"];
            var hand=Art.SourcePoint(arm.Hand.Key,marks["grip"])-Art.SourcePoint(arm.Hand.Key,marks["wrist"]);
            return Vector2.SignedAngle(hand,direction);
        }
        private bool PoseProp(Piece prop,Arm arm,JToken data)
        {
            // Fixed hand-relative orientation makes both position AND angle follow
            // the grip. Never parent to the hidden/disabled equipment renderer.
            if(!CostumeMotionEnabled){
                if(prop.View.transform.parent!=arm.Source.transform)prop.View.transform.SetParent(arm.Source.transform,false);
                var oldRotation=Quaternion.Euler(0,0,(float)data["angleDegrees"]);
                var oldGrip=arm.Source.transform.InverseTransformPoint(arm.Palm);oldGrip.z=0;
                return SetPose(prop,oldGrip-oldRotation*Art.SourcePoint(prop.Key,data["grip"]),oldRotation,Vector3.one);
            }
            float offset=(float)data["angleDegrees"]-RestHandAngle(arm);
            if(prop.View.transform.parent!=arm.Hand.View.transform)prop.View.transform.SetParent(arm.Hand.View.transform,false);
            var rotation=Quaternion.Euler(0,0,offset);
            var grip=prop.View.transform.parent.InverseTransformPoint(arm.Palm);grip.z=0;
            return SetPose(prop,grip-rotation*Art.SourcePoint(prop.Key,data["grip"]),rotation,Vector3.one);
        }
        internal bool GripFramesCoherent=>rigGroups.TrueForAll(g=>g.Sword==null||
            (g.Sword.View.transform.parent==g.Left.Hand.View.transform&&
             Quaternion.Angle(g.Sword.View.transform.localRotation,Quaternion.Euler(0,0,(float)Art.AtlasDefinition["rig"]["sword"]["angleDegrees"]-RestHandAngle(g.Left)))<.01f&&
             (g.Offhand==null||g.Offhand.View.transform.parent==g.Right.Hand.View.transform)));
        internal bool TryCostumeEmitter(out Vector3 position)
        {
            position=default;
            if(!Selected||!RigEnabled||!CostumeMotionEnabled)return false;
            SyncRigPose();
            foreach(var g in rigGroups)if(g.Body!=null&&g.Body.gameObject.activeInHierarchy&&g.Sword?.View!=null){
                var data=Art.AtlasDefinition["rig"]["sword"];var p=data["emitter"]??data["grip"];
                position=Point(g.Sword,p);return true;
            }
            return false;
        }
        internal static CombatVfxRequest AtCostumeEmitter(CombatVfxRequest r,Vector3 emitter,bool atSource=false)
        {
            return new CombatVfxRequest(r.StableKey,r.Anchor,r.Lifetime,r.ContentId,
                atSource||r.HasWorldPosition,atSource?emitter:r.WorldPosition,r.Scale,
                r.HasSourcePosition,r.HasSourcePosition?(atSource?emitter+r.SourcePosition-r.WorldPosition:emitter):r.SourcePosition,
                r.TargetHeight,r.Cue,r.IsCritical,r.IsCast,r.StyleOverride,r.Identity,r.Variant,r.FromEnemy,r.HitPause);
        }
        private void ClearCostumeMotion(){previewPhase=CostumePhase.Native;cosmeticCast=-10;FaceState="normal";}
    }

    // A synchronous, exception-safe source scope avoids guessing ownership from
    // nearest positions or sharing a mutable current character across instances.
    [HarmonyPatch]
    internal static class CostumeVfxSource
    {
        [ThreadStatic] internal static SkinState Current;
        [ThreadStatic] internal static bool AtSource;
        internal sealed class Scope
        {
            private readonly SkinState previous;private readonly bool previousPlacement;private bool restored;
            internal Scope(SkinState next,bool atSource=false){previous=Current;previousPlacement=AtSource;Current=next;AtSource=atSource;}
            internal void Restore(){if(!restored){Current=previous;AtSource=previousPlacement;restored=true;}}
        }
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(string name in new[]{"PlayAtSource","PlayAtTargetSide","PlayOnTarget"})yield return AccessTools.Method(typeof(CombatFeedbackPresenter),name);
        }
        private static void Prefix(BattleCombatantView source,MethodBase __originalMethod,out Scope __state)
        {
            var state=source?.GetComponent<SkinState>();
            if(state!=null&&!state.Selected)state=null;
            __state=new Scope(state,__originalMethod.Name=="PlayAtSource");
            if(__originalMethod.Name=="PlayAtSource")state?.BeginCostumeCast();
        }
        private static void Postfix(Scope __state)=>__state.Restore();
        private static Exception Finalizer(Exception __exception,Scope __state){__state?.Restore();return __exception;}
    }
    [HarmonyPatch(typeof(PooledVfxPlayer),nameof(PooledVfxPlayer.Play))]
    internal static class CostumeVfxOrigin
    {
        internal static Action<CombatVfxRequest> ObserveForVerification;
        private static void Prefix(ref CombatVfxRequest request)
        {
            if(CostumeVfxSource.Current!=null&&CostumeVfxSource.Current.TryCostumeEmitter(out var origin))
                request=SkinState.AtCostumeEmitter(request,origin,CostumeVfxSource.AtSource);
            ObserveForVerification?.Invoke(request);
        }
    }
}
