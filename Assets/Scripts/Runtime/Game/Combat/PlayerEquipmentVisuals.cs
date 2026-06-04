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

        /// <summary>Layer-Name fuer die Ranged-Waffe (Bow/Crossbow/Gun) auf dem
        /// Ranged-Slot. Eigene FLARE-Schicht, damit Bogen + Schwert/Schild
        /// gleichzeitig modelliert sein koennen \u2014 der Renderer zeigt aber pro
        /// Frame nur die "Stance"-Variante an (siehe Apply-Logik unten).</summary>
        public const string RangedLayerName = "Ranged";

        private FlareCharacter m_Character;
        private FlareAtlasLoader m_Loader;
        private PlayerCombat m_Combat;
        private CancellationTokenSource m_Cts;

        /// <summary>Aktuell auf MainHand sichtbare Gear-Id (Cache fuer Idempotenz-Check).</summary>
        private string m_AppliedMainHandId = string.Empty;

        /// <summary>Aktuell auf OffHand sichtbare Gear-Id (Cache fuer Idempotenz-Check).</summary>
        private string m_AppliedOffHandId = string.Empty;

        /// <summary>Aktuell auf Ranged sichtbare Gear-Id (Cache fuer Idempotenz-Check).</summary>
        private string m_AppliedRangedId = string.Empty;

        /// <summary>
        /// <c>true</c>, sobald der Spieler dauerhaft im Ranged-Auto-Attack-Modus
        /// steht (Toggle ueber Taste 'T', server-autoritativ via
        /// <see cref="PlayerCombat.WeaponModeChanged"/>). In diesem Zustand zeigt
        /// der Renderer dauerhaft den Bogen (Ranged-Schicht) statt MainHand/OffHand;
        /// die Cast-getriggerten <see cref="ShowRangedForCast"/> /
        /// <see cref="HideRangedAfterCast"/> werden dann zu No-Ops, damit ein
        /// Shoot-Cast die Bogen-Stance nicht versehentlich auf Schwert/Schild
        /// zuruecksetzt.
        /// </summary>
        private bool m_RangedStanceActive;

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
            m_Combat.WeaponModeChanged += OnWeaponModeChanged;
            // RangedChanged wird bewusst NICHT abonniert: der Ranged-Slot wird
            // entweder ueber die dauerhafte Ranged-Stance (WeaponModeChanged) oder
            // ueber ein Cast-getriggertes Visual (ShowRangedForCast/
            // HideRangedAfterCast) sichtbar gemacht. Welche Hand-Waffe pro Frame
            // sichtbar ist, entscheidet der aktuelle Stance (siehe ApplyStance).

            // Initiale Stance aus dem server-autoritativen Modus ableiten und sofort
            // anwenden — die NetworkVariables halten zum Spawn-Zeitpunkt bereits den
            // Server-Default (z. B. "longsword" / "buckler"), OnValueChanged feuert
            // nur bei spaeteren Aenderungen.
            m_RangedStanceActive = m_Combat.IsRangedModeActive;
            ApplyStance();
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
                m_Combat.WeaponModeChanged -= OnWeaponModeChanged;
            }
            if (m_Cts != null)
            {
                m_Cts.Cancel();
                m_Cts.Dispose();
                m_Cts = null;
            }
        }

        private void OnWeaponChanged(string _, string newId)
        {
            // In der Ranged-Stance bleibt die MainHand ausgeblendet; die neue Id
            // wird beim Zurueckschalten auf Melee frisch aus CurrentWeaponId
            // gelesen (siehe ApplyStance), daher hier kein sofortiges Einblenden.
            if (m_RangedStanceActive) { return; }
            ApplyAsync(MainHandLayerName, newId, AppliedSlot.Main);
        }

        private void OnOffhandChanged(string _, string newId)
        {
            if (m_RangedStanceActive) { return; }
            ApplyAsync(OffHandLayerName, newId, AppliedSlot.Off);
        }

        /// <summary>
        /// Reagiert auf den server-autoritativen Wechsel des Auto-Attack-Modus
        /// (Melee &lt;-&gt; Ranged). Im Ranged-Modus wird dauerhaft der Bogen gezeigt
        /// (MainHand/OffHand leer, Ranged-Schicht = <see cref="PlayerCombat.CurrentRangedId"/>),
        /// im Melee-Modus wieder Haupt-/Nebenhand aus den Equip-NetVars.
        /// </summary>
        private void OnWeaponModeChanged(bool rangedActive)
        {
            m_RangedStanceActive = rangedActive;
            ApplyStance();
        }

        /// <summary>
        /// Wendet die aktuell gueltige Waffen-Stance auf die FLARE-Schichten an.
        /// Ranged-Stance: nur der Bogen ist sichtbar. Melee-Stance: MainHand +
        /// OffHand aus den server-autoritativen <see cref="PlayerCombat"/>-NetVars,
        /// Ranged-Schicht leer. Idempotent ueber den Slot-Cache in
        /// <see cref="ApplyAsync"/>.
        /// </summary>
        private void ApplyStance()
        {
            if (m_Combat == null)
            {
                return;
            }
            if (m_RangedStanceActive)
            {
                ApplyAsync(MainHandLayerName, string.Empty, AppliedSlot.Main);
                ApplyAsync(OffHandLayerName, string.Empty, AppliedSlot.Off);
                ApplyAsync(RangedLayerName, m_Combat.CurrentRangedId, AppliedSlot.Ranged);
            }
            else
            {
                ApplyAsync(RangedLayerName, string.Empty, AppliedSlot.Ranged);
                ApplyAsync(MainHandLayerName, m_Combat.CurrentWeaponId, AppliedSlot.Main);
                ApplyAsync(OffHandLayerName, m_Combat.CurrentOffhandId, AppliedSlot.Off);
            }
        }

        /// <summary>
        /// Wechselt die Visuals fuer einen Shoot-Cast: MainHand + OffHand werden
        /// kurzzeitig ausgeblendet und der Ranged-Atlas (Bow/Crossbow/Gun) auf der
        /// <see cref="RangedLayerName"/>-Schicht eingeblendet. Wird vom
        /// <see cref="PlayerCombat.BeginCastClientRpc"/> auf allen Peers gerufen,
        /// sobald ein Spell mit <c>RequiredEquipment == 12</c> (Ranged) gestartet
        /// wird. <see cref="HideRangedAfterCast"/> stellt am Cast-Ende den
        /// MainHand/OffHand-Stand aus den server-autoritativen NetVars wieder her.
        /// </summary>
        public void ShowRangedForCast(string rangedId)
        {
            // In dauerhafter Ranged-Stance ist der Bogen bereits sichtbar — der
            // Cast-Trigger ist dann ein No-Op und darf MainHand/OffHand nicht
            // anfassen.
            if (m_RangedStanceActive)
            {
                return;
            }
            // Main/Offhand kurzzeitig ausblenden — der Bogen ist die einzige
            // sichtbare Hand-Waffe waehrend des Schuss-Casts.
            ApplyAsync(MainHandLayerName, string.Empty, AppliedSlot.Main);
            ApplyAsync(OffHandLayerName, string.Empty, AppliedSlot.Off);
            ApplyAsync(RangedLayerName, rangedId, AppliedSlot.Ranged);
        }

        /// <summary>
        /// Entfernt den Ranged-Atlas und stellt MainHand + OffHand aus den
        /// aktuellen <see cref="PlayerCombat"/>-NetVars wieder her. Idempotent —
        /// wiederholte Aufrufe sind ein No-Op. Wird vom
        /// <see cref="PlayerCombat.EndCastClientRpc"/> am Cast-Ende
        /// (Erfolg ODER Abbruch) auf allen Peers gerufen.
        /// </summary>
        public void HideRangedAfterCast()
        {
            // In dauerhafter Ranged-Stance bleibt der Bogen stehen — ein endender
            // Shoot-Cast darf nicht auf Schwert/Schild zuruecksetzen.
            if (m_RangedStanceActive)
            {
                return;
            }
            ApplyAsync(RangedLayerName, string.Empty, AppliedSlot.Ranged);
            if (m_Combat != null)
            {
                ApplyAsync(MainHandLayerName, m_Combat.CurrentWeaponId, AppliedSlot.Main);
                ApplyAsync(OffHandLayerName, m_Combat.CurrentOffhandId, AppliedSlot.Off);
            }
        }

        /// <summary>
        /// Drei Layer-Slots, deren Idempotenz-Cache getrennt gehalten wird.
        /// Wird intern an <see cref="ApplyAsync"/> uebergeben, damit die
        /// Race-Pruefung beim Async-Resume den richtigen Cache vergleicht.
        /// </summary>
        private enum AppliedSlot { Main, Off, Ranged }

        /// <summary>
        /// Laedt den Atlas asynchron und tauscht ihn auf der passenden FLARE-
        /// Schicht. Leere Id leert die Schicht (unsichtbar) — entspricht der
        /// <c>clearEquipmentModel(EquipSlot)</c>-Semantik des Originals.
        /// </summary>
        private async void ApplyAsync(string layerName, string gearId, AppliedSlot slot)
        {
            if (m_Character == null || m_Loader == null)
            {
                return;
            }

            string id = gearId ?? string.Empty;

            // Idempotenz: wenn die Schicht bereits genau diese Id zeigt, ueberspringen.
            switch (slot)
            {
                case AppliedSlot.Main:
                    if (m_AppliedMainHandId == id) { return; }
                    m_AppliedMainHandId = id;
                    break;
                case AppliedSlot.Off:
                    if (m_AppliedOffHandId == id) { return; }
                    m_AppliedOffHandId = id;
                    break;
                case AppliedSlot.Ranged:
                    if (m_AppliedRangedId == id) { return; }
                    m_AppliedRangedId = id;
                    break;
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
            string stillCurrent = slot switch
            {
                AppliedSlot.Main => m_AppliedMainHandId,
                AppliedSlot.Off => m_AppliedOffHandId,
                AppliedSlot.Ranged => m_AppliedRangedId,
                _ => string.Empty
            };
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
