using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Spotify.Data
{
    /// <summary>
    /// Spotify album external id.
    /// </summary>
    public class SpotifyAlbumExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => "Spotify";

        /// <inheritdoc />
        public string Key => "Spotify";

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.Album;

        /// <inheritdoc />
        public string? UrlFormatString => "https://open.spotify.com/album/{0}";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is MusicAlbum;
    }
}
