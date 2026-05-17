namespace Riftstorm.Game.Combat
{
    /// <summary>
    /// Spieler-spezifischer D&#252;nn-Wrapper um <see cref="UnitCombatVisuals"/>.
    /// Existiert ausschlie&#223;lich, damit die im Player-Prefab serialisierte
    /// Skript-Referenz (per GUID dieser Datei) intakt bleibt und alle bisherigen
    /// Inspector-Werte (m_AnimSwing &#8230; m_AnimDie, m_Character) automatisch in
    /// die geerbten Felder der Basisklasse migriert werden. Die gesamte Logik
    /// lebt in <see cref="UnitCombatVisuals"/> und ist damit ohne Code-Duplikat
    /// auch f&#252;r NPCs verwendbar.
    /// </summary>
    public sealed class PlayerCombatVisuals : UnitCombatVisuals
    {
        // Bewusst leer &#8212; gesamte Funktionalit&#228;t lebt in <see cref="UnitCombatVisuals"/>.
    }
}
