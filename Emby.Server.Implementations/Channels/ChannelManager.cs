﻿using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Progress;
using MediaBrowser.Model.IO;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;

namespace Emby.Server.Implementations.Channels
{
    public class ChannelManager : IChannelManager
    {
        internal IChannel[] Channels { get; private set; }

        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IDtoService _dtoService;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IHttpClient _httpClient;
        private readonly IProviderManager _providerManager;

        private readonly ILocalizationManager _localization;
        private readonly ConcurrentDictionary<Guid, bool> _refreshedItems = new ConcurrentDictionary<Guid, bool>();

        public ChannelManager(IUserManager userManager, IDtoService dtoService, ILibraryManager libraryManager, ILogger logger, IServerConfigurationManager config, IFileSystem fileSystem, IUserDataManager userDataManager, IJsonSerializer jsonSerializer, ILocalizationManager localization, IHttpClient httpClient, IProviderManager providerManager)
        {
            _userManager = userManager;
            _dtoService = dtoService;
            _libraryManager = libraryManager;
            _logger = logger;
            _config = config;
            _fileSystem = fileSystem;
            _userDataManager = userDataManager;
            _jsonSerializer = jsonSerializer;
            _localization = localization;
            _httpClient = httpClient;
            _providerManager = providerManager;
        }

        private TimeSpan CacheLength
        {
            get
            {
                return TimeSpan.FromHours(6);
            }
        }

        public void AddParts(IEnumerable<IChannel> channels)
        {
            Channels = channels.ToArray();
        }

        public bool EnableMediaSourceDisplay(BaseItem item)
        {
            var internalChannel = _libraryManager.GetItemById(item.ChannelId);
            var channel = Channels.FirstOrDefault(i => GetInternalChannelId(i.Name).Equals(internalChannel.Id));

            return !(channel is IDisableMediaSourceDisplay);
        }

        public bool CanDelete(BaseItem item)
        {
            var internalChannel = _libraryManager.GetItemById(item.ChannelId);
            var channel = Channels.FirstOrDefault(i => GetInternalChannelId(i.Name).Equals(internalChannel.Id));

            var supportsDelete = channel as ISupportsDelete;
            return supportsDelete != null && supportsDelete.CanDelete(item);
        }

        public Task DeleteItem(BaseItem item)
        {
            var internalChannel = _libraryManager.GetItemById(item.ChannelId);
            var channel = Channels.FirstOrDefault(i => GetInternalChannelId(i.Name).Equals(internalChannel.Id));

            var supportsDelete = channel as ISupportsDelete;

            if (supportsDelete == null)
            {
                throw new ArgumentException();
            }

            return supportsDelete.DeleteItem(item.ExternalId, CancellationToken.None);
        }

        private IEnumerable<IChannel> GetAllChannels()
        {
            return Channels
                .OrderBy(i => i.Name);
        }

        public IEnumerable<Guid> GetInstalledChannelIds()
        {
            return GetAllChannels().Select(i => GetInternalChannelId(i.Name));
        }

        public Task<QueryResult<Channel>> GetChannelsInternal(ChannelQuery query, CancellationToken cancellationToken)
        {
            var user = string.IsNullOrEmpty(query.UserId)
                ? null
                : _userManager.GetUserById(query.UserId);

            var channels = GetAllChannels()
                .Select(GetChannelEntity)
                .OrderBy(i => i.SortName)
                .ToList();

            if (query.SupportsLatestItems.HasValue)
            {
                var val = query.SupportsLatestItems.Value;
                channels = channels.Where(i =>
                {
                    try
                    {
                        return GetChannelProvider(i) is ISupportsLatestMedia == val;
                    }
                    catch
                    {
                        return false;
                    }

                }).ToList();
            }
            if (query.IsFavorite.HasValue)
            {
                var val = query.IsFavorite.Value;
                channels = channels.Where(i => _userDataManager.GetUserData(user, i).IsFavorite == val)
                    .ToList();
            }

            if (user != null)
            {
                channels = channels.Where(i =>
                {
                    if (!i.IsVisible(user))
                    {
                        return false;
                    }

                    try
                    {
                        return GetChannelProvider(i).IsEnabledFor(user.Id.ToString("N"));
                    }
                    catch
                    {
                        return false;
                    }

                }).ToList();
            }

            var all = channels;
            var totalCount = all.Count;

            if (query.StartIndex.HasValue)
            {
                all = all.Skip(query.StartIndex.Value).ToList();
            }
            if (query.Limit.HasValue)
            {
                all = all.Take(query.Limit.Value).ToList();
            }

            var returnItems = all.ToArray(all.Count);

            var result = new QueryResult<Channel>
            {
                Items = returnItems,
                TotalRecordCount = totalCount
            };

            return Task.FromResult(result);
        }

        public async Task<QueryResult<BaseItemDto>> GetChannels(ChannelQuery query, CancellationToken cancellationToken)
        {
            var user = string.IsNullOrEmpty(query.UserId)
                ? null
                : _userManager.GetUserById(query.UserId);

            var internalResult = await GetChannelsInternal(query, cancellationToken).ConfigureAwait(false);

            var dtoOptions = new DtoOptions()
            {
            };

            var returnItems = _dtoService.GetBaseItemDtos(internalResult.Items, dtoOptions, user);

            var result = new QueryResult<BaseItemDto>
            {
                Items = returnItems,
                TotalRecordCount = internalResult.TotalRecordCount
            };

            return result;
        }

        public async Task RefreshChannels(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _refreshedItems.Clear();

            var allChannelsList = GetAllChannels().ToList();

            var numComplete = 0;

            foreach (var channelInfo in allChannelsList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await GetChannel(channelInfo, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error getting channel information for {0}", ex, channelInfo.Name);
                }

                numComplete++;
                double percent = numComplete;
                percent /= allChannelsList.Count;

                progress.Report(100 * percent);
            }

            progress.Report(100);
        }

        private Channel GetChannelEntity(IChannel channel)
        {
            var item = GetChannel(GetInternalChannelId(channel.Name).ToString("N"));

            if (item == null)
            {
                item = GetChannel(channel, CancellationToken.None).Result;
            }

            return item;
        }

        private List<MediaSourceInfo> GetSavedMediaSources(BaseItem item)
        {
            var path = Path.Combine(item.GetInternalMetadataPath(), "channelmediasourceinfos.json");

            try
            {
                return _jsonSerializer.DeserializeFromFile<List<MediaSourceInfo>>(path) ?? new List<MediaSourceInfo>();
            }
            catch
            {
                return new List<MediaSourceInfo>();
            }
        }

        private void SaveMediaSources(BaseItem item, List<MediaSourceInfo> mediaSources)
        {
            var path = Path.Combine(item.GetInternalMetadataPath(), "channelmediasourceinfos.json");

            if (mediaSources == null || mediaSources.Count == 0)
            {
                try
                {
                    _fileSystem.DeleteFile(path);
                }
                catch
                {

                }
                return;
            }

            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(path));

            _jsonSerializer.SerializeToFile(mediaSources, path);
        }

        public IEnumerable<MediaSourceInfo> GetStaticMediaSources(BaseItem item, CancellationToken cancellationToken)
        {
            IEnumerable<MediaSourceInfo> results = GetSavedMediaSources(item);

            return results
                .Select(i => NormalizeMediaSource(item, i))
                .ToList();
        }

        public async Task<IEnumerable<MediaSourceInfo>> GetDynamicMediaSources(BaseItem item, CancellationToken cancellationToken)
        {
            var channel = GetChannel(item.ChannelId);
            var channelPlugin = GetChannelProvider(channel);

            var requiresCallback = channelPlugin as IRequiresMediaInfoCallback;

            IEnumerable<MediaSourceInfo> results;

            if (requiresCallback != null)
            {
                results = await GetChannelItemMediaSourcesInternal(requiresCallback, item.ExternalId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                results = new List<MediaSourceInfo>();
            }

            return results
                .Select(i => NormalizeMediaSource(item, i))
                .ToList();
        }

        private readonly ConcurrentDictionary<string, Tuple<DateTime, List<MediaSourceInfo>>> _channelItemMediaInfo =
            new ConcurrentDictionary<string, Tuple<DateTime, List<MediaSourceInfo>>>();

        private async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaSourcesInternal(IRequiresMediaInfoCallback channel, string id, CancellationToken cancellationToken)
        {
            Tuple<DateTime, List<MediaSourceInfo>> cachedInfo;

            if (_channelItemMediaInfo.TryGetValue(id, out cachedInfo))
            {
                if ((DateTime.UtcNow - cachedInfo.Item1).TotalMinutes < 5)
                {
                    return cachedInfo.Item2;
                }
            }

            var mediaInfo = await channel.GetChannelItemMediaInfo(id, cancellationToken)
                   .ConfigureAwait(false);
            var list = mediaInfo.ToList();

            var item2 = new Tuple<DateTime, List<MediaSourceInfo>>(DateTime.UtcNow, list);
            _channelItemMediaInfo.AddOrUpdate(id, item2, (key, oldValue) => item2);

            return list;
        }

        private MediaSourceInfo NormalizeMediaSource(BaseItem item, MediaSourceInfo info)
        {
            info.RunTimeTicks = info.RunTimeTicks ?? item.RunTimeTicks;

            return info;
        }

        private async Task<Channel> GetChannel(IChannel channelInfo, CancellationToken cancellationToken)
        {
            var parentFolderId = Guid.Empty;

            var id = GetInternalChannelId(channelInfo.Name);
            var idString = id.ToString("N");

            var path = Channel.GetInternalMetadataPath(_config.ApplicationPaths.InternalMetadataPath, id);

            var isNew = false;
            var forceUpdate = false;

            var item = _libraryManager.GetItemById(id) as Channel;

            if (item == null)
            {
                item = new Channel
                {
                    Name = channelInfo.Name,
                    Id = id,
                    DateCreated = _fileSystem.GetCreationTimeUtc(path),
                    DateModified = _fileSystem.GetLastWriteTimeUtc(path)
                };

                isNew = true;
            }

            if (!string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                isNew = true;
            }
            item.Path = path;

            if (!string.Equals(item.ChannelId, idString, StringComparison.OrdinalIgnoreCase))
            {
                forceUpdate = true;
            }
            item.ChannelId = idString;

            if (item.ParentId != parentFolderId)
            {
                forceUpdate = true;
            }
            item.ParentId = parentFolderId;

            item.OfficialRating = GetOfficialRating(channelInfo.ParentalRating);
            item.Overview = channelInfo.Description;
            item.HomePageUrl = channelInfo.HomePageUrl;

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                item.Name = channelInfo.Name;
            }

            item.OnMetadataChanged();

            if (isNew)
            {
                _libraryManager.CreateItem(item, cancellationToken);
            }
            else if (forceUpdate)
            {
                item.UpdateToRepository(ItemUpdateType.None, cancellationToken);
            }

            await item.RefreshMetadata(new MetadataRefreshOptions(_fileSystem), cancellationToken);
            return item;
        }

        private string GetOfficialRating(ChannelParentalRating rating)
        {
            switch (rating)
            {
                case ChannelParentalRating.Adult:
                    return "XXX";
                case ChannelParentalRating.UsR:
                    return "R";
                case ChannelParentalRating.UsPG13:
                    return "PG-13";
                case ChannelParentalRating.UsPG:
                    return "PG";
                default:
                    return null;
            }
        }

        public Channel GetChannel(string id)
        {
            return _libraryManager.GetItemById(id) as Channel;
        }

        public ChannelFeatures[] GetAllChannelFeatures()
        {
            return _libraryManager.GetItemIds(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Channel).Name },
                OrderBy = new Tuple<string, SortOrder>[] { new Tuple<string, SortOrder>(ItemSortBy.SortName, SortOrder.Ascending) }

            }).Select(i => GetChannelFeatures(i.ToString("N"))).ToArray();
        }

        public ChannelFeatures GetChannelFeatures(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException("id");
            }

            var channel = GetChannel(id);
            var channelProvider = GetChannelProvider(channel);

            return GetChannelFeaturesDto(channel, channelProvider, channelProvider.GetChannelFeatures());
        }

        public bool SupportsSync(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                throw new ArgumentNullException("channelId");
            }

            //var channel = GetChannel(channelId);
            var channelProvider = GetChannelProvider(channelId);

            return channelProvider.GetChannelFeatures().SupportsContentDownloading;
        }

        public ChannelFeatures GetChannelFeaturesDto(Channel channel,
            IChannel provider,
            InternalChannelFeatures features)
        {
            var supportsLatest = provider is ISupportsLatestMedia;

            return new ChannelFeatures
            {
                CanFilter = !features.MaxPageSize.HasValue,
                CanSearch = provider is ISearchableChannel,
                ContentTypes = features.ContentTypes.ToArray(),
                DefaultSortFields = features.DefaultSortFields.ToArray(),
                MaxPageSize = features.MaxPageSize,
                MediaTypes = features.MediaTypes.ToArray(),
                SupportsSortOrderToggle = features.SupportsSortOrderToggle,
                SupportsLatestMedia = supportsLatest,
                Name = channel.Name,
                Id = channel.Id.ToString("N"),
                SupportsContentDownloading = features.SupportsContentDownloading,
                AutoRefreshLevels = features.AutoRefreshLevels
            };
        }

        private Guid GetInternalChannelId(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("name");
            }
            return _libraryManager.GetNewItemId("Channel " + name, typeof(Channel));
        }

        public async Task<QueryResult<BaseItemDto>> GetLatestChannelItems(AllChannelMediaQuery query, CancellationToken cancellationToken)
        {
            var user = string.IsNullOrEmpty(query.UserId)
                ? null
                : _userManager.GetUserById(query.UserId);

            var limit = query.Limit;

            // See below about parental control
            if (user != null)
            {
                query.StartIndex = null;
                query.Limit = null;
            }

            var internalResult = await GetLatestChannelItemsInternal(query, cancellationToken).ConfigureAwait(false);

            var items = internalResult.Items;
            var totalRecordCount = internalResult.TotalRecordCount;

            // Supporting parental control is a hack because it has to be done after querying the remote data source
            // This will get screwy if apps try to page, so limit to 10 results in an attempt to always keep them on the first page
            if (user != null)
            {
                items = items.Where(i => i.IsVisible(user))
                    .Take(limit ?? 10)
                    .ToArray();

                totalRecordCount = items.Length;
            }

            var dtoOptions = new DtoOptions()
            {
                Fields = query.Fields
            };

            var returnItems = _dtoService.GetBaseItemDtos(items, dtoOptions, user);

            var result = new QueryResult<BaseItemDto>
            {
                Items = returnItems,
                TotalRecordCount = totalRecordCount
            };

            return result;
        }

        public async Task<QueryResult<BaseItem>> GetLatestChannelItemsInternal(AllChannelMediaQuery query, CancellationToken cancellationToken)
        {
            var user = string.IsNullOrEmpty(query.UserId)
                ? null
                : _userManager.GetUserById(query.UserId);

            if (!string.IsNullOrEmpty(query.UserId) && user == null)
            {
                throw new ArgumentException("User not found.");
            }

            var channels = GetAllChannels();

            if (query.ChannelIds.Length > 0)
            {
                // Avoid implicitly captured closure
                var ids = query.ChannelIds;
                channels = channels
                    .Where(i => ids.Contains(GetInternalChannelId(i.Name).ToString("N")))
                    .ToArray();
            }

            // Avoid implicitly captured closure
            var userId = query.UserId;

            var tasks = channels
                .Select(async i =>
                {
                    var indexable = i as ISupportsLatestMedia;

                    if (indexable != null)
                    {
                        try
                        {
                            var result = await GetLatestItems(indexable, i, userId, cancellationToken).ConfigureAwait(false);

                            var resultItems = result.ToList();

                            return new Tuple<IChannel, ChannelItemResult>(i, new ChannelItemResult
                            {
                                Items = resultItems,
                                TotalRecordCount = resultItems.Count
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.ErrorException("Error getting all media from {0}", ex, i.Name);
                        }
                    }
                    return new Tuple<IChannel, ChannelItemResult>(i, new ChannelItemResult());
                });

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            var totalCount = results.Length;

            IEnumerable<Tuple<IChannel, ChannelItemInfo>> items = results
                .SelectMany(i => i.Item2.Items.Select(m => new Tuple<IChannel, ChannelItemInfo>(i.Item1, m)));

            if (query.ContentTypes.Length > 0)
            {
                // Avoid implicitly captured closure
                var contentTypes = query.ContentTypes;

                items = items.Where(i => contentTypes.Contains(i.Item2.ContentType));
            }
            if (query.ExtraTypes.Length > 0)
            {
                // Avoid implicitly captured closure
                var contentTypes = query.ExtraTypes;

                items = items.Where(i => contentTypes.Contains(i.Item2.ExtraType));
            }

            // Avoid implicitly captured closure
            var token = cancellationToken;
            var internalItems = items.Select(i =>
            {
                var channelProvider = i.Item1;
                var internalChannelId = GetInternalChannelId(channelProvider.Name);
                return GetChannelItemEntity(i.Item2, channelProvider, internalChannelId, token);
            }).ToArray();

            internalItems = ApplyFilters(internalItems, query.Filters, user).ToArray();
            RefreshIfNeeded(internalItems);

            if (query.StartIndex.HasValue)
            {
                internalItems = internalItems.Skip(query.StartIndex.Value).ToArray();
            }
            if (query.Limit.HasValue)
            {
                internalItems = internalItems.Take(query.Limit.Value).ToArray();
            }

            return new QueryResult<BaseItem>
            {
                TotalRecordCount = totalCount,
                Items = internalItems
            };
        }

        private async Task<IEnumerable<ChannelItemInfo>> GetLatestItems(ISupportsLatestMedia indexable, IChannel channel, string userId, CancellationToken cancellationToken)
        {
            var cacheLength = CacheLength;
            var cachePath = GetChannelDataCachePath(channel, userId, "channelmanager-latest", null, false);

            try
            {
                if (_fileSystem.GetLastWriteTimeUtc(cachePath).Add(cacheLength) > DateTime.UtcNow)
                {
                    return _jsonSerializer.DeserializeFromFile<List<ChannelItemInfo>>(cachePath);
                }
            }
            catch (FileNotFoundException)
            {

            }
            catch (IOException)
            {

            }

            await _resourcePool.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                try
                {
                    if (_fileSystem.GetLastWriteTimeUtc(cachePath).Add(cacheLength) > DateTime.UtcNow)
                    {
                        return _jsonSerializer.DeserializeFromFile<List<ChannelItemInfo>>(cachePath);
                    }
                }
                catch (FileNotFoundException)
                {

                }
                catch (IOException)
                {

                }

                var result = await indexable.GetLatestMedia(new ChannelLatestMediaSearch
                {
                    UserId = userId

                }, cancellationToken).ConfigureAwait(false);

                var resultItems = result.ToList();

                CacheResponse(resultItems, cachePath);

                return resultItems;
            }
            finally
            {
                _resourcePool.Release();
            }
        }

        public async Task<QueryResult<BaseItem>> GetChannelItemsInternal(ChannelItemQuery query, IProgress<double> progress, CancellationToken cancellationToken)
        {
            // Get the internal channel entity
            var channel = GetChannel(query.ChannelId);

            // Find the corresponding channel provider plugin
            var channelProvider = GetChannelProvider(channel);

            var channelInfo = channelProvider.GetChannelFeatures();

            int? providerStartIndex = null;
            int? providerLimit = null;

            if (channelInfo.MaxPageSize.HasValue)
            {
                providerStartIndex = query.StartIndex;

                if (query.Limit.HasValue && query.Limit.Value > channelInfo.MaxPageSize.Value)
                {
                    query.Limit = Math.Min(query.Limit.Value, channelInfo.MaxPageSize.Value);
                }
                providerLimit = query.Limit;

                // This will cause some providers to fail
                if (providerLimit == 0)
                {
                    providerLimit = 1;
                }
            }

            var user = string.IsNullOrEmpty(query.UserId)
                ? null
                : _userManager.GetUserById(query.UserId);

            ChannelItemSortField? sortField = null;
            ChannelItemSortField parsedField;
            var sortDescending = false;

            if (query.OrderBy.Length == 1 &&
                Enum.TryParse(query.OrderBy[0].Item1, true, out parsedField))
            {
                sortField = parsedField;
                sortDescending = query.OrderBy[0].Item2 == SortOrder.Descending;
            }

            var itemsResult = await GetChannelItems(channelProvider,
                user,
                query.FolderId,
                providerStartIndex,
                providerLimit,
                sortField,
                sortDescending,
                cancellationToken)
                .ConfigureAwait(false);

            var providerTotalRecordCount = providerLimit.HasValue ? itemsResult.TotalRecordCount : null;

            var internalItems = itemsResult.Items.Select(i => GetChannelItemEntity(i, channelProvider, channel.Id, cancellationToken)).ToArray();

            if (user != null)
            {
                internalItems = internalItems.Where(i => i.IsVisible(user)).ToArray();

                if (providerTotalRecordCount.HasValue)
                {
                    providerTotalRecordCount = providerTotalRecordCount.Value;
                }
            }

            return GetReturnItems(internalItems, providerTotalRecordCount, user, query);
        }

        public async Task<QueryResult<BaseItemDto>> GetChannelItems(ChannelItemQuery query, CancellationToken cancellationToken)
        {
            var user = string.IsNullOrEmpty(query.UserId)
                ? null
                : _userManager.GetUserById(query.UserId);

            var internalResult = await GetChannelItemsInternal(query, new SimpleProgress<double>(), cancellationToken).ConfigureAwait(false);

            var dtoOptions = new DtoOptions()
            {
                Fields = query.Fields
            };

            var returnItems = _dtoService.GetBaseItemDtos(internalResult.Items, dtoOptions, user);

            var result = new QueryResult<BaseItemDto>
            {
                Items = returnItems,
                TotalRecordCount = internalResult.TotalRecordCount
            };

            return result;
        }

        private readonly SemaphoreSlim _resourcePool = new SemaphoreSlim(1, 1);
        private async Task<ChannelItemResult> GetChannelItems(IChannel channel,
            User user,
            string folderId,
            int? startIndex,
            int? limit,
            ChannelItemSortField? sortField,
            bool sortDescending,
            CancellationToken cancellationToken)
        {
            var userId = user.Id.ToString("N");

            var cacheLength = CacheLength;
            var cachePath = GetChannelDataCachePath(channel, userId, folderId, sortField, sortDescending);

            try
            {
                if (!startIndex.HasValue && !limit.HasValue)
                {
                    if (_fileSystem.GetLastWriteTimeUtc(cachePath).Add(cacheLength) > DateTime.UtcNow)
                    {
                        var cachedResult = _jsonSerializer.DeserializeFromFile<ChannelItemResult>(cachePath);
                        if (cachedResult != null)
                        {
                            return cachedResult;
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {

            }
            catch (IOException)
            {

            }

            await _resourcePool.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                try
                {
                    if (!startIndex.HasValue && !limit.HasValue)
                    {
                        if (_fileSystem.GetLastWriteTimeUtc(cachePath).Add(cacheLength) > DateTime.UtcNow)
                        {
                            var cachedResult = _jsonSerializer.DeserializeFromFile<ChannelItemResult>(cachePath);
                            if (cachedResult != null)
                            {
                                return cachedResult;
                            }
                        }
                    }
                }
                catch (FileNotFoundException)
                {

                }
                catch (IOException)
                {

                }

                var query = new InternalChannelItemQuery
                {
                    UserId = userId,
                    StartIndex = startIndex,
                    Limit = limit,
                    SortBy = sortField,
                    SortDescending = sortDescending
                };

                if (!string.IsNullOrEmpty(folderId))
                {
                    var categoryItem = _libraryManager.GetItemById(new Guid(folderId));

                    query.FolderId = categoryItem.ExternalId;
                }

                var result = await channel.GetChannelItems(query, cancellationToken).ConfigureAwait(false);

                if (result == null)
                {
                    throw new InvalidOperationException("Channel returned a null result from GetChannelItems");
                }

                if (!startIndex.HasValue && !limit.HasValue)
                {
                    CacheResponse(result, cachePath);
                }

                return result;
            }
            finally
            {
                _resourcePool.Release();
            }
        }

        private void CacheResponse(object result, string path)
        {
            try
            {
                _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(path));

                _jsonSerializer.SerializeToFile(result, path);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error writing to channel cache file: {0}", ex, path);
            }
        }

        private string GetChannelDataCachePath(IChannel channel,
            string userId,
            string folderId,
            ChannelItemSortField? sortField,
            bool sortDescending)
        {
            var channelId = GetInternalChannelId(channel.Name).ToString("N");

            var userCacheKey = string.Empty;

            var hasCacheKey = channel as IHasCacheKey;
            if (hasCacheKey != null)
            {
                userCacheKey = hasCacheKey.GetCacheKey(userId) ?? string.Empty;
            }

            var filename = string.IsNullOrEmpty(folderId) ? "root" : folderId;
            filename += userCacheKey;

            var version = (channel.DataVersion ?? string.Empty).GetMD5().ToString("N");

            if (sortField.HasValue)
            {
                filename += "-sortField-" + sortField.Value;
            }
            if (sortDescending)
            {
                filename += "-sortDescending";
            }

            filename = filename.GetMD5().ToString("N");

            return Path.Combine(_config.ApplicationPaths.CachePath,
                "channels",
                channelId,
                version,
                filename + ".json");
        }

        private QueryResult<BaseItem> GetReturnItems(IEnumerable<BaseItem> items,
            int? totalCountFromProvider,
            User user,
            ChannelItemQuery query)
        {
            items = ApplyFilters(items, query.Filters, user);

            items = _libraryManager.Sort(items, user, query.OrderBy);

            var all = items.ToList();
            var totalCount = totalCountFromProvider ?? all.Count;

            if (!totalCountFromProvider.HasValue)
            {
                if (query.StartIndex.HasValue)
                {
                    all = all.Skip(query.StartIndex.Value).ToList();
                }
                if (query.Limit.HasValue)
                {
                    all = all.Take(query.Limit.Value).ToList();
                }
            }

            var returnItemArray = all.ToArray(all.Count);
            RefreshIfNeeded(returnItemArray);

            return new QueryResult<BaseItem>
            {
                Items = returnItemArray,
                TotalRecordCount = totalCount
            };
        }

        private string GetIdToHash(string externalId, string channelName)
        {
            // Increment this as needed to force new downloads
            // Incorporate Name because it's being used to convert channel entity to provider
            return externalId + (channelName ?? string.Empty) + "16";
        }

        private T GetItemById<T>(string idString, string channelName, out bool isNew)
            where T : BaseItem, new()
        {
            var id = GetIdToHash(idString, channelName).GetMBId(typeof(T));

            T item = null;

            try
            {
                item = _libraryManager.GetItemById(id) as T;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error retrieving channel item from database", ex);
            }

            if (item == null)
            {
                item = new T();
                isNew = true;
            }
            else
            {
                isNew = false;
            }

            item.Id = id;
            return item;
        }

        private BaseItem GetChannelItemEntity(ChannelItemInfo info, IChannel channelProvider, Guid internalChannelId, CancellationToken cancellationToken)
        {
            BaseItem item;
            bool isNew;
            bool forceUpdate = false;

            if (info.Type == ChannelItemType.Folder)
            {
                if (info.FolderType == ChannelFolderType.MusicAlbum)
                {
                    item = GetItemById<MusicAlbum>(info.Id, channelProvider.Name, out isNew);
                }
                else if (info.FolderType == ChannelFolderType.MusicArtist)
                {
                    item = GetItemById<MusicArtist>(info.Id, channelProvider.Name, out isNew);
                }
                else if (info.FolderType == ChannelFolderType.PhotoAlbum)
                {
                    item = GetItemById<PhotoAlbum>(info.Id, channelProvider.Name, out isNew);
                }
                else if (info.FolderType == ChannelFolderType.Series)
                {
                    item = GetItemById<Series>(info.Id, channelProvider.Name, out isNew);
                }
                else if (info.FolderType == ChannelFolderType.Season)
                {
                    item = GetItemById<Season>(info.Id, channelProvider.Name, out isNew);
                }
                else
                {
                    item = GetItemById<Folder>(info.Id, channelProvider.Name, out isNew);
                }
            }
            else if (info.MediaType == ChannelMediaType.Audio)
            {
                if (info.ContentType == ChannelMediaContentType.Podcast)
                {
                    item = GetItemById<AudioPodcast>(info.Id, channelProvider.Name, out isNew);
                }
                else
                {
                    item = GetItemById<Audio>(info.Id, channelProvider.Name, out isNew);
                }
            }
            else
            {
                if (info.ContentType == ChannelMediaContentType.Episode)
                {
                    item = GetItemById<Episode>(info.Id, channelProvider.Name, out isNew);
                }
                else if (info.ContentType == ChannelMediaContentType.Movie)
                {
                    item = GetItemById<Movie>(info.Id, channelProvider.Name, out isNew);
                }
                else if (info.ContentType == ChannelMediaContentType.Trailer || info.ExtraType == ExtraType.Trailer)
                {
                    item = GetItemById<Trailer>(info.Id, channelProvider.Name, out isNew);
                }
                else
                {
                    item = GetItemById<Video>(info.Id, channelProvider.Name, out isNew);
                }
            }

            if (info.IsLiveStream)
            {
                item.RunTimeTicks = null;
            }

            else if (isNew || !info.EnableMediaProbe)
            {
                item.RunTimeTicks = info.RunTimeTicks;
            }

            if (isNew)
            {
                item.Name = info.Name;
                item.Genres = info.Genres;
                item.Studios = info.Studios.ToArray(info.Studios.Count);
                item.CommunityRating = info.CommunityRating;
                item.Overview = info.Overview;
                item.IndexNumber = info.IndexNumber;
                item.ParentIndexNumber = info.ParentIndexNumber;
                item.PremiereDate = info.PremiereDate;
                item.ProductionYear = info.ProductionYear;
                item.ProviderIds = info.ProviderIds;
                item.OfficialRating = info.OfficialRating;
                item.DateCreated = info.DateCreated ?? DateTime.UtcNow;
                item.Tags = info.Tags.ToArray(info.Tags.Count);
                item.HomePageUrl = info.HomePageUrl;
                item.OriginalTitle = info.OriginalTitle;
            }
            else if (info.Type == ChannelItemType.Folder && info.FolderType == ChannelFolderType.Container)
            {
                // At least update names of container folders
                if (item.Name != info.Name)
                {
                    item.Name = info.Name;
                    forceUpdate = true;
                }
            }

            var hasArtists = item as IHasArtist;
            if (hasArtists != null)
            {
                hasArtists.Artists = info.Artists.ToArray();
            }

            var hasAlbumArtists = item as IHasAlbumArtist;
            if (hasAlbumArtists != null)
            {
                hasAlbumArtists.AlbumArtists = info.AlbumArtists.ToArray(info.AlbumArtists.Count);
            }

            var trailer = item as Trailer;
            if (trailer != null)
            {
                if (!info.TrailerTypes.SequenceEqual(trailer.TrailerTypes))
                {
                    forceUpdate = true;
                }
                trailer.TrailerTypes = info.TrailerTypes;
            }

            if (info.DateModified > item.DateModified)
            {
                item.DateModified = info.DateModified;
                forceUpdate = true;
            }

            if (!string.Equals(item.ExternalEtag ?? string.Empty, info.Etag ?? string.Empty, StringComparison.Ordinal))
            {
                item.ExternalEtag = info.Etag;
                forceUpdate = true;
            }

            item.ChannelId = internalChannelId.ToString("N");

            if (item.ParentId != internalChannelId)
            {
                forceUpdate = true;
            }
            item.ParentId = internalChannelId;

            if (!string.Equals(item.ExternalId, info.Id, StringComparison.OrdinalIgnoreCase))
            {
                forceUpdate = true;
            }
            item.ExternalId = info.Id;

            var channelAudioItem = item as Audio;
            if (channelAudioItem != null)
            {
                channelAudioItem.ExtraType = info.ExtraType;

                var mediaSource = info.MediaSources.FirstOrDefault();
                item.Path = mediaSource == null ? null : mediaSource.Path;
            }

            var channelVideoItem = item as Video;
            if (channelVideoItem != null)
            {
                channelVideoItem.ExtraType = info.ExtraType;

                var mediaSource = info.MediaSources.FirstOrDefault();
                item.Path = mediaSource == null ? null : mediaSource.Path;
            }

            if (!string.IsNullOrEmpty(info.ImageUrl) && !item.HasImage(ImageType.Primary))
            {
                item.SetImagePath(ImageType.Primary, info.ImageUrl);
            }

            item.OnMetadataChanged();

            var metadataRefreshMode = MetadataRefreshMode.Default;

            if (isNew)
            {
                _libraryManager.CreateItem(item, cancellationToken);

                if (info.People != null && info.People.Count > 0)
                {
                    _libraryManager.UpdatePeople(item, info.People);
                }
            }
            else if (forceUpdate)
            {
                item.UpdateToRepository(ItemUpdateType.None, cancellationToken);
                metadataRefreshMode = MetadataRefreshMode.FullRefresh;
            }

            if ((isNew || forceUpdate) && info.Type == ChannelItemType.Media)
            {
                if (info.EnableMediaProbe && !info.IsLiveStream && item.HasPathProtocol)
                {
                    SaveMediaSources(item, new List<MediaSourceInfo>());

                    _providerManager.QueueRefresh(item.Id, new MetadataRefreshOptions(_fileSystem)
                    {
                        MetadataRefreshMode = metadataRefreshMode

                    }, RefreshPriority.Normal);
                }
                else
                {
                    SaveMediaSources(item, info.MediaSources);
                }
            }

            return item;
        }

        private void RefreshIfNeeded(BaseItem[] programs)
        {
            foreach (var program in programs)
            {
                RefreshIfNeeded(program);
            }
        }

        private void RefreshIfNeeded(BaseItem program)
        {
            if (!_refreshedItems.ContainsKey(program.Id))
            {
                _refreshedItems.TryAdd(program.Id, true);
                _providerManager.QueueRefresh(program.Id, new MetadataRefreshOptions(_fileSystem), RefreshPriority.Low);
            }

        }

        internal IChannel GetChannelProvider(Channel channel)
        {
            if (channel == null)
            {
                throw new ArgumentNullException("channel");
            }

            var result = GetAllChannels()
                .FirstOrDefault(i => string.Equals(GetInternalChannelId(i.Name).ToString("N"), channel.ChannelId, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Name, channel.Name, StringComparison.OrdinalIgnoreCase));

            if (result == null)
            {
                throw new ResourceNotFoundException("No channel provider found for channel " + channel.Name);
            }

            return result;
        }

        internal IChannel GetChannelProvider(string internalChannelId)
        {
            if (internalChannelId == null)
            {
                throw new ArgumentNullException("internalChannelId");
            }

            var result = GetAllChannels()
                .FirstOrDefault(i => string.Equals(GetInternalChannelId(i.Name).ToString("N"), internalChannelId, StringComparison.OrdinalIgnoreCase));

            if (result == null)
            {
                throw new ResourceNotFoundException("No channel provider found for channel id " + internalChannelId);
            }

            return result;
        }

        private IEnumerable<BaseItem> ApplyFilters(IEnumerable<BaseItem> items, IEnumerable<ItemFilter> filters, User user)
        {
            foreach (var filter in filters.OrderByDescending(f => (int)f))
            {
                items = ApplyFilter(items, filter, user);
            }

            return items;
        }

        private IEnumerable<BaseItem> ApplyFilter(IEnumerable<BaseItem> items, ItemFilter filter, User user)
        {
            // Avoid implicitly captured closure
            var currentUser = user;

            switch (filter)
            {
                case ItemFilter.IsFavoriteOrLikes:
                    return items.Where(item =>
                    {
                        var userdata = _userDataManager.GetUserData(user, item);

                        if (userdata == null)
                        {
                            return false;
                        }

                        var likes = userdata.Likes ?? false;
                        var favorite = userdata.IsFavorite;

                        return likes || favorite;
                    });

                case ItemFilter.Likes:
                    return items.Where(item =>
                    {
                        var userdata = _userDataManager.GetUserData(user, item);

                        return userdata != null && userdata.Likes.HasValue && userdata.Likes.Value;
                    });

                case ItemFilter.Dislikes:
                    return items.Where(item =>
                    {
                        var userdata = _userDataManager.GetUserData(user, item);

                        return userdata != null && userdata.Likes.HasValue && !userdata.Likes.Value;
                    });

                case ItemFilter.IsFavorite:
                    return items.Where(item =>
                    {
                        var userdata = _userDataManager.GetUserData(user, item);

                        return userdata != null && userdata.IsFavorite;
                    });

                case ItemFilter.IsResumable:
                    return items.Where(item =>
                    {
                        var userdata = _userDataManager.GetUserData(user, item);

                        return userdata != null && userdata.PlaybackPositionTicks > 0;
                    });

                case ItemFilter.IsPlayed:
                    return items.Where(item => item.IsPlayed(currentUser));

                case ItemFilter.IsUnplayed:
                    return items.Where(item => item.IsUnplayed(currentUser));

                case ItemFilter.IsFolder:
                    return items.Where(item => item.IsFolder);

                case ItemFilter.IsNotFolder:
                    return items.Where(item => !item.IsFolder);
            }

            return items;
        }
    }

    public class ChannelsEntryPoint : IServerEntryPoint
    {
        private readonly IServerConfigurationManager _config;
        private readonly IChannelManager _channelManager;
        private readonly ITaskManager _taskManager;
        private readonly IFileSystem _fileSystem;

        public ChannelsEntryPoint(IChannelManager channelManager, ITaskManager taskManager, IServerConfigurationManager config, IFileSystem fileSystem)
        {
            _channelManager = channelManager;
            _taskManager = taskManager;
            _config = config;
            _fileSystem = fileSystem;
        }

        public void Run()
        {
            var channels = ((ChannelManager)_channelManager).Channels
                .Select(i => i.GetType().FullName.GetMD5().ToString("N"))
                .ToArray();

            var channelsString = string.Join(",", channels);

            if (!string.Equals(channelsString, GetSavedLastChannels(), StringComparison.OrdinalIgnoreCase))
            {
                _taskManager.QueueIfNotRunning<RefreshChannelsScheduledTask>();

                SetSavedLastChannels(channelsString);
            }
        }

        private string DataPath
        {
            get { return Path.Combine(_config.ApplicationPaths.DataPath, "channels.txt"); }
        }

        private string GetSavedLastChannels()
        {
            try
            {
                return _fileSystem.ReadAllText(DataPath);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void SetSavedLastChannels(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                {
                    _fileSystem.DeleteFile(DataPath);

                }
                else
                {
                    _fileSystem.WriteAllText(DataPath, value);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}