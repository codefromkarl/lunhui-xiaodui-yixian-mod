using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.Runtime.Run;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    // Use the native character ability detail and native mod-choice transaction.
    // The ability's icon key is stable even when its display name is localized.
    internal static class SpiritConversionCharacterPanel
    {
        private sealed class Controls { internal Button Button; }
        private static readonly ConditionalWeakTable<InventoryWindow,Controls> Views=new ConditionalWeakTable<InventoryWindow,Controls>();
        private static T Read<T>(InventoryWindow window,string name)=>(T)AccessTools.Field(typeof(InventoryWindow),name).GetValue(window);
        private static IRunCommandGateway Gateway(InventoryWindow window)=>Read<IRunCommandGateway>(window,"modChoiceGateway");
        internal static void Render(InventoryWindow window)
        {
            var keys=Read<List<string>>(window,"abilityIconKeys");int index=Read<int>(window,"selectedAbilityIndex");
            var gateway=Gateway(window);var snapshot=gateway?.CurrentSnapshot;
            var player=snapshot?.Players.FirstOrDefault(p=>p.Slot==gateway.LocalPlayerSlot);
            bool selected=index>=0&&index<keys.Count&&keys[index]=="icon."+SpiritConversion.Ability
                &&player?.PurchasedShelfAbilityIds.Contains(new StableId(SpiritConversion.Ability))==true;
            if(!selected){if(Views.TryGetValue(window,out var previous))previous.Button.gameObject.SetActive(false);return;}
            var meta=Read<TMP_Text>(window,"abilityDetailMetaLabel");
            var controls=Views.GetValue(window,w=>{
                var button=FiveCardPicker.Button("SpiritConversionToggle",meta.transform.parent,meta.font,"",new Vector2(.55f,.08f),new Vector2(.98f,.92f),()=>Click(w));
                return new Controls{Button=button};
            });
            controls.Button.gameObject.SetActive(true);
            var choice=snapshot.ModChoices.FirstOrDefault(c=>c.PackageId==SectRegistry.Package&&c.Id.Value==SpiritConversion.Ability);
            bool enabled=choice?.Options.Any(o=>o.Id.Value=="on"&&o.Selected)==true;
            var option=choice?.Options.FirstOrDefault(o=>o.Id.Value==(enabled?"off":"on"));
            bool pending=Read<ulong>(window,"modChoicePendingSequence")!=0||Read<bool>(window,"modChoiceSubmitting");
            meta.text="当前："+(enabled?"已开启":"已关闭");
            controls.Button.GetComponentInChildren<TMP_Text>(true).text=pending?"切换中…":enabled?"关闭转化":"开启转化";
            controls.Button.interactable=gateway.IsAvailable&&!pending&&option?.CanSelect==true;
            var body=Read<TMP_Text>(window,"abilityDetailBodyLabel");
            body.text=SpiritConversion.Description+"\n\n"+(option?.CanSelect==true?"点击上方按钮可免费切换。":"返回主神空间后可切换。");
        }
        private static void Click(InventoryWindow window)
        {
            var gateway=Gateway(window);var snapshot=gateway?.CurrentSnapshot;
            if(snapshot==null||!gateway.IsAvailable||Read<ulong>(window,"modChoicePendingSequence")!=0||Read<bool>(window,"modChoiceSubmitting"))return;
            var choice=snapshot.ModChoices.FirstOrDefault(c=>c.PackageId==SectRegistry.Package&&c.Id.Value==SpiritConversion.Ability);
            bool enabled=choice?.Options.Any(o=>o.Id.Value=="on"&&o.Selected)==true;
            var option=choice?.Options.FirstOrDefault(o=>o.Id.Value==(enabled?"off":"on"));
            if(option?.CanSelect!=true)return;
            AccessTools.Method(typeof(InventoryWindow),"SubmitModChoice").Invoke(window,new object[]{choice,option,snapshot.Revision});
            AccessTools.Method(typeof(InventoryWindow),"RefreshCharacterFromCurrentSnapshot").Invoke(window,null);
            Render(window);
        }
    }
    [HarmonyPatch]
    internal static class SpiritConversionCharacterRefresh
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(string name in new[]{"RefreshAbilitySelection","RefreshModChoicesFromCurrentSnapshot"})
                yield return AccessTools.Method(typeof(InventoryWindow),name);
        }
        private static void Postfix(InventoryWindow __instance)=>SpiritConversionCharacterPanel.Render(__instance);
    }
}
