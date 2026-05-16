using Unity.Entities;
using Unity.NetCode;

namespace Riftstorm.Gameplay.Sim
{
    /// <summary>
    /// Bewegungsgeschwindigkeit einer Entity in Units pro Sekunde.
    /// </summary>
    /// <remarks>
    /// <see cref="GhostFieldAttribute"/> sorgt für Server→Client-Replikation via NfE-Snapshots.
    /// Quantization = 100 → 2 Nachkommastellen werden übertragen (spart Bandbreite vs. Full-Float).
    /// </remarks>
    public struct MoveSpeedComponent : IComponentData
    {
        /// <summary>Geschwindigkeit in Units pro Sekunde.</summary>
        [GhostField(Quantization = 100)]
        public float Value;
    }
}
