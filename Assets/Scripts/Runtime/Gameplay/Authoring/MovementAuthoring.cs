using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Riftstorm.Gameplay.Authoring
{
    /// <summary>
    /// Authoring-MonoBehaviour für Entities mit Bewegungs-Components.
    /// </summary>
    /// <remarks>
    /// Wird im Editor auf ein GameObject in einer SubScene gehängt.
    /// Der Baker übersetzt die Felder beim Build/Bake in DOTS-Components.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MovementAuthoring : MonoBehaviour
    {
        /// <summary>Geschwindigkeit in Units pro Sekunde.</summary>
        [SerializeField] private float m_Speed = 5f;

        /// <summary>Initiale Bewegungsrichtung (wird normalisiert).</summary>
        [SerializeField] private Vector3 m_InitialDirection = Vector3.right;

        /// <summary>
        /// Baker: übersetzt Authoring-Felder in DOTS-IComponentData zur Bake-Zeit.
        /// </summary>
        private sealed class MovementBaker : Baker<MovementAuthoring>
        {
            public override void Bake(MovementAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Sim.MoveSpeedComponent
                {
                    Value = authoring.m_Speed
                });
                AddComponent(entity, new Sim.MoveDirectionComponent
                {
                    Value = math.normalizesafe(new float3(authoring.m_InitialDirection))
                });
            }
        }
    }
}
