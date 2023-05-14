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
    /// Class ArtistList containing a list of Spotify Artists.
    /// </summary>
    public class ArtistList : IJSONToItems
    {
        /// <summary>
        /// Gets or sets the Spotify Artist List.
        /// </summary>
        [JsonRequired]
        public SpotifyArtist[] Items { get; set; }

        /// <inheritdoc/>
        public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            var items = Items.Select(i =>
                    (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { ArtistCount = 1 }));
            return items.ToList();
        }
    }
}
