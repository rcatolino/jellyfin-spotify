using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Spotify.Data
{
    /// <summary>
    /// Class SpotifyItemRepository.
    /// </summary>
    public class SpotifyItemRepository : IItemRepository
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IUserManager _userManager;
        private readonly string spotAPI = "https://api.spotify.com/v1";
        private readonly IUserDataManager _userDataRepository;
        private IItemRepository _backend;
        private ILogger<SpotifyItemRepository> _logger;
        private HttpClient _httpClient;
        private SpotifyRootFolder _rootFolder;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpotifyItemRepository"/> class.
        /// </summary>
        /// <param name="config">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        /// <param name="appHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{SqliteItemRepository}"/> interface.</param>
        /// <param name="memoryCache">The memory cache.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
        /// <param name="imageProcessor">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="httpClientFactory">The <see cref="IHttpClientFactory"/>.</param>
        /// <param name="userDataRepository">Instance of the <see cref="IUserDataManager"/> interface.</param>
        public SpotifyItemRepository(
            IServerConfigurationManager config,
            IServerApplicationHost appHost,
            ILogger<SpotifyItemRepository> logger,
            ILocalizationManager localization,
            IImageProcessor imageProcessor,
            IMemoryCache memoryCache,
            IUserManager userManager,
            IUserDataManager userDataRepository,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _backend = null!; // We will crash if this class is used before Initialize is called. That's fine, that would be a bug.
            _memoryCache = memoryCache;
            _userManager = userManager;
            _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
            _rootFolder = null!;
            _userDataRepository = userDataRepository;
        }

        /// <summary>
        /// Generates a GUID from a spotify ID.
        /// Warning : this is not a always a lossless operation as not all spotify IDs fit in 16 bytes.
        /// </summary>
        /// <param name="base62String">string containing Base62 encoded data.</param>
        /// <returns>Guid containing the first 16 bytes of the Base62 decoded data.</returns>
        public static Guid B62ToGuid(string base62String)
        {
            const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var baseValue = new BigInteger(62);
            var value = new BigInteger(0);
            foreach (var c in base62String)
            {
                var charValue = Base62Chars.IndexOf(c, StringComparison.Ordinal);
                value = BigInteger.Multiply(value, baseValue);
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

        /// <summary>
        /// Return a local item by Id if it exists in cache or db and comes from spotify (or else return null).
        /// </summary>
        /// <param name="itemRepository">Item repository.</param>
        /// <param name="memoryCache">Memory cache.</param>
        /// <param name="localId">Local Id.</param>
        /// <typeparam name="T">BaseItem type (MusicArtist/MusicAlbum or Audio.</typeparam>
        /// <returns>Existing Item or null.</returns>
        public static T? TryRetrieveItem<T>(IItemRepository itemRepository, IMemoryCache memoryCache, Guid localId)
            where T : BaseItem
        {
            if (memoryCache.TryGetValue<T>(localId, out T? cachedItem))
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

        private string ListToString<T>(T[] l)
            where T : IFormattable
        {
            var inner = string.Join(", ", l.Select(i => i.ToString()));
            return $"[{inner}]";
        }

        private string UriToId(string uri)
        {
            return uri.Split(":").Last();
        }

        private void LogQuery(string methodName, InternalItemsQuery query, int resultCount)
        {
            List<string> parts = new List<string> { methodName };
            if (query.User is not null)
            {
                parts.Add($"User {query.User.Username}");
            }

            if (query.IsFavorite is not null)
            {
                parts.Add($"Favorite {query.IsFavorite}");
            }

            if (query.SearchTerm is not null)
            {
                parts.Add($"Search {query.SearchTerm}");
            }

            if (query.Name is not null && query.Name.Length > 0)
            {
                parts.Add($"Name {query.Name}");
            }

            if (query.NameContains is not null && query.NameContains.Length > 0)
            {
                parts.Add($"NameContains {query.NameContains}");
            }

            if (query.TopParentIds.Length > 0)
            {
                parts.Add($"TopParents {ListToString(query.TopParentIds)}");
            }

            parts.Add($"Recursive {query.Recursive}");
            if (query.Limit is not null)
            {
                parts.Add($"limit {query.Limit}");
            }

            if (query.StartIndex is not null)
            {
                parts.Add($"start {query.StartIndex}");
            }

            if (query.IsFolder is not null)
            {
                parts.Add($"IsFolder {query.IsFolder}");
            }

            if (query.MediaTypes.Length > 0)
            {
                parts.Add($"MediaTypes {string.Join(", ", query.MediaTypes)}");
            }

            if (query.IncludeItemTypes.Length > 0)
            {
                parts.Add($"type {ListToString(query.IncludeItemTypes)}");
            }

            if (query.AlbumArtistIds.Length > 0)
            {
                parts.Add($"AlbumArtistIds : {ListToString(query.AlbumArtistIds)}");
            }

            if (query.ArtistIds.Length > 0)
            {
                parts.Add($"ArtistIds : {ListToString(query.ArtistIds)}");
            }

            if (query.AncestorIds.Length > 0)
            {
                parts.Add($"AncestorIds : {ListToString(query.AncestorIds)}");
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
        /// <param name="backend">The underlying item repository.</param>
        /// <param name="userManager">The user manager.</param>
        public void Initialize(IItemRepository backend, IUserManager userManager)
        {
            _logger.LogInformation("Initializing Spotify Item Repository with backend {Backend}", backend);
            _backend = backend;
            _rootFolder = SpotifyRootFolder.GetOrCreate(this, _memoryCache);
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
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
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
            _logger.LogInformation("DeleteItem : {Id}", id);
            _backend.DeleteItem(id);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            _httpClient.Dispose();
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
            where T : IJSONToItems
        {
            var taskSearch = AsyncSpotQuery<T>(user, query, parentId);
            return taskSearch.GetAwaiter().GetResult();
        }

        private async Task<List<(BaseItem Item, ItemCounts ItemCounts)>> AsyncSpotQuery<T>(User user, string query, Guid? parentId = null, bool retry = true, bool forceClientCreds = false)
            where T : IJSONToItems
        {
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, query);
            string? token = null;
            if (user.SpotifyWPToken is not null && !forceClientCreds)
            {
                // Try to use the oauth web token first as it has more rights than the client-credentials one
                token = user.SpotifyWPToken;
            }
            else if (user.SpotifyToken is not null)
            {
                // We have no oauth web token, and we can't start an oauth request from this context, use the client credentials
                token = user.SpotifyToken;
            }

            if (token is null)
            {
                token = await SpotLogin(user.SpotifyApiKey).ConfigureAwait(false);
                if (token is null)
                {
                    // Login failed with the client credentials, bail out
                    _logger.LogWarning("Connection to spotify failed");
                    return new List<(BaseItem, ItemCounts)>();
                }
            }

            requestMessage.Headers.Add("Authorization", "Bearer " + token);
            HttpResponseMessage resp = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (token == user.SpotifyToken)
                {
                    // We tried to use the client credentials and it didn't work. Invalidate it and retry.
                    user.SpotifyToken = null;
                }
                else
                {
                    // TODO: We should try to refresh the web token if we can, and retry
                    // But for now only retry with the client creds
                }

                if (retry)
                {
                    return await AsyncSpotQuery<T>(user, query, parentId, false, true).ConfigureAwait(false);
                }
            }
            else if (resp.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Spotify search {Query} failed with code {Code} : {Text}", query, resp.StatusCode, body);
            }

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
                else
                {
                    _logger.LogInformation("Spotify query : {Q}, result : {J}", query, json);
                    return json.ToItems(_logger, _memoryCache, this, parentId, user.Id);
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
            if (item is null || item.ExternalId is null || !item.ExternalId.StartsWith("spotify", StringComparison.InvariantCulture))
            {
                _logger.LogInformation("Spotify lookup by id : Item {Guid} is not in cache or doesn't come from spotify", itemId);
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
                string searchEP = $"{spotAPI}/artists/{UriToId(qdata.Item.ExternalId)}/albums?include_groups=album&limit={limit}&market={qdata.User.SpotifyMarket}";
                var res = SpotQuery<AlbumList>(qdata.User, searchEP, artistId);
                _logger.LogInformation("Searching spotify for albums by artist {ArtistId} -> {N} results", qdata.Item.ExternalId, res.Count);
                return res;
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<BaseItem> TracksById(Guid[] trackIds)
        {
            var tracks = trackIds
                .Select(id => _memoryCache.Get<BaseItem>(id))
                .Where(item => item is BaseItem && item.ExternalId is not null && item.ExternalId.StartsWith("spotify", StringComparison.InvariantCulture))
                .Select(item => item!)
                .ToList();
            return tracks;
        }

        private void LinkTracks(Folder album, List<BaseItem> tracks)
        {
            album.LinkedChildren = tracks
                .Where(t => t is Audio)
                .Select(t =>
                    {
                        var newChild = LinkedChild.Create(t);
                        newChild.ItemId = t.Id; // Save the original item id
                        newChild.LibraryItemId = t.ExternalId; // Save the spotify id
                        return newChild;
                    })
                .ToArray();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> AlbumTracks(User? user, Guid albumId)
        {
            if (ValidateQueryData(user, albumId) is QueryData qdata && qdata.Item is MusicAlbum album)
            {
                _logger.LogInformation("Album {AName} already has {C} linked tracks", album.Name, album.LinkedChildren.Length);
                if (album.LinkedChildren.Length > 0)
                {
                    // First see if we have the tracks in cache or db
                    var localTracks = album.LinkedChildren
                        .Where(child => child.ItemId is not null)
                        .Select(child => TryRetrieveItem<BaseItem>(this, _memoryCache, (Guid)child.ItemId!))
                        .Where(item => item is Audio)
                        .Select(item => (item!, new ItemCounts { SongCount = 1 }))
                        .ToList();
                    if (localTracks.Count == album.LinkedChildren.Length)
                    {
                        _logger.LogInformation("All {C} linked tracks for album {AName} are still in cache or db, not querying spotify", localTracks.Count);
                        return localTracks;
                    }
                }

                _logger.LogInformation("Searching for tracks on album {AName} {AId}", album.Name, album.ExternalId);
                string searchEP = $"{spotAPI}/albums/{UriToId(album.ExternalId)}/tracks?market={qdata.User.SpotifyMarket}&limit=50";
                var res = SpotQuery<TrackList>(qdata.User, searchEP, albumId);
                LinkTracks(album, res.Select(i => i.Item).ToList());
                _logger.LogInformation("Searching spotify for track on album {AlbumId} -> {N} results", album.ExternalId, res.Count);
                return res;
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> ArtistTopTracks(User? user, Guid artistId)
        {
            if (ValidateQueryData(user, artistId) is QueryData qdata)
            {
                string searchEP = $"{spotAPI}/artists/{UriToId(qdata.Item.ExternalId)}/top-tracks?market={qdata.User.SpotifyMarket}";
                var res = SpotQuery<TrackList>(qdata.User, searchEP);
                _logger.LogInformation("Searching spotify for track by artist {ArtistId} -> {N} results", qdata.Item.ExternalId, res.Count);
                return res;
            }

            return new List<(BaseItem, ItemCounts)>();
        }

        private void MarkFavorite(User user, BaseItem item)
        {
            var data = _userDataRepository.GetUserData(user, item);
            if (!data.IsFavorite)
            {
                data.IsFavorite = true;
                _logger.LogInformation("Marking Item {I} from spotify as favorite", item.Name);
                _userDataRepository.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, CancellationToken.None);
            }
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> SpotifyFavorites(User? user, int limit, int offset = 0)
        {
            if (user is null)
            {
                return new List<(BaseItem, ItemCounts)>();
            }

            if (limit > 50)
            {
                limit = 50;
            }

            string searchEP = $"https://api.spotify.com/v1/me/tracks?limit={limit}&offset={offset}&market={user.SpotifyMarket}";
            _logger.LogInformation("SearchSpotifyItem for {N} favorites from offset {O}", limit, offset);
            return SpotQuery<FavTrackList>(user, searchEP).Select(i =>
                {
                    MarkFavorite(user, i.Item);
                    return (i.Item, i.ItemCounts);
                }).ToList();
        }

        private List<(BaseItem Item, ItemCounts ItemCounts)> SearchSpotifyItem(User? user, string search, string what, int limit, Guid? parentId = null)
        {
            if (user is null)
            {
                return new List<(BaseItem, ItemCounts)>();
            }

            string searchEP = $"https://api.spotify.com/v1/search?q={search}&type={what}&limit={limit}";
            _logger.LogInformation("SearchSpotifyItem for {N} {What} matching {Search}", limit, what, search);
            return SpotQuery<SearchResponse>(user, searchEP, parentId);
        }

        /// <inheritdoc/>
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query)
        {
            if (query.TopParentIds.Length > 0)
            {
                query.TopParentIds = query.TopParentIds.Append(_rootFolder.Id).ToArray();
            }

            // First, query the local database.
            var results = _backend.GetArtists(query).Items.ToDictionary<(BaseItem, ItemCounts), Guid>(result => result.Item1.Id);
            LogQuery("GetArtists", query, results.Count);
            // Then if we are looking for more results, query spotify.
            if (query.SearchTerm is not null && results.Count < query.Limit)
            {
                int duplicates = 0;
                int count = 0;
                foreach (var (item, itemCount) in SearchSpotifyItem(
                            query.User,
                            query.SearchTerm,
                            "artist",
                            (query.Limit ?? 20) - results.Count,
                            _rootFolder.Id)) // We use spotify root folder as parent for artists to anchor all spotify items to the spotify root parent
                {
                    count += 1;
                    if (!results.TryAdd(item.Id, (item, itemCount)))
                    {
                        duplicates += 1;
                    }
                }

                _logger.LogInformation("Query Artist Items from stpotify matching {Search} -> {N} results, {D} duplicates", query.SearchTerm, count, duplicates);
            }

            return new QueryResult<(BaseItem, ItemCounts)>(results.Values.ToList());
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

        private void TryAddItems(Dictionary<Guid, BaseItem> items, IEnumerable<BaseItem> newItems, out int count, out int duplicates)
        {
            count = 0;
            duplicates = 0;
            foreach (var i in newItems)
            {
                count += 1;
                if (!items.TryAdd(i.Id, i))
                {
                    duplicates += 1;
                }
            }
        }

        private IReadOnlyList<BaseItem> AddSpotifyItems(InternalItemsQuery query, IReadOnlyList<BaseItem> db_results)
        {
            var itemtypes = query.IncludeItemTypes;
            if (query.Limit is not null && query.Limit <= db_results.Count)
            {
                return db_results;
            }

            // TODO: Add limit check before each query
            // var results = new List<IReadOnlyList<BaseItem>> { db_results };
            var results = db_results.ToDictionary<BaseItem, Guid>(result => result.Id);
            // var results = new Dictionary<Guid, BaseItem>(db_results.Select(result => new KeyValuePair<Guid,BaseItem>(result.Id, result)));
            if (itemtypes.Length == 0)
            {
                if (!query.ParentId.Equals(Guid.Empty))
                {
                    // This is the api's way to ask for an album tracks ?
                    TryAddItems(
                            results,
                            AlbumTracks(query.User, query.ParentId)
                                .Select(pair => pair.Item)
                                .OrderBy(track => track.IndexNumber),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query Audio Items from stpotify for album {Ids} -> {N} results, {D} duplicates", query.ParentId, count, duplicates);
                }
            }

            if (itemtypes.Contains(BaseItemKind.Audio) || query.MediaTypes.Contains(MediaType.Audio))
            {
                if (query.ArtistIds.Length > 0)
                {
                    TryAddItems(
                            results,
                            query.ArtistIds
                                .Select(id => ArtistTopTracks(query.User, id).Select(pair => pair.Item))
                                .SelectMany(list => list),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query Audio Items from stpotify for artist {Ids} -> {N} results, {D} duplicates", query.ArtistIds, count, duplicates);
                }

                if (query.AncestorIds.Length > 0)
                {
                    TryAddItems(
                            results,
                            query.AncestorIds
                                .Select(id => AlbumTracks(query.User, id).Select(pair => pair.Item))
                                .SelectMany(list => list)
                                .ToList(),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query Audio Items from stpotify for albums {Ids} -> {N} results, {D} duplicates", query.AncestorIds, count, duplicates);
                }

                if (query.SearchTerm is not null)
                {
                    TryAddItems(
                            results,
                            SearchSpotifyItem(query.User, query.SearchTerm, "track", query.Limit ?? 25)
                                .Select(itemAndCount => itemAndCount.Item)
                                .ToList(),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query Audio Items from stpotify matching {Search} -> {N} results, {D} duplicates", query.SearchTerm, count, duplicates);
                }

                if (query.IsFavorite ?? false)
                {
                    int offset = 0;
                    int totalDup = 0;
                    int limit = query.Limit ?? 20;
                    while (results.Count < limit)
                    {
                        TryAddItems(
                                results,
                                SpotifyFavorites(query.User, Math.Min(50, limit), offset)
                                    .Select(itemAndCount => itemAndCount.Item)
                                    .ToList(),
                                out int count,
                                out int duplicates);
                        offset += count;
                        totalDup += duplicates;
                        if (count == 0)
                        {
                            // No more results from spotify
                            break;
                        }
                    }

                    _logger.LogInformation("Query Favorite Audio Items from stpotify -> {N} results, {D} duplicates", offset, totalDup);
                }
            }

            if (itemtypes.Contains(BaseItemKind.MusicAlbum))
            {
                if (query.ArtistIds.Length > 0)
                {
                    TryAddItems(
                            results,
                            query.ArtistIds
                                .Select(id => ArtistAlbum(query.User, id, 50).Select(pair => pair.Item))
                                .SelectMany(list => list)
                                .ToList(),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query MusicAlbum Items from stpotify for {Ids} -> {N} results, {D} duplicates", query.ArtistIds, count, duplicates);
                }

                if (query.AlbumArtistIds.Length > 0)
                {
                    TryAddItems(
                            results,
                            query.AlbumArtistIds
                                .Select(id => ArtistAlbum(query.User, id, 50).Select(pair => pair.Item))
                                .SelectMany(list => list)
                                .ToList(),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query MusicAlbum Items from stpotify for {Ids} -> {N} results, {D} duplicates", query.ArtistIds, count, duplicates);
                }

                if (query.SearchTerm is not null)
                {
                    TryAddItems(
                            results,
                            SearchSpotifyItem(query.User, query.SearchTerm, "album", query.Limit ?? 25)
                                .Select(itemAndCount => itemAndCount.Item)
                                .ToList(),
                            out int count,
                            out int duplicates);
                    _logger.LogInformation("Query Album Items from stpotify matching {Search} -> {N} results, {D} duplicates", query.SearchTerm, count, duplicates);
                }
            }

            if (query.Limit is not null && query.Limit > 0)
            {
                return results.Values.Take((int)query.Limit).ToArray();
            }

            return results.Values.ToArray();
        }

        /// <inheritdoc/>
        public List<BaseItem> GetItemList(InternalItemsQuery query)
        {
            if (query.TopParentIds.Length > 0)
            {
                query.TopParentIds = query.TopParentIds.Append(_rootFolder.Id).ToArray();
            }

            var results = _backend.GetItemList(query);
            /*
            if (results.Count > 0 && query.Name != string.Empty && query.IncludeItemTypes.Contains(BaseItemKind.MusicArtist))
            {
                _logger.LogInformation("GetItemList got {N} results from DB : {Res}", results.Count, results.Select(r => $"{r.Name}, {r.Id}, {r.ExternalId}"));
            }
            */
            if (query.Name is null || query.Name.Length == 0)
            {
                LogQuery("GetItemList", query, results.Count);
            }

            return AddSpotifyItems(query, results).ToList();
        }

        /// <inheritdoc/>
        public QueryResult<BaseItem> GetItems(InternalItemsQuery query)
        {
            if (query.TopParentIds.Length > 0)
            {
                query.TopParentIds = query.TopParentIds.Append(_rootFolder.Id).ToArray();
            }

            QueryResult<BaseItem> results = _backend.GetItems(query);
            LogQuery("GetItems", query, results.TotalRecordCount);
            var resultsSpot = new QueryResult<BaseItem>(AddSpotifyItems(query, results.Items));
            // Fix TotalRecordCount and StartIndex
            resultsSpot.StartIndex = results.StartIndex;
            resultsSpot.TotalRecordCount = results.TotalRecordCount + (resultsSpot.Items.Count - results.Items.Count);

            if (query.Limit is not null && query.StartIndex is not null && resultsSpot.Items.Count >= query.Limit)
            {
                _logger.LogInformation("GetItems query limit {L}, total item count {C}", resultsSpot.Items.Count, query.Limit);
                if (resultsSpot.Items.Count >= query.Limit)
                {
                    // There are probably more spotify results available, let's raise the total record count by some arbitrary amount to allow the user to query more results
                    resultsSpot.TotalRecordCount += (int)(query.Limit - query.StartIndex);
                }
            }

            return resultsSpot;
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
                // _logger.LogInformation("GetMediaStreams : For item {G}/{IE}, type {T}, media index : {I}", query.ItemId, item.ExternalId, query.Type, query.Index);
                if (item.ExternalId is not null && item.ExternalId.StartsWith("spotify", StringComparison.InvariantCulture))
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

            // _logger.LogInformation("Got {N} media streams for {I} : {M}", res.Count, query.ItemId, string.Join("\n\t", res.Select(ms => FormatMediaStream(ms))));
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
            // _logger.LogInformation("RetrieveItem: {Id}", id);
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
            if (items.Count > 0)
            {
                _logger.LogInformation("SaveItems : {Items}", items.Select(i => $"{i.Name}, id {i.Id}, external id {i.ExternalId} keys {i.GetUserDataKeys().FirstOrDefault()}"));
            }

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
            _logger.LogInformation("SaveMediaStreams: {Items}", streams.Select(i => i.Path));
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
