using System;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    public sealed class Entry : ICodeMod
    {
        internal static ICodeModContext Ctx;
        public void OnLoad(ICodeModContext context)
        {
            Ctx=context;
            var beforeCards=CardTableCatalog.Cards;var beforePacks=CardTableCatalog.Packs;
            try
            {
                var table=context.Tables?["cardtable"] as JObject ?? throw new ArgumentException("缺少 tables.cardtable。");
                SwordRules.Configure(table);
                FiveCardSelection.Configure(JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(context.PackageDirectory,"content","five-water-pilot.json"))));
                CardTableBridge.InjectCatalog();
                new Harmony(context.PackageId).PatchAll(typeof(Entry).Assembly);
                OriginalHealthRules.Register(context);
                SwordRules.Register(context);
                FateRules.Register(context);
                FiveElementRules.Register(context);
                SevenStarRules.Register(context);
                RuntimeSelfTests.InstallIfRequested(context);
                FateRuntimeTests.InstallIfRequested(context);
                context.Log("弈仙门派：登记 "+SectRegistry.Sects.Count+" 门派、"+SectRegistry.Cards.Count+" 张卡、"+SectRegistry.Packs.Count+" 个卡包。");
            }
            catch(Exception ex)
            {
                new Harmony(context.PackageId).UnpatchAll(context.PackageId);
                typeof(CardTableCatalog).GetField("Cards").SetValue(null,beforeCards);
                typeof(CardTableCatalog).GetField("Packs").SetValue(null,beforePacks);
                context.Warn("弈仙门派加载失败："+ex);throw;
            }
        }
    }
}
