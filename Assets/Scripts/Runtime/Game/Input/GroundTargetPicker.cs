using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Input
{
    /// <summary>
    /// Owner-lokaler Ground-Target-Picker. Wird von <see cref="PlayerSpellInput"/>
    /// aktiviert, sobald der Spieler einen Spell mit
    /// <see cref="Spells.SpellAttributes.TargetsGround"/> auf einem Hotkey
    /// drueckt: der Picker zeigt einen Reticle-Ring unter der Maus an, sowie
    /// optional einen Range-Ring um den Caster, und ruft beim
    /// Linksklick <see cref="m_OnConfirmed"/> mit der Welt-Position auf.
    /// Rechtsklick oder Escape ruft <see cref="m_OnCancelled"/> auf.
    /// <para>
    /// Der eigentliche Cast-Trigger sitzt in
    /// <see cref="Combat.PlayerCombat.TryRequestCastSpellAtGround"/> &#8212; der
    /// Picker liefert nur den Welt-Punkt. Range-Validierung passiert serverseitig
    /// (Clamp im <see cref="Spells.SpellExecutor"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundTargetPicker : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Tooltip("Optional. Bei Prefabs leer lassen &#8212; Unity erlaubt keine Scene-Camera-Referenz im Prefab. " +
                 "Bleibt das Feld leer, faellt der Code zur Laufzeit auf Camera.main zurueck.")]
        [SerializeField] private Camera m_Camera;

        [Tooltip("Caster-Transform, gegen den der Range-Ring gezeichnet wird. " +
                 "Wird in Awake per GetComponentInParent ermittelt, falls leer.")]
        [SerializeField] private Transform m_CasterTransform;

        [Header("Raycast")]
        [SerializeField] private float m_MaxRayDistance = 200f;
        [Tooltip("Layer, die der Boden-Raycast trifft. Default = alles. Falls Units " +
                 "einen eigenen Layer haben, hier 'Default + Ground' eintragen, damit " +
                 "der Picker nicht auf Charaktere klebt.")]
        [SerializeField] private LayerMask m_GroundMask = ~0;
        [Tooltip("Fallback-Ebene (Welt-Y), falls der Raycast nichts trifft (z. B. ueber " +
                 "leere Skybox-Areas am Map-Rand).")]
        [SerializeField] private float m_GroundPlaneY = 0f;

        [Header("Visuals")]
        [Tooltip("Segmente fuer beide Ringe. 64 = glatte Kreise, billig.")]
        [SerializeField] private int m_RingSegments = 64;
        [SerializeField] private float m_ReticleRadius = 0.6f;
        [SerializeField] private float m_ReticleLineWidth = 0.08f;
        [SerializeField] private float m_RangeLineWidth = 0.08f;
        [SerializeField] private Color m_ReticleColor = new(0.4f, 0.85f, 1f, 0.95f);
        [SerializeField] private Color m_RangeColor = new(1f, 0.85f, 0.2f, 0.6f);

        // -------------------------------------------------------------------------
        // Laufzeit
        // -------------------------------------------------------------------------

        private InputAction m_ConfirmAction;
        private InputAction m_CancelMouseAction;
        private InputAction m_CancelKeyAction;

        private LineRenderer m_ReticleRing;
        private LineRenderer m_RangeRing;

        /// <summary>True, solange ein Pick-Vorgang aktiv ist.</summary>
        public bool IsActive { get; private set; }

        private int m_ActiveSpellEntry;
        private float m_ActiveRangeMeters;
        private Action<Vector3> m_OnConfirmed;
        private Action m_OnCancelled;

        private Vector3 m_LastReticlePos;
        private bool m_HasReticlePos;

        /// <summary>Letzte aktive Spell-Entry (0 = idle). Owner-lokal, nicht repliziert.</summary>
        public int ActiveSpellEntry => m_ActiveSpellEntry;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }
            if (m_CasterTransform == null)
            {
                m_CasterTransform = transform;
            }

            m_ConfirmAction = new InputAction(binding: "<Mouse>/leftButton");
            m_CancelMouseAction = new InputAction(binding: "<Mouse>/rightButton");
            m_CancelKeyAction = new InputAction(binding: "<Keyboard>/escape");

            BuildRing(ref m_ReticleRing, "GroundTargetPicker_Reticle", m_ReticleColor, m_ReticleLineWidth);
            BuildRing(ref m_RangeRing, "GroundTargetPicker_Range", m_RangeColor, m_RangeLineWidth);
            m_ReticleRing.enabled = false;
            m_RangeRing.enabled = false;
        }

        private void OnDestroy()
        {
            // Sauberes Teardown der dynamisch erzeugten InputActions &#8212; vermeidet
            // Leaks der nativen Action-Maps zwischen Scene-Reloads.
            CancelInternal(notify: false);
            m_ConfirmAction?.Dispose();
            m_CancelMouseAction?.Dispose();
            m_CancelKeyAction?.Dispose();
            m_ConfirmAction = null;
            m_CancelMouseAction = null;
            m_CancelKeyAction = null;
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Startet das Picken eines Boden-Zielpunkts fuer <paramref name="spellEntry"/>.
        /// Bereits laufende Pick-Sessions werden vorher mit Cancel-Callback beendet.
        /// </summary>
        /// <param name="spellEntry">Spell-Entry fuer Logging / UI-Anzeige (Reticle-Tint koennte spaeter daraus ableiten).</param>
        /// <param name="rangeMeters">Max. Reichweite ab Caster fuer den Range-Ring. 0 = keinen Range-Ring zeichnen.</param>
        /// <param name="onConfirmed">Wird bei LMB mit der Welt-Position aufgerufen. Pflichtfeld.</param>
        /// <param name="onCancelled">Wird bei RMB/Escape oder beim Abbruch durch einen neuen Pick-Aufruf gefeuert.</param>
        public void BeginPick(int spellEntry, float rangeMeters, Action<Vector3> onConfirmed, Action onCancelled)
        {
            if (onConfirmed == null)
            {
                return;
            }

            // Wenn bereits aktiv: alte Session sauber stornieren (mit Cancel-Callback,
            // damit der vorherige Aufrufer Bescheid weiss).
            if (IsActive)
            {
                CancelInternal(notify: true);
            }

            m_ActiveSpellEntry = spellEntry;
            m_ActiveRangeMeters = Mathf.Max(0f, rangeMeters);
            m_OnConfirmed = onConfirmed;
            m_OnCancelled = onCancelled;
            m_HasReticlePos = false;

            m_ConfirmAction.performed += OnConfirmPerformed;
            m_CancelMouseAction.performed += OnCancelPerformed;
            m_CancelKeyAction.performed += OnCancelPerformed;
            m_ConfirmAction.Enable();
            m_CancelMouseAction.Enable();
            m_CancelKeyAction.Enable();

            IsActive = true;
            m_ReticleRing.enabled = true;
            m_RangeRing.enabled = m_ActiveRangeMeters > 0f;

            UpdateRings();
        }

        /// <summary>
        /// Externer Cancel-Pfad (z. B. wenn der Spieler stirbt oder ein anderer
        /// Hotkey gedrueckt wird). Feuert <see cref="m_OnCancelled"/>.
        /// </summary>
        public void Cancel()
        {
            CancelInternal(notify: true);
        }

        /// <summary>
        /// Liefert den aktuellen Welt-Zielpunkt unter dem Mauscursor
        /// (Boden-Raycast), ohne eine Reticle-Session zu starten. Fuer sofortige
        /// Skillshots, die in Cursor-Richtung feuern. Liefert <c>false</c>, wenn
        /// keine Kamera oder Maus verfuegbar ist.
        /// </summary>
        public bool TryGetAimPoint(out Vector3 worldPoint)
        {
            worldPoint = default;
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null) { return false; }
            }
            Mouse mouse = Mouse.current;
            if (mouse == null) { return false; }
            worldPoint = ResolveGroundHit(mouse.position.ReadValue());
            return true;
        }

        // -------------------------------------------------------------------------
        // Update
        // -------------------------------------------------------------------------

        private void LateUpdate()
        {
            if (!IsActive)
            {
                return;
            }
            UpdateRings();
        }

        private void UpdateRings()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null) { return; }
            }
            Mouse mouse = Mouse.current;
            if (mouse == null) { return; }

            Vector2 screen = mouse.position.ReadValue();
            Vector3 hit = ResolveGroundHit(screen);
            m_LastReticlePos = hit;
            m_HasReticlePos = true;

            DrawCircle(m_ReticleRing, hit, m_ReticleRadius);
            if (m_RangeRing.enabled && m_CasterTransform != null)
            {
                DrawCircle(m_RangeRing, m_CasterTransform.position, m_ActiveRangeMeters);
            }
        }

        private Vector3 ResolveGroundHit(Vector2 screen)
        {
            Ray ray = m_Camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out RaycastHit hit, m_MaxRayDistance, m_GroundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }
            // Fallback: Schnittpunkt mit horizontaler Ebene auf m_GroundPlaneY.
            // Strahl parallel zur Ebene &#8594; nimm einen Punkt direkt vor der Kamera
            // (Edge-Case, sollte mit MOBA-Topdown-Kamera quasi nie auftreten).
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
            {
                return ray.origin + ray.direction * 10f;
            }
            float t = (m_GroundPlaneY - ray.origin.y) / ray.direction.y;
            if (t < 0f)
            {
                return ray.origin + ray.direction * 10f;
            }
            return ray.origin + ray.direction * t;
        }

        // -------------------------------------------------------------------------
        // Callbacks
        // -------------------------------------------------------------------------

        private void OnConfirmPerformed(InputAction.CallbackContext _)
        {
            if (!IsActive) { return; }
            // Mausposition jetzt neu lesen, damit der Confirm immer den aktuellsten
            // Hit nimmt (LateUpdate koennte dazwischen noch nicht gefeuert haben).
            UpdateRings();
            if (!m_HasReticlePos) { return; }

            Vector3 confirmed = m_LastReticlePos;
            Action<Vector3> cb = m_OnConfirmed;
            CancelInternal(notify: false);
            cb?.Invoke(confirmed);
        }

        private void OnCancelPerformed(InputAction.CallbackContext _)
        {
            CancelInternal(notify: true);
        }

        private void CancelInternal(bool notify)
        {
            if (!IsActive)
            {
                return;
            }

            // Erst Action-Hooks abbauen, damit kein zweiter Callback in flight ist.
            if (m_ConfirmAction != null)
            {
                m_ConfirmAction.performed -= OnConfirmPerformed;
                m_ConfirmAction.Disable();
            }
            if (m_CancelMouseAction != null)
            {
                m_CancelMouseAction.performed -= OnCancelPerformed;
                m_CancelMouseAction.Disable();
            }
            if (m_CancelKeyAction != null)
            {
                m_CancelKeyAction.performed -= OnCancelPerformed;
                m_CancelKeyAction.Disable();
            }

            IsActive = false;
            m_ActiveSpellEntry = 0;
            m_ActiveRangeMeters = 0f;
            m_HasReticlePos = false;

            if (m_ReticleRing != null) { m_ReticleRing.enabled = false; }
            if (m_RangeRing != null) { m_RangeRing.enabled = false; }

            Action cancelCb = m_OnCancelled;
            m_OnConfirmed = null;
            m_OnCancelled = null;

            if (notify)
            {
                cancelCb?.Invoke();
            }
        }

        // -------------------------------------------------------------------------
        // Visual Helpers
        // -------------------------------------------------------------------------

        private void BuildRing(ref LineRenderer lr, string name, Color color, float width)
        {
            GameObject go = new(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = Mathf.Max(8, m_RingSegments);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = new Material(Shader.Find("Sprites/Default")) { color = color };
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
        }

        private void DrawCircle(LineRenderer lr, Vector3 center, float radius)
        {
            if (lr == null || radius <= 0f) { return; }
            int n = lr.positionCount;
            float step = Mathf.PI * 2f / n;
            // Leicht ueber dem Boden zeichnen, damit der Ring nicht z-fightet.
            float y = center.y + 0.05f;
            for (int i = 0; i < n; i++)
            {
                float a = step * i;
                lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, y, center.z + Mathf.Sin(a) * radius));
            }
        }
    }
}
