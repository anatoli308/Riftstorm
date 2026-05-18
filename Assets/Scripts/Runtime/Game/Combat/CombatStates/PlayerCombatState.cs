using Riftstorm.Game.Spells;
using Riftstorm.Gameplay.Combat;
using Tolik.Riftstorm.Runtime.Core;

namespace Riftstorm.Game.Combat.CombatStates
{
    /// <summary>
    /// Abstrakte Basis für alle Combat-States von <see cref="PlayerCombat"/>.
    /// Stellt No-Op-Defaults für alle Lifecycle- und Input-Hooks bereit, damit
    /// konkrete States nur die für sie relevanten Methoden überschreiben.
    /// </summary>
    public abstract class PlayerCombatState : State<PlayerCombat>
    {
        /// <inheritdoc/>
        public override void Enter() { }

        /// <inheritdoc/>
        public override void Exit() { }

        /// <summary>
        /// Wird auf dem Server aufgerufen, sobald der Owner via ServerRpc eine
        /// Attacke anfragt. States entscheiden, ob die Anfrage akzeptiert wird
        /// (Idle → Attacking) oder verworfen (Attacking/Dead).
        /// </summary>
        /// <param name="weapon">Aktuell ausgerüstete Waffe (server-seitig aufgelöst).</param>
        public virtual void OnAttackRequested(WeaponDefinition weapon) { }

        /// <summary>
        /// Wird auf dem Server aufgerufen, sobald der Owner via ServerRpc einen
        /// Spell-Cast anfragt. States entscheiden, ob der Cast akzeptiert wird
        /// (Idle → Casting / sofortige Ausführung) oder verworfen (Attacking,
        /// Casting, Dead). Default ist no-op = verwerfen.
        /// </summary>
        /// <param name="spellEntry">Numerischer Spell-Entry (z. B. 133).</param>
        /// <param name="spell">Vorgeladenes Template aus <see cref="SpellCatalogLoader"/>.</param>
        /// <param name="targetNetId">NetworkObjectId des Primärziels (0 = self).</param>
        /// <param name="primaryTarget">Aufgelöste Combat-Unit des Primärziels.</param>
        public virtual void OnCastRequested(int spellEntry, SpellTemplate spell, ulong targetNetId, ICombatUnit primaryTarget) { }

        /// <summary>Wird beim Tod aufgerufen — erzwingt Übergang in den Dead-State.</summary>
        public virtual void OnDeath() { }

        /// <summary>Wird bei Respawn aufgerufen — kehrt aus Dead nach Idle zurück.</summary>
        public virtual void OnRespawn() { }
    }
}
