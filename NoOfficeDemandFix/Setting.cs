using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace NoOfficeDemandFix
{
    [FileLocation(nameof(NoOfficeDemandFix))]
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

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnablePhantomVacancyFix { get; set; } = true;

        [SettingsUISection(kSection, kDiagnosticsGroup)]
        public bool EnableDemandDiagnostics { get; set; }

        [SettingsUISection(kSection, kDiagnosticsGroup)]
        [SettingsUISlider(min = 1, max = 8, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int DiagnosticsSamplesPerDay { get; set; } = 2;

        [SettingsUISection(kSection, kDiagnosticsGroup)]
        public bool CaptureStableEvidence { get; set; }

        [SettingsUISection(kSection, kDiagnosticsGroup)]
        public bool VerboseLogging { get; set; }

        public override void SetDefaults()
        {
            EnableTradePatch = false;
            EnablePhantomVacancyFix = true;
            EnableDemandDiagnostics = false;
            DiagnosticsSamplesPerDay = 2;
            CaptureStableEvidence = false;
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
                { m_Setting.GetSettingsLocaleID(), "No Office Demand Fix" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDiagnosticsGroup), "Diagnostics" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableTradePatch)), "Enable office resource trade patch" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableTradePatch)), "Experimental software-track investigation aid. Adds office resources to outside connection and cargo station storage definitions so software can pass existing import and storage gates while you collect diagnostics. Restart or reload after changing this option." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnablePhantomVacancyFix)), "Enable phantom vacancy fix" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnablePhantomVacancyFix)), "Applies immediately to future simulation ticks by removing PropertyOnMarket and PropertyToBeOnMarket from occupied signature office and industrial properties before demand and property search evaluate them. Disabling it stops future corrections but does not restore already cleaned-up market state." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDemandDiagnostics)), "Enable office demand diagnostics" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDemandDiagnostics)), "Live-applies and logs office demand factors, free office properties, phantom vacancy counters, and software producer/consumer office state. Leave it off by default unless you are actively collecting software-track evidence." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DiagnosticsSamplesPerDay)), "Diagnostics samples per day" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DiagnosticsSamplesPerDay)), "Controls how many softwareEvidenceDiagnostics samples are emitted per displayed in-game day while diagnostics are active. This only matters when diagnostics are enabled. Default is 2, and time-scaling mods still respect the displayed day when choosing sample slots." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.CaptureStableEvidence)), "Capture stable evidence windows" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.CaptureStableEvidence)), "Keeps softwareEvidenceDiagnostics observation windows flowing at the configured per-day cadence while diagnostics are enabled, even when no suspicious signal is currently present. Use it only when you want baseline or no-symptom evidence for investigation." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VerboseLogging)), "Verbose logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VerboseLogging)), "Takes effect immediately for ongoing diagnostics and phantom vacancy corrections, forces office diagnostics output at the configured per-day cadence while diagnostics are enabled, and adds the noisier correction and patch traces. Use it for investigation only; it does not replay one-shot prefab patch logs that already happened earlier in the session." },
            };
        }

        public void Unload()
        {
        }
    }
}
