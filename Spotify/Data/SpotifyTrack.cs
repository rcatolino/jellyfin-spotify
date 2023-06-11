#nullable disable
#pragma warning disable CA1819
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class Track containing spotify track data.
    /// </summary>
    public class SpotifyTrack : SpotifyItem
    {
        /// <summary>
        /// Gets or sets the Spotify Artist List.
        /// </summary>
        [JsonRequired]
        public SpotifyArtist[] Artists { get; set; }

        /// <summary>
        /// Gets or sets the Spotify Album.
        /// </summary>
        public SpotifyAlbum Album { get; set; }

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
            var track = SpotifyItemRepository.TryRetrieveItem<Audio>(itemRepository, memoryCache, SpotifyItemRepository.B62ToGuid(Id));
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

                if (parentId is null && Album is not null)
                {
                    var album = Album.ToItem(logger, memoryCache, itemRepository, null, ownerId);
                    track.ParentId = album.Id;
                }

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
}
