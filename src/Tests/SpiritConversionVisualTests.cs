using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal static partial class VisualSelfTests
    {
        private sealed class SpiritToggleSink : IProjectUIIntentSink
        {
            internal Action<ProjectUIIntent> Submit;
            public bool TrySubmit(ProjectUIIntent intent,out string error) { error="";Submit(intent);return true; }
        }
        internal static JObject SpiritConversionShelf(string directory,RunPlayerSnapshot player,RunAuthoritySnapshot snapshot,DataManager data,BattleAuthorityCatalog battles,Func<bool,RunAuthoritySnapshot> toggle)
        {
            Require(!Application.isBatchMode,"spirit-conversion visuals require a rendered Unity process");Directory.CreateDirectory(directory);
            var root=new GameObject("SpiritConversionVisual",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.SetActive(false);
            var cameraObject=new GameObject("SpiritConversionCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            try
            {
                var source=Prefab<ShopWindow>();Require(source!=null,"spirit-conversion shop prefab missing");
                var shop=UnityEngine.Object.Instantiate(source,root.transform,false).GetComponent<ShopWindow>();shop.enabled=false;shop.SetContext(player,snapshot,data,battles);
                var order=(Array)AccessTools.Field(typeof(ShopWindow),"CategoryOrder").GetValue(null);int index=-1;
                for(int i=0;i<order.Length;i++)if(order.GetValue(i).ToString()=="Lineage")index=i;
                Require(index>=0,"lineage category missing");AccessTools.Method(typeof(ShopWindow),"HandleCategory").Invoke(shop,new object[]{index});
                AccessTools.Field(typeof(ShopWindow),"selectedProgressionId").SetValue(shop,SwordRules.Lineage);AccessTools.Method(typeof(ShopWindow),"RenderLadder").Invoke(shop,null);
                Layer(root.transform);root.SetActive(true);Canvas.ForceUpdateCanvases();
                string Text()=>System.Text.RegularExpressions.Regex.Replace(string.Join("\n",shop.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy).Select(t=>t.text)),"<[^>]*>|\\s","");
                AccessTools.Method(typeof(ShopWindow),"HandleProgressionAbilityTab").Invoke(shop,null);
                AccessTools.Method(typeof(ShopWindow),"HandleShelfSelected").Invoke(shop,new object[]{SpiritConversion.Ability});
                Layer(root.transform);Canvas.ForceUpdateCanvases();
                string visible=Text();
                Require(visible.Contains("灵剑转化")&&visible.Contains("持续被动")&&visible.Contains("后续加点")&&visible.Contains("各扣除60%")&&visible.Contains("实际扣除量")&&visible.Contains("不转换战斗临时属性"),"actual native ability detail missing debit/transfer explanation");
                var entry=shop.GetComponentsInChildren<Reincarnation.UI.Nodes.ExchangeEntry>(true).Single(e=>e.CurrentViewModel?.StableId==SpiritConversion.Ability);
                var price=(TMP_Text)AccessTools.Field(typeof(Reincarnation.UI.Nodes.ExchangeEntry),"priceLabel").GetValue(entry);
                var body=(TMP_Text)AccessTools.Field(typeof(ShopWindow),"shelfRequestBodyLabel").GetValue(shop);
                Require(price.gameObject.activeInHierarchy&&System.Text.RegularExpressions.Regex.IsMatch(System.Text.RegularExpressions.Regex.Replace(price.text,"<[^>]*>|\\s",""),@"(?<!\d)20(?:奖励)?点"),"selected conversion price is not twenty: active="+price.gameObject.activeInHierarchy+" text="+price.text);
                Require(System.Text.RegularExpressions.Regex.Matches(body.text,"各扣除60%").Count==1,"native conversion description duplicated");
                var corners=new Vector3[4];body.rectTransform.GetWorldCorners(corners);
                Require(corners.All(p=>{var screen=camera.WorldToScreenPoint(p);return screen.x>=0&&screen.x<=1600&&screen.y>=0&&screen.y<=900;}),"ability detail extends outside native safe canvas");
                Capture(camera,texture,root,Path.Combine(directory,"spirit-conversion.png"));
                var next=toggle(false);int clicks=0;
                shop.SetIntentSink(new SpiritToggleSink { Submit=intent=>{
                    Require(intent.Kind==ProjectUIIntentKind.PurchaseShelfAbility
                        && (intent.PrimaryValue==SpiritConversion.ToggleOn||intent.PrimaryValue==SpiritConversion.ToggleOff),"toggle UI submitted wrong command");
                    clicks++;next=toggle(intent.PrimaryValue==SpiritConversion.ToggleOn);
                    shop.NotifySubmissionSettled(SessionErrorCode.None);
                }});
                AccessTools.Method(typeof(ShopWindow),"BindListeners").Invoke(shop,null);
                foreach(bool enabled in new[]{false,true,false})
                {
                    shop.SetContext(next.Players.First(p=>p.Slot==player.Slot),next,data,battles);
                    AccessTools.Field(typeof(ShopWindow),"selectedProgressionId").SetValue(shop,SwordRules.Lineage);
                    AccessTools.Method(typeof(ShopWindow),"HandleProgressionAbilityTab").Invoke(shop,null);
                    AccessTools.Method(typeof(ShopWindow),"HandleShelfSelected").Invoke(shop,new object[]{SpiritConversion.Ability});
                    var button=(Button)AccessTools.Field(typeof(ShopWindow),"shelfRequestButton").GetValue(shop);
                    var label=(TMP_Text)AccessTools.Field(typeof(ShopWindow),"shelfRequestButtonLabel").GetValue(shop);
                    Require(button.interactable&&label.text==(enabled?"关闭转化":"开启转化"),"owned native ability button does not offer opposite action");
                    Require(Text().Contains(enabled?"当前状态：已开启":"当前状态：已关闭"),"ability toggle status missing");
                    Layer(root.transform);Canvas.ForceUpdateCanvases();
                    Capture(camera,texture,root,Path.Combine(directory,enabled?"spirit-enabled.png":"spirit-disabled.png"));
                    if(clicks<2){button.onClick.Invoke();Require(clicks==(enabled?2:1),"native toggle button did not submit once");}
                }
                return new JObject{["owned_toggle_states_visible"]=true,["native_button_clicks"]=clicks,["debit_and_transfer_description_visible"]=true,["twenty_point_price_visible"]=true,["screenshot"]="spirit-conversion.png"};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
