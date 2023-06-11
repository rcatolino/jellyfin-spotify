#pragma warning disable CA1819
#pragma warning disable CA1034
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class TrackList containing a list of Spotify Tracks.
    /// </summary>
    public class FavTrackList : IJSONToItems
    {
        /// <summary>
        /// Gets or sets the Spotify Track List.
        /// </summary>
        public FavoriteTrack[]? Items { get; set; }

        /// <inheritdoc/>
        public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            if (Items is null)
            {
                logger.LogError("Error, spotify favorites track list doesn't contain any Items or Track member");
                return new List<(BaseItem, ItemCounts)>();
            }

            var tracks = Items.Where(t => t.Track is not null).Select(t => t.Track!).ToArray();
            return tracks.Select(i =>
                    (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { SongCount = 1 }))
                .ToList();
        }

        /// <summary>
        /// Class FavoriteTrack containing a favorite track.
        /// I swear the spotify api was specially made to wind me up, but whatever.
        /// </summary>
        public class FavoriteTrack
        {
            /// <summary>
            /// Gets or sets the Spotify Track.
            /// </summary>
            public SpotifyTrack? Track { get; set; }
        }
    }
}
