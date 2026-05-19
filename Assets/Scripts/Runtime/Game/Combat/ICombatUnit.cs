using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using UnityEngine;

namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Abstraktion über jede kampffähige Einheit (Player, NPC, Pet). Wird von
    /// <c>SpellCaster</c>, <c>AuraManager</c>, <c>CooldownManager</c> konsumiert,
    /// damit diese Pure-Services keine direkte Abhängigkeit auf eine konkrete
    /// <see cref="UnityEngine.MonoBehaviour"/>/NetworkBehaviour-Klasse haben.
    /// </summary>
    /// <remarks>
    /// 1:1-Spiegel der lesenden + schreibenden Aufrufe, die im C++-Source auf
    /// <c>Entity*</c> gemacht werden (<c>getVariable / setVariable / takeDamage /
    /// heal / isStunned / isSilenced / getGuid</c>). Die konkrete Unit-Klasse
    /// liefert diese Werte aus ihren NetworkVariables.
    /// </remarks>
    public interface ICombatUnit
    {
        /// <summary>Stabile Netzwerk-Id (entspricht <c>Entity::getGuid</c>).</summary>
        ulong Guid { get; }

        /// <summary>Aktuelle HP. 0 = tot.</summary>
        int Health { get; }
        /// <summary>HP-Cap inkl. Stat-/Aura-Boni.</summary>
        int MaxHealth { get; }
        /// <summary>Aktuelles Mana.</summary>
        int Mana { get; }
        /// <summary>Mana-Cap inkl. Stat-/Aura-Boni.</summary>
        int MaxMana { get; }
        /// <summary>Charakter-Level (für Formel-Auswertung wie <c>clvl*20</c>).</summary>
        int Level { get; }

        /// <summary>Server-authoritative Welt-Position für Range-/LoS-Checks.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// Server-authoritative Blickrichtung (XZ-Plane, normalisiert). Wird vom
        /// <see cref="Riftstorm.Game.Spells.Runtime.SpellExecutor"/> für richtungs-
        /// abhängige Movement-Effekte konsumiert (<c>TeleportForward</c>, <c>Charge</c>,
        /// <c>SlideFrom</c>). Implementierungen liefern typischerweise eine projizierte
        /// Variante von <c>transform.forward</c>; fallen auf <c>Vector3.forward</c>
        /// zurück, falls keine sinnvolle Richtung verfügbar ist.
        /// </summary>
        Vector3 Forward { get; }

        /// <summary>True, wenn die Unit nicht handeln kann (Stun-Aura aktiv).</summary>
        bool IsStunned { get; }
        /// <summary>True, wenn die Unit nicht casten kann (Silence-Aura aktiv).</summary>
        bool IsSilenced { get; }
        /// <summary>True, wenn die Unit sich nicht bewegen kann (Root-Aura aktiv).</summary>
        bool IsRooted { get; }
        /// <summary>True, wenn die Unit gerade tot ist (<see cref="Health"/> &lt;= 0).</summary>
        bool IsDead { get; }

        /// <summary>True, wenn diese Unit ein menschlicher Spieler ist (für Cooldown-Send-Path).</summary>
        bool IsPlayer { get; }

        /// <summary>Faction-Id für friendly/hostile-Auflösung.</summary>
        int FactionId { get; }

        /// <summary>Aura-Container der Unit (jede Unit besitzt genau einen).</summary>
        AuraManager Auras { get; }
        /// <summary>Cooldown-Container der Unit (Players haben Inhalte, Mobs typischerweise leer).</summary>
        CooldownManager Cooldowns { get; }

        /// <summary>
        /// Lese-Schnittstelle auf die Stats der Unit. Wird von der Formel-Schicht
        /// (<see cref="Riftstorm.Gameplay.Combat.CombatFormulas"/>) konsumiert, damit diese keinen direkten
        /// Zugriff auf eine konkrete NetworkBehaviour-Klasse braucht.
        /// </summary>
        IUnitStats Stats { get; }

        /// <summary>
        /// Wendet Schaden an (mit Tod-Handling, Threat, Combat-Log).
        /// <paramref name="attacker"/> darf null sein bei DoT ohne Caster-Ref.
        /// </summary>
        void TakeDamage(int amount, ICombatUnit attacker);

        /// <summary>Heilt um <paramref name="amount"/> (gecappt auf <see cref="MaxHealth"/>).</summary>
        void Heal(int amount, ICombatUnit source);

        /// <summary>Setzt Mana direkt (für RestoreMana / BurnMana).</summary>
        void SetMana(int amount);

        /// <summary>
        /// Server-only: harte Reposition der Unit (Blink/Teleport/Charge-Landung).
        /// Auf Clients ein No-Op. Implementierungen müssen die replizierte
        /// Server-Position aktualisieren und sicherstellen, dass die Owner-
        /// Prediction (falls vorhanden) auf die neue Pose snappt &#8212; siehe
        /// <see cref="Riftstorm.Game.Movement.PlayerMovement.ServerTeleportTo"/>.
        /// </summary>
        void ServerTeleportTo(Vector3 position);

        /// <summary>
        /// Server-only: wendet eine externe Bewegung ueber <paramref name="durationSec"/>
        /// Sekunden an (KnockBack/PullTo/Charge/SlideFrom). Die Unit bewegt sich
        /// mit <c>direction.normalized * meters / durationSec</c> m/s. Während
        /// der Dauer wird Eigen-Input ignoriert. Auf Clients ein No-Op.
        /// </summary>
        void ServerApplyImpulse(Vector3 direction, float meters, float durationSec);
    }
}
