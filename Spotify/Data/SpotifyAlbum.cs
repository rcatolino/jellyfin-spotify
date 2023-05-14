#nullable disable
#pragma warning disable CA1819
using System.Globalization;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class Album containing spotify album data.
    /// </summary>
    public class SpotifyAlbum : SpotifyItem
    {
        /// <summary>
        /// Gets or sets the Spotify Artist List.
        /// </summary>
        [JsonRequired]
        public SpotifyArtist[] Artists { get; set; }

        /// <summary>
        /// Gets or sets the track number.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("total_tracks")]
        public int TrackCount { get; set; }

        /// <summary>
        /// Gets or sets the track number.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; }

        /// <inheritdoc/>
        public override BaseItem ToItem(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            var album = SpotifyItemRepository.TryRetrieveItem<MusicAlbum>(itemRepository, memoryCache, SpotifyItemRepository.B62ToGuid(Id));
            if (album is null)
            {
                album = new MusicAlbum();
                BaseToItem(album, parentId, ownerId);
                // album.TrackCount = TrackCount;
                album.ExternalId = $"spotify:album:{Id}";
                album.Artists = Artists.Select(a => a.Name).ToList();
                album.AlbumArtists = album.Artists;
                foreach (var artist in Artists)
                {
                    artist.ToItem(logger, memoryCache, itemRepository, null, ownerId);
                }

                if (parentId is null)
                {
                    album.ParentId = SpotifyItemRepository.B62ToGuid(Artists.First().Id);
                }

                try
                {
                    album.ProductionYear = int.Parse(ReleaseDate.Split("-")[0], new CultureInfo("en-US"));
                }
                catch
                {
                }

                itemRepository.SaveItems(new MusicAlbum[] { album }, CancellationToken.None);
            }

            memoryCache.Set(album.Id, album, new TimeSpan(1, 0, 0));
            return album;
        }
    }
}
