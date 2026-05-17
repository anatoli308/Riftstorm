using UnityEngine;

namespace Riftstorm.Game.Player
{
    /// <summary>
    /// Spieler-Spezialisierung von <see cref="UnitNameTag"/>. Der gesamte
    /// Anzeige-/Hover-/Outline-/Click-Pfad liegt in der Basisklasse. Diese
    /// Klasse existiert nur, damit:
    /// <list type="bullet">
    ///   <item>im Player-Prefab serialisierte Skript-Referenzen (GUID) intakt bleiben &#8212;
    ///   die Skript-GUID dieser Datei wandert nicht.</item>
    ///   <item>via <c>[RequireComponent]</c> die <see cref="PlayerIdentity"/>-Komponente
    ///   am selben GameObject erzwungen wird, damit der Inspector sie automatisch
    ///   anlegt, sobald jemand einen neuen Spieler aufbaut.</item>
    /// </list>
    /// Das serialisierte Feld <c>m_Identity</c> aus dem alten Prefab-YAML wird per
    /// <c>FormerlySerializedAs</c>-Attribut in <see cref="UnitNameTag"/> auf das geerbte
    /// <c>m_IdentitySource</c>-Feld migriert (PlayerIdentity ist MonoBehaviour-abgeleitet).
    /// </summary>
    [RequireComponent(typeof(PlayerIdentity))]
    public sealed class PlayerNameTag : UnitNameTag
    {
        // Bewusst leer. Alle Funktionalitaet in UnitNameTag.
    }
}
