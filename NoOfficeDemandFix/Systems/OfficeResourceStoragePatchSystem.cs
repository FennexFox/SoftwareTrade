using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class OfficeResourceStoragePatchSystem : GameSystemBase
    {
        private static readonly Resource kOfficeResources = Resource.Software | Resource.Telecom | Resource.Financial | Resource.Media;

        private EntityQuery m_OutsideConnectionPrefabs;
        private EntityQuery m_CargoStationPrefabs;
        private readonly Dictionary<Entity, Resource> m_TrackedAddedResources = new Dictionary<Entity, Resource>();
        private bool m_LoadReady;
        private bool m_InitializedForCurrentLoad;

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
            bool upcomingPatchEnabled = IsPatchEnabled();
            if (!upcomingPatchEnabled)
            {
                RevertTrackedResourceBits();
            }
            else
            {
                PruneTrackedResourceBits();
                if (IsVerboseLoggingEnabled() && m_TrackedAddedResources.Count > 0)
                {
                    Mod.log.Info("Skipped reverting tracked office resources during preload because the trade patch remains enabled for the upcoming load.");
                }
            }

            ResetLoadState();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_LoadReady = true;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!m_LoadReady)
            {
                return;
            }

            if (m_InitializedForCurrentLoad)
            {
                base.Enabled = false;
                return;
            }

            if (!IsPatchEnabled())
            {
                m_InitializedForCurrentLoad = true;
                base.Enabled = false;
                Mod.log.Info("Office resource storage patch is disabled for the current load.");
                return;
            }

            if (m_OutsideConnectionPrefabs.IsEmptyIgnoreFilter && m_CargoStationPrefabs.IsEmptyIgnoreFilter)
            {
                return;
            }

            int patchedOutsideConnections = PatchStorageCompanyPrefabs(m_OutsideConnectionPrefabs, "outside connection");
            int patchedCargoStations = PatchStorageCompanyPrefabs(m_CargoStationPrefabs, "cargo station");

            m_InitializedForCurrentLoad = true;
            base.Enabled = false;
            Mod.log.Info(MachineParsedLogContract.FormatOfficeResourcePatchApplied(patchedOutsideConnections, patchedCargoStations));
        }

        private static bool IsPatchEnabled()
        {
            return Mod.Settings != null && Mod.Settings.EnableTradePatch;
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

                Resource addedResources = kOfficeResources & ~original;
                storageCompanyData.m_StoredResources = updated;
                EntityManager.SetComponentData(entity, storageCompanyData);
                TrackAddedResources(entity, addedResources);
                patched++;

                if (IsVerboseLoggingEnabled())
                {
                    PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                    Mod.log.Info($"Patched {label} prefab index {prefabData.m_Index} with office resources.");
                }
            }

            return patched;
        }

        private void RevertTrackedResourceBits()
        {
            if (m_TrackedAddedResources.Count == 0)
            {
                return;
            }

            bool verboseLogging = IsVerboseLoggingEnabled();
            int reverted = 0;

            foreach (KeyValuePair<Entity, Resource> trackedEntry in m_TrackedAddedResources)
            {
                Entity entity = trackedEntry.Key;
                Resource addedResources = trackedEntry.Value;
                if (!EntityManager.Exists(entity))
                {
                    if (verboseLogging)
                    {
                        Mod.log.Info($"Skipped reverting tracked office resources because entity {entity.Index}:{entity.Version} no longer exists during preload.");
                    }

                    continue;
                }

                if (!EntityManager.HasComponent<StorageCompanyData>(entity))
                {
                    if (verboseLogging)
                    {
                        Mod.log.Info($"Skipped reverting tracked office resources because entity {entity.Index}:{entity.Version} no longer has StorageCompanyData during preload.");
                    }

                    continue;
                }

                StorageCompanyData storageCompanyData = EntityManager.GetComponentData<StorageCompanyData>(entity);
                Resource current = storageCompanyData.m_StoredResources;
                Resource updated = current & ~addedResources;
                if (updated == current)
                {
                    continue;
                }

                storageCompanyData.m_StoredResources = updated;
                EntityManager.SetComponentData(entity, storageCompanyData);
                reverted++;

                if (verboseLogging && EntityManager.HasComponent<PrefabData>(entity))
                {
                    PrefabData prefabData = EntityManager.GetComponentData<PrefabData>(entity);
                    Mod.log.Info($"Reverted tracked office resources on prefab index {prefabData.m_Index} during preload.");
                }
            }

            if (reverted > 0)
            {
                Mod.log.Info($"Office resource storage patch reverted tracked office resource bits on {reverted} prefabs during preload.");
            }

            m_TrackedAddedResources.Clear();
        }

        private void PruneTrackedResourceBits()
        {
            if (m_TrackedAddedResources.Count == 0)
            {
                return;
            }

            bool verboseLogging = IsVerboseLoggingEnabled();
            List<Entity> removedEntities = null;
            List<KeyValuePair<Entity, Resource>> narrowedEntries = null;

            foreach (KeyValuePair<Entity, Resource> trackedEntry in m_TrackedAddedResources)
            {
                Entity entity = trackedEntry.Key;
                Resource trackedResources = trackedEntry.Value;

                if (!EntityManager.Exists(entity))
                {
                    removedEntities ??= new List<Entity>();
                    removedEntities.Add(entity);
                    continue;
                }

                if (!EntityManager.HasComponent<StorageCompanyData>(entity))
                {
                    removedEntities ??= new List<Entity>();
                    removedEntities.Add(entity);
                    continue;
                }

                StorageCompanyData storageCompanyData = EntityManager.GetComponentData<StorageCompanyData>(entity);
                Resource remainingTrackedResources = storageCompanyData.m_StoredResources & trackedResources;
                if (remainingTrackedResources == default)
                {
                    removedEntities ??= new List<Entity>();
                    removedEntities.Add(entity);
                    continue;
                }

                if (remainingTrackedResources != trackedResources)
                {
                    narrowedEntries ??= new List<KeyValuePair<Entity, Resource>>();
                    narrowedEntries.Add(new KeyValuePair<Entity, Resource>(entity, remainingTrackedResources));
                }
            }

            if (removedEntities != null)
            {
                foreach (Entity entity in removedEntities)
                {
                    m_TrackedAddedResources.Remove(entity);
                }
            }

            if (narrowedEntries != null)
            {
                foreach (KeyValuePair<Entity, Resource> entry in narrowedEntries)
                {
                    m_TrackedAddedResources[entry.Key] = entry.Value;
                }
            }

            if (verboseLogging && (removedEntities != null || narrowedEntries != null))
            {
                int removedCount = removedEntities?.Count ?? 0;
                int narrowedCount = narrowedEntries?.Count ?? 0;
                Mod.log.Info($"Pruned tracked office resource cache during preload. Removed: {removedCount}, narrowed: {narrowedCount}.");
            }
        }

        private void TrackAddedResources(Entity entity, Resource addedResources)
        {
            if (addedResources == default)
            {
                return;
            }

            if (m_TrackedAddedResources.TryGetValue(entity, out Resource existingResources))
            {
                m_TrackedAddedResources[entity] = existingResources | addedResources;
                return;
            }

            m_TrackedAddedResources.Add(entity, addedResources);
        }

        private void ResetLoadState()
        {
            m_LoadReady = false;
            m_InitializedForCurrentLoad = false;
            base.Enabled = true;
        }
    }
}
