using System.Collections.Generic;
using Riftstorm.Game.Npc;
using Riftstorm.Game.Sprites;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Ein einzelner, im Weltraum platzierter MUGEN-Hitbox-Volumen-Eintrag (axis-aligned
    /// in unit-lokalem Koordinatensystem, danach rotiert nach FLARE-Richtung). Geeignet
    /// für <see cref="Physics.OverlapBoxNonAlloc(Vector3, Vector3, Collider[], Quaternion, int, QueryTriggerInteraction)"/>.
    /// </summary>
    public readonly struct MugenWorldBox
    {
        /// <summary>Weltkoordinaten-Mittelpunkt des Volumens.</summary>
        public readonly Vector3 Center;
        /// <summary>Halbe Ausdehnung entlang lokaler Achsen (Vorwärts × Lateral × Höhe).</summary>
        public readonly Vector3 HalfExtents;
        /// <summary>Rotation, die die lokale Vorwärts-Achse auf den FLARE-Richtungsvektor mappt.</summary>
        public readonly Quaternion Rotation;

        /// <summary>Erzeugt einen Hitbox-Volumen-Eintrag im Weltraum.</summary>
        public MugenWorldBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            Center = center;
            HalfExtents = halfExtents;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Server-autoritatives Combat-Adapter-Component zwischen <see cref="FlareCharacter"/>
    /// (sichtbarer MUGEN-Charakter) und der Combat-Pipeline. Liest pro Frame die aktive
    /// MUGEN-Zelle und übersetzt deren Attack-/Hurt-Boxen aus MUGEN-Pixel-Koordinaten
    /// (anker-relativ, Y-down) in unit-lokale Weltvolumen, die für
    /// <see cref="Physics.OverlapBoxNonAlloc(Vector3, Vector3, Collider[], Quaternion, int, QueryTriggerInteraction)"/>
    /// genutzt werden können.
    /// </summary>
    /// <remarks>
    /// <para>Wird vom <see cref="NpcController"/> in <c>TryAttack</c> abgefragt. Clients können
    /// die Boxen lesend für Debug-Visualisierung verwenden — Schadensentscheidungen
    /// trifft ausschließlich der Server.</para>
    /// <para>Mapping MUGEN→Welt (Top-Down):
    /// <list type="bullet">
    /// <item>+pixel_x relativ zum Anker → +<i>forward</i> (Blickrichtung der Einheit).</item>
    /// <item>+pixel_y relativ zum Anker (Y-down) → +<i>lateral</i> (Seitlich rechts vom Blick).</item>
    /// <item>Die "Höhe" wird auf eine konfigurierbare Y-Ausdehnung kollabiert, damit
    /// <c>OverlapBox</c> bodennahe Collider zuverlässig trifft (Topdown hat keine Z-Höhe).</item>
    /// </list>
    /// Per-Direction-Spiegelung ist im Importer bereits in die Box-Koordinaten eingerechnet,
    /// daher reicht hier eine simple Rotation um die Welt-Y-Achse.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MugenHitboxRuntime : MonoBehaviour
    {
        /// <summary>Falls keine Stats verfügbar sind: Default-PPM aus <see cref="MugenCharacterStats"/>.</summary>
        private const float k_DefaultPixelsPerMeter = 100f;

        /// <summary>Höhe (in m) der erzeugten OverlapBox; topdown haben Ziele keine echte Höhenkoordinate.</summary>
        [SerializeField] private float m_BoxYHalfExtent = 1.5f;

        /// <summary>Minimale halbe Kantenlänge pro Achse, damit degenerierte Boxen nicht durchrutschen.</summary>
        [SerializeField] private float m_MinHalfExtent = 0.05f;

        [SerializeField] private FlareCharacter m_Character;

        /// <summary>Aktive Pixel-pro-Meter, aus den NPC-Stats übernommen oder Default.</summary>
        private float m_PixelsPerMeter = k_DefaultPixelsPerMeter;

        /// <summary>Wiederverwendete Puffer ohne Per-Frame-Allokationen.</summary>
        private readonly List<MugenWorldBox> m_AttackBuffer = new(8);
        private readonly List<MugenWorldBox> m_HurtBuffer = new(8);

        /// <summary>Aktuelle Welt-Boxen für den Angriffsvolumen-Lookup (Clsn1).</summary>
        public IReadOnlyList<MugenWorldBox> AttackBoxes => m_AttackBuffer;

        /// <summary>Aktuelle Welt-Boxen für den Hurt-Volumen-Lookup (Clsn2).</summary>
        public IReadOnlyList<MugenWorldBox> HurtBoxes => m_HurtBuffer;

        /// <summary>
        /// Bindet den sichtbaren MUGEN-Charakter an dieses Runtime-Adapter. Wird vom
        /// <c>MugenNpcSpawner</c> nach <see cref="FlareCharacter"/>-Setup aufgerufen.
        /// </summary>
        public void BindCharacter(FlareCharacter character)
        {
            m_Character = character;
        }

        /// <summary>
        /// Übernimmt die Pixels-per-Meter aus den NPC-Stats. Defaults auf
        /// <see cref="k_DefaultPixelsPerMeter"/>, wenn <c>stats</c> <c>null</c> ist
        /// oder einen ungültigen Wert hält.
        /// </summary>
        public void BindStats(MugenCharacterStats stats)
        {
            float ppm = stats != null ? stats.PixelsPerMeter : 0f;
            m_PixelsPerMeter = ppm > 0f ? ppm : k_DefaultPixelsPerMeter;
        }

        /// <summary>
        /// Aktualisiert die zwischengespeicherten Welt-Boxen aus der aktuellen
        /// <see cref="FlareCell"/>. Gibt <c>true</c> zurück, wenn mindestens
        /// eine Attack- oder Hurt-Box gefunden wurde. Server-only sinnvoll.
        /// </summary>
        public bool RefreshFromCurrentFrame()
        {
            m_AttackBuffer.Clear();
            m_HurtBuffer.Clear();

            if (m_Character == null)
            {
                return false;
            }
            if (!m_Character.TryGetCurrentCell(out FlareCell cell) || cell == null)
            {
                return false;
            }

            int dir = m_Character.CurrentDirection & 7;
            Quaternion rotation = BuildDirectionRotation(dir);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 origin = transform.position;

            AppendBoxes(cell.AttackBoxes, origin, forward, right, rotation, m_AttackBuffer);
            AppendBoxes(cell.HurtBoxes, origin, forward, right, rotation, m_HurtBuffer);
            return m_AttackBuffer.Count > 0 || m_HurtBuffer.Count > 0;
        }

        private void AppendBoxes(int[][] boxesPx, Vector3 origin, Vector3 forward, Vector3 right, Quaternion rotation, List<MugenWorldBox> sink)
        {
            if (boxesPx == null || boxesPx.Length == 0)
            {
                return;
            }
            float ppm = m_PixelsPerMeter > 0f ? m_PixelsPerMeter : k_DefaultPixelsPerMeter;
            for (int i = 0; i < boxesPx.Length; i++)
            {
                int[] b = boxesPx[i];
                if (b == null || b.Length < 4)
                {
                    continue;
                }
                // MUGEN-Box: [x1, y1, x2, y2] in Pixel, anker-relativ, +x rechts, +y unten.
                // Box ist bereits per-Direction gespiegelt vom Importer.
                float x1 = b[0] / ppm;
                float y1 = b[1] / ppm;
                float x2 = b[2] / ppm;
                float y2 = b[3] / ppm;

                float fwdCenter = 0.5f * (x1 + x2);
                float latCenter = 0.5f * (y1 + y2);
                float fwdHalf = Mathf.Max(m_MinHalfExtent, 0.5f * Mathf.Abs(x2 - x1));
                float latHalf = Mathf.Max(m_MinHalfExtent, 0.5f * Mathf.Abs(y2 - y1));

                Vector3 center = origin + forward * fwdCenter + right * latCenter;
                Vector3 halfExtents = new(fwdHalf, m_BoxYHalfExtent, latHalf);
                sink.Add(new MugenWorldBox(center, halfExtents, rotation));
            }
        }

        /// <summary>
        /// Baut eine Y-Rotation, die die lokale +Z-Achse auf den Welt-Forward-Vektor
        /// der gegebenen FLARE-Richtung abbildet. Reihenfolge:
        /// 0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW (siehe <c>NpcController.ComputeFlareDirection</c>).
        /// </summary>
        private static Quaternion BuildDirectionRotation(int flareDir)
        {
            Vector3 forward = flareDir switch
            {
                0 => new Vector3(-1f, 0f, 0f),                       // W
                1 => new Vector3(-0.7071068f, 0f, 0.7071068f),       // SW
                2 => new Vector3(0f, 0f, 1f),                        // S
                3 => new Vector3(0.7071068f, 0f, 0.7071068f),        // SE
                4 => new Vector3(1f, 0f, 0f),                        // E
                5 => new Vector3(0.7071068f, 0f, -0.7071068f),       // NE
                6 => new Vector3(0f, 0f, -1f),                       // N
                7 => new Vector3(-0.7071068f, 0f, -0.7071068f),      // NW
                _ => new Vector3(1f, 0f, 0f),
            };
            return Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
