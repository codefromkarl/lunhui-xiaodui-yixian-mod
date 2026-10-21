using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.Domain.Save;
using Reincarnation.ExternalBridges.Spum;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Runtime.Birth;
using UnityEngine;

namespace MuyifengAppearance
{
    // Shared creation-menu registration. Tokens are persistent identities, never list ordinals.
    public sealed class AppearanceChoice
    {
        public string Id { get; }
        public string Name { get; }
        public BirthAppearance Token { get; }
        public string Group { get; }
        public BirthAppearance BaseAppearance { get; }
        internal readonly Action<GameObject,bool> BeforeLook;
        internal readonly Action<GameObject> AfterLook;
        public AppearanceChoice(string id,string name,BirthAppearance token,BirthAppearance baseAppearance,
            Action<GameObject,bool> beforeLook=null,Action<GameObject> afterLook=null)
            :this(id,name,token,baseAppearance,beforeLook,afterLook,null){}
        public AppearanceChoice(string id,string name,BirthAppearance token,BirthAppearance baseAppearance,
            Action<GameObject,bool> beforeLook,Action<GameObject> afterLook,string group)
        {Id=id;Name=name;Token=token;BaseAppearance=baseAppearance;BeforeLook=beforeLook;AfterLook=afterLook;Group=group;}
    }
    internal sealed class AppearanceLookTransaction : MonoBehaviour
    {
        internal BirthAppearance Appearance=BirthAppearance.Default;
        internal int Depth;
    }
    public static class AppearanceChoices
    {
        private static readonly List<AppearanceChoice> choices=new List<AppearanceChoice>();
        public static IReadOnlyList<AppearanceChoice> All=>choices.AsReadOnly();
        public static void Register(AppearanceChoice choice)
        {
            if(choice==null||string.IsNullOrWhiteSpace(choice.Id)||string.IsNullOrWhiteSpace(choice.Name)||(choice.Token.Body!=255||choice.Token.HairColor!=0)||!NativeValid(choice.BaseAppearance))
                throw new ArgumentException("An appearance needs a stable ID, name, reserved token and native base appearance.");
            if(choices.Any(c=>c.Id==choice.Id||c.Token==choice.Token))throw new ArgumentException("Appearance identity already registered: "+choice.Id);
            choices.Add(choice);choices.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
        }
        internal static bool Unregister(string id)=>choices.RemoveAll(c=>c.Id==id)>0;
        internal static bool NativeValid(BirthAppearance a)=>a.Body<5&&a.Eye<14&&a.Hair<10&&a.FacialHair<6&&a.Cloth<13&&a.Pant<5&&a.HairColor<8;
        public static AppearanceChoice Find(BirthAppearance appearance)=>choices.FirstOrDefault(c=>c.Token==appearance);
        internal static GameObject Root(SpumBirthAppearancePreview preview)=>preview.GetComponentInParent<BattleCombatantView>()?.gameObject??preview.gameObject;
        internal static void Begin(GameObject root,BirthAppearance appearance)
        {
            var transaction=root.GetComponent<AppearanceLookTransaction>()??root.AddComponent<AppearanceLookTransaction>();
            if(transaction.Depth++>0)return;
            transaction.Appearance=appearance;
            var actor=root.GetComponent<BattleCombatantView>();bool eligible=actor==null||actor.Kind==CombatantKind.Player&&actor.Side==CombatSide.Player;
            foreach(var choice in choices)choice.BeforeLook?.Invoke(root,eligible&&choice.Token==appearance);
        }
        internal static void BeginCurrent(GameObject root)=>Begin(root,root.GetComponent<AppearanceLookTransaction>()?.Appearance??BirthAppearance.Default);
        internal static void End(GameObject root)
        {
            var transaction=root.GetComponent<AppearanceLookTransaction>();
            if(transaction==null||transaction.Depth<=0||--transaction.Depth>0)return;
            foreach(var choice in choices)choice.AfterLook?.Invoke(root);
        }
        internal static void Clear(GameObject root)
        {
            var transaction=root.GetComponent<AppearanceLookTransaction>();
            if(transaction!=null){transaction.Depth=0;transaction.Appearance=BirthAppearance.Default;}
            foreach(var choice in choices)choice.BeforeLook?.Invoke(root,false);
        }
        internal static bool MatchesSave(BirthSelectionSaveDataV1 birth,AppearanceChoice choice)=>birth!=null&&
            birth.AppearanceBody==choice.Token.Body&&birth.AppearanceEye==choice.Token.Eye&&birth.AppearanceHair==choice.Token.Hair&&
            birth.AppearanceFacialHair==choice.Token.FacialHair&&birth.AppearanceCloth==choice.Token.Cloth&&birth.AppearancePant==choice.Token.Pant&&birth.AppearanceHairColor==choice.Token.HairColor;
        internal static void WriteSave(BirthSelectionSaveDataV1 b,BirthAppearance a)
        {b.AppearanceBody=a.Body;b.AppearanceEye=a.Eye;b.AppearanceHair=a.Hair;b.AppearanceFacialHair=a.FacialHair;b.AppearanceCloth=a.Cloth;b.AppearancePant=a.Pant;b.AppearanceHairColor=a.HairColor;}
    }
    internal static class MuyifengChoice
    {
        internal const string Id="appearance.muyifeng";
        // Extension namespace 255, ASCII MYF, version 1; none is a valid native body.
        internal static readonly BirthAppearance Token=new BirthAppearance(255,77,89,70,1,0,0);
        internal static void Register()=>SkinCatalog.Register(Id,"牧逸风",Token,"云灵剑宗",Art.Default,1000001);
    }
    [HarmonyPatch(typeof(BirthAppearance),nameof(BirthAppearance.IsValid),MethodType.Getter)]
    internal static class RegisteredAppearanceIsValid
    {
        private static void Postfix(BirthAppearance __instance,ref bool __result)
        {if(!__result&&AppearanceChoices.Find(__instance)!=null)__result=true;}
    }
    [HarmonyPatch]
    internal static class PreserveRegisteredSaveAppearance
    {
        private static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("Reincarnation.Domain.Save.RunSaveRepair"),"RepairAppearance");
        private static void Prefix(BirthSelectionSaveDataV1 birth,out AppearanceChoice __state)
        {__state=AppearanceChoices.All.FirstOrDefault(c=>AppearanceChoices.MatchesSave(birth,c));}
        private static void Postfix(BirthSelectionSaveDataV1 birth,AppearanceChoice __state)
        {if(__state!=null)AppearanceChoices.WriteSave(birth,__state.Token);}
    }
}
