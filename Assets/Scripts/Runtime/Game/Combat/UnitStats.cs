using System;
using Riftstorm.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Server-autoritative HP- und Combat-Stats-Komponente. Hängt auf jeder
    /// Einheit (Spieler, NPC) und ist die einzige Stelle, an der HP-Werte
    /// verändert werden dürfen.
    ///
    /// <para>
    /// Implementiert <see cref="IDamageable"/> für eingehenden Schaden und
    /// <see cref="IUnitStats"/> als Lese-Schnittstelle für die
    /// <see cref="CombatFormulas"/>.
    /// </para>
    /// <para>
    /// Aktuelle HP werden via <see cref="NetworkVariable{T}"/> an alle Clients
    /// gesynct (Phase-4-MVP — HUD/Floating-Text-Hookup folgt in einer späteren
    /// Phase, das Server-Event <see cref="OnServerDied"/> ist bereits da).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitStats : NetworkBehaviour, IDamageable, IUnitStats
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [Header("Base Stats")]
        [SerializeField, Min(1)] private int m_MaxHp = 100;
        [Tooltip("Maximale Mana. 0 = Einheit hat keine Mana-Ressource (z. B. reine Melee-Mobs); " +
                 "die Mana-Bar im Target-Frame wird dann ausgeblendet.")]
        [SerializeField, Min(0)] private int m_MaxMana = 0;
        [SerializeField, Min(0)] private int m_Strength = 5;
        [SerializeField, Min(0)] private int m_Armor = 0;
        [SerializeField, Min(1)] private int m_Level = 1;

        [Header("Hitbox")]
        [Tooltip("Radius der Einheits-Hitbox in Metern. Wird vom Server-seitigen Hit-Resolve " +
                 "als 'Reichweiten-Erweiterung' benutzt: distance(angreifer, opfer) <= " +
                 "weapon.Range + opfer.HitRadius. Damit fühlt sich der LoL-Style Range-Indicator " +
                 "konsistent an — sobald der Ring die Körperhülle des Ziels touchiert, landet der Schlag.")]
        [SerializeField, Min(0f)] private float m_HitRadius = 0.5f;

        // -------------------------------------------------------------------------
        // Netzwerk-State
        // -------------------------------------------------------------------------

        private readonly NetworkVariable<int> m_CurrentHp = new(
            value: 0,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> m_CurrentMana = new(
            value: 0,
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // -------------------------------------------------------------------------
        // IUnitStats
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public int CurrentHp => m_CurrentHp.Value;

        /// <inheritdoc/>
        public int MaxHp => m_MaxHp;

        /// <summary>Aktuelle Mana (0 falls die Einheit keine Mana hat).</summary>
        public int CurrentMana => m_CurrentMana.Value;

        /// <summary>Maximale Mana (0 falls die Einheit keine Mana-Ressource besitzt).</summary>
        public int MaxMana => m_MaxMana;

        /// <summary>True, wenn diese Einheit ueberhaupt eine Mana-Ressource hat.</summary>
        public bool HasMana => m_MaxMana > 0;

        /// <inheritdoc/>
        public int Strength => m_Strength;

        /// <inheritdoc/>
        public int Armor => m_Armor;

        /// <inheritdoc/>
        public int Level => m_Level;

        /// <summary>
        /// Radius der Körper-Hitbox in Metern. Wird vom Server-Hit-Resolve als
        /// Reichweiten-Bonus addiert (siehe <see cref="m_HitRadius"/>).
        /// </summary>
        public float HitRadius => m_HitRadius;

        // -------------------------------------------------------------------------
        // IDamageable
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public bool IsDead => m_CurrentHp.Value <= 0;

        /// <summary>
        /// Server-Event: wird einmalig ausgelöst, sobald HP auf ≤0 fällt. Wird
        /// vom Spawner / Combat-StateMachine benutzt, um in den Dead-State zu
        /// gehen oder Loot zu droppen.
        /// </summary>
        public event Action OnServerDied;

        /// <summary>
        /// Feuert auf jedem Peer, sobald sich die HP aendern (oder beim Spawn
        /// einmal initial). Erster Parameter = aktuelle HP, zweiter = MaxHp.
        /// Listener bauen damit ihre HP-Bar ohne Polling auf.
        /// </summary>
        public event Action<int, int> HpChanged;

        /// <summary>
        /// Feuert auf jedem Peer, sobald sich die Mana aendert (oder beim Spawn
        /// einmal initial). Erster Parameter = aktuelle Mana, zweiter = MaxMana.
        /// Bei <see cref="HasMana"/> == false bleibt der Wert auf 0.
        /// </summary>
        public event Action<int, int> ManaChanged;

        /// <inheritdoc/>
        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsServer)
            {
                return;
            }
            if (IsDead || info.FinalDamage <= 0)
            {
                // Trotzdem den Client-Fanout schicken, damit Miss/Dodge/Block-FX laufen können.
                BroadcastDamageClientRpc(info.FinalDamage, (byte)info.HitResult);
                return;
            }

            int previousHp = m_CurrentHp.Value;
            int newHp = Mathf.Max(0, previousHp - info.FinalDamage);
            m_CurrentHp.Value = newHp;

            BroadcastDamageClientRpc(info.FinalDamage, (byte)info.HitResult);

            if (newHp == 0)
            {
                try
                {
                    OnServerDied?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                m_CurrentHp.Value = m_MaxHp;
                m_CurrentMana.Value = m_MaxMana;
            }
            m_CurrentHp.OnValueChanged += OnHpValueChangedInternal;
            m_CurrentMana.OnValueChanged += OnManaValueChangedInternal;
            // Initialwerte einmal feuern, damit UI-Schichten korrekt initialisieren.
            HpChanged?.Invoke(m_CurrentHp.Value, m_MaxHp);
            ManaChanged?.Invoke(m_CurrentMana.Value, m_MaxMana);
        }

        /// <inheritdoc/>
        public override void OnNetworkDespawn()
        {
            m_CurrentHp.OnValueChanged -= OnHpValueChangedInternal;
            m_CurrentMana.OnValueChanged -= OnManaValueChangedInternal;
            base.OnNetworkDespawn();
        }

        private void OnHpValueChangedInternal(int previous, int current)
        {
            HpChanged?.Invoke(current, m_MaxHp);
        }

        private void OnManaValueChangedInternal(int previous, int current)
        {
            ManaChanged?.Invoke(current, m_MaxMana);
        }

        // -------------------------------------------------------------------------
        // Server-Helpers
        // -------------------------------------------------------------------------

        /// <summary>Server-only: setzt die HP zurück (z. B. bei Respawn).</summary>
        public void ServerResetHp()
        {
            if (!IsServer)
            {
                return;
            }
            m_CurrentHp.Value = m_MaxHp;
        }

        /// <summary>Server-only: setzt die Mana zurück (z. B. bei Respawn).</summary>
        public void ServerResetMana()
        {
            if (!IsServer)
            {
                return;
            }
            m_CurrentMana.Value = m_MaxMana;
        }

        /// <summary>
        /// Server-only: Manaverbrauch fuer Spells. Gibt true zurueck, wenn genug Mana
        /// vorhanden war (analog SoF-SpellCaster::checkResources). Zieht keinen Mana ab,
        /// wenn die Einheit keine Mana hat (<see cref="HasMana"/> == false).
        /// </summary>
        public bool ServerTryConsumeMana(int amount)
        {
            if (!IsServer || amount <= 0 || !HasMana)
            {
                return amount <= 0;
            }
            if (m_CurrentMana.Value < amount)
            {
                return false;
            }
            m_CurrentMana.Value -= amount;
            return true;
        }

        // -------------------------------------------------------------------------
        // Client-Fanout
        // -------------------------------------------------------------------------

        /// <summary>
        /// Feuert auf jedem Client (inkl. Host), sobald ein Schadens-Ereignis
        /// reinkommt. Parameter: <c>amount</c> = bereits gemilderter Final-
        /// Schaden, <c>result</c> = Hit-Klassifikation (Hit/Crit/Miss/…).
        /// Wird vom <see cref="FloatingCombatText"/> und ggf. Hit-Reactions
        /// abonniert. Keine Polling-Logik n&#246;tig.
        /// </summary>
        public event Action<int, HitResult> ClientDamageReceived;

        /// <summary>
        /// Verteilt das Schadens-Ereignis an alle Clients und l&#246;st das
        /// <see cref="ClientDamageReceived"/>-Event lokal aus.
        /// </summary>
        [ClientRpc]
        private void BroadcastDamageClientRpc(int amount, byte hitResult, ClientRpcParams _ = default)
        {
            HitResult result = (HitResult)hitResult;
            try
            {
                ClientDamageReceived?.Invoke(amount, result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }
    }
}
