using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace SoftwareTrade
{
    [FileLocation(nameof(SoftwareTrade))]
    [SettingsUIGroupOrder(kGeneralGroup, kDiagnosticsGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kDiagnosticsGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kDiagnosticsGroup = "Diagnostics";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableTradePatch { get; set; }

        [SettingsUISection(kSection, kDiagnosticsGroup)]
        public bool VerboseLogging { get; set; }

        public override void SetDefaults()
        {
            EnableTradePatch = true;
            VerboseLogging = false;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "SoftwareTrade" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDiagnosticsGroup), "Diagnostics" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableTradePatch)), "Enable office resource trade patch" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableTradePatch)), "Adds office resources to outside connection and cargo station storage definitions so software can pass existing import and storage gates. Restart or reload after changing this option." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VerboseLogging)), "Verbose logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VerboseLogging)), "Logs every prefab updated by the office resource storage patch." },
            };
        }

        public void Unload()
        {
        }
    }
}
