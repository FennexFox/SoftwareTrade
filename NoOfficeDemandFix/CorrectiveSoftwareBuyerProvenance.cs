using Game.Economy;
using Game.Pathfind;
using Unity.Entities;

namespace NoOfficeDemandFix
{
    public struct CorrectiveSoftwareBuyerProvenance : IComponentData
    {
        public Resource Resource;
        public int IssuedAmount;
        public SetupTargetFlags Flags;
    }
}
