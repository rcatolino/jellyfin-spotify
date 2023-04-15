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

        private void LogQuery(string methodName, InternalItemsQuery query, int resultCount)
        {
            _logger.LogInformation(
                    "{Method}: {SearchTerm} limit {Limit} type {Type} AlbumArtists {AA} Ancestor {Ancestor} Album {A} Artist {Ar} ContributingArtistIds {CAIs} ItemId {IId} -> {N} results found",
                    methodName,
                    query.SearchTerm,
                    query.Limit,
                    query.IncludeItemTypes,
                    query.AlbumArtistIds,
                    query.AncestorIds,
                    query.AlbumIds,
                    query.ArtistIds,
                    query.ContributingArtistIds,
                    query.ItemIds,
                    resultCount);
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

        private async Task<List<(BaseItem Item, ItemCounts ItemCounts)>> SearchAudio(string artistId, int limit)
        {
            // string searchEP = $"https://api.spotify.com/v1/search?q={search}&type=artist&limit={limit}";
            _logger.LogInformation("Searching spotify for {N} audio track by artis {artistId}", limit, artistId);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, searchEP);
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage);
            string body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                        "Spotify search failed with code {Code} : {Text}",
                        resp.StatusCode,
                        body);
            }
            else
            {
                // _logger.LogInformation("Spotify artist search returned {Result}", body);
                SpotifyData.Root? al = JsonSerializer.Deserialize<SpotifyData.Root>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                if (al is null)
                {
                    _logger.LogWarning("Error deserializing Spotify data {Data}", body);
                    return new List<(BaseItem, ItemCounts)>();
                }
                else
                {
                    return al.GetItems(_logger, _memoryCache);
                }
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private async Task<List<(BaseItem Item, ItemCounts ItemCounts)>> SearchArtist(string search, int limit)
        {
            string searchEP = $"https://api.spotify.com/v1/search?q={search}&type=artist&limit={limit}";
            _logger.LogInformation("Searching spotify for {N} artists matching {Search}", limit, search);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, searchEP);
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage);
            string body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                        "Spotify search failed with code {Code} : {Text}",
                        resp.StatusCode,
                        body);
            }
            else
            {
                // _logger.LogInformation("Spotify artist search returned {Result}", body);
                SpotifyData.Root? al = JsonSerializer.Deserialize<SpotifyData.Root>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                if (al is null)
                {
                    _logger.LogWarning("Error deserializing Spotify data {Data}", body);
                    return new List<(BaseItem, ItemCounts)>();
                }
                else
                {
                    return al.GetItems(_logger, _memoryCache);
                }
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        /// <inheritdoc/>
        // TODO: This method sould return additional artists from spotify, up to query.Limit
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query)
        {
            QueryResult<(BaseItem, ItemCounts)> results = _backend.GetArtists(query);
            LogQuery("GetArtists", query, results.TotalRecordCount);
            if (query.SearchTerm is not null && results.TotalRecordCount < query.Limit)
            {
                var spotSearch = SearchArtist(
                        query.SearchTerm,
                        (query.Limit ?? 20) - results.TotalRecordCount);
                var spotResults = spotSearch.GetAwaiter().GetResult();
                return new QueryResult<(BaseItem, ItemCounts)>(results.Items.Concat(spotResults).ToList());
            }
            else
            {
                return results;
            }
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
            LogQuery("GetItemIdsList", query, results.Count);
            return results;
        }

        /// <inheritdoc/>
        // TODO: we should query spotify for audio queries, up to query.Limit results
        public List<BaseItem> GetItemList(InternalItemsQuery query)
        {
            var itemtypes = query.IncludeItemTypes;
            List<BaseItem> results = _backend.GetItemList(query);
            if (itemtypes.Contains(BaseItemKind.Audio) || itemtypes.Contains(BaseItemKind.MusicAlbum) || itemtypes.Contains(BaseItemKind.MusicArtist))
            {
                LogQuery("GetItemList", query, results.Count);
            }

            if (results.Count < query.Limit && query.IncludeItemTypes.Contains(BaseItemKind.Audio))
            {
                _logger.LogInformation("We should query spotify for {N} results", query.Limit - results.Count);
            }

            return results;
        }

        /// <inheritdoc/>
        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            QueryResult<BaseItem> results = _backend.GetItems(query);
            LogQuery("GetItems", query, results.TotalRecordCount);
            return results;
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
