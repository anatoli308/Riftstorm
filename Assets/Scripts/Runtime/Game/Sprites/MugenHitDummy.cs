using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Zweiter Charakter als Trainingspuppe: steht dem Angreifer gegenüber, damit
    /// <c>postype = p2</c> und Trefferfunken eine echte Bezugsposition haben, und
    /// spielt bei jedem Treffer eine Getroffen-Animation.
    /// </summary>
    /// <remarks>
    /// Nutzt denselben Atlas wie der Angreifer — MUGEN-Charaktere bringen ihre
    /// Get-Hit-States (5000er) selbst mit, ein zweiter Import ist dafür nicht nötig.
    /// </remarks>
    public sealed class MugenHitDummy
    {
        /// <summary>MUGEN-Action der leichten Steh-Trefferreaktion.</summary>
        private const string HitAnimation = "action_5000";

        /// <summary>MUGEN-Action des Stands, auf den die Puppe zurückfällt.</summary>
        private const string IdleAnimation = "action_0";

        private const float PixelsPerUnit = 64f;

        /// <summary>MUGEN rechnet in 60 Ticks pro Sekunde.</summary>
        private const float TicksPerSecond = 60f;

        private readonly GameObject m_Go;
        private readonly FlareLayerAnimator m_Anim;
        private readonly SpriteRenderer m_Renderer;
        private readonly FlareAtlas m_Atlas;
        private readonly bool m_HasHitAnimation;
        private readonly float m_Gravity;
        private readonly float m_StandFriction;
        private float m_ReactionEndsAt;
        private bool m_Reacting;
        private Vector3 m_BasePosition;
        // Rueckstoss in MUGEN-Pixeln, y zaehlt nach unten wie beim Angreifer.
        private Vector2 m_Offset;
        private Vector2 m_Velocity;
        private bool m_Airborne;

        /// <summary>Transform der Puppe; Bezugspunkt für <c>postype = p2</c>.</summary>
        public Transform Transform => m_Go.transform;

        /// <summary>Lokale Position der Puppe relativ zum Angreifer.</summary>
        public Vector3 LocalPosition => m_Go.transform.localPosition;

        /// <summary>
        /// Baut die Puppe im ungeskalierten MUGEN-Pixelraum. Der Abstand zählt damit in
        /// echten Stage-Pixeln, während das Sprite selbst — wie jeder Charakter —
        /// mit <c>size.xscale</c> gezeichnet wird.
        /// </summary>
        /// <param name="mugenSpace">Ungeskalierter Bezugsraum des Angreifers.</param>
        /// <param name="atlas">Atlas, aus dem die Puppe ihre Animationen zieht.</param>
        /// <param name="distancePixels">Abstand in MUGEN-Pixeln vor dem Angreifer.</param>
        /// <param name="sortingOrder">Sortier-Order des Puppen-Renderers.</param>
        /// <param name="blends">Geteilte Blend-Materialien.</param>
        /// <param name="characterScale"><c>size.xscale</c>/<c>yscale</c> des Charakters.</param>
        public MugenHitDummy(
            Transform mugenSpace, FlareAtlas atlas, float distancePixels, int sortingOrder,
            MugenBlendMaterialCache blends, Vector3 characterScale)
        {
            m_Go = new GameObject("HitDummy");
            m_Go.transform.SetParent(mugenSpace, worldPositionStays: false);
            m_BasePosition = new Vector3(distancePixels / PixelsPerUnit, 0f, 0f);
            m_Go.transform.localPosition = m_BasePosition;
            m_Go.transform.localScale = characterScale;

            m_Renderer = m_Go.AddComponent<SpriteRenderer>();
            m_Renderer.sortingOrder = sortingOrder;

            m_Atlas = atlas;
            m_Gravity = atlas.Gravity;
            m_StandFriction = atlas.StandFriction;
            m_Anim = m_Go.AddComponent<FlareLayerAnimator>();
            m_Anim.SetBlendMaterials(blends);
            m_Anim.SetAtlas(atlas);
            m_HasHitAnimation = atlas.TryGet(HitAnimation, out FlareAnimation hit) && hit != null;
            m_Anim.Play(atlas.Animations.ContainsKey(IdleAnimation) ? IdleAnimation : HitAnimation, force: true);
        }

        /// <summary>
        /// Setzt die Zeichenpriorität der Puppe. Sie teilt sie sich mit dem
        /// Angreifer, damit ein Hyper-Hintergrund beide zugleich überdeckt oder
        /// beide zugleich freilässt.
        /// </summary>
        public void SetSortingOrder(int sortingOrder)
        {
            m_Renderer.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// Setzt die Blickrichtung: die Puppe schaut dem Angreifer entgegen und
        /// steht auf dessen Vorderseite.
        /// </summary>
        public void SetAttackerFacing(int flareDirection, float distancePixels)
        {
            bool attackerFacesWest = flareDirection is 0 or 1 or 7;
            m_Anim.SetDirection(attackerFacesWest ? 4 : 0);
            float sign = attackerFacesWest ? -1f : 1f;
            m_BasePosition = new Vector3(sign * distancePixels / PixelsPerUnit, 0f, 0f);
            ApplyOffset();
        }

        /// <summary>Spielt die Getroffen-Animation für <paramref name="seconds"/>.</summary>
        public void React(float seconds)
        {
            if (!m_HasHitAnimation)
            {
                return;
            }
            m_Anim.Play(HitAnimation, force: true);
            m_Reacting = true;
            m_ReactionEndsAt = seconds;
        }

        /// <summary>
        /// Reagiert auf einen konkreten Treffer: <c>common1.cns</c> Statedef 5000
        /// wählt die Animation aus <c>animtype</c>/<c>groundtype</c>, und
        /// <c>ground.velocity</c> schleudert die Puppe vom Angreifer weg.
        /// </summary>
        /// <param name="victim">Trefferdaten aus der HitDef.</param>
        /// <param name="attackerFacingSign">-1, wenn der Angreifer nach Westen schaut.</param>
        /// <param name="seconds">Mindestdauer der Reaktion.</param>
        public void React(FlareVictimReaction victim, int attackerFacingSign, float seconds)
        {
            string animation = victim.ResolveAnimation(m_Atlas);
            if (!m_Atlas.Animations.ContainsKey(animation))
            {
                React(seconds);
                return;
            }
            m_Anim.Play(animation, force: true);
            m_Reacting = true;
            m_ReactionEndsAt = Mathf.Max(seconds, victim.HitTime / TicksPerSecond);
            m_Velocity = new Vector2(
                attackerFacingSign * victim.GroundVelocity.x, victim.GroundVelocity.y);
            m_Airborne = victim.LiftsOff;
        }

        /// <summary>
        /// Integriert einen MUGEN-Tick Rückstoß: in der Luft zieht die Schwerkraft,
        /// am Boden bremst die Reibung. Unter einem Pixel pro Tick steht die Puppe.
        /// </summary>
        public void StepMotion()
        {
            if (m_Velocity == Vector2.zero && m_Offset == Vector2.zero)
            {
                return;
            }
            m_Offset += m_Velocity;
            if (m_Airborne)
            {
                m_Velocity.y += m_Gravity;
            }
            else
            {
                m_Velocity.x *= m_StandFriction;
                if (Mathf.Abs(m_Velocity.x) < 1f)
                {
                    m_Velocity.x = 0f;
                }
            }
            if (m_Offset.y > 0f)
            {
                m_Offset.y = 0f;
                m_Velocity = Vector2.zero;
                m_Airborne = false;
            }
            ApplyOffset();
        }

        /// <summary>Setzt die Puppe auf ihre Standposition.</summary>
        private void ApplyOffset()
        {
            m_Go.transform.localPosition = m_BasePosition + new Vector3(
                m_Offset.x / PixelsPerUnit, -m_Offset.y / PixelsPerUnit, 0f);
        }

        /// <summary>
        /// Zwingt der Puppe eine Animation des Angreifers auf — MUGENs
        /// <c>ChangeAnim2</c>. Sie läuft, bis der Move sie ersetzt oder neu startet.
        /// </summary>
        public void PlayVictim(string animation)
        {
            if (string.IsNullOrEmpty(animation))
            {
                return;
            }
            m_Anim.Play(animation, force: true);
            m_Reacting = false;
        }

        /// <summary>Setzt die Puppe auf ihren Stand zurück.</summary>
        public void ResetToIdle()
        {
            m_Reacting = false;
            m_Offset = Vector2.zero;
            m_Velocity = Vector2.zero;
            m_Airborne = false;
            ApplyOffset();
            m_Anim.Play(IdleAnimation, force: true);
        }

        /// <summary>Zählt die laufende Reaktion herunter und kehrt danach in den Stand zurück.</summary>
        public void Tick(float deltaTime)
        {
            if (!m_Reacting)
            {
                return;
            }
            m_ReactionEndsAt -= deltaTime;
            if (m_ReactionEndsAt <= 0f && m_Velocity == Vector2.zero && m_Offset.y >= 0f)
            {
                ResetToIdle();
            }
        }

        /// <summary>Blendet die Puppe ein oder aus.</summary>
        public void SetVisible(bool visible)
        {
            m_Renderer.enabled = visible;
        }

        /// <summary>Zerstört die Puppe.</summary>
        public void Destroy()
        {
            if (m_Go != null)
            {
                Object.Destroy(m_Go);
            }
        }
    }
}
