using System.Threading;
using System.Threading.Tasks;
using Riftstorm.Game.Sprites;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Client-lokale Bruecke zwischen den server-autoritativen Equip-NetworkVariables
    /// auf <see cref="PlayerCombat"/> (Waffe + Offhand) und den FLARE-Sprite-Layern
    /// <c>"MainHand"</c> / <c>"OffHand"</c> auf dem <see cref="FlareCharacter"/>.
    /// Wird vom <see cref="Bootstrap.GamePlayerBootstrap"/> nach dem Aufbau der
    /// Visual-Hierarchie als reine MonoBehaviour an den Prefab-Root gehaengt und
    /// per <see cref="Bind"/> initialisiert. Auf Pure-Server-Builds laeuft die
    /// Komponente nicht (Bootstrap erzeugt sie dort nicht), weil ein Server
    /// keinerlei Renderer fuehrt.
    /// </summary>
    /// <remarks>
    /// Source-Parity zum Original: das Original mapped <c>EquipSlot::Weapon1</c>
    /// auf BodyPart <c>Weapon</c>, <c>EquipSlot::Offhand</c> auf BodyPart
    /// <c>Offhand</c>, jeweils mit eigenem Atlas pro Gear-Id. Wir spiegeln das
    /// 1:1 ueber zwei separate <see cref="FlareLayerAnimator"/>-Schichten, deren
    /// Atlas-Datei per Id (z. B. <c>"longbow"</c>) aus
    /// <c>StreamingAssets/player_male</c> bzw. <c>player_female</c> nachgeladen
    /// wird.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipmentVisuals : MonoBehaviour
    {
        /// <summary>Layer-Name fuer die Haupthand-Waffe (Sword/Bow/Staff/...).</summary>
        public const string MainHandLayerName = "MainHand";

        /// <summary>Layer-Name fuer die Offhand (Buckler/Shield/Torch/...).</summary>
        public const string OffHandLayerName = "OffHand";

        private FlareCharacter m_Character;
        private FlareAtlasLoader m_Loader;
        private PlayerCombat m_Combat;
        private CancellationTokenSource m_Cts;

        /// <summary>Aktuell auf MainHand sichtbare Gear-Id (Cache fuer Idempotenz-Check).</summary>
        private string m_AppliedMainHandId = string.Empty;

        /// <summary>Aktuell auf OffHand sichtbare Gear-Id (Cache fuer Idempotenz-Check).</summary>
        private string m_AppliedOffHandId = string.Empty;

        /// <summary>
        /// Verdrahtet die Komponente nach dem Visual-Aufbau. Muss vom
        /// <see cref="Bootstrap.GamePlayerBootstrap"/> aufgerufen werden, sobald
        /// FLARE-Character + Loader existieren und <see cref="PlayerCombat"/> auf
        /// dem Root liegt. Mehrfach-Aufrufe haengen sauber um (alte Subscriptions
        /// werden geloest), sodass ein zweiter Bind im selben Lebenszyklus kein
        /// Doppel-Event ausloest.
        /// </summary>
        public void Bind(FlareCharacter character, FlareAtlasLoader loader, PlayerCombat combat)
        {
            Unbind();

            m_Character = character;
            m_Loader = loader;
            m_Combat = combat;

            if (m_Combat == null || m_Character == null || m_Loader == null)
            {
                Debug.LogWarning("[PlayerEquipmentVisuals] Bind: missing dependency — equip visuals stay disabled.", this);
                return;
            }

            m_Cts = new CancellationTokenSource();

            m_Combat.WeaponChanged += OnWeaponChanged;
            m_Combat.OffhandChanged += OnOffhandChanged;

            // Aktuellen Stand sofort anwenden — die NetworkVariable haelt zum
            // Spawn-Zeitpunkt bereits den Server-Default (z. B. "longsword" /
            // "buckler"), und OnValueChanged feuert nur bei spaeteren Aenderungen.
            ApplyAsync(MainHandLayerName, m_Combat.CurrentWeaponId, true);
            ApplyAsync(OffHandLayerName, m_Combat.CurrentOffhandId, false);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (m_Combat != null)
            {
                m_Combat.WeaponChanged -= OnWeaponChanged;
                m_Combat.OffhandChanged -= OnOffhandChanged;
            }
            if (m_Cts != null)
            {
                m_Cts.Cancel();
                m_Cts.Dispose();
                m_Cts = null;
            }
        }

        private void OnWeaponChanged(string _, string newId) => ApplyAsync(MainHandLayerName, newId, true);

        private void OnOffhandChanged(string _, string newId) => ApplyAsync(OffHandLayerName, newId, false);

        /// <summary>
        /// Laedt den Atlas asynchron und tauscht ihn auf der passenden FLARE-
        /// Schicht. Leere Id leert die Schicht (unsichtbar) — entspricht der
        /// <c>clearEquipmentModel(EquipSlot)</c>-Semantik des Originals.
        /// </summary>
        private async void ApplyAsync(string layerName, string gearId, bool isMainHand)
        {
            if (m_Character == null || m_Loader == null)
            {
                return;
            }

            string id = gearId ?? string.Empty;

            // Idempotenz: wenn die Schicht bereits genau diese Id zeigt, ueberspringen.
            if (isMainHand)
            {
                if (m_AppliedMainHandId == id) { return; }
                m_AppliedMainHandId = id;
            }
            else
            {
                if (m_AppliedOffHandId == id) { return; }
                m_AppliedOffHandId = id;
            }

            if (string.IsNullOrEmpty(id))
            {
                m_Character.SetLayerAtlas(layerName, null);
                return;
            }

            FlareAtlasLoader loader = m_Loader;
            CancellationTokenSource cts = m_Cts;

            FlareAtlas atlas;
            try
            {
                Task<FlareAtlas> load = loader.LoadAsync(id);
                atlas = await load;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PlayerEquipmentVisuals] Atlas '{id}' load failed: {ex.Message}");
                return;
            }

            // Race-Schutz: zwischen Await und Resume kann der Spieler bereits
            // wieder umgeequippt haben oder die Komponente despawnt sein.
            if (cts == null || cts.IsCancellationRequested || m_Character == null)
            {
                return;
            }
            string stillCurrent = isMainHand ? m_AppliedMainHandId : m_AppliedOffHandId;
            if (stillCurrent != id)
            {
                return;
            }

            if (atlas == null)
            {
                Debug.LogWarning($"[PlayerEquipmentVisuals] No atlas '{id}' under StreamingAssets/player_*/ — layer stays empty.");
                m_Character.SetLayerAtlas(layerName, null);
                return;
            }

            m_Character.SetLayerAtlas(layerName, atlas);
        }
    }
}
