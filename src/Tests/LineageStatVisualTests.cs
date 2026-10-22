using System;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Managers;
using Reincarnation.UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal static partial class VisualSelfTests
    {
        internal static JObject LineageStatLadder(string directory, string school, RunPlayerSnapshot player, RunAuthoritySnapshot snapshot, DataManager data, BattleAuthorityCatalog battles)
        {
            Require(!Application.isBatchMode, "lineage visuals require rendered Unity");
            Directory.CreateDirectory(directory);
            var root = new GameObject("LineageStatVisual", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); root.SetActive(false);
            var cameraObject = new GameObject("LineageStatCamera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 30;
            var texture = new RenderTexture(1600, 900, 24); camera.targetTexture = texture; camera.aspect = 1600f / 900;
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            try
            {
                var source = Prefab<ShopWindow>(); Require(source != null, "native shop prefab missing");
                var shop = UnityEngine.Object.Instantiate(source, root.transform, false).GetComponent<ShopWindow>(); shop.enabled = false; shop.SetContext(player, snapshot, data, battles);
                var order = (Array)AccessTools.Field(typeof(ShopWindow), "CategoryOrder").GetValue(null); int index = -1;
                for (int i = 0; i < order.Length; i++) if (order.GetValue(i).ToString() == "Lineage") index = i;
                Require(index >= 0, "lineage category missing");
                AccessTools.Method(typeof(ShopWindow), "HandleCategory").Invoke(shop, new object[] { index });
                AccessTools.Field(typeof(ShopWindow), "selectedProgressionId").SetValue(shop, school);
                AccessTools.Method(typeof(ShopWindow), "RenderLadder").Invoke(shop, null);
                Layer(root.transform); root.SetActive(true); Canvas.ForceUpdateCanvases();
                Capture(camera, texture, root, Path.Combine(directory, "lineage-stats.png"));
                return new JObject { ["school"] = school, ["screenshot"] = "lineage-stats.png" };
            }
            finally { UnityEngine.Object.DestroyImmediate(root); camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(cameraObject); }
        }
    }
}
