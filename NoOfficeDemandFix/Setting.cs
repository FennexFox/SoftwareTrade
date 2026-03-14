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
        public bool EnablePhantomVacancyFix { get; set; } = true;

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableOutsideConnectionVirtualSellerFix { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableVirtualOfficeResourceBuyerFix { get; set; }

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
            EnablePhantomVacancyFix = true;
            EnableOutsideConnectionVirtualSellerFix = false;
            EnableVirtualOfficeResourceBuyerFix = false;
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

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnablePhantomVacancyFix)), "Enable phantom vacancy fix" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnablePhantomVacancyFix)), "Applies immediately to future simulation ticks by removing PropertyOnMarket and PropertyToBeOnMarket from occupied signature office and industrial properties before demand and property search evaluate them. Disabling it stops future corrections but does not restore already cleaned-up market state." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableOutsideConnectionVirtualSellerFix)), "Enable outside-connection virtual seller fix" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableOutsideConnectionVirtualSellerFix)), "Experimental outside-connection virtual seller correction. Takes effect on the next game launch by allowing office virtual-resource imports to target outside connections even when their storage prefab does not advertise that resource. It does not modify cargo or storage definitions and is intended for virtual office-resource investigation and fix validation." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableVirtualOfficeResourceBuyerFix)), "Enable virtual office buyer cadence fix" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableVirtualOfficeResourceBuyerFix)), "Experimental virtual office-resource buyer correction. Applies a post-vanilla top-up for zero-weight office inputs when a company is below the vanilla low-stock threshold but no ResourceBuyer, path, trip, or current trading state exists yet. Intended for software-track investigation and fix validation." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDemandDiagnostics)), "Enable office demand diagnostics" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDemandDiagnostics)), "Live-applies and logs office demand factors, free office properties, phantom vacancy counters, and software producer/consumer office state. Leave it off by default unless you are actively collecting software-track evidence." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DiagnosticsSamplesPerDay)), "Diagnostics samples per day" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DiagnosticsSamplesPerDay)), "Controls how many scheduled diagnostic sample slots exist per displayed in-game day. `sample_slot` follows the runtime `TimeSystem` time-of-day path, while `sample_day` uses a logical displayed-clock day that is seeded from the runtime day value and advances when the sampled slot wraps at midnight. `clock_source` is normally `runtime_time_system`, `sample_count` counts emitted observation windows in the current run, and `skipped_sample_slots` reports scheduled gaps that were not backfilled. This only matters when diagnostics are enabled. Default is 2." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.CaptureStableEvidence)), "Capture stable evidence windows" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.CaptureStableEvidence)), "Keeps scheduled softwareEvidenceDiagnostics observation windows flowing at the configured per-day cadence while diagnostics are enabled, even when no suspicious signal is currently present. Missed scheduled slots are reported through `skipped_sample_slots` instead of backfilled logs. Use it only when you want baseline or no-symptom evidence for investigation." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VerboseLogging)), "Verbose logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VerboseLogging)), "Takes effect immediately for ongoing diagnostics and phantom vacancy corrections, forces office diagnostics output at the configured per-day cadence while diagnostics are enabled, and adds the noisier correction and office-trade detail traces. Use it for investigation only." },
            };
        }

        public void Unload()
        {
        }
    }
}
