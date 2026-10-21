using Newtonsoft.Json.Linq;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        // Current probes no longer use the retired five-pack recommendation UI.
        private JObject BuildAdjustmentContract() => FiveFixedDeckContract();
    }
}
