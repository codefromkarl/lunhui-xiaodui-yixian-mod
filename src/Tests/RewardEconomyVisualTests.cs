using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal static partial class VisualSelfTests
    {
        internal static JObject RewardEconomyPrices(string directory,RunPlayerSnapshot player,RunAuthoritySnapshot snapshot,DataManager data,BattleAuthorityCatalog battles)
        {
            Require(!Application.isBatchMode,"reward-price visuals require a rendered Unity process");Directory.CreateDirectory(directory);
            var root=new GameObject("RewardPriceVisual",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.SetActive(false);
            var cameraObject=new GameObject("RewardPriceCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;
            var texture=new RenderTexture(1600,900,24);camera.targetTexture=texture;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);
            try
            {
                var source=Prefab<ShopWindow>();Require(source!=null,"reward-price shop prefab missing");
                var shop=UnityEngine.Object.Instantiate(source,root.transform,false).GetComponent<ShopWindow>();shop.enabled=false;shop.SetContext(player,snapshot,data,battles);
                var order=(Array)AccessTools.Field(typeof(ShopWindow),"CategoryOrder").GetValue(null);int index=-1;
                for(int i=0;i<order.Length;i++)if(order.GetValue(i).ToString()=="Lineage")index=i;
                Require(index>=0,"lineage category missing");AccessTools.Method(typeof(ShopWindow),"HandleCategory").Invoke(shop,new object[]{index});
                AccessTools.Field(typeof(ShopWindow),"selectedProgressionId").SetValue(shop,SwordRules.Lineage);AccessTools.Method(typeof(ShopWindow),"RenderLadder").Invoke(shop,null);
                Layer(root.transform);root.SetActive(true);Canvas.ForceUpdateCanvases();
                string Text()=>System.Text.RegularExpressions.Regex.Replace(string.Join("\n",shop.GetComponentsInChildren<TMP_Text>(true).Where(t=>t.gameObject.activeInHierarchy).Select(t=>t.text)),"<[^>]*>|\\s","");
                foreach(int price in new[]{15,55,130,240,380})Require(System.Text.RegularExpressions.Regex.IsMatch(Text(),@"(?<!\d)"+price+@"(?:奖励)?点"),"actual shop omitted lineage price "+price);
                Capture(camera,texture,root,Path.Combine(directory,"lineage-prices.png"));
                AccessTools.Method(typeof(ShopWindow),"HandleProgressionAbilityTab").Invoke(shop,null);Layer(root.transform);Canvas.ForceUpdateCanvases();
                Require(System.Text.RegularExpressions.Regex.Matches(Text(),@"(?<!\d)20(?:奖励)?点").Count>=4,"actual fate shelf does not show four separate twenty-point prices");
                Capture(camera,texture,root,Path.Combine(directory,"fate-prices.png"));
                return new JObject{["lineage_prices_visible"]=new JArray(15,55,130,240,380),["fate_price_visible"]=20,["screenshots"]=new JArray("lineage-prices.png","fate-prices.png")};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
