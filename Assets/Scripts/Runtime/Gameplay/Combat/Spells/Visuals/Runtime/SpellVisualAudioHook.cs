using System;
using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Statischer Audio-Bridge fuer <see cref="SpellVisualSpawner"/>. Die
    /// Gameplay-Assembly darf nicht direkt auf <c>Riftstorm.Management</c>
    /// oder <c>Riftstorm.ApplicationLifecycle</c> referenzieren (das wuerde
    /// einen Assembly-Zyklus erzeugen, da ApplicationLifecycle bereits
    /// Gameplay referenziert). Stattdessen registriert die obere Schicht
    /// (typisch <c>PlayerCombat</c> in <c>Riftstorm.Game</c>) hier einen
    /// Clip-Resolver, der den Dateinamen (inkl. Extension) auf einen
    /// <see cref="AudioClip"/> aus dem <c>SoundManager</c> mappt.
    /// </summary>
    /// <remarks>
    /// Solange kein Resolver gesetzt ist, sind Phase-Sounds ein stilles
    /// No-Op &#8212; der Renderpfad bleibt funktional.
    /// </remarks>
    public static class SpellVisualAudioHook
    {
        /// <summary>
        /// Loest einen Sound-Dateinamen (inkl. Extension, z. B.
        /// <c>"skill_heal.wav"</c>) auf einen <see cref="AudioClip"/> auf.
        /// Wird vom Bootstrap (z. B. <c>PlayerCombat.Awake</c>) gesetzt.
        /// </summary>
        public static Func<string, AudioClip> ClipResolver;
    }
}
