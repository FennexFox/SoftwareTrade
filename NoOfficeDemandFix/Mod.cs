using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using NoOfficeDemandFix.Systems;

namespace NoOfficeDemandFix
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(NoOfficeDemandFix)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }

        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            updateSystem.UpdateAfter<OfficeResourceStoragePatchSystem, PrefabSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAfter<SignaturePropertyMarketGuardSystem, PropertyProcessingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<SignaturePropertyMarketGuardSystem, RentAdjustSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<SignaturePropertyMarketGuardSystem, CompanyMoveAwaySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<SignaturePropertyMarketGuardSystem, IndustrialFindPropertySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<SignaturePropertyMarketGuardSystem, IndustrialDemandSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAfter<OfficeDemandDiagnosticsSystem, IndustrialDemandSystem>(SystemUpdatePhase.GameSimulation);

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            Settings = m_Setting;

            AssetDatabase.global.LoadSettings(nameof(NoOfficeDemandFix), m_Setting, new Setting(this));
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            Settings = null;

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
