using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using UnityEngine;

namespace MuyifengAppearance
{
    internal sealed partial class RuntimeVerification
    {
        private IEnumerable VerifyExpandedCharacters()
        {
            var summaries=new JArray();report["expanded_characters"]=summaries;
            report["expanded_animation_samples_per_phase"]=8;
            try
            {
                foreach(var entry in SkinCatalog.All.Where(x=>x.Key!=MuyifengChoice.Id))
                {
                    var choice=AppearanceChoices.All.Single(c=>c.Id==entry.Key);var art=entry.Value;
                    showcaseAppearance=choice.Token;
                    Case("character_identity_"+choice.Id,()=>{
                        foreach(var actor in Actors)actor.ApplyLook(actor==hero||actor==detailHero?choice.Token:BirthAppearance.Default,"EQ-RFL-01");
                        var state=hero.GetComponent<SkinState>();
                        Require(state.Selected&&ReferenceEquals(state.SkinArt,art),"selected character art missed");
                        Require(Rows(hero).Where(x=>x.PartType=="Weapons").All(x=>x.renderer.sprite==null),"native equipment leaked into character");
                        Require(!HasSkin(hero)&&!HasSkin(native),"Muyifeng or selected art leaked");
                        Require(hero.WeaponEquipmentId=="EQ-RFL-01","cosmetic changed actual weapon ID");
                        Require(hero.GetComponentsInChildren<SpriteRenderer>(true).Any(r=>r.enabled&&r.gameObject.activeInHierarchy&&(r.sprite==art.Parts["Body/Head"]||r.sprite==art.Parts["Face/damaged"]||r.sprite==art.Parts["Face/downed"])),"new character head not rendered");
                        Require(hero.GetComponentsInChildren<SpriteRenderer>(true).Count(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sprite==art.Parts["Weapon/Sword"])==1,"primary prop missing or duplicated");
                        hero.ApplyLook(choice.Token,"");Require(hero.WeaponEquipmentId=="","unarmed identity changed");
                        Require(hero.GetComponentsInChildren<SpriteRenderer>(true).Any(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sprite==art.Parts["Weapon/Sword"]),"unarmed refresh hid character prop");
                        hero.ApplyLook(choice.Token,"EQ-RFL-01");
                    });
                    Case("character_pair_and_portraits_"+choice.Id,()=>{
                        var choices=AppearanceChoices.All.Where(c=>SkinCatalog.All.ContainsKey(c.Id)).ToArray();
                        int index=Array.FindIndex(choices,c=>c.Id==choice.Id);var other=choices[(index+1)%choices.Length];var otherArt=SkinCatalog.All[other.Id];
                        native.ApplyLook(other.Token,"EQ-PST-01");hero.ApplyLook(choice.Token,"EQ-RFL-01");
                        Require(ReferenceEquals(native.GetComponent<SkinState>().SkinArt,otherArt)&&ReferenceEquals(hero.GetComponent<SkinState>().SkinArt,art),"two selected characters share mutable current art");
                        Require(HasArtHead(native,otherArt)&&HasArtHead(hero,art),"two selected characters mixed head sprites");
                        localPortrait.Set(PortraitModel(1,true,choice.Token),null);remotePortrait.Set(PortraitModel(2,false,other.Token),null);
                        Require(HasArtHead(Entry.Read<GameObject>(localPortrait,"rig").transform,art)&&HasArtHead(Entry.Read<GameObject>(remotePortrait,"rig").transform,otherArt),"portrait art ignores per-character choice");
                        native.ApplyLook(BirthAppearance.Default,"EQ-RFL-01");
                    });
                    Case("character_pool_and_summon_"+choice.Id,()=>{
                        hero.Apply(Snapshot(2,"reused-"+choice.Id),0,1,2.5f);hero.ApplyLook(choice.Token,"EQ-RFL-01");Require(HasArtHead(hero,art),"remote pool reuse lost choice");
                        hero.Apply(Snapshot(2,"summon-"+choice.Id,CombatantKind.Summon),0,1,2.5f);hero.ApplyLook(choice.Token,"EQ-RFL-01");Require(!hero.GetComponent<SkinState>().Selected,"summon inherited owner skin");
                        hero.Apply(Snapshot(1,"restored-"+choice.Id),0,1,2.5f);hero.ApplyLook(choice.Token,"EQ-RFL-01");Require(HasArtHead(hero,art),"player pool reuse failed to rebind");
                    });
                    foreach(string phase in new[]{"IDLE","MOVE","ATTACK","DAMAGED","DEATH","MIRROR","RESTORE"})
                    {
                        phaseLabel.text=choice.Group+" / "+choice.Name+" / "+phase;
                        Case("character_pose_"+choice.Id+"_"+phase,()=>{
                            BeginPhase(phase);
                            for(int sample=0;sample<8;sample++)
                            {
                                foreach(var actor in Actors)foreach(var animator in actor.GetComponentsInChildren<Animator>(true))
                                    activeClip.SampleAnimation(animator.gameObject,activeClip.length*sample/8f);
                                PreviewCostumePhase(phase,sample/8f);
                                foreach(var actor in Actors)actor.GetComponent<SkinState>()?.SyncRigPose();
                                var binding=hero.GetComponent<SkinState>();Require(binding.Joints.Count>=10,"character lacks anatomical/prop bindings");
                                Require(binding.NativePartsHidden,"animation reintroduced native body/clothes/equipment sprites");
                                AssertVisibleRig(hero,art,phase);
                                foreach(var joint in binding.Joints)Require(Vector2.Distance(camera.WorldToScreenPoint(joint.Actual),camera.WorldToScreenPoint(joint.Target))<1,"animated joint gap: "+joint.Name);
                            }
                            foreach(var actor in Actors)foreach(var animator in actor.GetComponentsInChildren<Animator>(true))
                                activeClip.SampleAnimation(animator.gameObject,Mathf.Min(.32f,activeClip.length-.001f));
                            PreviewCostumePhase(phase,.38f);
                            foreach(var actor in Actors){actor.GetComponent<SkinState>()?.SyncRigPose();PrepareDisplay(actor);actor.GetComponent<SkinState>()?.SyncRigPose();}
                            var state=hero.GetComponent<SkinState>();Require(ReferenceEquals(state.SkinArt,art),"pose changed character identity");
                            foreach(var joint in state.Joints)Require(Vector2.Distance(camera.WorldToScreenPoint(joint.Actual),camera.WorldToScreenPoint(joint.Target))<1,"anatomical joint gap: "+joint.Name);
                            Require(hero.GetComponentsInChildren<SpriteRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sprite!=null&&r.sprite.name.StartsWith("Yixian/",StringComparison.Ordinal)).All(r=>art.Parts.Values.Contains(r.sprite)),"mixed another character's art");
                        });
                        yield return new WaitForEndOfFrame();
                        Case("character_capture_"+choice.Id+"_"+phase,()=>{
                            foreach(var actor in Actors){PrepareDisplay(actor);actor.GetComponent<SkinState>()?.SyncRigPose();}
                            localPortrait.RenderPortrait();remotePortrait.RenderPortrait();
                            camera.Render();Capture(System.IO.Path.Combine(output,"character-"+choice.Id+"-"+phase.ToLowerInvariant()+".png"));
                            if(phase=="IDLE")CapturePortrait(localPortrait,System.IO.Path.Combine(output,"portrait-"+choice.Id+".png"));
                        });
                    }
                    summaries.Add(new JObject{["id"]=choice.Id,["name"]=choice.Name,["group"]=choice.Group,["parts"]=art.Parts.Count});
                    Case("character_restore_"+choice.Id,()=>{
                        hero.ApplyLook(BirthAppearance.Default,"EQ-RFL-01");
                        Require(!hero.GetComponent<SkinState>().Selected,"native choice retained character binding");
                        Require(Rows(hero).Any(x=>x.PartType=="Weapons"&&x.renderer.sprite!=null),"native weapon not restored");
                    });
                }
            }
            finally
            {
                showcaseAppearance=MuyifengChoice.Token;
                localPortrait.Set(PortraitModel(1,true),null);remotePortrait.Set(PortraitModel(2,false),null);
                foreach(var actor in Actors)actor.ApplyLook(actor==hero||actor==detailHero?MuyifengChoice.Token:BirthAppearance.Default,"EQ-BLD-01");
                foreach(var actor in Actors)SetNativeFacing(actor,false);
            }
        }
        private static bool HasArtHead(Component root,AppearanceArt art)=>root.GetComponentsInChildren<SpriteRenderer>(true)
            .Any(r=>r.enabled&&r.gameObject.activeInHierarchy&&(r.sprite==art.Parts["Body/Head"]||r.sprite==art.Parts["Face/damaged"]||r.sprite==art.Parts["Face/downed"]));
    }
}
