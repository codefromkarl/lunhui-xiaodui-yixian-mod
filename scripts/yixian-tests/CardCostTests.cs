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
            Check(System.Text.RegularExpressions.Regex.IsMatch((string)card["description"], @"消耗：[^\n]*，\s*"+expected+@"\s*斗气"),
                "displayed qi tier: " + card["name"]);
        }
        Check(SwordRules.ResourceCost(Card(1), 0, 0, false) == 0, "probe cloud has no lingqi fee");
        Check(SwordRules.ResourceCost(Card(32), 0, 0, false) == 4, "three peaks costs one lingqi");
        Check(SwordRules.ResourceCost(Card(36), 0, 0, false) == 8, "giant kun costs two lingqi");
        Check(SwordRules.ResourceCost(Card(36), 0, 0, true) == 4, "spirit discount subtracts one lingqi, not all cost");
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
        Check(Effect(Run(SwordRules.Play, Id(Card(32)), State(("energyGuard", 1))), ModBattleEffectKind.Shield) == 4,
            "spending shield uses actual four qi cost");
        var malformed = JObject.Parse(System.IO.File.ReadAllText(Mod + "/content/cardtable.json"));
        malformed["cards"][0]["rules"]["cost"] = -1;
        int before = SectRegistry.Cards.Count;
        try { SectRegistry.Configure(malformed); throw new Exception("negative lingqi fee accepted"); }
        catch (ArgumentException) { Check(SectRegistry.Cards.Count == before, "invalid fee rejected before registry mutation"); }
        Console.WriteLine("PASS card costs: 0/4/8 qi tiers, all-school routing, zero-cost discounts, spending shields and explicit all-qi payment");
    }
}
