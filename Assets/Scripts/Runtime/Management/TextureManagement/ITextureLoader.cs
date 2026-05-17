namespace Riftstorm.Management.TextureManagement
{
    /// <summary>
    /// Pluggable Loader-Strategie fuer den <see cref="TextureRegistry"/>.
    /// Default ist der <see cref="LazyTextureLoader"/>, weitere Loader (z. B.
    /// Addressables) koennen via <see cref="TextureRegistry.RegisterLoader"/>
    /// angefuegt werden.
    /// </summary>
    public interface ITextureLoader
    {
        /// <summary>Kann dieser Loader den angegebenen Key aufloesen?</summary>
        bool CanLoad(string key);

        /// <summary>Laedt die Textur fuer den Key oder gibt null zurueck.</summary>
        TextureData Load(string key);
    }
}
