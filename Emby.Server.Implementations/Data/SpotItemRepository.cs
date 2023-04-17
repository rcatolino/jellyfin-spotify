using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Data
{
    /// <summary>
    /// Class SpotItemRepository.
    /// </summary>
    public class SpotItemRepository : IItemRepository
    {
        private readonly IMemoryCache _memoryCache;
        private readonly string spotAPI = "https://api.spotify.com/v1";
        private SqliteItemRepository _backend;
        private ILogger<SqliteItemRepository> _logger;
        private HttpClient _httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpotItemRepository"/> class.
        /// </summary>
        /// <param name="config">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        /// <param name="appHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{SqliteItemRepository}"/> interface.</param>
        /// <param name="memoryCache">The memory cache.</param>
        /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
        /// <param name="imageProcessor">Instance of the <see cref="IImageProcessor"/> interface.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        public SpotItemRepository(
            IServerConfigurationManager config,
            IServerApplicationHost appHost,
            ILogger<SqliteItemRepository> logger,
            ILocalizationManager localization,
            IImageProcessor imageProcessor,
            IMemoryCache memoryCache,
            IHttpClientFactory httpClientFactory)
        {
            _backend = new SqliteItemRepository(config, appHost, logger, localization, imageProcessor);
            _logger = logger;
            _memoryCache = memoryCache;
            _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        }

        private string ListToString<T>(T[] l)
            where T : IFormattable
        {
            var inner = string.Join(", ", l.Select(i => i.ToString()));
            return $"[{inner}]";
        }

        private void LogQuery(string methodName, InternalItemsQuery query, int resultCount)
        {
            List<string> parts = new List<string> { methodName };
            if (query.SearchTerm is not null)
            {
                parts.Add($"Search {query.SearchTerm}");
            }

            if (query.Limit is not null)
            {
                parts.Add($"limit {query.Limit}");
            }

            if (query.IncludeItemTypes.Length > 0)
            {
                parts.Add($"type {ListToString(query.IncludeItemTypes)}");
            }

            if (query.AlbumArtistIds.Length > 0)
            {
                parts.Add($"ArtistIds : {ListToString(query.AlbumArtistIds)}");
            }

            if (query.AncestorIds.Length > 0)
            {
                parts.Add($"AlbumArtistIds : {ListToString(query.AlbumArtistIds)}");
            }

            if (query.AlbumIds.Length > 0)
            {
                parts.Add($"AlbumIds : {ListToString(query.AlbumIds)}");
            }

            if (query.ContributingArtistIds.Length > 0)
            {
                parts.Add($"ContributingArtistIds : {ListToString(query.ContributingArtistIds)}");
            }

            if (query.ItemIds.Length > 0)
            {
                parts.Add($"ItemIds : {ListToString(query.ItemIds)}");
            }

            if (query.PersonIds.Length > 0)
            {
                parts.Add($"PersonIds : {ListToString(query.PersonIds)}");
            }

            if (!query.ParentId.Equals(Guid.Empty))
            {
                parts.Add($"ParentId : {query.ParentId}");
            }

            parts.Add($"-> {resultCount} results found");
            _logger.LogInformation("{Msg}", string.Join(" ", parts));
        }

        /// <summary>
        /// Initialize backing sqlite item repo.
        /// </summary>
        /// <param name="userDataRepo">The user data repository.</param>
        /// <param name="userManager">The user manager.</param>
        public void Initialize(SqliteUserDataRepository userDataRepo, IUserManager userManager)
        {
            SpotLogin();
            _backend.Initialize(userDataRepo, userManager);
        }

        private async void SpotLogin()
        {
            string tokenEP = "https://accounts.spotify.com/api/token";
            var reqBody = new StringContent(
                    "grant_type=client_credentials",
                    Encoding.UTF8,
                    "application/x-www-form-urlencoded");
            string apiToken = "apiid:apikey";
            string authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(apiToken));
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, tokenEP);
            requestMessage.Content = reqBody;
            requestMessage.Headers.Add("Authorization", "Basic " + authToken);
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage);
            string body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                        "Spotify login failed with code {Code} : {Text}",
                        resp.StatusCode,
                        body);
            }
            else
            {
                var jsonBody = JsonDocument.Parse(body);
                _logger.LogInformation(
                        "Spotify Login result : {Resp}",
                        body);

                var bearerToken = jsonBody.RootElement.GetProperty("access_token").GetString();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
            }
        }

        /// <inheritdoc/>
        public void DeleteItem(Guid id)
        {
            _backend.DeleteItem(id);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            _backend.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAlbumArtists(InternalItemsQuery query)
        {
            QueryResult<(BaseItem, ItemCounts)> results = _backend.GetAlbumArtists(query);
            LogQuery("GetAlbumArtists", query, results.TotalRecordCount);
            return results;
        }

        /// <inheritdoc/>
        public List<string> GetAllArtistNames()
        {
            return _backend.GetAllArtistNames();
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetAllArtists(InternalItemsQuery query)
        {
            return _backend.GetAllArtists(query);
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> SpotQuery<T>(string query, Guid? parentId = null)
            where T : SpotifyData.IJSONToItems
        {
            var taskSearch = AsyncSpotQuery<T>(query, parentId);
            return taskSearch.GetAwaiter().GetResult();
        }

        private async Task<List<(BaseItem Item, ItemCounts ItemCounts)>> AsyncSpotQuery<T>(string query, Guid? parentId = null, bool retry = true)
            where T : SpotifyData.IJSONToItems
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, query);
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage);
            string body = await resp.Content.ReadAsStringAsync();
            try
            {
                var json = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (json is null)
                {
                    _logger.LogWarning("Error deserializing Spotify data {Data}", body);
                    return new List<(BaseItem, ItemCounts)>();
                }

                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    SpotLogin();
                    return await AsyncSpotQuery<T>(query, parentId, false);
                }
                else if (resp.StatusCode != HttpStatusCode.OK)
                {
                    _logger.LogWarning("Spotify search failed with code {Code} : {Text}", resp.StatusCode, body);
                }
                else
                {
                    _logger.LogInformation("Spotify query : {Q}, result : {J}", query, json);
                    return json.ToItems(_logger, _memoryCache, parentId);
                }
            }
            catch (System.Text.Json.JsonException e)
            {
                _logger.LogWarning(
                        "Error trying to deserialize json {J} for query {Q} : {E}",
                        body,
                        query,
                        e.Message);
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> ArtistAlbum(Guid artistId, int limit)
        {
            _memoryCache.TryGetValue(artistId, out BaseItem? item);
            if (item is not null && item.ServiceName == "spotify")
            {
                string searchEP = $"{spotAPI}/artists/{item.ExternalId}/albums?include_groups=album&limit={limit}";
                var res = SpotQuery<SpotifyData.AlbumList>(searchEP, artistId);
                _logger.LogInformation("Searching spotify for albums by artist {ArtistId} -> {N} results", item.ExternalId, res.Count);
                return res;
            }
            else
            {
                _logger.LogInformation("Spotify artist album on spotify : Artist {Guid} is not in cache or doesn't come from spotify", artistId);
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> AlbumTracks(Guid albumId)
        {
            _memoryCache.TryGetValue(albumId, out BaseItem? item);
            if (item is not null && item.ServiceName == "spotify")
            {
                // TODO: don't hardcode market. But where to get it ?
                string searchEP = $"{spotAPI}/albums/{item.ExternalId}/tracks?market=FR&limit=50";
                var res = SpotQuery<SpotifyData.TrackList>(searchEP, albumId);
                _logger.LogInformation("Searching spotify for track on album {AlbumId} -> {N} results", item.ExternalId, res.Count);
                return res;
            }
            else
            {
                _logger.LogInformation("Spotify album tracks lookup : Album {Guid} is not in cache or doesn't come from spotify", albumId);
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> ArtistTopTracks(Guid artistId)
        {
            _memoryCache.TryGetValue(artistId, out BaseItem? item);
            if (item is not null && item.ServiceName == "spotify")
            {
                // TODO: don't hardcode market. But where to get it ?
                string searchEP = $"{spotAPI}/artists/{item.ExternalId}/top-tracks?market=FR";
                var res = SpotQuery<SpotifyData.TopTrackList>(searchEP, artistId);
                _logger.LogInformation("Searching spotify for track by artist {ArtistId} -> {N} results", item.ExternalId, res.Count);
                return res;
            }
            else
            {
                _logger.LogInformation("Spotify artist track lookup : Artist {Guid} is not in cache or doesn't come from spotify", artistId);
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> SearchSpotItem(string search, string what, int limit)
        {
            string searchEP = $"https://api.spotify.com/v1/search?q={search}&type={what}&limit={limit}";
            _logger.LogInformation("SearchSpotItem for {N} {What} matching {Search}", limit, what, search);
            return SpotQuery<SpotifyData.SearchResponse>(searchEP);
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query)
        {
            // First, query the local database.
            QueryResult<(BaseItem, ItemCounts)> results = _backend.GetArtists(query);
            LogQuery("GetArtists", query, results.TotalRecordCount);
            // Then if we are looking for more results, query spotify.
            if (query.SearchTerm is not null && results.TotalRecordCount < query.Limit)
            {
                var spotResults = SearchSpotItem(
                        query.SearchTerm,
                        "artist",
                        (query.Limit ?? 20) - results.TotalRecordCount);
                return new QueryResult<(BaseItem, ItemCounts)>(results.Items.Concat(spotResults).ToList());
            }

            return results;
        }

        /// <inheritdoc/>
        public ChapterInfo GetChapter(BaseItem item, int index)
        {
            return _backend.GetChapter(item, index);
        }

        /// <inheritdoc/>
        public List<ChapterInfo> GetChapters(BaseItem item)
        {
            return _backend.GetChapters(item);
        }

        /// <inheritdoc/>
        public int GetCount(InternalItemsQuery query)
        {
            return _backend.GetCount(query);
        }

        /// <inheritdoc/>
        public List<string> GetGenreNames()
        {
            return _backend.GetGenreNames();
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetGenres(InternalItemsQuery query)
        {
            return _backend.GetGenres(query);
        }

        /// <inheritdoc/>
        public List<Guid> GetItemIdsList(InternalItemsQuery query)
        {
            List<Guid> results = _backend.GetItemIdsList(query);
            return results;
        }

        private IReadOnlyList<BaseItem> AddSpotItems(InternalItemsQuery query, IReadOnlyList<BaseItem> db_results)
        {
            var itemtypes = query.IncludeItemTypes;
            LogQuery("AddSpotItems", query, db_results.Count);
            if (query.Limit is not null && query.Limit < db_results.Count)
            {
                return db_results;
            }

            // TODO: Add limit check before each query
            var results = new List<IReadOnlyList<BaseItem>> { db_results };
            if (itemtypes.Length == 0 && !query.ParentId.Equals(Guid.Empty))
            {
                // This is the api's way to ask for an album tracks ?
                results.Add(AlbumTracks(query.ParentId).Select(pair => pair.Item).OrderBy(track => track.IndexNumber).ToList());
                _logger.LogInformation("Query Audio Items from stpotify for album {Ids} -> {N} results", query.ParentId, results.Last().Count);
            }

            if (itemtypes.Contains(BaseItemKind.Audio))
            {
                if (query.ArtistIds.Length > 0)
                {
                    results.Add(query.ArtistIds
                        .Select(id => ArtistTopTracks(id).Select(pair => pair.Item))
                        .SelectMany(list => list)
                        .ToList());
                    _logger.LogInformation("Query Audio Items from stpotify for {Ids} -> {N} results", query.ArtistIds, results.Last().Count);
                }

                if (query.SearchTerm is not null)
                {
                    results.Add(
                            SearchSpotItem(query.SearchTerm, "track", query.Limit ?? 25)
                            .Select(itemAndCount => itemAndCount.Item)
                            .ToList());
                    _logger.LogInformation("Query Audio Items from stpotify matching {Search} -> {N} results", query.SearchTerm, results.Last().Count);
                }
            }

            if (itemtypes.Contains(BaseItemKind.MusicAlbum))
            {
                if (query.ArtistIds.Length > 0)
                {
                    results.Add(query.ArtistIds
                        .Select(id => ArtistAlbum(id, 50).Select(pair => pair.Item))
                        .SelectMany(list => list)
                        .ToList());
                    _logger.LogInformation("Query MusicAlbum Items from stpotify for {Ids} -> {N} results", query.ArtistIds, results.Last().Count);
                }

                if (query.AlbumArtistIds.Length > 0)
                {
                    results.Add(query.AlbumArtistIds
                        .Select(id => ArtistAlbum(id, 50).Select(pair => pair.Item))
                        .SelectMany(list => list)
                        .ToList());
                    _logger.LogInformation("Query MusicAlbum Items from stpotify for {Ids} -> {N} results", query.ArtistIds, results.Last().Count);
                }

                if (query.SearchTerm is not null)
                {
                    results.Add(
                            SearchSpotItem(query.SearchTerm, "album", query.Limit ?? 25)
                            .Select(itemAndCount => itemAndCount.Item)
                            .ToList());
                    _logger.LogInformation("Query Album Items from stpotify matching {Search} -> {N} results", query.SearchTerm, results.Last().Count);
                }
            }

            return results.SelectMany(list => list).ToList();
        }

        /// <inheritdoc/>
        public List<BaseItem> GetItemList(InternalItemsQuery query)
        {
            var results = _backend.GetItemList(query);
            return AddSpotItems(query, results).ToList();
        }

        /// <inheritdoc/>
        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            QueryResult<BaseItem> results = _backend.GetItems(query);
            return new QueryResult<BaseItem>(AddSpotItems(query, results.Items));
        }

        /// <inheritdoc/>
        public List<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query)
        {
            return _backend.GetMediaAttachments(query);
        }

        /// <inheritdoc/>
        public List<MediaStream> GetMediaStreams(MediaStreamQuery query)
        {
            _logger.LogInformation("GetMediaStreams : {SearchTerm}", query);
            return _backend.GetMediaStreams(query);
        }

        /// <inheritdoc/>
        public List<string> GetMusicGenreNames()
        {
            return _backend.GetMusicGenreNames();
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetMusicGenres(InternalItemsQuery query)
        {
            return _backend.GetMusicGenres(query);
        }

        /// <inheritdoc/>
        public List<PersonInfo> GetPeople(InternalPeopleQuery query)
        {
            return _backend.GetPeople(query);
        }

        /// <inheritdoc/>
        public List<string> GetPeopleNames(InternalPeopleQuery query)
        {
            return _backend.GetPeopleNames(query);
        }

        /// <inheritdoc/>
        public List<string> GetStudioNames()
        {
            return _backend.GetStudioNames();
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetStudios(InternalItemsQuery query)
        {
            return _backend.GetStudios(query);
        }

        /// <inheritdoc/>
        public BaseItem RetrieveItem(Guid id)
        {
            _logger.LogInformation("RetrieveItem: {Id}", id);
            return _backend.RetrieveItem(id);
        }

        /// <inheritdoc/>
        public void SaveChapters(Guid id, IReadOnlyList<ChapterInfo> chapters)
        {
            _backend.SaveChapters(id, chapters);
        }

        /// <inheritdoc/>
        public void SaveImages(BaseItem item)
        {
            _backend.SaveImages(item);
        }

        /// <inheritdoc/>
        public void SaveItems(IReadOnlyList<BaseItem> items, CancellationToken cancellationToken)
        {
            _backend.SaveItems(items, cancellationToken);
        }

        /// <inheritdoc/>
        public void SaveMediaAttachments(Guid id, IReadOnlyList<MediaAttachment> attachments, CancellationToken cancellationToken)
        {
            _backend.SaveMediaAttachments(id, attachments, cancellationToken);
        }

        /// <inheritdoc/>
        public void SaveMediaStreams(Guid id, IReadOnlyList<MediaStream> streams, CancellationToken cancellationToken)
        {
            _backend.SaveMediaStreams(id, streams, cancellationToken);
        }

        /// <inheritdoc/>
        public void UpdateInheritedValues()
        {
            _backend.UpdateInheritedValues();
        }

        /// <inheritdoc/>
        public void UpdatePeople(Guid itemId, List<PersonInfo> people)
        {
            _backend.UpdatePeople(itemId, people);
        }
    }
}
