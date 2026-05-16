using Riftstorm.Game.Input;
using Riftstorm.Game.Sprites;
using UnityEngine;

namespace Riftstorm.Game.Movement
{
    /// <summary>
    /// Topdown-Bewegung auf der XZ-Ebene. Liest WASD aus
    /// <see cref="PlayerInputController"/>, schiebt das Wurzel-Transform
    /// und triggert Run-/Idle-Animation samt FLARE-Blickrichtung auf
    /// dem zugewiesenen <see cref="FlareCharacter"/>.
    /// </summary>
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private PlayerInputController m_Input;
        [SerializeField] private FlareCharacter m_Character;

        [Header("Bewegung")]
        [SerializeField] private float m_Speed = 4f;

        [Header("Animationen")]
        [SerializeField] private string m_IdleAnimation = "stance";
        [SerializeField] private string m_RunAnimation = "run";

        // FLARE-Konvention dieses Projekts: 0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW.
        private int m_LastDirection = 2;

        private void Update()
        {
            if (m_Input == null)
            {
                return;
            }

            Vector2 input = m_Input.MoveDirection;
            if (m_Input.IsMoving)
            {
                Vector3 delta = new(input.x, 0f, input.y);
                transform.position += delta * (m_Speed * Time.deltaTime);
                m_LastDirection = ComputeFlareDirection(input);

                if (m_Character != null)
                {
                    m_Character.Play(m_RunAnimation);
                    m_Character.SetDirection(m_LastDirection);
                }
                return;
            }

            if (m_Character != null)
            {
                m_Character.Play(m_IdleAnimation);
                m_Character.SetDirection(m_LastDirection);
            }
        }

        /// <summary>
        /// Mappt einen 2D-Bewegungsvektor (x=rechts, y=oben) auf den FLARE-Direction-Index.
        /// Reihenfolge: 0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW.
        /// </summary>
        private static int ComputeFlareDirection(Vector2 dir)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // E=0°, N=+90°
            int octant = Mathf.RoundToInt(angle / 45f);
            octant = ((octant % 8) + 8) % 8; // 0..7 mit 0=E, 2=N, 4=W, 6=S
            return (octant + 4) & 7;        // shift auf 0=W, 2=S, 4=E, 6=N
        }
    }
}
