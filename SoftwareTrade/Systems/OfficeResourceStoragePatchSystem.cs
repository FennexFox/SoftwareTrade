using Colossal.Serialization.Entities;
using Game;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SoftwareTrade.Systems
{
    [Preserve]
    public partial class OfficeResourceStoragePatchSystem : GameSystemBase
    {
        private static readonly Resource kOfficeResources = Resource.Software | Resource.Telecom | Resource.Financial | Resource.Media;

        private EntityQuery m_OutsideConnectionPrefabs;
        private EntityQuery m_CargoStationPrefabs;
        private bool m_Applied;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_OutsideConnectionPrefabs = GetEntityQuery(
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<OutsideConnectionData>(),
                ComponentType.ReadWrite<StorageCompanyData>());
            m_CargoStationPrefabs = GetEntityQuery(
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<CargoTransportStationData>(),
                ComponentType.ReadWrite<StorageCompanyData>());
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ResetPatchState();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            ResetPatchState();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!IsPatchEnabled())
            {
                m_Applied = true;
                base.Enabled = false;
                Mod.log.Info("Office resource storage patch is disabled.");
                return;
            }

            if (m_Applied)
            {
                base.Enabled = false;
                return;
            }

            if (m_OutsideConnectionPrefabs.IsEmptyIgnoreFilter && m_CargoStationPrefabs.IsEmptyIgnoreFilter)
            {
                return;
            }

            int patchedOutsideConnections = PatchStorageCompanyPrefabs(m_OutsideConnectionPrefabs, "outside connection");
            int patchedCargoStations = PatchStorageCompanyPrefabs(m_CargoStationPrefabs, "cargo station");

            m_Applied = true;
            base.Enabled = false;
            Mod.log.Info($"Office resource storage patch applied. Outside connections: {patchedOutsideConnections}, cargo stations: {patchedCargoStations}.");
        }

        private static bool IsPatchEnabled()
        {
            return Mod.Settings == null || Mod.Settings.EnableTradePatch;
        }

        private static bool IsVerboseLoggingEnabled()
        {
            return Mod.Settings != null && Mod.Settings.VerboseLogging;
        }

        private int PatchStorageCompanyPrefabs(EntityQuery query, string label)
        {
            int patched = 0;

            using NativeArray<Entity> prefabs = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++)
            {
                Entity entity = prefabs[i];
                StorageCompanyData storageCompanyData = EntityManager.GetComponentData<StorageCompanyData>(entity);
                Resource original = storageCompanyData.m_StoredResources;
                Resource updated = original | kOfficeResources;
                if (updated == original)
                {
                    continue;
                }

                storageCompanyData.m_StoredResources = updated;
                EntityManager.SetComponentData(entity, storageCompanyData);
                patched++;

                if (IsVerboseLoggingEnabled())
                {
                    PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                    Mod.log.Info($"Patched {label} prefab index {prefabData.m_Index} with office resources.");
                }
            }

            return patched;
        }

        private void ResetPatchState()
        {
            m_Applied = false;
            base.Enabled = true;
        }
    }
}
