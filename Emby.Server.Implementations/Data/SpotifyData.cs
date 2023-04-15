#nullable disable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Dto;
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
        /// Class Artist containing spotify artist data.
        /// </summary>
        public class Artist
        {
            /// <summary>
            /// Gets or sets the Spotify Artist ID.
            /// </summary>
            [JsonRequired]
            public string Id { get; set; }

            /// <summary>
            /// Gets or sets the Spotify Artist Name.
            /// </summary>
            [JsonRequired]
            public string Name { get; set; }

            /// <summary>
            /// Gets or sets the Spotify Artist uri.
            /// </summary>
            [JsonRequired]
            public string Uri { get; set; }

            /// <summary>
            /// Gets or sets the item urls.
            /// </summary>
            [JsonRequired]
            [JsonPropertyName("external_urls")]
            public Dictionary<string, string> Urls { get; set; }

            /// <summary>
            /// Gets or sets the genres.
            /// </summary>
            /// <value>The genres.</value>
            [JsonRequired]
            public string[] Genres { get; set; }
        }

        /// <summary>
        /// Class ArtistList containing a list of Spotify Artists.
        /// </summary>
        public class ArtistList
        {
            /// <summary>
            /// Gets or sets the Spotify Artist List.
            /// </summary>
            [JsonRequired]
            public Artist[] Items { get; set; }
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
        /// Class Root containing a Spotify Api result.
        /// </summary>
        public class Root
        {
            /// <summary>
            /// Gets or sets the list of artists.
            /// </summary>
            [JsonRequired]
            public ArtistList Artists { get; set; }

            /// <summary>
            /// Exports BaseItem list the spotify data.
            /// </summary>
            /// <param name="logger">Logger.</param>
            /// <param name="memoryCache">MemoryCache.</param>
            /// <returns> The list of artists as BaseItems.</returns>
            public List<(BaseItem Items, ItemCounts Counts)> GetItems(ILogger logger, IMemoryCache memoryCache)
            {
                var gtest = new Guid("66c034dd-360e-2cda-3576-5c59b994cb96");
                var bgtest = Base62.B62ToGuid("6jPPWvp74YGsboZjvxfvVe", logger);
                if (!bgtest.Equals(gtest))
                {
                    logger.LogWarning("Base62 decoder test failed for value 6jPPWvp74YGsboZjvxfvVe, expected {EG}, got {GG}", gtest, bgtest);
                    throw new InvalidBase62Decoder();
                }

                var artists = new List<(BaseItem, ItemCounts)>();
                foreach (var a in Artists.Items)
                {
                    var artist = new MusicArtist
                    {
                        Name = a.Name,
                        Id = Base62.B62ToGuid(a.Id, logger),
                        Genres = a.Genres,
                        ServiceName = "spotify",
                        ExternalId = a.Id,
                        ProviderIds = new Dictionary<string, string>() { { "spotify", a.Id } },
                    };

                    if (a.Urls.ContainsKey("spotify"))
                    {
                        artist.HomePageUrl = $"https://open.spotify.com/artist/{a.Id}";
                    }

                    // We need to keep this artist in cache in order to be able to answer future artist queries by ID :
                    // we can't always query spotify with the GUID as it doesn't always map to the spotify ID.
                    // But we don't need to keep it forever as it should be saved in the database if we actually listen to it
                    memoryCache.Set(artist.Id, artist, new TimeSpan(1, 0, 0));
                    artists.Add((artist, new ItemCounts { ArtistCount = 1 }));
                }

                return artists;
            }
        }
    }
}
