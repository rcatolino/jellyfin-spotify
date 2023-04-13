using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
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
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Data
{
    /// <summary>
    /// Class SpotItemRepository.
    /// </summary>
    public class SpotItemRepository : IItemRepository
    {
        private SqliteItemRepository _backend;
        private ILogger<SqliteItemRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpotItemRepository"/> class.
        /// </summary>
        /// <param name="config">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
        /// <param name="appHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{SqliteItemRepository}"/> interface.</param>
        /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
        /// <param name="imageProcessor">Instance of the <see cref="IImageProcessor"/> interface.</param>
        public SpotItemRepository(
            IServerConfigurationManager config,
            IServerApplicationHost appHost,
            ILogger<SqliteItemRepository> logger,
            ILocalizationManager localization,
            IImageProcessor imageProcessor)
        {
            _backend = new SqliteItemRepository(config, appHost, logger, localization, imageProcessor);
            _logger = logger;
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
            _backend.Initialize(userDataRepo, userManager);
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

        /// <inheritdoc/>
        // TODO: This method sould return additional artists from spotify, up to query.Limit
        public QueryResult<(BaseItem Item, ItemCounts ItemCounts)> GetArtists(InternalItemsQuery query)
        {
            QueryResult<(BaseItem, ItemCounts)> results = _backend.GetArtists(query);
            LogQuery("GetArtists", query, results.TotalRecordCount);
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
