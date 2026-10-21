using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // One registry owns the native catalog. School modules never patch it independently.
    public static class SectRegistry
    {
        public const string Package = "yixian.sect";
        public const string CardPrefix = SectIds.CardPrefix;
        public static readonly Dictionary<string, JObject> Cards = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public static readonly List<JObject> Packs = new List<JObject>();
        public static readonly Dictionary<string, JObject> Fates = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public static readonly Dictionary<string, JObject> Sects = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public static int BaseCost { get; private set; } = 0;
        public static string Owner(JObject row) => (string)row["sect"] ?? SwordRules.Lineage;
        public static bool IsSect(string id) => Sects.ContainsKey(id);
        public static JObject Definition(string id) => Sects.TryGetValue(id, out var s) ? s : null;
        public static string Code(string id) => (string)Definition(id)?["code"] ?? "";
        public static string Prefix(string id) => (string)Definition(id)?["statePrefix"] ?? "sword";
        public static int Rank(PlayerRunState player, string id) => player.Lineages.Where(l => l.LineageId.Value == id).Select(l => l.Rank).DefaultIfEmpty().Max();
        public static int Rank(ModCombatantSnapshot actor, string id) => actor?.Loadout.Lineages.Where(l => l.Id.Value == id).Select(l => l.Count).DefaultIfEmpty().Max() ?? 0;
        public static bool CanAcquire(PlayerRunState player, string id) => !IsSect(id) || !player.Lineages.Any(l => IsSect(l.LineageId.Value) && l.LineageId.Value != id);
        public static StableId PackId(JObject pack) => new StableId(SectIds.Pack((string)pack["key"]));
        public static JObject FindPack(StableId id) => Packs.SingleOrDefault(p => PackId(p) == id);
        public static string StateKey(string sect, StableId actor, string key) => Prefix(sect) + "." + actor.Value + "." + key;
        public static int Read(ModStateSnapshot state, string key) => state?.Entries.FirstOrDefault(e => e.PackageId == Package && e.Key == key)?.Number ?? 0;
        public static string Summary(string sect) => (string)Definition(sect)?["summary"] ?? "";

        public static void Configure(JObject table)
        {
            var cards = ((JArray)table["cards"]).OfType<JObject>().ToList();
            var packs = ((JArray)table["packs"]).OfType<JObject>().ToList();
            var sects = (table["sects"] as JArray ?? new JArray(new JObject { ["id"] = SwordRules.Lineage, ["name"] = "云灵剑宗", ["code"] = "yixian_sword", ["statePrefix"] = "sword" })).OfType<JObject>().ToList();
            var ids = new HashSet<string>(sects.Select(s => (string)s["id"]));
            if (ids.Count != sects.Count || cards.Select(c => (string)c["key"]).Distinct().Count() != cards.Count || packs.Select(p => (int)p["index"]).Distinct().Count() != packs.Count || packs.Select(p => (string)p["key"]).Distinct().Count() != packs.Count)
                throw new ArgumentException("门派／卡牌／卡包身份重复。");
            foreach (var card in cards)
            {
                if (!ids.Contains(Owner(card)) || (int)card["rank"] < 1 || (int)card["rank"] > 5) throw new ArgumentException("卡牌门派或境界无效。");
                var fee = card["rules"]?["cost"];
                if (fee != null && (fee.Type != JTokenType.Integer || (long)fee < 0 || (long)fee > int.MaxValue / SectCardRules.QiPerLingqi)) throw new ArgumentException("单卡灵气费用必须为非负整数，且换算后不能溢出。");
            }
            foreach (var pack in packs)
            {
                var keys = ((JArray)pack["cards"]).Values<string>().ToList();
                if (!ids.Contains(Owner(pack)) || (Owner(pack) == SwordRules.Lineage ? keys.Count != 3 && keys.Count != 4 : keys.Count != 4) || keys.Distinct().Count() != keys.Count || (int)pack["price"] < 0) throw new ArgumentException("卡包声明无效。");
                foreach (var key in keys)
                {
                    var card = cards.SingleOrDefault(c => (string)c["key"] == key);
                    if (card == null || Owner(card) != Owner(pack) || (int)card["rank"] != (int)pack["rank"]) throw new ArgumentException("卡包跨门派／跨境界或缺牌：" + key);
                }
            }
            var fates = (table["fates"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            var fateIds = fates.Select(f => SectIds.Fate(Owner(f), (string)f["key"])).ToList();
            if (fateIds.Distinct().Count() != fateIds.Count || fates.Any(f => !ids.Contains(Owner(f)) || ((int)f["rank"] != 2 && (int)f["rank"] != 3) || (int)f["price"] != 20))
                throw new ArgumentException("独立仙命身份、境界或价格无效。");
            int cost = (int?)table["playBaseCost"] ?? 0;
            if (cost != 0) throw new ArgumentException("门派卡牌不收取基础斗气费用。");
            Cards.Clear(); Packs.Clear(); Fates.Clear(); Sects.Clear(); BaseCost = cost;
            foreach (var sect in sects) Sects.Add((string)sect["id"], sect);
            foreach (var card in cards) Cards.Add(CardPrefix + (string)card["key"], card);
            Packs.AddRange(packs);
            for(int i=0;i<fates.Count;i++)Fates.Add(fateIds[i],fates[i]);
        }
    }
}
