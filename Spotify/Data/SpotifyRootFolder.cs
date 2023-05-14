using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Caching.Memory;

namespace Spotify.Data
{
    /// <summary>
    /// Spotify root folder class.
    /// </summary>
    public class SpotifyRootFolder : BasePluginFolder
    {
        private static Guid fixedId = new Guid("55e493c8-6411-4424-a2e7-0e2dec791e47");

        /// <summary>
        /// Initializes a new instance of the <see cref="SpotifyRootFolder"/> class.
        /// </summary>
        public SpotifyRootFolder()
        {
            Name = "Spotify";
            Id = fixedId;
            SortName = Name;
        }

        /// <summary>
        /// Gets the collection type.
        /// </summary>
        public override string CollectionType => MediaBrowser.Model.Entities.CollectionType.Playlists;

        /// <inheritdoc/>
        public override IEnumerable<Guid> GetAncestorIds()
        {
            return Array.Empty<Guid>();
        }

        /// <inheritdoc/>
        protected override string CreateSortName()
        {
            return Name;
        }

        /// <summary>
        /// Retrieves the spority root folder from db or create a new one class.
        /// </summary>
        /// <param name="itemRepository">Item repository.</param>
        /// <param name="memoryCache">The memory cache.</param>
        /// <returns>The spotify root folder.</returns>
        public static SpotifyRootFolder GetOrCreate(IItemRepository itemRepository, IMemoryCache memoryCache)
        {
            var root = SpotifyItemRepository.TryRetrieveItem<SpotifyRootFolder>(itemRepository, memoryCache, fixedId);
            if (root is not null)
            {
                return root;
            }

            root = new SpotifyRootFolder();
            itemRepository.SaveItems(new SpotifyRootFolder[] { root }, CancellationToken.None);
            memoryCache.Set(root.Id, root, new TimeSpan(1, 0, 0));
            return root;
        }
    }
}
