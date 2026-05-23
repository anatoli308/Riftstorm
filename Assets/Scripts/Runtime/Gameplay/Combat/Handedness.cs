namespace Riftstorm.Gameplay.Combat
{
    /// <summary>
    /// Beidhaendigkeits-Eigenschaft einer Waffe. Bestimmt, ob beim Equip in den
    /// MainHand-Slot der Offhand-Slot zwangsweise geleert werden muss
    /// (Source-Parity zum Original: Zweihaender belegen MainHand und blockieren
    /// Schild/Buckler/Torch in der Offhand).
    /// </summary>
    public enum Handedness
    {
        /// <summary>
        /// Default. Die Waffe belegt nur MainHand; eine Offhand kann parallel
        /// gefuehrt werden (z. B. Longsword + Buckler).
        /// </summary>
        OneHanded = 0,

        /// <summary>
        /// Beidhaendig. Equip in MainHand verdraengt den Offhand-Slot automatisch.
        /// Versuch, eine Offhand bei aktiver TwoHanded-Waffe zu equippen, wird
        /// vom Server abgelehnt.
        /// </summary>
        TwoHanded = 1,
    }
}
