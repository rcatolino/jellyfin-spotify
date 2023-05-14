#nullable disable
using System.Text.Json.Serialization;

namespace Spotify.Data
{
    /// <summary>
    /// Class Image containing spotify image data.
    /// </summary>
    public class SpotifyImage
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
}
