using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace NoOfficeDemandFix
{
    [FileLocation(nameof(NoOfficeDemandFix))]
    [SettingsUIGroupOrder(kGeneralGroup, kDiagnosticsLogsGroup, kPerformanceLogsGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kDiagnosticsLogsGroup, kPerformanceLogsGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kDiagnosticsLogsGroup = "DiagnosticsLogs";
        public const string kPerformanceLogsGroup = "PerformanceLogs";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnablePhantomVacancyFix { get; set; } = true;

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableOutsideConnectionVirtualSellerFix { get; set; } = true;

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableVirtualOfficeResourceBuyerFix { get; set; } = true;

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableOfficeDemandDirectPatch { get; set; } = true;

        [SettingsUISection(kSection, kDiagnosticsLogsGroup)]
        public bool EnableDemandDiagnostics { get; set; } = false;

        [SettingsUISection(kSection, kDiagnosticsLogsGroup)]
        [SettingsUISlider(min = 1, max = 8, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int DiagnosticsSamplesPerDay { get; set; } = 2;

        [SettingsUISection(kSection, kDiagnosticsLogsGroup)]
        public bool CaptureStableEvidence { get; set; }

        [SettingsUISection(kSection, kDiagnosticsLogsGroup)]
        public bool VerboseLogging { get; set; }

        [SettingsUISection(kSection, kPerformanceLogsGroup)]
        public bool EnablePerformanceTelemetry { get; set; } = false;

        [SettingsUISection(kSection, kPerformanceLogsGroup)]
        [SettingsUISlider(min = 0.25f, max = 5f, step = 0.25f, scalarMultiplier = 1, unit = Unit.kFloatTwoFractions)]
        public float PerformanceTelemetrySamplingIntervalSec { get; set; } = 1f;

        [SettingsUISection(kSection, kPerformanceLogsGroup)]
        [SettingsUISlider(min = 50, max = 2000, step = 25, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int PerformanceTelemetryStallThresholdMs { get; set; } = 250;

        public override void SetDefaults()
        {
            EnablePhantomVacancyFix = true;
            EnableOutsideConnectionVirtualSellerFix = true;
            EnableVirtualOfficeResourceBuyerFix = true;
            EnableOfficeDemandDirectPatch = true;
            EnableDemandDiagnostics = false;
            DiagnosticsSamplesPerDay = 2;
            CaptureStableEvidence = false;
            VerboseLogging = false;
            EnablePerformanceTelemetry = false;
            PerformanceTelemetrySamplingIntervalSec = 1f;
            PerformanceTelemetryStallThresholdMs = 250;
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
                { m_Setting.GetOptionGroupLocaleID(Setting.kDiagnosticsLogsGroup), "Diagnostics Logs" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kPerformanceLogsGroup), "Performance Logs" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnablePhantomVacancyFix)), "Enable phantom vacancy fix" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnablePhantomVacancyFix)), "Applies immediately to future simulation ticks by removing PropertyOnMarket and PropertyToBeOnMarket from occupied signature office and industrial properties before demand and property search evaluate them. Disabling it stops future corrections but does not restore already cleaned-up market state." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableOutsideConnectionVirtualSellerFix)), "Enable software import seller correction" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableOutsideConnectionVirtualSellerFix)), "Experimental software import seller correction. Takes effect on the next game launch by letting office virtual-resource imports consider outside connections in a narrow fallback case. It does not modify cargo or storage definitions." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableVirtualOfficeResourceBuyerFix)), "Enable software import buyer timing correction" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableVirtualOfficeResourceBuyerFix)), "Experimental software import buyer timing correction. Adds a narrow fallback ResourceBuyer for zero-weight office inputs when a company is below the vanilla low-stock threshold but no buyer, path, trip, or current trading state exists yet." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableOfficeDemandDirectPatch)), "Restore pre-1.5.6f1 office demand baseline" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableOfficeDemandDirectPatch)), "The 1.5.6f1 hotfix raises office demand by increasing the office resource-demand baseline inside IndustrialDemandSystem. Turn this on before launch when you need the pre-hotfix baseline for like-for-like comparisons, and consistent behavior across runs." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDemandDiagnostics)), "Enable office demand diagnostics" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDemandDiagnostics)), "Logs office-demand factors, free office properties, phantom-vacancy counters, and software producer/consumer office state when the simulation looks suspicious. Turn it on if you want diagnostic logs investigate further." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DiagnosticsSamplesPerDay)), "Diagnostics samples per day" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DiagnosticsSamplesPerDay)), "Controls how many scheduled diagnostic samples run per displayed in-game day while diagnostics are enabled. Higher values create denser logs. Default is 2." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.CaptureStableEvidence)), "Capture stable baseline windows" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.CaptureStableEvidence)), "Keeps scheduled software diagnostics running at the configured cadence even when the city looks stable. Use it only when you want baseline troubleshooting logs." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VerboseLogging)), "Verbose logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VerboseLogging)), "Takes effect immediately for ongoing diagnostics and phantom-vacancy corrections, forces diagnostics output at the configured cadence, and adds noisier correction and office-trade detail traces. Use it only when you want detailed troubleshooting logs." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnablePerformanceTelemetry)), "Enable performance telemetry" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnablePerformanceTelemetry)), "Captures coarse in-memory performance summaries and stall events, then writes them to CSV when the current session ends. Keeps observer overhead low by avoiding frequent text logs and raw per-frame traces." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PerformanceTelemetrySamplingIntervalSec)), "Telemetry sampling interval (sec)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PerformanceTelemetrySamplingIntervalSec)), "Controls the coarse wall-clock summary window for performance telemetry. Lower values produce denser CSV output and slightly more telemetry overhead. Default is 1.00 seconds." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PerformanceTelemetryStallThresholdMs)), "Stall threshold (ms)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PerformanceTelemetryStallThresholdMs)), "Defines the render-latency threshold used to start and end coarse stall-event tracking. Default is 250 ms." },
            };
        }

        public void Unload()
        {
        }
    }
}
