using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;

namespace Riftstorm.Gameplay.Sim
{
    /// <summary>
    /// Wendet <see cref="MoveDirectionComponent"/> × <see cref="MoveSpeedComponent"/> auf
    /// <see cref="LocalTransform.Position"/> an.
    /// </summary>
    /// <remarks>
    /// Phase 1: Läuft in der normalen <see cref="SimulationSystemGroup"/> → in jeder World aktiv,
    /// auch ohne aktive NfE-Verbindung. In Phase 2 wird das auf
    /// <c>PredictedSimulationSystemGroup</c> umgestellt + Server-Filter für echte Server-Authority.
    /// Burst-kompiliert, Job-parallelisiert, allokationsfrei.
    /// Profiler-Marker eingebaut für frühe Performance-Baseline.
    /// </remarks>
    [BurstCompile]
    public partial struct MovementSystem : ISystem
    {
        private static readonly ProfilerMarker s_Marker = new("Riftstorm.MovementSystem");

        /// <summary>
        /// Setup: stellt sicher dass nur Entities mit allen drei Components verarbeitet werden.
        /// </summary>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MoveSpeedComponent>();
            state.RequireForUpdate<MoveDirectionComponent>();
        }

        /// <summary>
        /// Pro Tick: position += direction × speed × deltaTime.
        /// </summary>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            using var _ = s_Marker.Auto();

            var deltaTime = SystemAPI.Time.DeltaTime;
            var job = new MoveJob { DeltaTime = deltaTime };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        /// <summary>
        /// Parallel-Job über alle Entities mit Move-Components.
        /// </summary>
        [BurstCompile]
        private partial struct MoveJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref LocalTransform transform,
                in MoveSpeedComponent speed,
                in MoveDirectionComponent direction)
            {
                transform.Position += direction.Value * speed.Value * DeltaTime;
            }
        }
    }
}
