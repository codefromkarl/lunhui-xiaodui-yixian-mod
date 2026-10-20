using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void CardCostTests()
    {
        Check(SectRegistry.BaseCost == 0 && SwordRules.PlayBaseCost == 0, "no universal qi fee");
        var owner = Player(5);
        var actor = new CombatantState(ActorId, new StableId("test.cost"), CombatantKind.Player,
            CombatSide.Player, FormationRow.Front, 300, 60, 0, 0, 0, BuffCatalog.Empty, new PlayerSlotId(1));
        foreach (var card in SectRegistry.Cards.Values)
        {
            int expected = 4 * ((int?)card["rules"]?["cost"] ?? 0);
            Check(SectCardRules.Cost(card, owner, actor, ModStateSnapshot.Empty) == expected,
                "single-card qi cost: " + card["name"]);
            Check(((string)card["description"]).Split('\n').Last() == "消耗："+expected+"斗气。",
                "displayed qi tier: " + card["name"]);
        }
        Check(SwordRules.ResourceCost(Card(1), 0, 0, false) == 0, "probe cloud has no lingqi fee");
        // Expectations are transcribed from inspected original faces, never derived
        // from the runtime card's own rules.cost (which previously hid bad input).
        var original = JObject.Parse(System.IO.File.ReadAllText("data/sources/original-correction/original-costs.json"))["cards"];
        foreach (var card in SwordRules.Cards.Values)
            Check(SectCardRules.Cost(card, owner, actor, ModStateSnapshot.Empty) == 4 * (int)original[(string)card["sourceId"]]["cost"], "original face / runtime cost: " + card["name"]);
        Check(SwordRules.ResourceCost(Card(21), 0, 0, false) == 4, "flying fang original face costs one lingqi");
        Check(SwordRules.ResourceCost(Card(9), 0, 0, false) == 4, "thunder sword original face costs one lingqi");
        Check(SwordRules.ResourceCost(Card(32), 0, 0, false) == 0, "three peaks original face has no lingqi fee");
        Check(SwordRules.ResourceCost(Card(29), 0, 0, false) == 8, "giant peng costs two lingqi");
        Check(SwordRules.ResourceCost(Card(36), 0, 0, false) == 12, "giant kun costs three lingqi");
        Check(SwordRules.ResourceCost(Card(36), 0, 0, true) == 8, "spirit discount subtracts one of three lingqi");
        Check(SwordRules.ResourceCost(Card(59), 0, 0, true) == 0, "spirit discount can reach zero");
        Check(SwordRules.ResourceCost(Card(25), 1, 0, false) == 0, "shield conditional exemption");
        Check(SwordRules.ResourceCost(Card(98), 0, 1, false) == 0, "cloud conditional exemption");
        Check(SwordRules.ResourceCost(Card(65), 0, 0, true) == 0, "discount never makes free recovery cost negative");
        Check(SwordRules.ResourceCost(Card(45), 0, 0, false) == 0, "all-qi finisher has no entry fee");
        var finisher = Run(SwordRules.Play, Id(Card(45)), actor: Unit(energy: 24));
        Check(finisher.Effects.Where(e => e.Kind == ModBattleEffectKind.Energy && e.Energy.Kind == ModEnergyOperationKind.Pay).Sum(e => e.Energy.Payment.Primary) == 24,
            "all-qi finisher still pays its explicitly consumed resource");
        Check(Effect(Run(SwordRules.Play, Id(Card(1)), State(("energyGuard", 1))), ModBattleEffectKind.Shield) == 0,
            "free attack does not generate phantom spending shield");
        Check(Effect(Run(SwordRules.Play, Id(Card(21)), State(("energyGuard", 1))), ModBattleEffectKind.Shield) == 4,
            "spending shield uses actual four qi cost");
        Check(Effect(Run(SwordRules.Play, Id(Card(32)), State(("energyGuard", 1))), ModBattleEffectKind.Shield) == 0,
            "original free multihit card gives no spending shield");
        var malformed = JObject.Parse(System.IO.File.ReadAllText(Mod + "/content/cardtable.json"));
        malformed["cards"][0]["rules"]["cost"] = -1;
        int before = SectRegistry.Cards.Count;
        try { SectRegistry.Configure(malformed); throw new Exception("negative lingqi fee accepted"); }
        catch (ArgumentException) { Check(SectRegistry.Cards.Count == before, "invalid fee rejected before registry mutation"); }
        Console.WriteLine("PASS card costs: original face ledger for 48 sword cards, 0/4/8/12 qi tiers, discounts, spending shields and explicit all-qi payment");
    }
}
