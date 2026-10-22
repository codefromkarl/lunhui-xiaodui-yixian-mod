using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Runtime.Content;
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
        var operationDetailPatch = typeof(Entry).Assembly.GetType("YixianSect.SectAbilityOperationDetail");
        foreach(var file in new[]{"abilities","fates"})
            foreach(var row in JObject.Parse(File.ReadAllText(Mod+"/content/"+file+".json"))["operations"].OfType<JObject>())
            {
                string id=(string)row["id"];var pack=SectRegistry.FindPack(new StableId(id));
                if(!SectRegistry.Cards.ContainsKey(id)&&!SectRegistry.Fates.ContainsKey(id)&&pack==null)continue;
                var f=(JObject)row["fields"];var rank=(JObject)f["ranks"][0];
                var definition=new ContentAbilityDefinition(id,1,(string)f["displayName"],(string)f["sourceContentId"],(ContentAbilityMode)(int)f["mode"],new[]{new ContentAbilityRankDefinition((string)rank["rank"],1,Operation((JObject)rank["operation"]))},null,(string)f["flavour"],(int)f["purchasePrice"],(ContentAbilityAcquisition)(int)f["acquisition"]);
                var ability=(RuntimeAbilityDefinition)PrivateCtor(typeof(RuntimeAbilityDefinition),definition);
                string nativeText=RunUIPresentationMapper.BuildAbilityOperationDetail(ability,ability.Ranks[0].Operation,null);
                var actual=new object[]{ability,nativeText};
                Check(!(bool)Method(operationDetailPatch,"Prefix").Invoke(null,actual),"owned ability uses one authored description: "+id);
                string expected=SectRegistry.Cards.TryGetValue(id,out var c)?SectDescriptions.CardDetail(c):SectRegistry.Fates.TryGetValue(id,out var fate)?(string)fate["description"]+"\n独立被动，本局限购一次，不占手牌。":SectDescriptions.PackDetail(pack);
                Check((string)actual[1]==expected,"no duplicated flavour, cost or acquisition: "+id);
            }
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
