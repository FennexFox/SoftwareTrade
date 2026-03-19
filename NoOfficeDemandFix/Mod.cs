using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using HarmonyLib;
using NoOfficeDemandFix.Patches;
using NoOfficeDemandFix.Systems;

namespace NoOfficeDemandFix
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(NoOfficeDemandFix)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }

        private Setting m_Setting;
        private Harmony m_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

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

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            Settings = m_Setting;

            AssetDatabase.global.LoadSettings(nameof(NoOfficeDemandFix), m_Setting, new Setting(this));
            BootstrapHarmonyPatchesIfNeeded();
        }

        private void BootstrapHarmonyPatchesIfNeeded()
        {
            if (!RequiresHarmonyPatches())
            {
                log.Info("Outside-connection virtual seller fix disabled; skipping Harmony patch bootstrap.");
                return;
            }

            try
            {
                m_Harmony = new Harmony(nameof(NoOfficeDemandFix));
                m_Harmony.PatchAll(typeof(Mod).Assembly);
                log.Info("Outside-connection virtual seller fix enabled; Harmony patch bootstrap completed.");
            }
            catch (System.Exception ex)
            {
                log.Error($"Harmony patch bootstrap failed. Falling back to vanilla behavior for this session. {ex}");
                m_Harmony = null;
            }
        }

        private bool RequiresHarmonyPatches()
        {
            return m_Setting != null && m_Setting.EnableOutsideConnectionVirtualSellerFix;
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
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
    }
}
