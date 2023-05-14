#nullable disable
#pragma warning disable CA1819
#pragma warning disable CA2227
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class CommonData containing data common to many spotify types.
    /// </summary>
    public class SpotifyItem
    {
        /// <summary>
        /// Gets or sets the Spotify ID.
        /// </summary>
        [JsonRequired]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the track name.
        /// </summary>
        [JsonRequired]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the item urls.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("external_urls")]
        public Dictionary<string, string> Urls { get; set; }

        /// <summary>
        /// Gets or sets the Spotify uri.
        /// </summary>
        [JsonRequired]
        public string Uri { get; set; }

        /// <summary>
        /// Gets or sets the Spotify API href.
        /// </summary>
        [JsonRequired]
        public string Href { get; set; }

        /// <summary>
        /// Gets or sets the genres.
        /// </summary>
        /// <value>The genres.</value>
        public string[] Genres { get; set; }

        /// <summary>
        /// Gets or sets the list of images.
        /// </summary>
        public SpotifyImage[] Images { get; set; }

        /// <summary>
        /// Fill provided base item values.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="memoryCache">MemoryCache.</param>
        /// <param name="itemRepository">Item Repository.</param>
        /// <param name="parentId">ID of parent Album/Artist if there is one.</param>
        /// <param name="ownerId">ID of the user who has requested this metadata from spotify.</param>
        /// <returns> The BaseItem with filled values.</returns>
        public virtual BaseItem ToItem(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
        {
            return new Audio();
        }

        /// <summary>
        /// Fill provided base item values.
        /// </summary>
        /// <param name="item">BaseItem.</param>
        /// <param name="parentId">ID of parent Album/Artist if there is one.</param>
        /// <param name="ownerId">ID of the user who has requested this metadata from spotify.</param>
        /// <returns> The BaseItem with filled values.</returns>
        public BaseItem BaseToItem(BaseItem item, Guid? parentId = null, Guid? ownerId = null)
        {
            item.Name = Name;
            item.Id = SpotifyItemRepository.B62ToGuid(Id);
            item.ServiceName = "spotify";
            item.SortName = Name;
            item.ProviderIds = new Dictionary<string, string>() { { "Spotify", Id } };
            item.Path = Href;

            if (parentId is not null)
            {
                item.ParentId = (Guid)parentId;
            }

            if (ownerId is not null)
            {
                item.OwnerId = (Guid)ownerId;
            }

            if (Urls.ContainsKey("spotify"))
            {
                item.HomePageUrl = Urls["spotify"];
            }

            if (Genres is not null)
            {
                item.Genres = Genres;
            }

            // Use the biggest (widest) image as primary
            if (Images is not null && Images.Length > 0)
            {
                var img = Images.OrderByDescending(i => i.Width).First();
                item.AddImage(new ItemImageInfo { Width = img.Width, Height = img.Height, Path = img.Url, Type = ImageType.Primary });
            }

            // If we have two images, use the smallest (narrowest) as thumb
            if (Images is not null && Images.Length > 1)
            {
                var img = Images.OrderBy(i => i.Width).First();
                item.AddImage(new ItemImageInfo { Width = img.Width, Height = img.Height, Path = img.Url, Type = ImageType.Thumb });
            }

            item.PresentationUniqueKey = item.CreatePresentationUniqueKey();
            return item;
        }
    }
}
