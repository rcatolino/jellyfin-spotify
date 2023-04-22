#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
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
            /// <param name="parentId">ID of parent Album/Artist if there is one.</param>
            /// <param name="ownerId">ID of the user who has requested this metadata from spotify.</param>
            /// <returns> The list of artists as BaseItems.</returns>
            List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, Guid? parentId = null, Guid? ownerId = null);
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
            /// <typeparam name="T">Entity type (Audio/MusicArtist/MusicAlbum,etc.).</typeparam>
            /// <param name="item">BaseItem.</param>
            /// <param name="logger">Logger.</param>
            /// <param name="parentId">ID of parent Album/Artist if there is one.</param>
            /// <param name="ownerId">ID of the user who has requested this metadata from spotify.</param>
            /// <returns> The BaseItem with filled values.</returns>
            public T ToItem<T>(T item, ILogger logger, Guid? parentId = null, Guid? ownerId = null)
                where T : BaseItem
            {
                item.Name = Name;
                item.Id = Base62.B62ToGuid(Id, logger);
                item.ServiceName = "spotify";
                item.ExternalId = Id;
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
                    // logger.LogInformation("Setting {U} : {W}x{H} as primary image", img.Url, img.Width, img.Height);
                    item.AddImage(new ItemImageInfo { Width = img.Width, Height = img.Height, Path = img.Url, Type = ImageType.Primary });
                }

                // If we have two images, use the smallest (narrowest) as thumb
                if (Images is not null && Images.Length > 1)
                {
                    var img = Images.OrderBy(i => i.Width).First();
                    // logger.LogInformation("Setting {U} : {W}x{H} as thumb image", img.Url, img.Width, img.Height);
                    item.AddImage(new ItemImageInfo { Width = img.Width, Height = img.Height, Path = img.Url, Type = ImageType.Thumb });
                }

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
        }

        /// <summary>
        /// Class Artist containing spotify artist data.
        /// </summary>
        public class Artist : CommonData
        {
        }

        /// <summary>
        /// Class TrackList containing a list of Spotify Tracks.
        /// </summary>
        public class TrackList : IJSONToItems
        {
            /// <summary>
            /// Gets or sets the Spotify Album List.
            /// </summary>
            [JsonRequired]
            public Track[] Items { get; set; }

            /// <inheritdoc/>
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = Items.Select(i =>
                    {
                        var track = i.ToItem(new Audio(), logger, parentId, ownerId);
                        track.IndexNumber = i.TrackNumber;
                        track.ParentIndexNumber = i.DiscNumber;
                        track.RunTimeTicks = i.DurationMs * 10000;
                        track.Artists = i.Artists.Select(a => a.Name).ToList();
                        memoryCache.Set(track.Id, track, new TimeSpan(1, 0, 0));
                        return ((BaseItem)track, new ItemCounts { SongCount = 1 });
                    });
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
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, Guid? parentId = null, Guid? ownerId = null)
            {
                // TODO: refactor with TrackList somehow.
                var items = Tracks.Select(i =>
                    {
                        var track = i.ToItem(new Audio(), logger, parentId, ownerId);
                        track.IndexNumber = i.TrackNumber;
                        track.ParentIndexNumber = i.DiscNumber;
                        track.RunTimeTicks = i.DurationMs * 10000;
                        track.Artists = i.Artists.Select(a => a.Name).ToList();
                        memoryCache.Set(track.Id, track, new TimeSpan(1, 0, 0));
                        return ((BaseItem)track, new ItemCounts { SongCount = 1 });
                    });
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
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = Items.Select(i =>
                    {
                        var album = i.ToItem(new MusicAlbum(), logger, parentId, ownerId);
                        album.Artists = i.Artists.Select(a => a.Name).ToList();
                        try
                        {
                            album.ProductionYear = int.Parse(i.ReleaseDate.Split("-")[0], new CultureInfo("en-US"));
                        }
                        catch
                        {
                        }

                        memoryCache.Set(album.Id, album, new TimeSpan(1, 0, 0));
                        return ((BaseItem)album, new ItemCounts { AlbumCount = 1 });
                    });
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
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = Items.Select(i =>
                    {
                        var artist = i.ToItem(new MusicArtist(), logger, parentId, ownerId);
                        memoryCache.Set(artist.Id, artist, new TimeSpan(1, 0, 0));
                        // We need to keep this artist in cache in order to be able to answer future artist queries by ID :
                        // we can't always query spotify with the GUID as it doesn't always map to the spotify ID.
                        // But we don't need to keep it forever as it should be saved in the database if we actually listen to it
                        return ((BaseItem)artist, new ItemCounts { ArtistCount = 1 });
                    });
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
            public static Guid B62ToGuid(string base62String, ILogger logger)
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
                    logger.LogInformation("Spotify 17 byte ID {SpotID} : {Hex}", base62String, byteArray);
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

        private class InvalidBase62Decoder : Exception
        {
            public InvalidBase62Decoder()
            {
            }
        }

        /// <summary>
        /// Class Error containing a Spotify error result.
        /// </summary>
        public class Error
        {
            /// <summary>
            /// Gets or sets the http status.
            /// </summary>
            [JsonRequired]
            public int Status { get; set; }

            /// <summary>
            /// Gets or sets the error message.
            /// </summary>
            [JsonRequired]
            public string Message { get; set; }
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

            /// <summary>
            /// Gets or sets spotify error.
            /// </summary>
            public Error Error { get; set; }

            /// <inheritdoc/>
            public List<(BaseItem Items, ItemCounts Counts)> ToItems(ILogger logger, IMemoryCache memoryCache, Guid? parentId = null, Guid? ownerId = null)
            {
                var items = new List<List<(BaseItem, ItemCounts)>>();

                if (Artists is not null)
                {
                    logger.LogInformation("Spotify query got {N} artists matches", Artists.Items.Length);
                    items.Add(Artists.ToItems(logger, memoryCache, parentId, ownerId));
                }

                if (Tracks is not null)
                {
                    logger.LogInformation("Spotify query got {N} track matches", Tracks.Items.Length);
                    items.Add(Tracks.ToItems(logger, memoryCache, parentId, ownerId));
                }

                if (Albums is not null)
                {
                    logger.LogInformation("Spotify query got {N} album matches", Albums.Items.Length);
                    items.Add(Albums.ToItems(logger, memoryCache, parentId, ownerId));
                }

                var res = items.SelectMany(list => list).ToList();
                return res;
            }
        }
    }
}
