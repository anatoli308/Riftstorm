using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Client-lokales Visual eines einzelnen Cast-Events. Haelt bis zu zwei
    /// <see cref="SpellAnimationPlayer"/>-Children (Primary + Secondary) und
    /// durchlaeuft drei Phasen (Casting &#8594; Travel &#8594; Impact). Jede
    /// Phase ist optional; fehlt das zugehoerige Visual, wird die Phase
    /// uebersprungen.
    /// </summary>
    /// <remarks>
    /// Bewusst keine eigene <c>StateMachine&lt;,&gt;</c> &#8212; die Sequenz
    /// ist linear und kurzlebig. Billboarding laeuft in <c>LateUpdate</c>
    /// gegen die <see cref="Camera.main"/>. Zerstoert sich selbst, sobald die
    /// letzte aktive Phase fertig ist.
    /// </remarks>
    public sealed class WorldSpellAnimation : MonoBehaviour
    {
        /// <summary>Lineare Phasen-Reihenfolge.</summary>
        public enum Phase
        {
            /// <summary>Noch nichts gespielt.</summary>
            None = 0,
            /// <summary>Animation am Caster (Channel/Wind-up).</summary>
            Casting = 1,
            /// <summary>Projektil bewegt sich vom Caster zum Ziel.</summary>
            Travel = 2,
            /// <summary>Treffer-Animation am Ziel.</summary>
            Impact = 3,
            /// <summary>Sequenz fertig; GameObject wird zerstoert.</summary>
            Done = 4,
        }

        private const float k_TravelArrivalEpsilon = 0.05f;
        private const int k_PrimarySortingOrder = 100;
        private const int k_SecondaryTopSortingOrder = 101;
        private const int k_SecondaryBottomSortingOrder = 99;

        private Camera m_Camera;

        private SpellVisualDefinition m_Kit;
        private SpellAnimationCatalog m_Anims;
        private Transform m_Source;
        private Transform m_Target;

        private SpellAnimationPlayer m_Primary;
        private SpellAnimationPlayer m_Secondary;
        private SpriteRenderer m_PrimaryRenderer;
        private SpriteRenderer m_SecondaryRenderer;
        private Transform m_PrimaryRoot;
        private Transform m_SecondaryRoot;
        private GameObject m_GlowLight;
        private GameObject m_AuraRoot;

        private Phase m_Phase = Phase.None;
        private Vector3 m_TravelFrom;
        private Vector3 m_TravelTo;
        private float m_TravelSpeed;

        /// <summary>Aktuelle Phase (fuer Debug/Tests).</summary>
        public Phase CurrentPhase => m_Phase;

        /// <summary>
        /// Startet die Visual-Sequenz. Muss direkt nach dem
        /// <see cref="Object.Instantiate(Object)"/> aufgerufen werden.
        /// </summary>
        /// <param name="kit">Per-Spell-Visual-Plan (Phasen + Travel-Speed).</param>
        /// <param name="anims">Animations-Katalog zur Aufloesung der Namen.</param>
        /// <param name="source">Caster-Transform (Anker fuer Casting/Travel-Start).</param>
        /// <param name="target">Ziel-Transform (Anker fuer Travel-Ende/Impact). Bei <c>null</c>
        ///   wird das Visual am Caster gespielt (Self-Target).</param>
        public void Play(
            SpellVisualDefinition kit,
            SpellAnimationCatalog anims,
            Transform source,
            Transform target)
        {
            m_Kit = kit;
            m_Anims = anims;
            m_Source = source;
            m_Target = target != null ? target : source;
            m_TravelSpeed = kit != null ? kit.TravelSpeed : 0f;

            EnsureCamera();
            EnsureLayers();
            AnchorTo(m_Source);
            SpawnAuraLoop();
            StartNextPhase(Phase.Casting);
        }

        void Awake()
        {
            EnsureCamera();
        }

        void Update()
        {
            if (m_Phase == Phase.Travel)
            {
                TickTravel();
            }
            else if (m_Phase == Phase.Casting || m_Phase == Phase.Impact)
            {
                // Casting bleibt am Caster, Impact am Ziel.
                AnchorTo(m_Phase == Phase.Casting ? m_Source : m_Target);
            }
        }

        void LateUpdate()
        {
            if (m_Camera == null)
            {
                return;
            }
            // Topdown-Billboard: zur Kamera ausrichten.
            Vector3 fwd = transform.position - m_Camera.transform.position;
            if (fwd.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(fwd, m_Camera.transform.up);
            }
        }

        // ---- Setup ----------------------------------------------------

        private void EnsureCamera()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
        }

        private void EnsureLayers()
        {
            if (m_Primary == null)
            {
                m_Primary = SpellVisualSpawner.CreateSpriteLayer(
                    transform,
                    "Primary",
                    Vector2.zero,
                    Color.white,
                    SpellVisualBlend.Default,
                    k_PrimarySortingOrder);
                m_PrimaryRoot = m_Primary.transform;
                m_PrimaryRenderer = m_Primary.GetComponent<SpriteRenderer>();
            }
            if (m_Secondary == null)
            {
                m_Secondary = SpellVisualSpawner.CreateSpriteLayer(
                    transform,
                    "Secondary",
                    Vector2.zero,
                    Color.white,
                    SpellVisualBlend.Default,
                    k_SecondaryTopSortingOrder);
                m_SecondaryRoot = m_Secondary.transform;
                m_SecondaryRenderer = m_Secondary.GetComponent<SpriteRenderer>();
                m_Secondary.gameObject.SetActive(false);
            }
        }

        // ---- Phase orchestration --------------------------------------

        private void StartNextPhase(Phase phase)
        {
            switch (phase)
            {
                case Phase.Casting:
                    if (TryPlayPhase(m_Kit?.Casting, m_Source, loopPrimary: false, OnCastingFinished))
                    {
                        m_Phase = Phase.Casting;
                        return;
                    }
                    goto case Phase.Travel;

                case Phase.Travel:
                    if (m_Source != null && m_Target != null && m_Source != m_Target
                        && m_Kit?.Travel != null && m_Kit.Travel.HasPrimary)
                    {
                        if (TryPlayPhase(m_Kit.Travel, m_Source, loopPrimary: true, onPrimaryFinished: null))
                        {
                            m_Phase = Phase.Travel;
                            m_TravelFrom = m_Source.position;
                            m_TravelTo = m_Target.position;
                            transform.position = m_TravelFrom;
                            return;
                        }
                    }
                    goto case Phase.Impact;

                case Phase.Impact:
                    if (TryPlayPhase(m_Kit?.Impact, m_Target, loopPrimary: false, OnImpactFinished))
                    {
                        m_Phase = Phase.Impact;
                        return;
                    }
                    goto case Phase.Done;

                case Phase.Done:
                    m_Phase = Phase.Done;
                    Destroy(gameObject);
                    return;
            }
        }

        /// <summary>
        /// Wendet die Phase auf beide Sprite-Layer an und triggert Sound +
        /// Glow. Liefert <c>false</c>, wenn weder ein primaerer Sprite noch
        /// eine sekundaere Anim aufloesbar ist (Phase wird dann uebersprungen).
        /// </summary>
        private bool TryPlayPhase(
            SpellVisualPhase phase,
            Transform anchor,
            bool loopPrimary,
            System.Action onPrimaryFinished)
        {
            if (phase == null || !phase.HasAny)
            {
                return false;
            }

            AnchorTo(anchor);
            PlayPrimaryLayer(phase, loopPrimary, onPrimaryFinished, out bool primaryStarted);
            PlaySecondaryLayer(phase, out bool secondaryStarted);
            ApplyGlowLight(phase);
            SpellVisualSpawner.PlayPhaseSound(
                phase,
                anchor != null ? anchor.position : transform.position);

            return primaryStarted || secondaryStarted;
        }

        private void PlayPrimaryLayer(
            SpellVisualPhase phase,
            bool loop,
            System.Action onFinished,
            out bool started)
        {
            started = false;
            if (!phase.HasPrimary
                || m_Anims == null
                || !m_Anims.TryGet(phase.PrimaryAnim, out SpellAnimationDefinition def)
                || def == null)
            {
                m_Primary.Stop();
                m_Primary.OnFinished = onFinished;
                onFinished?.Invoke();
                return;
            }

            ApplyLayerStyle(m_PrimaryRoot, m_PrimaryRenderer, phase.EffectivePrimaryOffsetPx(def.CanvasSize), phase.PrimaryTint, phase.PrimaryBlend);
            m_PrimaryRenderer.sortingOrder = k_PrimarySortingOrder;
            m_Primary.gameObject.SetActive(true);
            m_Primary.OnFinished = onFinished;
            m_Primary.Play(def, loop && def.HasLoop);
            started = true;
        }

        private void PlaySecondaryLayer(SpellVisualPhase phase, out bool started)
        {
            started = false;
            if (!phase.HasSecondary
                || m_Anims == null
                || !m_Anims.TryGet(phase.SecondaryAnim, out SpellAnimationDefinition def)
                || def == null)
            {
                m_Secondary.Stop();
                m_Secondary.gameObject.SetActive(false);
                return;
            }

            ApplyLayerStyle(m_SecondaryRoot, m_SecondaryRenderer, phase.EffectiveSecondaryOffsetPx(def.CanvasSize), phase.SecondaryTint, phase.SecondaryBlend);
            m_SecondaryRenderer.sortingOrder = phase.SecondaryTopmost
                ? k_SecondaryTopSortingOrder
                : k_SecondaryBottomSortingOrder;
            m_Secondary.gameObject.SetActive(true);
            m_Secondary.OnFinished = null;
            // Sekundaer immer loopen, wenn moeglich (typisch FX-Overlay).
            m_Secondary.Play(def, def.HasLoop);
            started = true;
        }

        private static void ApplyLayerStyle(
            Transform layerRoot,
            SpriteRenderer renderer,
            Vector2 offsetPx,
            Color tint,
            SpellVisualBlend blend)
        {
            layerRoot.localPosition = new Vector3(
                offsetPx.x / SpellVisualSpawner.SourcePixelsPerUnit,
                -offsetPx.y / SpellVisualSpawner.SourcePixelsPerUnit,
                0f);
            renderer.color = tint;
            // null bedeutet: Default-Sprite-Material vom Renderer weiter
            // verwenden. Bei Additive setzt die Cache eine eigene Instance.
            // WICHTIG: NIEMALS null zuweisen — das wuerde das von Unity beim
            // AddComponent gesetzte URP-Sprite-Default-Material ueberschreiben
            // und in URP zu rosa/Magenta-Rendering fuehren.
            Material mat = SpellMaterialCache.Get(blend);
            if (mat != null)
            {
                renderer.sharedMaterial = mat;
            }
        }

        private void ApplyGlowLight(SpellVisualPhase phase)
        {
            // Pro Phase wird das alte Light verworfen und ggf. neu aufgebaut.
            if (m_GlowLight != null)
            {
                Destroy(m_GlowLight);
                m_GlowLight = null;
            }
            if (phase.UnitGlowColor.a > 0f)
            {
                Light light = SpellVisualSpawner.CreateGlowLight(transform, phase.UnitGlowColor);
                m_GlowLight = light.gameObject;
            }
        }

        // ---- Phase callbacks ------------------------------------------

        private void OnCastingFinished()
        {
            StartNextPhase(Phase.Travel);
        }

        private void OnImpactFinished()
        {
            StartNextPhase(Phase.Done);
        }

        // ---- Travel ---------------------------------------------------

        private void TickTravel()
        {
            if (m_Target != null)
            {
                m_TravelTo = m_Target.position;
            }
            Vector3 current = transform.position;
            Vector3 toTarget = m_TravelTo - current;
            float dist = toTarget.magnitude;

            float speed = m_TravelSpeed > 0f ? m_TravelSpeed : 20f;
            float step = speed * Time.deltaTime;

            if (dist <= step + k_TravelArrivalEpsilon)
            {
                transform.position = m_TravelTo;
                StartNextPhase(Phase.Impact);
                return;
            }
            transform.position = current + toTarget / dist * step;
        }

        private void AnchorTo(Transform anchor)
        {
            if (anchor != null)
            {
                transform.position = anchor.position;
            }
        }

        /// <summary>
        /// Spawnt die AuraLoop-Phase als persistente Sprite-Layer am
        /// <see cref="m_Source"/>. Lebenszeit = Lebenszeit dieser
        /// <see cref="WorldSpellAnimation"/> (Cleanup in <see cref="OnDestroy"/>).
        /// </summary>
        /// <remarks>
        /// AuraLoop ist konzeptuell eine Buff-Visualisierung am Caster und
        /// gehoert langfristig ins Buff-System &#8212; bis dahin spielen wir
        /// sie als kurzes Aura-Overlay waehrend des Casts, damit FLARE-Spells
        /// mit definiertem <c>aura_kit*</c> sichtbar werden.
        /// </remarks>
        private void SpawnAuraLoop()
        {
            if (m_Kit == null || m_Source == null)
            {
                return;
            }
            SpellVisualPhase phase = m_Kit.AuraLoop;
            if (phase == null || !phase.HasAny || m_Anims == null)
            {
                return;
            }

            GameObject root = new("AuraLoop");
            root.transform.SetParent(m_Source, worldPositionStays: false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            m_AuraRoot = root;

            if (phase.HasPrimary
                && m_Anims.TryGet(phase.PrimaryAnim, out SpellAnimationDefinition primaryDef)
                && primaryDef != null)
            {
                SpellAnimationPlayer primary = SpellVisualSpawner.CreateSpriteLayer(
                    root.transform,
                    "Primary",
                    phase.EffectivePrimaryOffsetPx(primaryDef.CanvasSize),
                    phase.PrimaryTint,
                    phase.PrimaryBlend,
                    k_PrimarySortingOrder);
                primary.Play(primaryDef, primaryDef.HasLoop);
            }

            if (phase.HasSecondary
                && m_Anims.TryGet(phase.SecondaryAnim, out SpellAnimationDefinition secondaryDef)
                && secondaryDef != null)
            {
                int order = phase.SecondaryTopmost
                    ? k_SecondaryTopSortingOrder
                    : k_SecondaryBottomSortingOrder;
                SpellAnimationPlayer secondary = SpellVisualSpawner.CreateSpriteLayer(
                    root.transform,
                    "Secondary",
                    phase.EffectiveSecondaryOffsetPx(secondaryDef.CanvasSize),
                    phase.SecondaryTint,
                    phase.SecondaryBlend,
                    order);
                secondary.Play(secondaryDef, secondaryDef.HasLoop);
            }

            if (phase.UnitGlowColor.a > 0f)
            {
                SpellVisualSpawner.CreateGlowLight(root.transform, phase.UnitGlowColor);
            }

            SpellVisualSpawner.PlayPhaseSound(phase, m_Source.position);
        }

        private void OnDestroy()
        {
            if (m_AuraRoot != null)
            {
                Destroy(m_AuraRoot);
                m_AuraRoot = null;
            }
        }
    }
}
