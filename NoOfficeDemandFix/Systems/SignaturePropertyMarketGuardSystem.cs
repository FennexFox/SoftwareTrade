using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class SignaturePropertyMarketGuardSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_SignaturePropertyQuery;
        private int m_CorrectionsSinceLastSample;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SignaturePropertyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Signature>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<Abandoned>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Condemned>()
                }
            });
            RequireForUpdate(m_SignaturePropertyQuery);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_CorrectionsSinceLastSample = 0;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_CorrectionsSinceLastSample = 0;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!IsFixEnabled())
            {
                return;
            }

            using NativeArray<Entity> properties = m_SignaturePropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length; i++)
            {
                Entity property = properties[i];
                if (!TryGetTrackedPropertyType(property, out string propertyType))
                {
                    continue;
                }

                bool hasOnMarket = EntityManager.HasComponent<PropertyOnMarket>(property);
                bool hasToBeOnMarket = EntityManager.HasComponent<PropertyToBeOnMarket>(property);
                if (!hasOnMarket && !hasToBeOnMarket)
                {
                    continue;
                }

                if (!HasActiveCompanyRenter(property))
                {
                    continue;
                }

                List<string> removedComponents = null;
                if (hasOnMarket)
                {
                    EntityManager.RemoveComponent<PropertyOnMarket>(property);
                    removedComponents = removedComponents ?? new List<string>(2);
                    removedComponents.Add(nameof(PropertyOnMarket));
                }

                if (hasToBeOnMarket)
                {
                    EntityManager.RemoveComponent<PropertyToBeOnMarket>(property);
                    removedComponents = removedComponents ?? new List<string>(2);
                    removedComponents.Add(nameof(PropertyToBeOnMarket));
                }

                if (removedComponents == null)
                {
                    continue;
                }

                m_CorrectionsSinceLastSample++;
                if (IsVerboseLoggingEnabled())
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(property);
                    Mod.log.Info(
                        $"Signature phantom vacancy guard corrected {propertyType} property {FormatEntity(property)} " +
                        $"prefab={GetPrefabLabel(prefabRef.m_Prefab)} removed=[{string.Join(", ", removedComponents)}]");
                }
            }
        }

        public int ConsumeCorrectionCount()
        {
            int correctionCount = m_CorrectionsSinceLastSample;
            m_CorrectionsSinceLastSample = 0;
            return correctionCount;
        }

        private bool HasActiveCompanyRenter(Entity property)
        {
            if (!EntityManager.HasBuffer<Renter>(property))
            {
                return false;
            }

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(property, isReadOnly: true);
            for (int i = 0; i < renters.Length; i++)
            {
                Entity renter = renters[i].m_Renter;
                if (!EntityManager.HasComponent<CompanyData>(renter) || !EntityManager.HasComponent<PropertyRenter>(renter))
                {
                    continue;
                }

                PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(renter);
                if (propertyRenter.m_Property == property)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTrackedPropertyType(Entity property, out string propertyType)
        {
            if (EntityManager.HasComponent<OfficeProperty>(property))
            {
                propertyType = "office";
                return true;
            }

            if (EntityManager.HasComponent<IndustrialProperty>(property))
            {
                propertyType = "industrial";
                return true;
            }

            propertyType = null;
            return false;
        }

        private string GetPrefabLabel(Entity prefab)
        {
            string prefabName = m_PrefabSystem.GetPrefabName(prefab);
            if (string.IsNullOrEmpty(prefabName))
            {
                return FormatEntity(prefab);
            }

            return '"' + prefabName + "\" (" + FormatEntity(prefab) + ')';
        }

        private static bool IsFixEnabled()
        {
            return Mod.Settings == null || Mod.Settings.EnablePhantomVacancyFix;
        }

        private static bool IsVerboseLoggingEnabled()
        {
            return Mod.Settings != null && Mod.Settings.VerboseLogging;
        }

        private static string FormatEntity(Entity entity)
        {
            return entity.Index + ":" + entity.Version;
        }
    }
}
