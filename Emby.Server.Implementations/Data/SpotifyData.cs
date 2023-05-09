#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Data
{
    /// <summary>
    /// Class SpotifyData containing spotify api types.
    /// </summary>
    public class SpotifyData
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

        /// <summary>
        /// Return a local item by Id if it exists in cache or db and comes from spotify (or else return null).
        /// </summary>
        /// <param name="itemRepository">Item repository.</param>
        /// <param name="memoryCache">Memory cache.</param>
        /// <param name="localId">Local Id.</param>
        /// <typeparam name="T">BaseItem type (MusicArtist/MusicAlbum or Audio.</typeparam>
        /// <returns>Existing Item or null.</returns>
        public static T TryRetrieveItem<T>(IItemRepository itemRepository, IMemoryCache memoryCache, Guid localId)
            where T : BaseItem
        {
            if (memoryCache.TryGetValue<T>(localId, out T cachedItem))
            {
                return cachedItem;
            }

            var item = itemRepository.RetrieveItem(localId);
            if (item is T localItem && item.ExternalId is not null && item.ExternalId.StartsWith("spotify", StringComparison.InvariantCulture))
            {
                memoryCache.Set(localItem.Id, localItem, new TimeSpan(1, 0, 0));
                return localItem;
            }

            return null;
        }

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
                return new Guid[] { };
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
                var root = TryRetrieveItem<SpotifyRootFolder>(itemRepository, memoryCache, fixedId);
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

        /// <summary>
        /// Class CommonData containing data common to many spotify types.
        /// </summary>
        public class CommonData
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
            public Image[] Images { get; set; }

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
                item.Id = Base62.B62ToGuid(Id);
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

        /// <summary>
        /// Class Image containing spotify image data.
        /// </summary>
        public class Image
        {
            /// <summary>
            /// Gets or sets the url.
            /// </summary>
            [JsonRequired]
            public string Url { get; set; }

            /// <summary>
            /// Gets or sets the height.
            /// </summary>
            [JsonRequired]
            public int Height { get; set; }

            /// <summary>
            /// Gets or sets the width.
            /// </summary>
            [JsonRequired]
            public int Width { get; set; }
        }

        /// <summary>
        /// Class Album containing spotify album data.
        /// </summary>
        public class Album : CommonData
        {
            /// <summary>
            /// Gets or sets the Spotify Artist List.
            /// </summary>
            [JsonRequired]
            public Artist[] Artists { get; set; }

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
                var album = TryRetrieveItem<MusicAlbum>(itemRepository, memoryCache, Base62.B62ToGuid(Id));
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
                        album.ParentId = Base62.B62ToGuid(Artists.First().Id);
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

        /// <summary>
        /// Class Track containing spotify track data.
        /// </summary>
        public class Track : CommonData
        {
            /// <summary>
            /// Gets or sets the Spotify Artist List.
            /// </summary>
            [JsonRequired]
            public Artist[] Artists { get; set; }

            /// <summary>
            /// Gets or sets the duration (in ms).
            /// </summary>
            [JsonRequired]
            [JsonPropertyName("duration_ms")]
            public long DurationMs { get; set; }

            /// <summary>
            /// Gets or sets the track number.
            /// </summary>
            [JsonRequired]
            [JsonPropertyName("track_number")]
            public int TrackNumber { get; set; }

            /// <summary>
            /// Gets or sets the disc number.
            /// </summary>
            [JsonRequired]
            [JsonPropertyName("disc_number")]
            public int DiscNumber { get; set; }

            /// <inheritdoc/>
            public override BaseItem ToItem(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
            {
                var track = TryRetrieveItem<Audio>(itemRepository, memoryCache, Base62.B62ToGuid(Id));
                bool mustSave = false;
                if (track is null)
                {
                    track = new Audio();
                    BaseToItem(track, parentId, ownerId);
                    track.ExternalId = $"spotify:track:{Id}";
                    track.SortName = $"{DiscNumber:D3}-{TrackNumber:D3}-{track.SortName}";
                    track.IndexNumber = TrackNumber;
                    track.ParentIndexNumber = DiscNumber;
                    track.RunTimeTicks = DurationMs * 10000;
                    track.Artists = Artists.Select(a => a.Name).ToList();
                    foreach (var artist in Artists)
                    {
                        artist.ToItem(logger, memoryCache, itemRepository, null, ownerId);
                    }

                    // This track doesn't exist in the database yet, create it and save it.
                    mustSave = true;
                }
                else
                {
                    if (parentId is not null && track.ParentId.Equals(Guid.Empty))
                    {
                        logger.LogInformation("Setting parentId to {P} for track {T}", parentId, track.Id);
                        track.ParentId = (Guid)parentId;
                        mustSave = true;
                    }
                }

                if (mustSave)
                {
                    itemRepository.SaveItems(new Audio[] { track }, CancellationToken.None); // TODO: move saving to parent to batch DB ops
                    // This track doesn't exist in the database yet (or is missing data), create it and save it.
                    // TODO: do we really need to save every track ? Maybe only once they appear in a playlist/playqueue is enough. ie if this track has no parent don't bother saving it
                    // TODO: force saving if metadata is too old
                }

                memoryCache.Set(track.Id, track, new TimeSpan(1, 0, 0));
                return track;
            }
        }

        /// <summary>
        /// Class Artist containing spotify artist data.
        /// </summary>
        public class Artist : CommonData
        {
            /// <inheritdoc/>
            public override BaseItem ToItem(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
            {
                var artist = TryRetrieveItem<MusicArtist>(itemRepository, memoryCache, Base62.B62ToGuid(Id));
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

        /// <summary>
        /// Class TrackList containing a list of Spotify Tracks.
        /// </summary>
        public class TrackList : IJSONToItems
        {
            /// <summary>
            /// Gets or sets the Spotify Track List.
            /// </summary>
            [JsonRequired]
            public Track[] Items { get; set; }

            /// <inheritdoc/>
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = Items.Select(i =>
                        (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { SongCount = 1 }));
                return items.ToList();
            }
        }

        /// <summary>
        /// Class TrackList2 containing a list of Spotify Tracks.
        /// </summary>
        public class TrackList2 : IJSONToItems
        {
            /// <summary>
            /// Gets or sets the Spotify Track List.
            /// </summary>
            [JsonRequired]
            public Track[] Tracks { get; set; }

            /// <inheritdoc/>
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = Tracks.Select(i =>
                        (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { SongCount = 1 }));
                return items.ToList();
            }
        }

        /// <summary>
        /// Class AlbumList containing a list of Spotify albums.
        /// </summary>
        public class AlbumList : IJSONToItems
        {
            /// <summary>
            /// Gets or sets the Spotify Album List.
            /// </summary>
            [JsonRequired]
            public Album[] Items { get; set; }

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

        /// <summary>
        /// Class ArtistList containing a list of Spotify Artists.
        /// </summary>
        public class ArtistList : IJSONToItems
        {
            /// <summary>
            /// Gets or sets the Spotify Artist List.
            /// </summary>
            [JsonRequired]
            public Artist[] Items { get; set; }

            /// <inheritdoc/>
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, IItemRepository itemRepository, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = Items.Select(i =>
                        (i.ToItem(logger, memoryCache, itemRepository, parentId, ownerId), new ItemCounts { ArtistCount = 1 }));
                return items.ToList();
            }
        }

        private class Base62
        {
            private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            private static readonly BigInteger BaseValue = new BigInteger(62);

            /// <summary>
            /// Generates a GUID from a spotify ID.
            /// Warning : this is not a always a lossless operation as not all spotify IDs fit in 16 bytes.
            /// </summary>
            public static Guid B62ToGuid(string base62String)
            {
                var value = new BigInteger(0);
                foreach (var c in base62String)
                {
                    var charValue = Base62Chars.IndexOf(c, StringComparison.Ordinal);
                    value = BigInteger.Multiply(value, BaseValue);
                    value = BigInteger.Add(value, new BigInteger(charValue));
                }

                var byteArray = value.ToByteArray(true, true);
                // Sometimes spotify will use base62 IDs that don't fit on 16 bytes. (e.g. 7y9COUDxusQXRjW95vOubE is a valid spotify artist ID, but has 17 bytes)
                // In this case we just remove the MSB, the original id will be stored in the ExternalId anyway and the MusicArtist is kept in cache.
                if (byteArray.Length == 17)
                {
                    return new Guid(new ArraySegment<byte>(byteArray, 1, byteArray.Length - 1));
                }
                else if (byteArray.Length < 16)
                {
                    byte[] result = new byte[16];
                    byteArray.CopyTo(result, 16 - byteArray.Length);
                    return new Guid(result);
                }

                return new Guid(byteArray);
            }
        }

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
}
