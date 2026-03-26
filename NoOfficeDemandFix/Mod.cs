using System.Reflection;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Pathfind;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using HarmonyLib;
using NoOfficeDemandFix.Patches;
using NoOfficeDemandFix.Systems;
using NoOfficeDemandFix.Telemetry;

namespace NoOfficeDemandFix
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(NoOfficeDemandFix)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }
        public static string ModVersion { get; private set; } = string.Empty;

        private Setting m_Setting;
        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));
            ModVersion = GetAssemblyVersion();

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
                ModVersion = asset.version != null ? asset.version.ToString() : ModVersion;
            }

            log.Info($"Resolved mod version {ModVersion}");

            updateSystem.UpdateAfter<SignaturePropertyMarketGuardSystem, PropertyProcessingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<SignaturePropertyMarketGuardSystem, RentAdjustSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<SignaturePropertyMarketGuardSystem, CompanyMoveAwaySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<SignaturePropertyMarketGuardSystem, IndustrialFindPropertySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<SignaturePropertyMarketGuardSystem, IndustrialDemandSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<OfficeAIHotfixSystem, OfficeAISystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<OfficeAIHotfixSystem, ProcessingCompanySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<OfficeDemandDiagnosticsSystem, IndustrialDemandSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<VirtualOfficeResourceBuyerFixSystem, BuyingCompanySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<VirtualOfficeResourceBuyerFixSystem, ResourceBuyerSystem>(SystemUpdatePhase.GameSimulation);
            // Run performance telemetry in LateUpdate so metrics are captured after simulation completes,
            // aligned with the rendered frame rather than the main GameSimulation step.
            updateSystem.UpdateAfter<PerformanceTelemetrySystem, SimulationSystem>(SystemUpdatePhase.LateUpdate);

            m_Setting = new Setting(this);
            AssetDatabase.global.LoadSettings(nameof(NoOfficeDemandFix), m_Setting, new Setting(this));
            Settings = m_Setting;

            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            m_Setting.RegisterInOptionsUI();

            BootstrapHarmonyPatchesIfNeeded();
        }

        private void BootstrapHarmonyPatchesIfNeeded()
        {
            try
            {
                m_Harmony = new Harmony(nameof(NoOfficeDemandFix));
                m_Harmony.PatchAll(typeof(Mod).Assembly);
                log.Info("Harmony patch bootstrap completed.");
            }
            catch (System.Exception ex)
            {
                log.Error($"Harmony patch bootstrap failed. Falling back to vanilla behavior for this session. {ex}");
                m_Harmony = null;
            }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            PerformanceTelemetryCollector.FlushActiveRun();
            Settings = null;

            if (m_Harmony != null)
            {
                m_Harmony.UnpatchAll(m_Harmony.Id);
                m_Harmony = null;
            }

            OutsideConnectionVirtualSellerFixPatch.LogProbeSummary();

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }

        private static string GetAssemblyVersion()
        {
            AssemblyInformationalVersionAttribute informationalVersionAttribute =
                typeof(Mod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informationalVersionAttribute != null &&
                !string.IsNullOrWhiteSpace(informationalVersionAttribute.InformationalVersion))
            {
                return informationalVersionAttribute.InformationalVersion.Trim();
            }

            return typeof(Mod).Assembly.GetName().Version?.ToString() ?? string.Empty;
        }
    }
}
