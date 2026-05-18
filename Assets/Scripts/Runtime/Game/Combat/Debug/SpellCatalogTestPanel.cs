#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Riftstorm.Gameplay.Combat.Spells;
using Tolik.Riftstorm.Runtime.ApplicationLifecycle;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Riftstorm.Game.Combat.Debugging
{
    /// <summary>
    /// Dev-Overlay zum manuellen Testen aller Spells aus <see cref="SpellCatalog"/>.
    /// Drop diese Komponente auf ein beliebiges Scene-GameObject (z. B. einen
    /// Debug-Manager in der Game-Scene). Drücke <c>F1</c> um das Panel ein/aus
    /// zu blenden. Klicks auf einen Button rufen
    /// <see cref="PlayerCombat.TryRequestCastSpell"/> auf der lokalen Owner-Instanz
    /// auf — Target-Auswahl folgt der normalen <c>TargetSelection</c>-Logik.
    /// Nur in <c>UNITY_EDITOR</c> oder <c>DEVELOPMENT_BUILD</c> kompiliert.
    /// </summary>
    public sealed class SpellCatalogTestPanel : MonoBehaviour
    {
        [Tooltip("Input-System Binding-Pfad zum Ein/Ausblenden des Panels (z. B. <Keyboard>/f1).")]
        [SerializeField] private string m_ToggleBinding = "<Keyboard>/f1";

        [Tooltip("Maximale Höhe der Spell-Liste in Pixel.")]
        [SerializeField] private float m_PanelHeight = 540f;

        [Tooltip("Breite des Panels in Pixel.")]
        [SerializeField] private float m_PanelWidth = 360f;

        [Tooltip("Wenn aktiv, wird das Panel beim Start sofort angezeigt.")]
        [SerializeField] private bool m_VisibleOnStart = true;

        private bool m_Visible;
        private Vector2 m_Scroll;
        private string m_Filter = string.Empty;
        private SpellCatalogLoader m_Loader;
        private SpellCatalog m_Catalog;
        private readonly List<SpellDefinition> m_Filtered = new();
        private PlayerCombat m_CachedCombat;
        private string m_LastStatus = "(noch nichts gecastet)";
        private InputAction m_ToggleAction;

        private void Awake()
        {
            m_Visible = m_VisibleOnStart;
            m_ToggleAction = new(name: "SpellCatalogTestPanel.Toggle", binding: m_ToggleBinding);
            m_ToggleAction.performed += OnTogglePerformed;
        }

        private void OnEnable()
        {
            m_ToggleAction?.Enable();
        }

        private void OnDisable()
        {
            m_ToggleAction?.Disable();
        }

        private void OnDestroy()
        {
            if (m_ToggleAction != null)
            {
                m_ToggleAction.performed -= OnTogglePerformed;
                m_ToggleAction.Dispose();
                m_ToggleAction = null;
            }
        }

        /// <summary>
        /// Toggle-Callback der <see cref="InputAction"/>. Schaltet die Panel-Sichtbarkeit um.
        /// </summary>
        private void OnTogglePerformed(InputAction.CallbackContext _)
        {
            m_Visible = !m_Visible;
        }

        private void OnGUI()
        {
            if (!m_Visible)
            {
                return;
            }

            EnsureCatalog();

            float x = Screen.width - m_PanelWidth - 16f;
            float y = 16f;
            GUILayout.BeginArea(new Rect(x, y, m_PanelWidth, m_PanelHeight), GUI.skin.box);
            GUIStyle titleStyle = new(GUI.skin.label) { richText = true };
            GUILayout.Label("<b>Spell Test Catalog</b>", titleStyle);
            if (m_Catalog == null || m_Catalog.Count == 0)
            {
                GUILayout.Label("Catalog nicht geladen.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Total: {m_Catalog.Count}  |  Last: {m_LastStatus}");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter:", GUILayout.Width(48f));
            string nextFilter = GUILayout.TextField(m_Filter);
            if (nextFilter != m_Filter)
            {
                m_Filter = nextFilter;
                RebuildFilter();
            }
            GUILayout.EndHorizontal();

            m_Scroll = GUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < m_Filtered.Count; i++)
            {
                SpellDefinition def = m_Filtered[i];
                if (def == null)
                {
                    continue;
                }
                string label = $"{def.Name}  ({def.School})  ·  {def.Id}";
                if (GUILayout.Button(label))
                {
                    CastSpell(def.Id);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Lädt den Catalog beim ersten OnGUI lazy, falls noch nicht geschehen.
        /// Bricht still ab, solange der ServiceLocator den Loader noch nicht hat.
        /// </summary>
        private void EnsureCatalog()
        {
            if (m_Catalog != null)
            {
                return;
            }
            m_Loader ??= ServiceLocator.Get<SpellCatalogLoader>();
            m_Catalog = m_Loader?.GetCached();
            if (m_Catalog != null)
            {
                RebuildFilter();
            }
        }

        /// <summary>
        /// Baut die gefilterte Liste neu auf (Id + Name + School matchen).
        /// </summary>
        private void RebuildFilter()
        {
            m_Filtered.Clear();
            if (m_Catalog == null)
            {
                return;
            }
            string needle = m_Filter?.Trim().ToLowerInvariant() ?? string.Empty;
            foreach (SpellDefinition def in m_Catalog.All)
            {
                if (def == null)
                {
                    continue;
                }
                if (needle.Length == 0
                    || (def.Id != null && def.Id.ToLowerInvariant().Contains(needle))
                    || (def.Name != null && def.Name.ToLowerInvariant().Contains(needle))
                    || def.School.ToString().ToLowerInvariant().Contains(needle))
                {
                    m_Filtered.Add(def);
                }
            }
        }

        /// <summary>
        /// Holt den lokalen Owner-PlayerCombat (über NetworkManager.LocalClient)
        /// und ruft <see cref="PlayerCombat.TryRequestCastSpell"/>. Setzt
        /// <c>m_LastStatus</c> für die Statuszeile.
        /// </summary>
        private void CastSpell(string spellId)
        {
            PlayerCombat combat = ResolveLocalCombat();
            if (combat == null)
            {
                m_LastStatus = $"FAIL: kein lokaler PlayerCombat ({spellId})";
                return;
            }
            combat.TryRequestCastSpell(spellId);
            m_LastStatus = $"sent: {spellId}";
        }

        /// <summary>
        /// Cached Lookup des lokalen Owner-PlayerCombat. Invalidiert, wenn die
        /// Referenz zerstört wurde (z. B. nach Scene-Wechsel).
        /// </summary>
        private PlayerCombat ResolveLocalCombat()
        {
            if (m_CachedCombat != null)
            {
                return m_CachedCombat;
            }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return null;
            }
            NetworkObject playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (playerObj == null)
            {
                return null;
            }
            m_CachedCombat = playerObj.GetComponent<PlayerCombat>();
            return m_CachedCombat;
        }
    }
}
#endif
