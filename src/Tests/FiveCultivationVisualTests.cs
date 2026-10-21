using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace YixianSect
{
    internal static partial class VisualSelfTests
    {
        internal static JObject FiveCultivationControls(string directory, RunPlayerSnapshot player, RunAuthoritySnapshot snapshot, DataManager data, BattleAuthorityCatalog battles)
        {
            if (Application.isBatchMode) return new JObject { ["status"] = "not_run", ["reason"] = "graphical cultivation controls require rendered Unity" };
            Directory.CreateDirectory(directory);
            var root = new GameObject("FiveCultivationVisualTest", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); root.SetActive(false);
            var cameraObject = new GameObject("FiveCultivationCamera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
            var texture = new RenderTexture(1600, 900, 24); camera.targetTexture = texture;
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            root.AddComponent<GraphicRaycaster>().enabled = false;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900);
            try
            {
                var source = Prefab<ShopWindow>(); Require(source != null, "cultivation shop prefab missing");
                var shop = UnityEngine.Object.Instantiate(source, root.transform, false).GetComponent<ShopWindow>(); shop.enabled = false;
                foreach (var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true)) raycaster.enabled = false;
                shop.SetContext(player, snapshot, data, battles);
                var order = (Array)AccessTools.Field(typeof(ShopWindow), "CategoryOrder").GetValue(null);
                int index = -1; for (int i = 0; i < order.Length; i++) if (order.GetValue(i).ToString() == "Lineage") index = i;
                Require(index >= 0, "native lineage category unavailable");
                AccessTools.Method(typeof(ShopWindow), "HandleCategory").Invoke(shop, new object[] { index });
                AccessTools.Field(typeof(ShopWindow), "selectedProgressionId").SetValue(shop, FiveElementRules.Lineage);
                AccessTools.Method(typeof(ShopWindow), "RenderLadder").Invoke(shop, null);
                Require(!shop.GetComponentsInChildren<Button>(true).Any(b => b.name == "FiveCultivationOpen"), "manual cultivation button remains");
                Require(shop.transform.Find("FiveCultivationSelector") == null, "manual cultivation modal remains");
                var label = shop.GetComponentsInChildren<TMP_Text>(true).Single(t => t.name == "FiveAutomaticPrimary");
                Require(label.text == "自动主属性：反应" && !label.raycastTarget, "read-only primary status incorrect");
                Layer(root.transform); root.SetActive(true); Canvas.ForceUpdateCanvases();
                var tab = Field<Button>(shop, "ladderAbilityTabButton");
                Require(!HudRect((RectTransform)root.transform, label.rectTransform).Overlaps(HudRect((RectTransform)root.transform, (RectTransform)tab.transform)), "automatic status overlaps native tab");
                Capture(camera, texture, root, Path.Combine(directory, "automatic-primary.png"));
                var alternate = new RenderTexture(1280, 960, 24);
                try
                {
                    camera.targetTexture = alternate; Capture(camera, alternate, root, Path.Combine(directory, "automatic-primary-4x3.png"));
                    label.ForceMeshUpdate(); Require(!label.isTextOverflowing, "automatic primary status overflow");
                }
                finally { camera.targetTexture = texture; UnityEngine.Object.DestroyImmediate(alternate); }
                AccessTools.Field(typeof(ShopWindow), "selectedProgressionId").SetValue(shop, SwordRules.Lineage);
                AccessTools.Method(typeof(ShopWindow), "RenderLadder").Invoke(shop, null);
                Require(!label.gameObject.activeSelf, "Five status leaked into other lineage");
                return new JObject { ["manual_selector_removed"] = true, ["automatic_status"] = label.text,
                    ["status_noninteractive"] = true, ["native_tab_overlap"] = false, ["other_lineages_unchanged"] = true,
                    ["screenshots"] = new JArray("automatic-primary.png", "automatic-primary-4x3.png") };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); }
        }
    }
}
