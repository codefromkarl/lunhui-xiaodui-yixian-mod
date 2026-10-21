using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.ViewModels;
using YixianSect;

partial class Program
{
    static void DescriptionTests()
    {
        foreach (var card in SectRegistry.Cards.Values)
        {
            var summary = SectDescriptions.CardSummary(card);
            Check(!string.IsNullOrWhiteSpace(summary) && summary.Length <= 80 && !summary.Contains('\n'), "authored compact card summary: " + card["name"]);
        }
        foreach (var card in SectRegistry.Cards.Values)
            Check(SectDescriptions.CardDetail(card) == (string)card["description"], "runtime detail stays concise instead of appending source metadata: " + card["name"]);
        var refuge = SectRegistry.Cards.Values.Single(c => (string)c["name"] == "金蝉脱壳");
        Check(SectDescriptions.CardSummary(refuge).Contains("护盾") && SectDescriptions.CardSummary(refuge).Contains("生命") && SectDescriptions.CardSummary(refuge).Contains("后招"), "summary retains support and conditional effects, not just first line");
        var blade = SectRegistry.Cards.Values.Single(c => (string)c["name"] == "金灵·回锋刃");
        Check(SectDescriptions.CardSummary(blade).Contains("锋锐"), "summary retains core blade mechanic");
        var tiger = SectRegistry.Cards.Values.Single(c => (string)c["name"] == "星弈·虎");
        Check(((string)tiger["description"]).Contains("星位：对方获得1层虚弱；虚弱每个宿主回合末减1层。") && !((string)tiger["description"]).Contains("持续1回合"), "tiger position weakness states per-stack decay, not whole-status expiration");
        foreach (var sect in SectRegistry.Sects.Keys)
        {
            var rules = SectDescriptions.RulesFor(sect);
            Check(!string.IsNullOrWhiteSpace(rules) && SectDescriptions.RankFor(sect, 1).Contains(rules), "opening school description exposes shared keywords: " + sect);
        }
        // Exercise the actual summary prefix with a native card object; CoreCLR does not detour Harmony.
        CardTableBridge.BuildCatalog(out var nativeCards, out var _);
        var native = nativeCards.Single(c => c.AbilityId.Value == SectRegistry.CardPrefix + (string)refuge["key"]);
        var patch = typeof(Entry).Assembly.GetType("YixianSect.SectCardSummary");
        var args = new object[] { native, "old native summary" };
        Check(!(bool)Method(patch, "Prefix").Invoke(null, args) && (string)args[1] == SectDescriptions.CardSummary(refuge), "native narrator uses complete authored summary");
        args = new object[] { native, "old native detail" };
        var detailPatch = typeof(Entry).Assembly.GetType("YixianSect.SectCardDetail");
        Check(!(bool)Method(detailPatch, "Prefix").Invoke(null, args) && (string)args[1] == SectDescriptions.CardDetail(refuge), "native scrolling card detail uses the concise actual effect");
        var commandPatch = typeof(Entry).Assembly.GetType("YixianSect.SectCardCommand");
        bool Classify(string id)
        {
            var action = id == null ? null : new ActionCardViewModel(id, id, "", "", null, 0, true, false, false, actionKind: CombatActionKind.UseAbility);
            var result = new object[] { action, BattleUIPresentationMapper.IsCardTableCommand(action) };
            Method(commandPatch, "Postfix").Invoke(null, result);
            return (bool)result[1];
        }
        foreach (var id in SectRegistry.Cards.Keys) Check(Classify(id), "canonical card belongs to Cards tab: " + id);
        Check(Classify(CardTableRules.DrawActionId.Value) && Classify("ability.lineage.BL08.card.fireball"), "native card classification preserved");
        Check(!Classify(null) && !Classify("ability.other.test") && !Classify(SectRegistry.CardPrefix + "unregistered"), "null and unrelated abilities retain native classification");
        Console.WriteLine("PASS descriptions: 136 compact summaries, meaningful condition/support copy, shared school rules and native narrator routing");
    }
}
