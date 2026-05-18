namespace Riftstorm.Management.SoundManagement
{
    /// <summary>Loader-Plugin fuer den <see cref="SoundRegistry"/>.</summary>
    public interface ISoundLoader
    {
        /// <summary>True, wenn dieser Loader den angegebenen Key bedienen kann.</summary>
        bool CanLoad(string key);

        /// <summary>Laedt den Clip (sync) und liefert den aktualisierten Eintrag oder <c>null</c>.</summary>
        SoundData Load(string key);
    }
}
