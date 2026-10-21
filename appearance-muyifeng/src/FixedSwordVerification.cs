using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Birth;
using Reincarnation.ExternalBridges.Spum;
using UnityEngine;

namespace MuyifengAppearance
{
    internal sealed partial class RuntimeVerification
    {
        private IEnumerable VerifyFixedSwordEquipment()
        {
            var equipment=new[]{"", "EQ-BLD-01", "EQ-BLD-02", "EQ-BLD-10", "EQ-HVR-06",
                "EQ-BOW-01", "EQ-PST-01", "EQ-RFL-01", "EQ-PLM-01", "EQ-FOC-01"};
            var matrix=new JArray();report["fixed_sword_equipment"]=matrix;
            try
            {
                foreach(string id in equipment)
                {
                    string label=string.IsNullOrEmpty(id)?"unarmed":id;
                    phaseLabel.text="固定持剑 / 实际装备："+label;
                    Case("fixed_sword_"+label,()=>{
                        foreach(var actor in Actors)
                            actor.ApplyLook(actor==hero||actor==detailHero?MuyifengChoice.Token:BirthAppearance.Default,id);
                        var preview=hero.GetComponentInChildren<SpumBirthAppearancePreview>(true);
                        var actual=Entry.Read<SpumWeaponVisual>(preview,"currentWeapon");var expected=SpumWeaponCatalog.GetVisual(id);
                        Require(hero.WeaponEquipmentId==id&&native.WeaponEquipmentId==id,"equipment ID changed by cosmetic");
                        Require(actual.ResourcePath==expected.ResourcePath&&actual.OffhandResourcePath==expected.OffhandResourcePath&&
                            actual.PartSubType==expected.PartSubType&&actual.DrawnAxisDegrees==expected.DrawnAxisDegrees,"native weapon presentation identity changed");
                        Require(Rows(hero).Where(x=>x.PartType=="Weapons").All(x=>x.renderer.sprite==null),"native weapon overlaps costume sword");
                        Require(SwordCount(hero)==1&&VisiblePart(hero,"Hand/Grip_L")&&VisiblePart(hero,"Hand/Open_R"),"costume needs one sword, left grip and open right hand");
                        Require(!HasSword(native),"costume sword leaked to native control");
                        if(expected.IsArmed)Require(Rows(native).Any(x=>x.PartType=="Weapons"&&x.renderer.sprite!=null),"armed control has no native weapon");
                        else Require(Rows(native).Where(x=>x.PartType=="Weapons").All(x=>x.renderer.sprite==null),"unarmed control gained a weapon");
                        Entry.Bind(hero.gameObject,false);
                        var restored=Rows(hero).Where(x=>x.PartType=="Weapons").Select(x=>x.renderer.sprite?.name).ToArray();
                        var control=Rows(native).Where(x=>x.PartType=="Weapons").Select(x=>x.renderer.sprite?.name).ToArray();
                        Require(!HasSword(hero)&&restored.SequenceEqual(control),"native equipment not restored after removing skin");
                        var bones=hero.PresentationMount.GetComponentsInChildren<Transform>(true).Where(t=>!t.name.StartsWith("MuyifengBinding/",StringComparison.Ordinal)).ToArray();
                        var poses=bones.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();
                        Entry.Bind(hero.gameObject,true);
                        for(int i=0;i<bones.Length;i++)Require(bones[i].localPosition==poses[i].localPosition&&bones[i].localRotation==poses[i].localRotation&&bones[i].localScale==poses[i].localScale,"cosmetic modified native skeleton");
                        Require(SwordCount(hero)==1,"rebind lost or duplicated fixed sword");
                        matrix.Add(new JObject{["equipmentId"]=id,["nativeResource"]=actual.ResourcePath,["nativeOffhandResource"]=actual.OffhandResourcePath,["swords"]=SwordCount(hero)});
                    });
                    foreach(string phase in new[]{"IDLE","ATTACK"})
                    {
                        Case("fixed_sword_pose_"+label+"_"+phase,()=>{
                            BeginPhase(phase);
                            foreach(var actor in Actors)foreach(var animator in actor.GetComponentsInChildren<Animator>(true))
                                activeClip.SampleAnimation(animator.gameObject,Mathf.Min(.3f,activeClip.length-.001f));
                            foreach(var actor in Actors){actor.GetComponent<SkinState>()?.SyncRigPose();PrepareDisplay(actor);actor.GetComponent<SkinState>()?.SyncRigPose();}
                            Require(SwordCount(hero)==1&&Rows(hero).Where(x=>x.PartType=="Weapons").All(x=>x.renderer.sprite==null),"equipment returned or sword missing in pose");
                            var binding=hero.GetComponent<SkinState>();var grip=binding.Joints.Single(x=>x.Name=="Left grip");
                            Require(Vector2.Distance(camera.WorldToScreenPoint(grip.Actual),camera.WorldToScreenPoint(grip.Target))<1,"sword detached from palm");
                            var sword=hero.GetComponentsInChildren<SpriteRenderer>(true).Single(x=>x.enabled&&x.gameObject.activeInHierarchy&&x.sprite==Art.Parts["Weapon/Sword"]);
                            Require(Quaternion.Angle(sword.transform.localRotation,Quaternion.Euler(0,0,(float)Art.AtlasDefinition["rig"]["sword"]["angleDegrees"]))<.001f,"actual equipment rotated costume sword");
                        });
                        yield return new WaitForEndOfFrame();
                        Case("fixed_sword_capture_"+label+"_"+phase,()=>{
                            // Native LateUpdate can restore its motion base while
                            // waiting for the frame. Reapply the inspection scale
                            // at capture time, as in the main animation recorder.
                            foreach(var actor in Actors){PrepareDisplay(actor);actor.GetComponent<SkinState>()?.SyncRigPose();}
                            camera.Render();Capture(System.IO.Path.Combine(output,"fixed-sword-"+label+"-"+phase.ToLowerInvariant()+".png"));
                        });
                    }
                }
                Case("fixed_sword_independent_of_native_weapon_objects",()=>{
                    var sources=Rows(hero).Where(x=>x.PartType=="Weapons").Select(x=>x.renderer.gameObject).Distinct().ToArray();
                    var active=sources.Select(x=>x.activeSelf).ToArray();
                    try{foreach(var source in sources)source.SetActive(false);hero.GetComponent<SkinState>().SyncRigPose();Require(SwordCount(hero)==1,"disabled native weapon object hid costume sword");}
                    finally{for(int i=0;i<sources.Length;i++)sources[i].SetActive(active[i]);}
                });
                Case("fixed_sword_repeated_choice_and_equipment_cycles",()=>{
                    for(int i=0;i<3;i++)foreach(string id in equipment){
                        hero.ApplyLook(MuyifengChoice.Token,id);Require(SwordCount(hero)==1,"choice/equipment cycle duplicated or lost sword");
                        hero.ApplyLook(BirthAppearance.Default,id);Require(!HasSkin(hero)&&!HasSword(hero),"native choice retained costume");
                    }
                    hero.ApplyLook(MuyifengChoice.Token,"");Require(SwordCount(hero)==1,"unarmed selected choice did not recover");
                });
            }
            finally
            {
                foreach(var actor in Actors)actor.ApplyLook(actor==hero||actor==detailHero?MuyifengChoice.Token:BirthAppearance.Default,"EQ-BLD-01");
            }
        }
        private static int SwordCount(Component root)=>root.GetComponentsInChildren<SpriteRenderer>(true)
            .Count(x=>x.enabled&&x.gameObject.activeInHierarchy&&x.sprite==Art.Parts["Weapon/Sword"]);
    }
}
