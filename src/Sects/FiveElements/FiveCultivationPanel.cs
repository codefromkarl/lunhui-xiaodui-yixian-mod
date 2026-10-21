using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    // Read-only status beside native tabs: no selector, modal or confirmation.
    internal static class FiveCultivationPanel
    {
        private static readonly ConditionalWeakTable<ShopWindow, TMP_Text> Labels = new ConditionalWeakTable<ShopWindow, TMP_Text>();
        private static T Read<T>(object owner, string name) => (T)AccessTools.Field(owner.GetType(), name).GetValue(owner);
        internal static void Render(ShopWindow window)
        {
            var player = Read<RunPlayerSnapshot>(window, "player");
            bool visible = Read<string>(window, "selectedProgressionId") == FiveElementRules.Lineage
                && player != null && player.Lineages.Any(l => l.LineageId.Value == FiveElementRules.Lineage);
            if (!visible) { if (Labels.TryGetValue(window, out var old)) old.gameObject.SetActive(false); return; }
            Read<TMP_Text>(window, "ladderUpgradeRequestFlavourLabel").gameObject.SetActive(false);
            var tab = (RectTransform)Read<Button>(window, "ladderAbilityTabButton").transform;
            var label = Labels.GetValue(window, w => FiveCardPicker.Text("FiveAutomaticPrimary", tab.parent,
                Read<TMP_Text>(w, "freePickTitleLabel").font, "", 21, Vector2.zero, Vector2.one));
            label.gameObject.SetActive(true); label.raycastTarget = false;
            label.text = "自动主属性：" + FiveCultivation.AttributeName(FiveCultivation.SelectPrimary(player.DerivedStats.EffectiveAttributes));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var rect = label.rectTransform;
            rect.anchorMin = tab.anchorMin; rect.anchorMax = tab.anchorMax; rect.pivot = tab.pivot;
            rect.sizeDelta = new Vector2(240, tab.rect.height);
            rect.anchoredPosition = tab.anchoredPosition + new Vector2(tab.rect.width / 2 + 12 + 120, 0);
        }
    }
    [HarmonyPatch(typeof(ShopWindow), "RenderLadder")]
    internal static class FiveCultivationShop
    {
        private static void Postfix(ShopWindow __instance) => FiveCultivationPanel.Render(__instance);
    }
}
