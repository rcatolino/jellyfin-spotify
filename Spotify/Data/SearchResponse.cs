#nullable disable
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class SearchResponse containing a Spotify Api result to a Search query.
    /// </summary>
    public class SearchResponse : IJSONToItems
    {
        /// <summary>
        /// Gets or sets the list of artists.
        /// </summary>
        public ArtistList Artists { get; set; }

        /// <summary>
        /// Gets or sets the list of albums.
        /// </summary>
        public TrackList Tracks { get; set; }

        /// <summary>
        /// Gets or sets the list of albums.
        /// </summary>
        public AlbumList Albums { get; set; }

        /// <inheritdoc/>
        public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            var items = new List<List<(BaseItem, ItemCounts)>>();

            if (Artists is not null)
            {
                logger.LogInformation("Spotify query got {N} artists matches", Artists.Items.Length);
                items.Add(Artists.ToItems(logger, memoryCache, itemRepository, parentId, ownerId));
            }

            if (Tracks is not null)
            {
                logger.LogInformation("Spotify query got {N} track matches", Tracks.Items.Length);
                items.Add(Tracks.ToItems(logger, memoryCache, itemRepository, parentId, ownerId));
            }

            if (Albums is not null)
            {
                logger.LogInformation("Spotify query got {N} album matches", Albums.Items.Length);
                items.Add(Albums.ToItems(logger, memoryCache, itemRepository, parentId, ownerId));
            }

            var res = items.SelectMany(list => list).ToList();
            return res;
        }
    }
}
