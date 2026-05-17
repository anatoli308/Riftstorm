namespace Riftstorm.Management.TextureManagement
{
    /// <summary>
    /// Quelle einer registrierten Texture. System-Texturen kommen aus
    /// <c>Application.dataPath/Art/</c>, Custom-Texturen aus
    /// <c>Application.persistentDataPath/CustomTextures/</c> und ueberschreiben
    /// gleichlautende System-Keys.
    /// </summary>
    public enum TextureSource
    {
        System,
        Custom,
    }
}
