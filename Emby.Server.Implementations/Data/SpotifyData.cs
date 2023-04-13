#nullable disable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Dto;
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

            public static Guid B62ToGuid(string base62String, ILogger logger)
            {
                var value = new BigInteger(0);
                foreach (var c in base62String)
                {
                    var charValue = Base62Chars.IndexOf(c, StringComparison.Ordinal);
                    logger.LogInformation("decoding char {Char} : value {Value}", c, charValue);
                    value = BigInteger.Multiply(value, BaseValue);
                    value = BigInteger.Add(value, new BigInteger(charValue));
                }

                var byteArray = value.ToByteArray(true, true);
                logger.LogInformation("Decoding {Input}, byte length : {Length}, bytes : {Bytes}", base62String, value.GetByteCount(), byteArray);
                if (byteArray[0] == 0)
                {
                    return new Guid(new ArraySegment<byte>(byteArray, 1, byteArray.Length));
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
            /// <returns> The list of artists as BaseItems.</returns>
            public List<(BaseItem Items, ItemCounts Counts)> GetItems(ILogger logger)
            {
                var gtest = new Guid("dd34c0660e36da2c35765c59b994cb96");
                var bgtest = Base62.B62ToGuid("6jPPWvp74YGsboZjvxfvVe", logger);
                if (!bgtest.Equals(gtest))
                {
                    logger.LogWarning("Base62 decoder test failed for value 6jPPWvp74YGsboZjvxfvVe, expected {EG}, got {GG}", gtest, bgtest);
                    throw new InvalidBase62Decoder();
                }

                var artists = new List<(BaseItem, ItemCounts)>();
                foreach (var a in Artists.Items)
                {
                    artists.Add((new MusicArtist
                    {
                        Name = a.Name,
                        Id = Base62.B62ToGuid(a.Id, logger),
                    }, new ItemCounts
                    {
                        ArtistCount = 1,
                    }));
                }

                return artists;
            }
        }
    }
}
