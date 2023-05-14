#pragma warning disable CA1002
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Interface IJSONToItems representing classes that can be converted to a list of BaseItem.
    /// </summary>
    public interface IJSONToItems
    {
        /// <summary>
        /// Convert json response to BaseItem.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="memoryCache">MemoryCache.</param>
        /// <param name="itemRepository">Item Repository.</param>
        /// <param name="parentId">ID of parent Album/Artist if there is one.</param>
        /// <param name="ownerId">ID of the user who has requested this metadata from spotify.</param>
        /// <returns> The list of artists as BaseItems.</returns>
        List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null);
    }
}
