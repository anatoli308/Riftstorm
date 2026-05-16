using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Riftstorm.Gameplay.Sim
{
    /// <summary>
    /// Normalisierte Bewegungsrichtung einer Entity.
    /// </summary>
    /// <remarks>
    /// Setzt das <see cref="MovementSystem"/> in eine Position-Delta um.
    /// Für Player-Entities wird das im Input-System aus <c>PlayerInputCommand</c> gesetzt (Phase 2).
    /// Für AI-Entities setzen es die AI-Systems (Phase 4).
    /// </remarks>
    public struct MoveDirectionComponent : IComponentData
    {
        /// <summary>Normalisierte Richtung in Welt-Koordinaten (Y meist 0 für Topdown).</summary>
        [GhostField(Quantization = 1000)]
        public float3 Value;
    }
}
