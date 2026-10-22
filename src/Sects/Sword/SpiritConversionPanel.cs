using System.Linq;
using HarmonyLib;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine.UI;

namespace YixianSect
{
    // Reuse the selected ability's native request area, including pending/error
    // handling. Merely selecting the shelf entry never enables the conversion.
    internal static class SpiritConversionPanel
    {
        private static T Read<T>(ShopWindow window, string field)
            => (T)AccessTools.Field(typeof(ShopWindow), field).GetValue(window);
        internal static bool SelectedOwned(ShopWindow window)
            => Read<string>(window, "selectedShelfAbilityId") == SpiritConversion.Ability
                && Read<RunPlayerSnapshot>(window, "player")?.PurchasedShelfAbilityIds
                    .Contains(new StableId(SpiritConversion.Ability)) == true;
        internal static ModRunChoiceOptionView Target(ShopWindow window)
        {
            var choice = Read<RunAuthoritySnapshot>(window, "snapshot")?.ModChoices
                .FirstOrDefault(c => c.PackageId == SectRegistry.Package && c.Id.Value == SpiritConversion.Ability);
            bool enabled = choice?.Options.Any(o => o.Id.Value == "on" && o.Selected) == true;
            return choice?.Options.FirstOrDefault(o => o.Id.Value == (enabled ? "off" : "on"));
        }
        internal static void Render(ShopWindow window)
        {
            if (!SelectedOwned(window)) return;
            var option = Target(window);
            bool enabled = option?.Id.Value == "off";
            Read<TMP_Text>(window, "shelfRequestButtonLabel").text = enabled ? "关闭转化" : "开启转化";
            var sub = Read<TMP_Text>(window, "shelfRequestButtonSubLabel");
            sub.gameObject.SetActive(true);
            sub.text = "当前" + (enabled ? "已开启" : "已关闭") + " · 免费切换";
            Read<TMP_Text>(window, "shelfRequestBodyLabel").text += "\n当前状态：" + (enabled ? "已开启" : "已关闭（未转化）");
            Read<Button>(window, "shelfRequestButton").interactable = option?.CanSelect == true
                && !Read<bool>(window, "exchangeSubmissionPending");
        }
        internal static bool Handle(ShopWindow window)
        {
            if (!SelectedOwned(window)) return true;
            var option = Target(window);
            if (option?.CanSelect == true && !Read<bool>(window, "exchangeSubmissionPending"))
            {
                var intent = new ProjectUIIntent(ProjectUIIntentKind.PurchaseShelfAbility,
                    option.Id.Value == "on" ? SpiritConversion.ToggleOn : SpiritConversion.ToggleOff,
                    "", 0, false, "", 0, 0, -1L, -1L);
                AccessTools.Method(typeof(ShopWindow), "SubmitExchangeIntent").Invoke(window, new object[] { intent, null });
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(ShopWindow), "RenderShelfRequest")]
    internal static class SpiritConversionShopRender
    { private static void Postfix(ShopWindow __instance) => SpiritConversionPanel.Render(__instance); }
    [HarmonyPatch(typeof(ShopWindow), "HandleShelfPurchase")]
    internal static class SpiritConversionShopClick
    { private static bool Prefix(ShopWindow __instance) => SpiritConversionPanel.Handle(__instance); }
}
