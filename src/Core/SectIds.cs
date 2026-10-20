namespace YixianSect
{
    public static class SectIds
    {
        public const string CardPrefix = "yixian.sect.card.";
        public static string Pack(string key) => "yixian.sect.pack." + key;
        public static string Fate(string lineage, string key) => "yixian.sect.fate." + lineage.Substring(lineage.LastIndexOf('.') + 1) + "." + key;
    }
}
