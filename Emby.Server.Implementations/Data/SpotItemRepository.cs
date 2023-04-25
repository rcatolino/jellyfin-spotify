using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
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
        private readonly IUserManager _userManager;
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
        /// <param name="userManager">The user manager.</param>
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
            IUserManager userManager,
            IHttpClientFactory httpClientFactory)
        {
            _backend = new SqliteItemRepository(config, appHost, logger, localization, imageProcessor);
            _logger = logger;
            _memoryCache = memoryCache;
            _userManager = userManager;
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
            if (query.User is not null)
            {
                parts.Add($"User {query.User.Username}");
            }

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

            if (query.Tags.Length > 0)
            {
                parts.Add($"Tags : [{string.Join(", ", query.Tags)}");
            }

            if (!query.ParentId.Equals(Guid.Empty))
            {
                parts.Add($"ParentId : {query.ParentId}");
            }

            parts.Add($"-> {resultCount} local results found");
            _logger.LogInformation("{Msg}", string.Join(" ", parts));
        }

        /// <summary>
        /// Initialize backing sqlite item repo.
        /// </summary>
        /// <param name="userDataRepo">The user data repository.</param>
        /// <param name="userManager">The user manager.</param>
        public void Initialize(SqliteUserDataRepository userDataRepo, IUserManager userManager)
        {
            _backend.Initialize(userDataRepo, userManager);
            foreach (var user in userManager.Users)
            {
                if (user.SpotifyApiKey is not null)
                {
                    var loginTask = SpotLogin(user.SpotifyApiKey);
                    var token = loginTask.GetAwaiter().GetResult();
                    user.SpotifyToken = token;
                }
            }
        }

        private async Task<string?> SpotLogin(string? apiKey)
        {
            string tokenEP = "https://accounts.spotify.com/api/token";
            var reqBody = new StringContent(
                    "grant_type=client_credentials",
                    Encoding.UTF8,
                    "application/x-www-form-urlencoded");
            if (apiKey is null)
            {
                _logger.LogInformation("Spotify API key missing");
                return null;
            }

            string authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(apiKey));
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
                return jsonBody.RootElement.GetProperty("access_token").GetString();
                // _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
            }

            return null;
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

        private List<(BaseItem Item, ItemCounts ItemCounts)> SpotQuery<T>(User user, string query, Guid? parentId = null)
            where T : SpotifyData.IJSONToItems
        {
            var taskSearch = AsyncSpotQuery<T>(user, query, parentId);
            return taskSearch.GetAwaiter().GetResult();
        }

        private async Task<List<(BaseItem Item, ItemCounts ItemCounts)>> AsyncSpotQuery<T>(User user, string query, Guid? parentId = null, bool retry = true)
            where T : SpotifyData.IJSONToItems
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, query);
            if (user.SpotifyToken is null)
            {
                user.SpotifyToken = await SpotLogin(user.SpotifyApiKey);
            }

            if (user.SpotifyToken is null)
            {
                _logger.LogWarning("Connection to spotify failed");
                return new List<(BaseItem, ItemCounts)>();
            }

            requestMessage.Headers.Add("Authorization", "Bearer " + user.SpotifyToken);
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
                    user.SpotifyToken = null;
                    if (retry)
                    {
                        return await AsyncSpotQuery<T>(user, query, parentId, false);
                    }
                }
                else if (resp.StatusCode != HttpStatusCode.OK)
                {
                    _logger.LogWarning("Spotify search failed with code {Code} : {Text}", resp.StatusCode, body);
                }
                else
                {
                    _logger.LogInformation("Spotify query : {Q}, result : {J}", query, json);
                    return json.ToItems(_logger, _memoryCache, parentId, user.Id);
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

        private QueryData? ValidateQueryData(User? user, Guid itemId)
        {
            _memoryCache.TryGetValue(itemId, out BaseItem? item);
            if (item is null || item.ServiceName != "spotify")
            {
                _logger.LogInformation("Spotify album tracks lookup : Album {Guid} is not in cache or doesn't come from spotify", itemId);
                return null;
            }

            // If the query had no User information we can use the owner of the Album for the query.
            // This means any user with access to the cached folder can query spotify for its track list
            // I think thats OK.
            if (user is null && (Guid?)item.OwnerId is not null && !item.OwnerId.Equals(Guid.Empty))
            {
                user = _userManager.GetUserById(item.OwnerId);
            }

            // If user is still null there was no valid owner information, we can't query spotify
            if (user is null)
            {
                _logger.LogInformation("Spotify lookup : Unknown query user and no owner for item {I}", itemId);
                return null;
            }

            return new QueryData { User = user, Item = item };
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> ArtistAlbum(User? user, Guid artistId, int limit)
        {
            if (ValidateQueryData(user, artistId) is QueryData qdata)
            {
                string searchEP = $"{spotAPI}/artists/{qdata.Item.ExternalId}/albums?include_groups=album&limit={limit}&market=FR";
                var res = SpotQuery<SpotifyData.AlbumList>(qdata.User, searchEP, artistId);
                _logger.LogInformation("Searching spotify for albums by artist {ArtistId} -> {N} results", qdata.Item.ExternalId, res.Count);
                return res;
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> TracksById(User? user, Guid[] trackIds)
        {
            if (user is User u)
            {
                // TODO: This endpoint only supports up to 50 track id per request.
                // We should deal with the case where more than 50 tracks are requested.
                //
                // TODO: If we have the tracks in cache, which we must have if we know their spotify ids,
                // then why should we request them again ??
                var spotIds = trackIds
                    .Select(id => _memoryCache.Get<BaseItem>(id))
                    .Where(item => item is BaseItem)
                    .Where(item => item!.ServiceName == "spotify") // item can't be null because of the previous where.
                    .Select(item => item!.ExternalId)
                    .ToList();
                if (spotIds.Count > 0)
                {
                    var spotIdsStr = string.Join(",", spotIds);
                    string searchEP = $"{spotAPI}/tracks?market=FR&ids={spotIdsStr}";
                    var res = SpotQuery<SpotifyData.TrackList2>(user, searchEP);
                    _logger.LogInformation("Searching spotify for multiple tracks {T} -> {N} results", spotIdsStr, res.Count);
                    return res;
                }
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> AlbumTracks(User? user, Guid albumId)
        {
            if (ValidateQueryData(user, albumId) is QueryData qdata)
            {
                // TODO: don't hardcode market. But where to get it ?
                string searchEP = $"{spotAPI}/albums/{qdata.Item.ExternalId}/tracks?market=FR&limit=50";
                var res = SpotQuery<SpotifyData.TrackList>(qdata.User, searchEP, albumId);
                _logger.LogInformation("Searching spotify for track on album {AlbumId} -> {N} results", qdata.Item.ExternalId, res.Count);
                return res;
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> ArtistTopTracks(User? user, Guid artistId)
        {
            if (ValidateQueryData(user, artistId) is QueryData qdata)
            {
                // TODO: don't hardcode market. But where to get it ?
                string searchEP = $"{spotAPI}/artists/{qdata.Item.ExternalId}/top-tracks?market=FR";
                var res = SpotQuery<SpotifyData.TrackList2>(qdata.User, searchEP, artistId);
                _logger.LogInformation("Searching spotify for track by artist {ArtistId} -> {N} results", qdata.Item.ExternalId, res.Count);
                return res;
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> SearchSpotItem(User? user, string search, string what, int limit)
        {
            if (user is null)
            {
                return new List<(BaseItem, ItemCounts)>();
            }

            string searchEP = $"https://api.spotify.com/v1/search?q={search}&type={what}&limit={limit}";
            _logger.LogInformation("SearchSpotItem for {N} {What} matching {Search}", limit, what, search);
            return SpotQuery<SpotifyData.SearchResponse>(user, searchEP);
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
                        query.User,
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
            _logger.LogInformation("GetItemIdsList {@InternalItemsQuery} : {@List}", query, results);
            return results;
        }

        private IReadOnlyList<BaseItem> AddSpotItems(InternalItemsQuery query, IReadOnlyList<BaseItem> db_results)
        {
            var itemtypes = query.IncludeItemTypes;
            if (query.Limit is not null && query.Limit < db_results.Count)
            {
                return db_results;
            }

            // TODO: Add limit check before each query
            var results = new List<IReadOnlyList<BaseItem>> { db_results };
            if (itemtypes.Length == 0)
            {
                if (!query.ParentId.Equals(Guid.Empty))
                {
                    // This is the api's way to ask for an album tracks ?
                    results.Add(AlbumTracks(query.User, query.ParentId).Select(pair => pair.Item).OrderBy(track => track.IndexNumber).ToList());
                    _logger.LogInformation("Query Audio Items from stpotify for album {Ids} -> {N} results", query.ParentId, results.Last().Count);
                }

                if (query.ItemIds.Length > 0)
                {
                    results.Add(TracksById(query.User, query.ItemIds)
                        .Select(pair => pair.Item)
                        .ToList());
                    _logger.LogInformation("Query Audio Items from stpotify with {Ids} -> {N} results", query.ItemIds, results.Last().Count);
                }
            }

            if (itemtypes.Contains(BaseItemKind.Audio))
            {
                if (query.ArtistIds.Length > 0)
                {
                    results.Add(query.ArtistIds
                        .Select(id => ArtistTopTracks(query.User, id).Select(pair => pair.Item))
                        .SelectMany(list => list)
                        .ToList());
                    _logger.LogInformation("Query Audio Items from stpotify for artist {Ids} -> {N} results", query.ArtistIds, results.Last().Count);
                }

                if (query.SearchTerm is not null)
                {
                    results.Add(
                            SearchSpotItem(query.User, query.SearchTerm, "track", query.Limit ?? 25)
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
                        .Select(id => ArtistAlbum(query.User, id, 50).Select(pair => pair.Item))
                        .SelectMany(list => list)
                        .ToList());
                    _logger.LogInformation("Query MusicAlbum Items from stpotify for {Ids} -> {N} results", query.ArtistIds, results.Last().Count);
                }

                if (query.AlbumArtistIds.Length > 0)
                {
                    results.Add(query.AlbumArtistIds
                        .Select(id => ArtistAlbum(query.User, id, 50).Select(pair => pair.Item))
                        .SelectMany(list => list)
                        .ToList());
                    _logger.LogInformation("Query MusicAlbum Items from stpotify for {Ids} -> {N} results", query.ArtistIds, results.Last().Count);
                }

                if (query.SearchTerm is not null)
                {
                    results.Add(
                            SearchSpotItem(query.User, query.SearchTerm, "album", query.Limit ?? 25)
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
            LogQuery("GetItemList", query, results.Count);
            return AddSpotItems(query, results).ToList();
        }

        /// <inheritdoc/>
        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            QueryResult<BaseItem> results = _backend.GetItems(query);
            LogQuery("GetItems", query, results.TotalRecordCount);
            return new QueryResult<BaseItem>(AddSpotItems(query, results.Items));
        }

        /// <inheritdoc/>
        public List<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query)
        {
            // I think this is for stuff like subtitles
            var res = _backend.GetMediaAttachments(query);
            // _logger.LogInformation("GetMediaAttachments {@MediaAttachmentQuery} - Got {N} results : {@List<MediaAttachment>}", query, res.Count, res);
            return res;
        }

        private string FormatMediaStream(MediaStream ms)
        {
            return $"Title {ms.Title}, Codec {ms.Codec}, Bitrate {ms.BitRate}, Type {ms.Type}, Index {ms.Index}, Url {ms.DeliveryUrl}, Method {ms.DeliveryMethod}, External : {ms.IsExternal}, ExternalUrl : {ms.IsExternalUrl}, Path : {ms.Path}";
        }

        /// <inheritdoc/>
        public List<MediaStream> GetMediaStreams(MediaStreamQuery query)
        {
            var res = _backend.GetMediaStreams(query);
            if (_memoryCache.Get<BaseItem>(query.ItemId) is BaseItem item)
            {
                _logger.LogInformation("GetMediaStreams : For item {G}/{IE}, type {T}, media index : {I}", query.ItemId, item.ExternalId, query.Type, query.Index);
                if (item.ServiceName == "spotify")
                {
                    var stream = new MediaStream
                    {
                        Codec = "spotify",
                        Type = MediaStreamType.Audio,
                        Index = res.Count,
                        // IsExternal = true, // This is for subtitles ?
                        Path = item.Path, // If we omit the path the MediaStream gets filtered out in BaseItem.GetVersionInfo
                    };
                    res.Add(stream);
                }
            }

            _logger.LogInformation("Got {N} media streams for {I} : {M}", res.Count, query.ItemId, string.Join("\n\t", res.Select(ms => FormatMediaStream(ms))));
            return res;
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

        private struct QueryData
        {
            public BaseItem Item;

            public User User;
        }
    }
}
