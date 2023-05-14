#pragma warning disable CA1819
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
    public class TrackList : IJSONToItems
    {
        /// <summary>
        /// Gets or sets the Spotify Track List.
        /// </summary>
        public SpotifyTrack[]? Items { get; set; }

        /// <summary>
        /// Gets or sets the Spotify Track List.
        /// </summary>
        public SpotifyTrack[]? Tracks { get; set; }

        /// <inheritdoc/>
        public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            var tracks = Items ?? Tracks;
            if (tracks is null)
            {
                logger.LogError("Error, spotify track list doesn't contain any Items or Track member");
                return new List<(BaseItem, ItemCounts)>();
            }

            return tracks.Select(i =>
                    (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { SongCount = 1 }))
                .ToList();
        }
    }
}
