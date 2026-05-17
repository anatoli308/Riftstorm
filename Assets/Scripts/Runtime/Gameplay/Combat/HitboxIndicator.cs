using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Tolik.Riftstorm.Runtime.Gameplay.Combat
{
    /// <summary>
    /// Zeichnet einen Selection-Indicator am Boden um eine Einheit (League-of-
    /// Legends-Style) als flaches Sprite mit der Textur aus
    /// <c>HudConfig.selectionIndicatorTexture</c> (default
    /// <c>interface/unit_selected</c>). Rein visuell, lokal pro Client. Kein
    /// Netcode, keine Server-Autoritaet noetig. Sichtbarkeit wird extern per
    /// <see cref="Show"/> / <see cref="Hide"/> geschaltet — typischerweise vom
    /// Owner-Client als Reaktion auf das LOCK-Target
    /// (<c>TargetSelection.CurrentTargetIdChanged</c>).
    ///
    /// <para>
    /// Der Radius kommt ausschliesslich aus <see cref="IUnitStats.HitRadius"/>
    /// des Parent-<c>UnitStats</c> — derselbe Wert, gegen den der Server in
    /// <c>ServerResolveMeleeHit</c> prueft. Dadurch ist der Ring optisch
    /// 1:1 die echte Server-Hitbox; einen separaten Indicator-Radius gibt es
    /// bewusst nicht (Single Source of Truth).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitboxIndicator : MonoBehaviour
    {
        [Header("Geometrie")]
        [SerializeField] private float m_GroundOffset = 0.02f;

        [Header("Darstellung")]
        [SerializeField] private bool m_AlwaysVisible = false;

        private IUnitStats m_MatchStats;
        private float m_Radius = 0.5f;

        // Sprite-Variante: SpriteRenderer (nicht Quad+Material), damit URP
        // automatisch das richtige Unlit-Sprite-Material mit Alpha zuweist.
        // Bei fehlender Textur wird kein Visual erzeugt und ein Warn-Log
        // abgesetzt — kein LineRenderer-Fallback mehr (war nur toter Code,
        // da SelectionIndicatorBootstrap die Textur immer laedt).
        private GameObject m_QuadObject;
        private SpriteRenderer m_QuadSpriteRenderer;
        private Sprite m_QuadSprite;

        private void Awake()
        {
            m_MatchStats = GetComponentInParent<IUnitStats>();
            // SyncRadius bewusst NICHT hier &#8212; die Reihenfolge der Awakes auf
            // demselben GameObject ist undefiniert. <see cref="MugenNpcSpawner.Awake"/>
            // ueberschreibt <c>UnitStats.HitRadius</c> aus dem .stats.json-Sidecar;
            // wenn HitboxIndicator zuerst Awakes, wuerde der Ring mit dem Prefab-
            // Default-HitRadius gebaut. Deshalb lesen wir den Radius erst in
            // <see cref="EnsureVisualBuilt"/> (in Start), wo garantiert alle
            // Awakes durch sind.
        }

        private void Start()
        {
            // Visual eagerly bauen, sobald SelectionIndicatorBootstrap garantiert
            // gelaufen ist (RuntimeInitializeOnLoadMethod.AfterSceneLoad laeuft
            // zwischen allen OnEnables und allen Starts).
            EnsureVisualBuilt();
            SetVisible(m_AlwaysVisible);
        }

        /// <summary>
        /// Erlaubt es externen Systemen (Buffs, Stat-Reload, Skin-Wechsel), den
        /// Indicator neu auf den aktuellen <c>UnitStats.HitRadius</c> auszurichten.
        /// Zerstoert das alte Visual und baut es neu auf, damit der Sprite-
        /// Durchmesser ueber <c>pixelsPerUnit</c> garantiert auf den neuen Radius
        /// passt (eine reine Scale-Aenderung wuerde die Textur unscharf strecken).
        /// </summary>
        public void RefreshFromStats()
        {
            if (m_MatchStats == null)
            {
                m_MatchStats = GetComponentInParent<IUnitStats>();
            }
            float oldRadius = m_Radius;
            SyncRadius();
            if (!Mathf.Approximately(oldRadius, m_Radius) && m_QuadObject != null)
            {
                bool wasVisible = m_QuadObject.activeSelf;
                if (m_QuadSprite != null)
                {
                    Destroy(m_QuadSprite);
                    m_QuadSprite = null;
                }
                Destroy(m_QuadObject);
                m_QuadObject = null;
                m_QuadSpriteRenderer = null;
                EnsureVisualBuilt();
                if (m_QuadObject != null)
                {
                    m_QuadObject.SetActive(wasVisible);
                }
            }
        }

        /// <summary>
        /// Lazy-idempotenter Build des sichtbaren Indicators. Wird ueblicherweise
        /// in <c>Start()</c> aufgerufen, aber auch defensiv in <see cref="SetVisible"/>,
        /// falls ein anderer Component schon in seinem OnEnable ein <see cref="Show"/>
        /// triggert, bevor unser eigenes <c>Start()</c> gelaufen ist.
        /// </summary>
        private void EnsureVisualBuilt()
        {
            if (m_QuadObject != null)
            {
                return;
            }

            // Radius spaet lesen, damit Stat-Overrides aus anderen Awakes
            // (z. B. MugenNpcSpawner.ApplyBaseStats) bereits eingeflossen sind.
            SyncRadius();

            Texture2D tex = SelectionIndicatorAssets.Texture;
            if (tex == null)
            {
                Debug.LogWarning(
                    $"[HitboxIndicator] '{name}': SelectionIndicatorAssets.Texture ist null — " +
                    "pruefe HudConfig.selectionIndicatorTexture und SelectionIndicatorBootstrap.");
                return;
            }

            BuildTexturedQuad(tex, SelectionIndicatorAssets.Scale);
        }

        /// <summary>
        /// Erzeugt ein flaches Sprite-Child mit der uebergebenen Textur. Liegt auf
        /// dem Boden (X-Rotation 90, Sprite-Normale +Y), Durchmesser = 2 * Radius * scale.
        /// Nutzt einen <see cref="SpriteRenderer"/>, damit URP automatisch das
        /// passende Unlit-Sprite-Material mit Alpha zuweist (keine manuelle
        /// Shader-Suche, kein Magenta-Risiko durch fehlende Built-in-Shader).
        /// Das erzeugte <see cref="Sprite"/> wird in <see cref="OnDestroy"/>
        /// aufgeraeumt.
        /// </summary>
        private void BuildTexturedQuad(Texture2D tex, float scale)
        {
            float diameter = 2f * m_Radius * Mathf.Max(0.01f, scale);
            // Sprite ueber pixelsPerUnit auf den gewuenschten Durchmesser bringen,
            // sodass localScale = 1 bleibt und der HitboxIndicator-Radius weiter
            // 1:1 als Sichtgroesse wirkt.
            float pixelsPerUnit = Mathf.Max(1f, tex.width / Mathf.Max(0.001f, diameter));

            m_QuadObject = new("SelectionIndicatorSprite");
            Transform t = m_QuadObject.transform;
            t.SetParent(transform, worldPositionStays: false);
            t.localPosition = new Vector3(0f, m_GroundOffset, 0f);
            // SpriteRenderer rendert standardmaessig in der XY-Ebene (Normale +Z).
            // +90deg um X dreht die Normale auf +Y — Sprite liegt flach auf dem Boden.
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            t.localScale = Vector3.one;

            m_QuadSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            m_QuadSprite.name = "SelectionIndicatorSprite";

            m_QuadSpriteRenderer = m_QuadObject.AddComponent<SpriteRenderer>();
            m_QuadSpriteRenderer.sprite = m_QuadSprite;
            m_QuadSpriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_QuadSpriteRenderer.receiveShadows = false;
            // Hinter Einheiten-Sprites sortieren, damit FLARE-Charakter-Layer
            // ueber dem Boden-Ring liegen (Ring ist Boden-Decal).
            m_QuadSpriteRenderer.sortingOrder = -100;
        }

        /// <summary>
        /// Uebernimmt den Radius aus <see cref="IUnitStats.HitRadius"/> des
        /// Parent-<c>UnitStats</c>. Ohne <c>UnitStats</c> bleibt der zuletzt
        /// gueltige Default stehen — der Ring sitzt aber per Design immer an
        /// einer Einheit, daher ist dieser Fallback nur Editor-Sicherheitsnetz.
        /// </summary>
        private void SyncRadius()
        {
            if (m_MatchStats != null && m_MatchStats.HitRadius > 0.05f)
            {
                m_Radius = m_MatchStats.HitRadius;
            }
        }

        private void OnDestroy()
        {
            if (m_QuadSprite != null)
            {
                Destroy(m_QuadSprite);
            }
            if (m_QuadObject != null)
            {
                Destroy(m_QuadObject);
            }
        }

        /// <summary>Zeigt den Indicator an (z.B. bei Hover oder Selection).</summary>
        public void Show() => SetVisible(true);

        /// <summary>Versteckt den Indicator, sofern nicht <see cref="m_AlwaysVisible"/> aktiv ist.</summary>
        public void Hide()
        {
            if (!m_AlwaysVisible)
            {
                SetVisible(false);
            }
        }

        /// <summary>
        /// Schaltet die Sichtbarkeit des Sprite-Childs um. Baut den Visual lazy
        /// auf, falls extern vor <c>Start()</c> ein <see cref="Show"/> kommt.
        /// </summary>
        private void SetVisible(bool visible)
        {
            if (visible)
            {
                EnsureVisualBuilt();
            }
            if (m_QuadObject != null)
            {
                m_QuadObject.SetActive(visible);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_MatchStats == null)
            {
                m_MatchStats = GetComponentInParent<IUnitStats>();
            }
            SyncRadius();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.25f, 1f, 0.45f, 0.4f);
            Vector3 c = transform.position + Vector3.up * m_GroundOffset;
            const int n = 48;
            Vector3 prev = c + new Vector3(m_Radius, 0f, 0f);
            for (int i = 1; i <= n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * m_Radius, 0f, Mathf.Sin(a) * m_Radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
