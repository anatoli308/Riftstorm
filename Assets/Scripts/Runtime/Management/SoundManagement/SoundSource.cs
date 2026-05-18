namespace Riftstorm.Management.SoundManagement
{
    /// <summary>Quelle eines Sound-Eintrags (System = Projekt-Assets, Custom = Mods).</summary>
    public enum SoundSource
    {
        /// <summary>Aus <c>Application.dataPath/Art/sounds</c> gescannt.</summary>
        System = 0,
        /// <summary>Aus <c>Application.persistentDataPath/CustomSounds</c> gescannt.</summary>
        Custom = 1,
    }
}
