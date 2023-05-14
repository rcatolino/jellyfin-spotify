#nullable disable
#pragma warning disable CA1819
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class AlbumList containing a list of Spotify albums.
    /// </summary>
    public class AlbumList : IJSONToItems
    {
        /// <summary>
        /// Gets or sets the Spotify Album List.
        /// </summary>
        [JsonRequired]
        public SpotifyAlbum[] Items { get; set; }

        /// <summary>
        /// Gets or sets the number of albums found.
        /// </summary>
        [JsonRequired]
        public int Total { get; set; }

        /// <inheritdoc/>
        public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            var items = Items.Select(i =>
                    (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { AlbumCount = 1 }));
            return items.ToList();
        }
    }
}
