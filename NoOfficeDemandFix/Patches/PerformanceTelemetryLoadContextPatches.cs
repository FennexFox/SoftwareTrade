using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Assets;
using Game.SceneFlow;
using HarmonyLib;
using NoOfficeDemandFix.Telemetry;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Load), new[] { typeof(GameMode), typeof(Purpose), typeof(IAssetData) })]
    public static class GameManagerLoadPerformanceTelemetryPatch
    {
        public static void Prefix(Purpose purpose, IAssetData asset)
        {
            if (purpose == Purpose.LoadGame)
            {
                string saveName = ResolveLoadedSaveName(asset);
                if (!string.IsNullOrWhiteSpace(saveName))
                {
                    PerformanceTelemetryCollector.SetPendingLoadedSaveName(saveName);
                    return;
                }
            }

            PerformanceTelemetryCollector.ClearPendingLoadedSaveName();
        }

        private static string ResolveLoadedSaveName(IAssetData asset)
        {
            if (asset is SaveGameMetadata saveGameMetadata)
            {
                SaveInfo saveInfo = saveGameMetadata.target;
                if (!string.IsNullOrWhiteSpace(saveInfo?.displayName))
                {
                    return saveInfo.displayName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(saveInfo?.id))
                {
                    return saveInfo.id.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(asset?.name) ? null : asset.name.Trim();
        }
    }
}
