using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void LoaderNamespaceChecks()
    {
        var asm = typeof(Reincarnation.Runtime.Content.ModEconomyManifestReader).Assembly;
        var loader = asm.GetType("Reincarnation.Runtime.Content.LocalModLoader");
        var sinkType = asm.GetType("Reincarnation.Runtime.Content.ModDiagnosticSink");
        var itemType = asm.GetType("Reincarnation.Runtime.Content.ModDiagnostic");
        var lines = new List<string>();
        var items = Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        var sink = PrivateCtor(sinkType, lines, items);
        var kinds = (System.Collections.IDictionary)loader.GetField("Kinds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        var catalog = new JObject();
        foreach (string kind in kinds.Keys) catalog[kind] = new JArray();
        var operations = new[] { "lineages", "abilities", "statuses", "fates", "fate-statuses" }.SelectMany(name => JObject.Parse(File.ReadAllText(Mod + "/content/" + name + ".json"))["operations"].OfType<JObject>()).ToArray();
        foreach (var op in operations.Where(o => o["copyFrom"] != null))
        {
            var rows = (JArray)catalog[(string)op["kind"]]; string copyFrom = (string)op["copyFrom"];
            if (!rows.OfType<JObject>().Any(r => (string)r["stableId"] == copyFrom)) rows.Add(new JObject { ["stableId"] = copyFrom, ["shortCode"] = 0 });
        }
        var writes = new Dictionary<string, string>(); var owners = new Dictionary<string, string>(); var added = new HashSet<string>();
        void Apply(JObject op) => Method(loader, "ApplyOperation").Invoke(null, new object[] { catalog, op, SectRegistry.Package, writes, sink, owners, added });
        foreach (var op in operations.Where(o => (string)o["kind"] == "statuses"))
            foreach (var effect in (op["fields"]["timedEffects"] as JArray ?? new JArray()).OfType<JObject>())
                Check(Enum.IsDefined(typeof(BuffAmountBasis), (byte)((int?)effect["basis"] ?? 0)), "timed effect basis must be explicit and native-valid: " + op["id"]);
        foreach (var op in operations) Apply(op);
        Check(lines.Count == 0, "native loader must emit no diagnostics for canonical declarations: " + string.Join("; ", lines));
        var probe = (JObject)operations.First(o => (string)o["kind"] == "abilities").DeepClone();
        probe["id"] = "ability.lineage.BL08.card.yixian_legacy_warning_probe";
        Apply(probe);
        Check(lines.Count == 1 && lines[0].Contains("没有以包身份"), "unmodified native loader still warns about an unnamespaced control");
        try { Apply(operations[0]); throw new Exception("native loader duplicate identity accepted"); }
        catch (System.Reflection.TargetInvocationException e) { Check(e.InnerException is FormatException && e.InnerException.Message.Contains("已存在"), "native duplicate identity check remains active"); }
        Console.WriteLine("PASS native loader: " + operations.Length + " canonical declarations without diagnostics, unnamespaced negative control warns, duplicate control rejects; isolated JSON seeds, not game startup");
    }
    static void IdentityTests()
    {
        LoaderNamespaceChecks();
        var ids = new HashSet<string>();
        foreach (var name in new[] { "abilities", "lineages", "statuses", "fates", "fate-statuses" })
            foreach (var op in JObject.Parse(File.ReadAllText(Mod + "/content/" + name + ".json"))["operations"])
                ids.Add((string)op["id"]);
        var table = JObject.Parse(File.ReadAllText(Mod + "/content/cardtable.json"));
        Check(table["legacyIds"] == null && table["identityVersion"] == null, "shipped table has no legacy mapping payload");
        Check(typeof(SectIds).GetProperty("Legacy") == null, "runtime identity helper has no legacy aliases");
        Check(!SectRegistry.IsSect("lineage.yixian.sword"), "old lineage IDs are not registered as aliases");
        foreach (var type in typeof(Entry).Assembly.GetTypes())
            foreach (HarmonyLib.HarmonyPatch patch in type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false))
                Check(patch.info.declaringType?.FullName != "Reincarnation.Domain.Save.RunSaveDataV1Mapper" && patch.info.declaringType?.FullName != "Reincarnation.Domain.Save.ProfileSaveDataV1Mapper", "no run/profile restore migration patch: " + type.Name);
        CardTableBridge.BuildCatalog(out var cards, out var packs);
        foreach (var original in CardTableCatalog.Cards) Check(cards.Any(c => ReferenceEquals(c, original)), "native card object unchanged: " + original.Key);
        foreach (var original in CardTableCatalog.Packs) Check(packs.Any(p => ReferenceEquals(p, original)), "native pack object unchanged: " + original.Key);
        foreach (var card in cards.Where(c => SectRegistry.Cards.ContainsKey(c.AbilityId.Value)))
            Check(card.AbilityId.Value == SectRegistry.CardPrefix + card.Key && ids.Contains(card.AbilityId.Value), "native card constructor projected to canonical identity");
        foreach (var pack in packs.Where(p => SectRegistry.Packs.Any(row => (string)row["key"] == p.Key)))
        {
            Check(pack.UnlockAbilityId.Value == SectIds.Pack(pack.Key) && ids.Contains(pack.UnlockAbilityId.Value), "native pack unlock uses canonical key");
            Check(pack.CardIds.Count == ((JArray)SectRegistry.FindPack(pack.UnlockAbilityId)["cards"]).Count && pack.CardIds.All(id => SectRegistry.Cards.ContainsKey(id.Value)), "native pack members use canonical IDs");
        }
        Console.WriteLine("PASS canonical identities: native catalog/card/pack references, original objects retained; no legacy mapping or run/profile mapper migration patches");
    }
}
