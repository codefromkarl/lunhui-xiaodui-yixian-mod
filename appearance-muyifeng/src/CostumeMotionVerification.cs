using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Presentation.Combat;
using UnityEngine;

namespace MuyifengAppearance
{
    internal sealed class CostumeVerificationSink : MonoBehaviour,IVfxPlayer
    {
        internal PooledVfxPlayer Player;
        public bool IsAvailable=>true;
        public void Play(CombatVfxRequest request)=>Player.Play(request);
        public void StopAll() { }
    }
    internal sealed partial class RuntimeVerification
    {
        private void PreviewCostumePhase(string phase,float t)
        {
            var kind=phase=="ATTACK"?CostumePhase.Attack:phase=="DAMAGED"?CostumePhase.Damaged:phase=="DEATH"?CostumePhase.Downed:CostumePhase.Idle;
            foreach(var actor in Actors)actor.GetComponent<SkinState>()?.PreviewMotion(kind,t);
        }
        private IEnumerable VerifyCostumeMotion()
        {
            var summary=new JArray();report["costume_motion"]=summary;
            try
            {
                foreach(var choice in AppearanceChoices.All)
                {
                    var art=choice.Id==MuyifengChoice.Id?Art.Default:SkinCatalog.All[choice.Id];
                    foreach(var actor in Actors)actor.ApplyLook(actor==hero||actor==detailHero?choice.Token:BirthAppearance.Default,"EQ-RFL-01");
                    var state=hero.GetComponent<SkinState>();
                    BeginPhase("IDLE");
                    Case("motion_live_clocks_"+choice.Id,()=>{
                        var field=AccessTools.Field(typeof(BattleCombatantView),"attackElapsed");
                        var hit=AccessTools.Field(typeof(BattleCombatantView),"hitElapsed");
                        object old=field.GetValue(hero),oldHit=hit.GetValue(hero);
                        try{
                            state.PreviewMotion(CostumePhase.Native);hit.SetValue(hero,-1f);
                            hero.PlayAttackMotion();state.SyncRigPose();Require(state.MotionProgress>=0,"actual native attack did not trigger costume motion");
                            field.SetValue(hero,.17f);state.SyncRigPose();Require(Mathf.Abs(state.MotionProgress-.5f)<.001f,"motion does not follow native attack clock");
                            hit.SetValue(hero,.04f);state.SyncRigPose();Require(state.FaceState=="damaged","actual native hit did not select injured face");
                            hero.SetStagedDowned(true);state.SyncRigPose();Require(state.FaceState=="downed","actual staged downed did not close eyes");
                        }finally{field.SetValue(hero,old);hit.SetValue(hero,oldHit);hero.SetStagedDowned(false);state.PreviewMotion(CostumePhase.Idle);state.SyncRigPose();}
                    });
                    Case("motion_grip_and_native_contract_"+choice.Id,()=>{
                        var bones=hero.PresentationMount.GetComponentsInChildren<Transform>(true).Where(t=>!t.name.StartsWith("MuyifengBinding/",StringComparison.Ordinal)).ToArray();
                        var poses=bones.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();
                        for(int i=0;i<=24;i++){
                            state.PreviewMotion(CostumePhase.Attack,i/24f);state.SyncRigPose();
                            Require(state.GripFramesCoherent,"hand/prop angle diverged");
                            Require(state.NativePartsHidden,"native parts reappeared");
                            foreach(var joint in state.Joints)Require(Vector2.Distance(camera.WorldToScreenPoint(joint.Actual),camera.WorldToScreenPoint(joint.Target))<1,"pose detached joint: "+joint.Name);
                            for(int b=0;b<bones.Length;b++)Require(bones[b].localPosition==poses[b].localPosition&&bones[b].localRotation==poses[b].localRotation&&bones[b].localScale==poses[b].localScale,"presentation wrote native bone");
                        }
                        Require(hero.WeaponEquipmentId=="EQ-RFL-01","presentation changed actual rifle");
                    });
                    foreach(float t in new[]{0f,.22f,.38f,.60f,.82f,1f})
                    {
                        PreviewCostumePhase("ATTACK",t);
                        foreach(var actor in Actors){actor.GetComponent<SkinState>()?.SyncRigPose();PrepareDisplay(actor);actor.GetComponent<SkinState>()?.SyncRigPose();}
                        phaseLabel.text=choice.Name+" / "+state.MotionKind+" / "+t.ToString("F2");
                        yield return new WaitForEndOfFrame();
                        foreach(var actor in Actors){PrepareDisplay(actor);actor.GetComponent<SkinState>()?.SyncRigPose();}
                        camera.Render();Capture(Path.Combine(output,"motion-"+choice.Id+"-"+Mathf.RoundToInt(t*100).ToString("D3")+".png"));
                    }
                    Case("motion_vfx_scoped_origin_"+choice.Id,()=>{
                        state.PreviewMotion(CostumePhase.Attack,.38f);state.SyncRigPose();
                        Require(state.TryCostumeEmitter(out var emitter),"costume has no emitter");
                        var go=new GameObject("AppearanceVfxVerification");
                        try{
                            var player=go.AddComponent<PooledVfxPlayer>();
                            var original=new CombatVfxRequest("appearance.test.no.asset",hero.PresentationMount,.25f,new StableId("test.action"),true,new Vector3(9,8,7),.8f,true,new Vector3(4,3,2),1.2f,CombatFeedbackCue.None,true,false,CombatVfxStyle.Fire,default,2,false,.04f);
                            CombatVfxRequest observed=default;CostumeVfxOrigin.ObserveForVerification=r=>observed=r;
                            var scope=new CostumeVfxSource.Scope(state);
                            try{player.Play(original);}finally{scope.Restore();}
                            Require(Vector3.Distance(observed.SourcePosition,emitter)<.0001f,"patched VFX player did not use this costume's prop");
                            Require(observed.WorldPosition==original.WorldPosition&&observed.StableKey==original.StableKey&&observed.StyleOverride==original.StyleOverride&&observed.Lifetime==original.Lifetime&&observed.ContentId==original.ContentId&&observed.Variant==original.Variant&&observed.HitPause==original.HitPause,"costume changed target/type/timing");
                            player.Play(original);Require(observed.SourcePosition==original.SourcePosition,"source scope leaked into subsequent native effect");
                            var cast=SkinState.AtCostumeEmitter(new CombatVfxRequest("cast",hero.PresentationMount,.2f,isCast:true),emitter,true);
                            Require(cast.WorldPosition==emitter,"cast is not placed on prop");
                            var selfTarget=new CombatVfxRequest("self.heal",hero.PresentationMount,.2f,hasWorldPosition:true,worldPosition:new Vector3(1,2,3));
                            Require(SkinState.AtCostumeEmitter(selfTarget,emitter).WorldPosition==selfTarget.WorldPosition,"self-target healing moved away from its target");
                        }finally{CostumeVfxOrigin.ObserveForVerification=null;DestroyImmediate(go);}
                    });
                    Case("motion_native_source_route_"+choice.Id,()=>{
                        var root=new GameObject("NativeCostumeSourceRoute");root.SetActive(false);
                        try{
                            var presenter=root.AddComponent<CombatFeedbackPresenter>();
                            var player=root.AddComponent<PooledVfxPlayer>();
                            var sink=root.AddComponent<CostumeVerificationSink>();sink.Player=player;
                            AccessTools.Field(typeof(CombatFeedbackPresenter),"vfxPlayer").SetValue(presenter,sink);
                            var bindingType=typeof(CombatFeedbackPresenter).GetNestedType("ResolvedActionBinding",System.Reflection.BindingFlags.NonPublic);
                            var binding=Activator.CreateInstance(bindingType,new object[]{"vfx.combat.magic","",CombatVfxPlacement.Source,.24f,.8f,CombatVfxStyle.Magic,default(CombatVfxIdentity)});
                            CombatVfxRequest actual=default;CostumeVfxOrigin.ObserveForVerification=r=>actual=r;
                            bool played=(bool)AccessTools.Method(typeof(CombatFeedbackPresenter),"PlayAtSource").Invoke(presenter,new object[]{binding,hero,new StableId("test.cast"),true});
                            Require(played&&state.TryCostumeEmitter(out var emitter)&&Vector3.Distance(actual.WorldPosition,emitter)<.0001f,"native source callback did not route this character to its prop");
                            Require(CostumeVfxSource.Current==null,"native source callback leaked its owner");
                        }finally{CostumeVfxOrigin.ObserveForVerification=null;DestroyImmediate(root);}
                    });
                    summary.Add(new JObject{["id"]=choice.Id,["profile"]=state.MotionKind,["attack_samples"]=25,["captured_beats"]=6,["faces"]=new JArray("normal","damaged","downed")});
                    state.PreviewMotion(CostumePhase.Native);
                }
                Case("motion_legacy_rig_without_opt_in",()=>{
                    hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");var state=hero.GetComponent<SkinState>();
                    var rig=Art.AtlasDefinition["rig"];var profile=rig["presentation"];rig["presentation"].Parent.Remove();
                    try{
                        state.PreviewMotion(CostumePhase.Attack,.38f);state.SyncRigPose();
                        var prop=hero.GetComponentsInChildren<SpriteRenderer>(true).Single(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sprite==Art.Parts["Weapon/Sword"]);
                        var arm=Rows(hero).First(r=>r.PartType=="Body"&&r.Structure=="Arm_L"&&r.renderer.gameObject.activeInHierarchy).renderer;
                        Require(prop.transform.parent==arm.transform,"old rig gained the new grip-frame contract without opting in");
                        Require(Quaternion.Angle(prop.transform.localRotation,Quaternion.Euler(0,0,(float)rig["sword"]["angleDegrees"]))<.01f,"old rig prop angle changed");
                        Require(!state.TryCostumeEmitter(out _),"old rig acquired costume FX routing");
                    }finally{rig["presentation"]=profile;state.PreviewMotion(CostumePhase.Native);state.SyncRigPose();}
                });
                Case("motion_scope_nested_and_restore",()=>{
                    var outer=new CostumeVfxSource.Scope(hero.GetComponent<SkinState>());
                    var inner=new CostumeVfxSource.Scope(null);Require(CostumeVfxSource.Current==null,"native nested source inherited costume");
                    inner.Restore();Require(CostumeVfxSource.Current==hero.GetComponent<SkinState>(),"nested source lost parent");
                    outer.Restore();outer.Restore();Require(CostumeVfxSource.Current==null,"source scope not restored/idempotent");
                });
            }
            finally
            {
                foreach(var actor in Actors){actor.GetComponent<SkinState>()?.PreviewMotion(CostumePhase.Native);actor.ApplyLook(actor==hero||actor==detailHero?MuyifengChoice.Token:BirthAppearance.Default,"EQ-BLD-01");}
                CostumeVfxOrigin.ObserveForVerification=null;
            }
        }
    }
}
