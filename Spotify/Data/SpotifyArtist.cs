#nullable disable
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class Artist containing spotify artist data.
    /// </summary>
    public class SpotifyArtist : SpotifyItem
    {
        /// <inheritdoc/>
        public override BaseItem ToItem(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            var artist = SpotifyItemRepository.TryRetrieveItem<MusicArtist>(itemRepository, memoryCache, SpotifyItemRepository.B62ToGuid(Id));
            if (artist is null)
            {
                // This artist doesn't exist in the database yet, create it and save it.
                artist = new MusicArtist();
                BaseToItem(artist, parentId, ownerId);
                artist.ExternalId = $"spotify:artist:{Id}";
                itemRepository.SaveItems(new MusicArtist[] { artist }, CancellationToken.None);
            }

            memoryCache.Set(artist.Id, artist, new TimeSpan(1, 0, 0));
            return artist;
        }
    }
}
