using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Zustandsspuren, die den Körper selbst betreffen: <c>Turn</c> kippt die
    /// Blickrichtung mitten im Move, <c>AssertSpecial invisible</c> blendet ihn aus.
    /// </summary>
    public sealed partial class MugenMoveStage
    {
        /// <summary>Nächster noch nicht ausgeführter <c>Turn</c>.</summary>
        private int m_NextTurnIndex;

        /// <summary>Nächste noch nicht ausgeführte Unsichtbarkeits-Marke.</summary>
        private int m_NextInvisibleIndex;

        /// <summary>Richtung, auf die <see cref="Restart"/> zurückfällt.</summary>
        private int m_BaseDirection;

        /// <summary>
        /// Führt alle bis <paramref name="tick"/> fälligen <c>Turn</c>-Controller aus.
        /// Jeder spiegelt die Blickrichtung an der Senkrechten.
        /// </summary>
        private void AdvanceTurns(int tick)
        {
            while (m_NextTurnIndex < m_Sequence.Turns.Length
                   && m_Sequence.Turns[m_NextTurnIndex] <= tick)
            {
                m_NextTurnIndex++;
                ApplyDirection(MirrorDirection(m_Direction));
            }
        }

        /// <summary>
        /// Blendet den Körper aus, sobald eine <c>AssertSpecial invisible</c>-Marke
        /// fällig ist. MUGEN setzt das Flag pro Tick neu; für die Vorschau genügt
        /// das Ausblenden ab dem ersten Vorkommen bis zum Neustart.
        /// </summary>
        private void AdvanceInvisible(int tick)
        {
            while (m_NextInvisibleIndex < m_Sequence.Invisible.Length
                   && m_Sequence.Invisible[m_NextInvisibleIndex] <= tick)
            {
                m_NextInvisibleIndex++;
                if (m_BodyRenderer != null)
                {
                    m_BodyRenderer.enabled = false;
                }
            }
        }

        /// <summary>Setzt Dreh- und Unsichtbarkeitsspur auf den Move-Anfang zurück.</summary>
        private void ResetBodyState()
        {
            m_NextTurnIndex = 0;
            m_NextInvisibleIndex = 0;
            if (m_BodyRenderer != null)
            {
                m_BodyRenderer.enabled = true;
            }
            if (m_Direction != m_BaseDirection)
            {
                ApplyDirection(m_BaseDirection);
            }
        }

        /// <summary>
        /// Spiegelt eine FLARE-Richtung an der Senkrechten: 0=W wird zu 4=E,
        /// 1=SW zu 3=SE, 5=NE zu 7=NW; Süd und Nord bleiben stehen.
        /// </summary>
        private static int MirrorDirection(int direction)
        {
            return (4 - direction + 8) % 8;
        }
    }
}
