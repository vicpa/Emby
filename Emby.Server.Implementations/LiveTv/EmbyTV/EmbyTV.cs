﻿using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Security;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Model.IO;
using MediaBrowser.Common.Events;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Progress;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Threading;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.MediaInfo;

namespace Emby.Server.Implementations.LiveTv.EmbyTV
{
    public class EmbyTV : ILiveTvService, ISupportsDirectStreamProvider, ISupportsNewTimerIds, IDisposable
    {
        private readonly IServerApplicationHost _appHost;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _config;
        private readonly IJsonSerializer _jsonSerializer;

        private readonly ItemDataProvider<SeriesTimerInfo> _seriesTimerProvider;
        private readonly TimerManager _timerProvider;

        private readonly LiveTvManager _liveTvManager;
        private readonly IFileSystem _fileSystem;

        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IProcessFactory _processFactory;
        private readonly ISystemEvents _systemEvents;
        private readonly IAssemblyInfo _assemblyInfo;

        public static EmbyTV Current;

        public event EventHandler DataSourceChanged;
        public event EventHandler<RecordingStatusChangedEventArgs> RecordingStatusChanged;

        private readonly ConcurrentDictionary<string, ActiveRecordingInfo> _activeRecordings =
            new ConcurrentDictionary<string, ActiveRecordingInfo>(StringComparer.OrdinalIgnoreCase);

        public EmbyTV(IServerApplicationHost appHost, IAssemblyInfo assemblyInfo, ILogger logger, IJsonSerializer jsonSerializer, IPowerManagement powerManagement, IHttpClient httpClient, IServerConfigurationManager config, ILiveTvManager liveTvManager, IFileSystem fileSystem, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, IProviderManager providerManager, IMediaEncoder mediaEncoder, ITimerFactory timerFactory, IProcessFactory processFactory, ISystemEvents systemEvents)
        {
            Current = this;

            _appHost = appHost;
            _logger = logger;
            _httpClient = httpClient;
            _config = config;
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
            _libraryMonitor = libraryMonitor;
            _providerManager = providerManager;
            _mediaEncoder = mediaEncoder;
            _processFactory = processFactory;
            _systemEvents = systemEvents;
            _liveTvManager = (LiveTvManager)liveTvManager;
            _jsonSerializer = jsonSerializer;
            _assemblyInfo = assemblyInfo;

            _seriesTimerProvider = new SeriesTimerManager(fileSystem, jsonSerializer, _logger, Path.Combine(DataPath, "seriestimers"));
            _timerProvider = new TimerManager(fileSystem, jsonSerializer, _logger, Path.Combine(DataPath, "timers"), _logger, timerFactory, powerManagement);
            _timerProvider.TimerFired += _timerProvider_TimerFired;

            _config.NamedConfigurationUpdated += _config_NamedConfigurationUpdated;
        }

        private void _config_NamedConfigurationUpdated(object sender, ConfigurationUpdateEventArgs e)
        {
            if (string.Equals(e.Key, "livetv", StringComparison.OrdinalIgnoreCase))
            {
                OnRecordingFoldersChanged();
            }
        }

        public async void Start()
        {
            _timerProvider.RestartTimers();

            _systemEvents.Resume += _systemEvents_Resume;
            await CreateRecordingFolders().ConfigureAwait(false);
        }

        private void _systemEvents_Resume(object sender, EventArgs e)
        {
            _timerProvider.RestartTimers();
        }

        private async void OnRecordingFoldersChanged()
        {
            await CreateRecordingFolders().ConfigureAwait(false);
        }

        internal async Task CreateRecordingFolders()
        {
            try
            {
                var recordingFolders = GetRecordingFolders();

                var virtualFolders = _libraryManager.GetVirtualFolders()
                    .ToList();

                var allExistingPaths = virtualFolders.SelectMany(i => i.Locations).ToList();

                var pathsAdded = new List<string>();

                foreach (var recordingFolder in recordingFolders)
                {
                    var pathsToCreate = recordingFolder.Locations
                        .Where(i => !allExistingPaths.Any(p => _fileSystem.AreEqual(p, i)))
                        .ToList();

                    if (pathsToCreate.Count == 0)
                    {
                        continue;
                    }

                    var mediaPathInfos = pathsToCreate.Select(i => new MediaPathInfo { Path = i }).ToArray();

                    var libraryOptions = new LibraryOptions
                    {
                        PathInfos = mediaPathInfos
                    };
                    try
                    {
                        await _libraryManager.AddVirtualFolder(recordingFolder.Name, recordingFolder.CollectionType, libraryOptions, true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error creating virtual folder", ex);
                    }

                    pathsAdded.AddRange(pathsToCreate);
                }

                var config = GetConfiguration();

                var pathsToRemove = config.MediaLocationsCreated
                    .Except(recordingFolders.SelectMany(i => i.Locations))
                    .ToList();

                if (pathsAdded.Count > 0 || pathsToRemove.Count > 0)
                {
                    pathsAdded.InsertRange(0, config.MediaLocationsCreated);
                    config.MediaLocationsCreated = pathsAdded.Except(pathsToRemove).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    _config.SaveConfiguration("livetv", config);
                }

                foreach (var path in pathsToRemove)
                {
                    await RemovePathFromLibrary(path).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error creating recording folders", ex);
            }
        }

        private async Task RemovePathFromLibrary(string path)
        {
            _logger.Debug("Removing path from library: {0}", path);

            var requiresRefresh = false;
            var virtualFolders = _libraryManager.GetVirtualFolders()
               .ToList();

            foreach (var virtualFolder in virtualFolders)
            {
                if (!virtualFolder.Locations.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (virtualFolder.Locations.Length == 1)
                {
                    // remove entire virtual folder
                    try
                    {
                        await _libraryManager.RemoveVirtualFolder(virtualFolder.Name, true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error removing virtual folder", ex);
                    }
                }
                else
                {
                    try
                    {
                        _libraryManager.RemoveMediaPath(virtualFolder.Name, path);
                        requiresRefresh = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error removing media path", ex);
                    }
                }
            }

            if (requiresRefresh)
            {
                _libraryManager.ValidateMediaLibrary(new SimpleProgress<Double>(), CancellationToken.None);
            }
        }

        public string Name
        {
            get { return "Emby"; }
        }

        public string DataPath
        {
            get { return Path.Combine(_config.CommonApplicationPaths.DataPath, "livetv"); }
        }

        private string DefaultRecordingPath
        {
            get
            {
                return Path.Combine(DataPath, "recordings");
            }
        }

        private string RecordingPath
        {
            get
            {
                var path = GetConfiguration().RecordingPath;

                return string.IsNullOrWhiteSpace(path)
                    ? DefaultRecordingPath
                    : path;
            }
        }

        public string HomePageUrl
        {
            get { return "https://emby.media"; }
        }

        public async Task RefreshSeriesTimers(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var seriesTimers = await GetSeriesTimersAsync(cancellationToken).ConfigureAwait(false);

            foreach (var timer in seriesTimers)
            {
                await UpdateTimersForSeriesTimer(timer, false, true).ConfigureAwait(false);
            }
        }

        public async Task RefreshTimers(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var timers = await GetTimersAsync(cancellationToken).ConfigureAwait(false);

            var tempChannelCache = new Dictionary<string, LiveTvChannel>();

            foreach (var timer in timers)
            {
                if (DateTime.UtcNow > timer.EndDate && !_activeRecordings.ContainsKey(timer.Id))
                {
                    OnTimerOutOfDate(timer);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(timer.ProgramId) || string.IsNullOrWhiteSpace(timer.ChannelId))
                {
                    continue;
                }

                var program = GetProgramInfoFromCache(timer);
                if (program == null)
                {
                    OnTimerOutOfDate(timer);
                    continue;
                }

                CopyProgramInfoToTimerInfo(program, timer, tempChannelCache);
                _timerProvider.Update(timer);
            }
        }

        private void OnTimerOutOfDate(TimerInfo timer)
        {
            _timerProvider.Delete(timer);
        }

        private async Task<IEnumerable<ChannelInfo>> GetChannelsAsync(bool enableCache, CancellationToken cancellationToken)
        {
            var list = new List<ChannelInfo>();

            foreach (var hostInstance in _liveTvManager.TunerHosts)
            {
                try
                {
                    var channels = await hostInstance.GetChannels(enableCache, cancellationToken).ConfigureAwait(false);

                    list.AddRange(channels);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error getting channels", ex);
                }
            }

            foreach (var provider in GetListingProviders())
            {
                var enabledChannels = list
                    .Where(i => IsListingProviderEnabledForTuner(provider.Item2, i.TunerHostId))
                    .ToList();

                if (enabledChannels.Count > 0)
                {
                    try
                    {
                        await AddMetadata(provider.Item1, provider.Item2, enabledChannels, enableCache, cancellationToken).ConfigureAwait(false);
                    }
                    catch (NotSupportedException)
                    {

                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error adding metadata", ex);
                    }
                }
            }

            return list;
        }

        private async Task AddMetadata(IListingsProvider provider, ListingsProviderInfo info, List<ChannelInfo> tunerChannels, bool enableCache, CancellationToken cancellationToken)
        {
            var epgChannels = await GetEpgChannels(provider, info, enableCache, cancellationToken).ConfigureAwait(false);

            foreach (var tunerChannel in tunerChannels)
            {
                var epgChannel = GetEpgChannelFromTunerChannel(info, tunerChannel, epgChannels);

                if (epgChannel != null)
                {
                    if (!string.IsNullOrWhiteSpace(epgChannel.Name))
                    {
                        //tunerChannel.Name = epgChannel.Name;
                    }
                    if (!string.IsNullOrWhiteSpace(epgChannel.ImageUrl))
                    {
                        tunerChannel.ImageUrl = epgChannel.ImageUrl;
                    }
                }
            }
        }

        private readonly ConcurrentDictionary<string, List<ChannelInfo>> _epgChannels =
            new ConcurrentDictionary<string, List<ChannelInfo>>(StringComparer.OrdinalIgnoreCase);

        private async Task<List<ChannelInfo>> GetEpgChannels(IListingsProvider provider, ListingsProviderInfo info, bool enableCache, CancellationToken cancellationToken)
        {
            List<ChannelInfo> result;
            if (!enableCache || !_epgChannels.TryGetValue(info.Id, out result))
            {
                result = await provider.GetChannels(info, cancellationToken).ConfigureAwait(false);

                foreach (var channel in result)
                {
                    _logger.Info("Found epg channel in {0} {1} {2} {3}", provider.Name, info.ListingsId, channel.Name, channel.Id);
                }

                _epgChannels.AddOrUpdate(info.Id, result, (k, v) => result);
            }

            return result;
        }

        private async Task<ChannelInfo> GetEpgChannelFromTunerChannel(IListingsProvider provider, ListingsProviderInfo info, ChannelInfo tunerChannel, CancellationToken cancellationToken)
        {
            var epgChannels = await GetEpgChannels(provider, info, true, cancellationToken).ConfigureAwait(false);

            return GetEpgChannelFromTunerChannel(info, tunerChannel, epgChannels);
        }

        private string GetMappedChannel(string channelId, NameValuePair[] mappings)
        {
            foreach (NameValuePair mapping in mappings)
            {
                if (StringHelper.EqualsIgnoreCase(mapping.Name, channelId))
                {
                    return mapping.Value;
                }
            }
            return channelId;
        }

        private ChannelInfo GetEpgChannelFromTunerChannel(ListingsProviderInfo info, ChannelInfo tunerChannel, List<ChannelInfo> epgChannels)
        {
            return GetEpgChannelFromTunerChannel(info.ChannelMappings, tunerChannel, epgChannels);
        }

        public ChannelInfo GetEpgChannelFromTunerChannel(NameValuePair[] mappings, ChannelInfo tunerChannel, List<ChannelInfo> epgChannels)
        {
            if (!string.IsNullOrWhiteSpace(tunerChannel.Id))
            {
                var mappedTunerChannelId = GetMappedChannel(tunerChannel.Id, mappings);

                if (string.IsNullOrWhiteSpace(mappedTunerChannelId))
                {
                    mappedTunerChannelId = tunerChannel.Id;
                }

                var channel = epgChannels.FirstOrDefault(i => string.Equals(mappedTunerChannelId, i.Id, StringComparison.OrdinalIgnoreCase));

                if (channel != null)
                {
                    return channel;
                }
            }

            if (!string.IsNullOrWhiteSpace(tunerChannel.TunerChannelId))
            {
                var tunerChannelId = tunerChannel.TunerChannelId;
                if (tunerChannelId.IndexOf(".json.schedulesdirect.org", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    tunerChannelId = tunerChannelId.Replace(".json.schedulesdirect.org", string.Empty, StringComparison.OrdinalIgnoreCase).TrimStart('I');
                }

                var mappedTunerChannelId = GetMappedChannel(tunerChannelId, mappings);

                if (string.IsNullOrWhiteSpace(mappedTunerChannelId))
                {
                    mappedTunerChannelId = tunerChannelId;
                }

                var channel = epgChannels.FirstOrDefault(i => string.Equals(mappedTunerChannelId, i.Id, StringComparison.OrdinalIgnoreCase));

                if (channel != null)
                {
                    return channel;
                }
            }

            if (!string.IsNullOrWhiteSpace(tunerChannel.Number))
            {
                var tunerChannelNumber = GetMappedChannel(tunerChannel.Number, mappings);

                if (string.IsNullOrWhiteSpace(tunerChannelNumber))
                {
                    tunerChannelNumber = tunerChannel.Number;
                }

                var channel = epgChannels.FirstOrDefault(i => string.Equals(tunerChannelNumber, i.Number, StringComparison.OrdinalIgnoreCase));

                if (channel != null)
                {
                    return channel;
                }
            }

            if (!string.IsNullOrWhiteSpace(tunerChannel.Name))
            {
                var normalizedName = NormalizeName(tunerChannel.Name);

                var channel = epgChannels.FirstOrDefault(i => string.Equals(normalizedName, NormalizeName(i.Name ?? string.Empty), StringComparison.OrdinalIgnoreCase));

                if (channel != null)
                {
                    return channel;
                }
            }

            return null;
        }

        private string NormalizeName(string value)
        {
            return value.Replace(" ", string.Empty).Replace("-", string.Empty);
        }

        public async Task<List<ChannelInfo>> GetChannelsForListingsProvider(ListingsProviderInfo listingsProvider, CancellationToken cancellationToken)
        {
            var list = new List<ChannelInfo>();

            foreach (var hostInstance in _liveTvManager.TunerHosts)
            {
                try
                {
                    var channels = await hostInstance.GetChannels(false, cancellationToken).ConfigureAwait(false);

                    list.AddRange(channels);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error getting channels", ex);
                }
            }

            return list
                .Where(i => IsListingProviderEnabledForTuner(listingsProvider, i.TunerHostId))
                .ToList();
        }

        public Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            return GetChannelsAsync(false, cancellationToken);
        }

        public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            var timers = _timerProvider
                .GetAll()
                .Where(i => string.Equals(i.SeriesTimerId, timerId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var timer in timers)
            {
                CancelTimerInternal(timer.Id, true, true);
            }

            var remove = _seriesTimerProvider.GetAll().FirstOrDefault(r => string.Equals(r.Id, timerId, StringComparison.OrdinalIgnoreCase));
            if (remove != null)
            {
                _seriesTimerProvider.Delete(remove);
            }
            return Task.FromResult(true);
        }

        private void CancelTimerInternal(string timerId, bool isSeriesCancelled, bool isManualCancellation)
        {
            var timer = _timerProvider.GetTimer(timerId);
            if (timer != null)
            {
                timer.Status = RecordingStatus.Cancelled;

                if (isManualCancellation)
                {
                    timer.IsManual = true;
                }

                if (string.IsNullOrWhiteSpace(timer.SeriesTimerId) || isSeriesCancelled)
                {
                    _timerProvider.Delete(timer);
                }
                else
                {
                    _timerProvider.AddOrUpdate(timer, false);
                }
            }
            ActiveRecordingInfo activeRecordingInfo;

            if (_activeRecordings.TryGetValue(timerId, out activeRecordingInfo))
            {
                activeRecordingInfo.Timer = timer;
                activeRecordingInfo.CancellationTokenSource.Cancel();
            }
        }

        public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            CancelTimerInternal(timerId, false, true);
            return Task.FromResult(true);
        }

        public Task DeleteRecordingAsync(string recordingId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<string> CreateTimer(TimerInfo timer, CancellationToken cancellationToken)
        {
            var existingTimer = string.IsNullOrWhiteSpace(timer.ProgramId) ?
                null :
                _timerProvider.GetTimerByProgramId(timer.ProgramId);

            if (existingTimer != null)
            {
                if (existingTimer.Status == RecordingStatus.Cancelled ||
                    existingTimer.Status == RecordingStatus.Completed)
                {
                    existingTimer.Status = RecordingStatus.New;
                    existingTimer.IsManual = true;
                    _timerProvider.Update(existingTimer);
                    return Task.FromResult(existingTimer.Id);
                }
                else
                {
                    throw new ArgumentException("A scheduled recording already exists for this program.");
                }
            }

            timer.Id = Guid.NewGuid().ToString("N");

            LiveTvProgram programInfo = null;

            if (!string.IsNullOrWhiteSpace(timer.ProgramId))
            {
                programInfo = GetProgramInfoFromCache(timer);
            }
            if (programInfo == null)
            {
                _logger.Info("Unable to find program with Id {0}. Will search using start date", timer.ProgramId);
                programInfo = GetProgramInfoFromCache(timer.ChannelId, timer.StartDate);
            }

            if (programInfo != null)
            {
                CopyProgramInfoToTimerInfo(programInfo, timer);
            }

            timer.IsManual = true;
            _timerProvider.Add(timer);
            return Task.FromResult(timer.Id);
        }

        public async Task<string> CreateSeriesTimer(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            info.Id = Guid.NewGuid().ToString("N");

            // populate info.seriesID
            var program = GetProgramInfoFromCache(info.ProgramId);

            if (program != null)
            {
                info.SeriesId = program.ExternalSeriesId;
            }
            else
            {
                throw new InvalidOperationException("SeriesId for program not found");
            }

            // If any timers have already been manually created, make sure they don't get cancelled
            var existingTimers = (await GetTimersAsync(CancellationToken.None).ConfigureAwait(false))
                .Where(i =>
                {
                    if (string.Equals(i.ProgramId, info.ProgramId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(info.ProgramId))
                    {
                        return true;
                    }

                    if (string.Equals(i.SeriesId, info.SeriesId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(info.SeriesId))
                    {
                        return true;
                    }

                    return false;
                })
                .ToList();

            _seriesTimerProvider.Add(info);

            foreach (var timer in existingTimers)
            {
                timer.SeriesTimerId = info.Id;
                timer.IsManual = true;

                _timerProvider.AddOrUpdate(timer, false);
            }

            await UpdateTimersForSeriesTimer(info, true, false).ConfigureAwait(false);

            return info.Id;
        }

        public async Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            var instance = _seriesTimerProvider.GetAll().FirstOrDefault(i => string.Equals(i.Id, info.Id, StringComparison.OrdinalIgnoreCase));

            if (instance != null)
            {
                instance.ChannelId = info.ChannelId;
                instance.Days = info.Days;
                instance.EndDate = info.EndDate;
                instance.IsPostPaddingRequired = info.IsPostPaddingRequired;
                instance.IsPrePaddingRequired = info.IsPrePaddingRequired;
                instance.PostPaddingSeconds = info.PostPaddingSeconds;
                instance.PrePaddingSeconds = info.PrePaddingSeconds;
                instance.Priority = info.Priority;
                instance.RecordAnyChannel = info.RecordAnyChannel;
                instance.RecordAnyTime = info.RecordAnyTime;
                instance.RecordNewOnly = info.RecordNewOnly;
                instance.SkipEpisodesInLibrary = info.SkipEpisodesInLibrary;
                instance.KeepUpTo = info.KeepUpTo;
                instance.KeepUntil = info.KeepUntil;
                instance.StartDate = info.StartDate;

                _seriesTimerProvider.Update(instance);

                await UpdateTimersForSeriesTimer(instance, true, true).ConfigureAwait(false);
            }
        }

        public Task UpdateTimerAsync(TimerInfo updatedTimer, CancellationToken cancellationToken)
        {
            var existingTimer = _timerProvider.GetTimer(updatedTimer.Id);

            if (existingTimer == null)
            {
                throw new ResourceNotFoundException();
            }

            // Only update if not currently active
            ActiveRecordingInfo activeRecordingInfo;
            if (!_activeRecordings.TryGetValue(updatedTimer.Id, out activeRecordingInfo))
            {
                existingTimer.PrePaddingSeconds = updatedTimer.PrePaddingSeconds;
                existingTimer.PostPaddingSeconds = updatedTimer.PostPaddingSeconds;
                existingTimer.IsPostPaddingRequired = updatedTimer.IsPostPaddingRequired;
                existingTimer.IsPrePaddingRequired = updatedTimer.IsPrePaddingRequired;

                _timerProvider.Update(existingTimer);
            }

            return Task.FromResult(true);
        }

        private void UpdateExistingTimerWithNewMetadata(TimerInfo existingTimer, TimerInfo updatedTimer)
        {
            // Update the program info but retain the status
            existingTimer.ChannelId = updatedTimer.ChannelId;
            existingTimer.CommunityRating = updatedTimer.CommunityRating;
            existingTimer.EndDate = updatedTimer.EndDate;
            existingTimer.EpisodeNumber = updatedTimer.EpisodeNumber;
            existingTimer.EpisodeTitle = updatedTimer.EpisodeTitle;
            existingTimer.Genres = updatedTimer.Genres;
            existingTimer.HomePageUrl = updatedTimer.HomePageUrl;
            existingTimer.IsKids = updatedTimer.IsKids;
            existingTimer.IsNews = updatedTimer.IsNews;
            existingTimer.IsMovie = updatedTimer.IsMovie;
            existingTimer.IsSeries = updatedTimer.IsSeries;
            existingTimer.Tags = updatedTimer.Tags;
            existingTimer.IsProgramSeries = updatedTimer.IsProgramSeries;
            existingTimer.IsRepeat = updatedTimer.IsRepeat;
            existingTimer.IsSports = updatedTimer.IsSports;
            existingTimer.Name = updatedTimer.Name;
            existingTimer.OfficialRating = updatedTimer.OfficialRating;
            existingTimer.OriginalAirDate = updatedTimer.OriginalAirDate;
            existingTimer.Overview = updatedTimer.Overview;
            existingTimer.ProductionYear = updatedTimer.ProductionYear;
            existingTimer.ProgramId = updatedTimer.ProgramId;
            existingTimer.SeasonNumber = updatedTimer.SeasonNumber;
            existingTimer.StartDate = updatedTimer.StartDate;
            existingTimer.ShowId = updatedTimer.ShowId;
            existingTimer.ProviderIds = updatedTimer.ProviderIds;
            existingTimer.SeriesProviderIds = updatedTimer.SeriesProviderIds;
        }

        public string GetActiveRecordingPath(string id)
        {
            ActiveRecordingInfo info;

            if (_activeRecordings.TryGetValue(id, out info))
            {
                return info.Path;
            }
            return null;
        }

        public IEnumerable<ActiveRecordingInfo> GetAllActiveRecordings()
        {
            return _activeRecordings.Values.Where(i => i.Timer.Status == RecordingStatus.InProgress && !i.CancellationTokenSource.IsCancellationRequested);
        }

        public ActiveRecordingInfo GetActiveRecordingInfo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            foreach (var recording in _activeRecordings.Values)
            {
                if (string.Equals(recording.Path, path, StringComparison.Ordinal) && !recording.CancellationTokenSource.IsCancellationRequested)
                {
                    var timer = recording.Timer;
                    if (timer.Status != RecordingStatus.InProgress)
                    {
                        return null;
                    }
                    return recording;
                }
            }
            return null;
        }

        public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        {
            var excludeStatues = new List<RecordingStatus>
            {
                RecordingStatus.Completed
            };

            var timers = _timerProvider.GetAll()
                .Where(i => !excludeStatues.Contains(i.Status));

            return Task.FromResult(timers);
        }

        public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo program = null)
        {
            var config = GetConfiguration();

            var defaults = new SeriesTimerInfo()
            {
                PostPaddingSeconds = Math.Max(config.PostPaddingSeconds, 0),
                PrePaddingSeconds = Math.Max(config.PrePaddingSeconds, 0),
                RecordAnyChannel = false,
                RecordAnyTime = true,
                RecordNewOnly = true,

                Days = new List<DayOfWeek>
                {
                    DayOfWeek.Sunday,
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday
                }
            };

            if (program != null)
            {
                defaults.SeriesId = program.SeriesId;
                defaults.ProgramId = program.Id;
                defaults.RecordNewOnly = !program.IsRepeat;
                defaults.Name = program.Name;
            }

            defaults.SkipEpisodesInLibrary = defaults.RecordNewOnly;
            defaults.KeepUntil = KeepUntil.UntilDeleted;

            return Task.FromResult(defaults);
        }

        public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult((IEnumerable<SeriesTimerInfo>)_seriesTimerProvider.GetAll());
        }

        private bool IsListingProviderEnabledForTuner(ListingsProviderInfo info, string tunerHostId)
        {
            if (info.EnableAllTuners)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(tunerHostId))
            {
                throw new ArgumentNullException("tunerHostId");
            }

            return info.EnabledTuners.Contains(tunerHostId, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
        {
            var channels = await GetChannelsAsync(true, cancellationToken).ConfigureAwait(false);
            var channel = channels.First(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase));

            foreach (var provider in GetListingProviders())
            {
                if (!IsListingProviderEnabledForTuner(provider.Item2, channel.TunerHostId))
                {
                    _logger.Debug("Skipping getting programs for channel {0}-{1} from {2}-{3}, because it's not enabled for this tuner.", channel.Number, channel.Name, provider.Item1.Name, provider.Item2.ListingsId ?? string.Empty);
                    continue;
                }

                _logger.Debug("Getting programs for channel {0}-{1} from {2}-{3}", channel.Number, channel.Name, provider.Item1.Name, provider.Item2.ListingsId ?? string.Empty);

                var epgChannel = await GetEpgChannelFromTunerChannel(provider.Item1, provider.Item2, channel, cancellationToken).ConfigureAwait(false);

                List<ProgramInfo> programs;

                if (epgChannel == null)
                {
                    _logger.Debug("EPG channel not found for tuner channel {0}-{1} from {2}-{3}", channel.Number, channel.Name, provider.Item1.Name, provider.Item2.ListingsId ?? string.Empty);
                    programs = new List<ProgramInfo>();
                }
                else
                {
                    programs = (await provider.Item1.GetProgramsAsync(provider.Item2, epgChannel.Id, startDateUtc, endDateUtc, cancellationToken)
                           .ConfigureAwait(false)).ToList();
                }

                // Replace the value that came from the provider with a normalized value
                foreach (var program in programs)
                {
                    program.ChannelId = channelId;

                    if (provider.Item2.EnableNewProgramIds)
                    {
                        program.Id += "_" + channelId;
                    }
                }

                if (programs.Count > 0)
                {
                    return programs;
                }
            }

            return new List<ProgramInfo>();
        }

        private List<Tuple<IListingsProvider, ListingsProviderInfo>> GetListingProviders()
        {
            return GetConfiguration().ListingProviders
                .Select(i =>
                {
                    var provider = _liveTvManager.ListingProviders.FirstOrDefault(l => string.Equals(l.Type, i.Type, StringComparison.OrdinalIgnoreCase));

                    return provider == null ? null : new Tuple<IListingsProvider, ListingsProviderInfo>(provider, i);
                })
                .Where(i => i != null)
                .ToList();
        }

        public Task<MediaSourceInfo> GetRecordingStream(string recordingId, string streamId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private readonly SemaphoreSlim _liveStreamsSemaphore = new SemaphoreSlim(1, 1);
        private readonly List<ILiveStream> _liveStreams = new List<ILiveStream>();

        public async Task<MediaSourceInfo> GetChannelStream(string channelId, string streamId, CancellationToken cancellationToken)
        {
            var result = await GetChannelStreamWithDirectStreamProvider(channelId, streamId, cancellationToken).ConfigureAwait(false);

            return result.Item1;
        }

        public async Task<Tuple<MediaSourceInfo, ILiveStream>> GetChannelStreamWithDirectStreamProvider(string channelId, string streamId, CancellationToken cancellationToken)
        {
            var result = await GetChannelStreamInternal(channelId, streamId, cancellationToken).ConfigureAwait(false);

            return new Tuple<MediaSourceInfo, ILiveStream>(result.Item2, result.Item1);
        }

        private MediaSourceInfo CloneMediaSource(MediaSourceInfo mediaSource, bool enableStreamSharing)
        {
            var json = _jsonSerializer.SerializeToString(mediaSource);
            mediaSource = _jsonSerializer.DeserializeFromString<MediaSourceInfo>(json);

            mediaSource.Id = Guid.NewGuid().ToString("N") + "_" + mediaSource.Id;

            //if (mediaSource.DateLiveStreamOpened.HasValue && enableStreamSharing)
            //{
            //    var ticks = (DateTime.UtcNow - mediaSource.DateLiveStreamOpened.Value).Ticks - TimeSpan.FromSeconds(10).Ticks;
            //    ticks = Math.Max(0, ticks);
            //    mediaSource.Path += "?t=" + ticks.ToString(CultureInfo.InvariantCulture) + "&s=" + mediaSource.DateLiveStreamOpened.Value.Ticks.ToString(CultureInfo.InvariantCulture);
            //}

            return mediaSource;
        }

        public async Task<ILiveStream> GetLiveStream(string uniqueId)
        {
            await _liveStreamsSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                return _liveStreams
                    .FirstOrDefault(i => string.Equals(i.UniqueId, uniqueId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _liveStreamsSemaphore.Release();
            }
        }

        public Task<List<ILiveStream>> GetLiveStreams(TunerHostInfo host, CancellationToken cancellationToken)
        {
            //await _liveStreamsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            //try
            //{
            var hostId = host.Id;

            return Task.FromResult(_liveStreams
                .Where(i => string.Equals(i.TunerHostId, hostId, StringComparison.OrdinalIgnoreCase))
                .ToList());
            //}
            //finally
            //{
            //    _liveStreamsSemaphore.Release();
            //}
        }

        private async Task<Tuple<ILiveStream, MediaSourceInfo>> GetChannelStreamInternal(string channelId, string streamId, CancellationToken cancellationToken)
        {
            await _liveStreamsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            _logger.Info("Streaming Channel " + channelId);

            try
            {
                var result = _liveStreams.FirstOrDefault(i => string.Equals(i.OriginalStreamId, streamId, StringComparison.OrdinalIgnoreCase));

                if (result != null && result.EnableStreamSharing)
                {
                    var openedMediaSource = CloneMediaSource(result.OpenedMediaSource, result.EnableStreamSharing);
                    result.SharedStreamIds.Add(openedMediaSource.Id);

                    _logger.Info("Live stream {0} consumer count is now {1}", streamId, result.ConsumerCount);

                    return new Tuple<ILiveStream, MediaSourceInfo>(result, openedMediaSource);
                }

                foreach (var hostInstance in _liveTvManager.TunerHosts)
                {
                    try
                    {
                        result = await hostInstance.GetChannelStream(channelId, streamId, cancellationToken).ConfigureAwait(false);

                        var openedMediaSource = CloneMediaSource(result.OpenedMediaSource, result.EnableStreamSharing);

                        result.SharedStreamIds.Add(openedMediaSource.Id);
                        _liveStreams.Add(result);

                        result.OriginalStreamId = streamId;

                        _logger.Info("Returning mediasource streamId {0}, mediaSource.Id {1}, mediaSource.LiveStreamId {2}",
                            streamId, openedMediaSource.Id, openedMediaSource.LiveStreamId);

                        return new Tuple<ILiveStream, MediaSourceInfo>(result, openedMediaSource);
                    }
                    catch (FileNotFoundException)
                    {
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
            finally
            {
                _liveStreamsSemaphore.Release();
            }

            throw new Exception("Tuner not found.");
        }

        public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentNullException("channelId");
            }

            foreach (var hostInstance in _liveTvManager.TunerHosts)
            {
                try
                {
                    var sources = await hostInstance.GetChannelStreamMediaSources(channelId, cancellationToken).ConfigureAwait(false);

                    if (sources.Count > 0)
                    {
                        return sources;
                    }
                }
                catch (NotImplementedException)
                {

                }
            }

            throw new NotImplementedException();
        }

        public Task<List<MediaSourceInfo>> GetRecordingStreamMediaSources(string recordingId, CancellationToken cancellationToken)
        {
            ActiveRecordingInfo info;

            recordingId = recordingId.Replace("recording", string.Empty);

            if (_activeRecordings.TryGetValue(recordingId, out info))
            {
                return GetRecordingStreamMediaSources(info, cancellationToken);
            }

            throw new FileNotFoundException();
        }

        public async Task<List<MediaSourceInfo>> GetRecordingStreamMediaSources(ActiveRecordingInfo info, CancellationToken cancellationToken)
        {
            var stream = new MediaSourceInfo
            {
                EncoderPath = _appHost.GetLocalApiUrl("127.0.0.1") + "/LiveTv/LiveRecordings/" + info.Id + "/stream",
                EncoderProtocol = MediaBrowser.Model.MediaInfo.MediaProtocol.Http,
                Path = info.Path,
                Protocol = MediaBrowser.Model.MediaInfo.MediaProtocol.File,
                Id = info.Id,
                SupportsDirectPlay = false,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
                IsInfiniteStream = true,
                RequiresOpening = false,
                RequiresClosing = false,
                BufferMs = 0,
                IgnoreDts = true,
                IgnoreIndex = true
            };

            var isAudio = false;
            await new LiveStreamHelper(_mediaEncoder, _logger, _jsonSerializer, _config.CommonApplicationPaths).AddMediaInfoWithProbe(stream, isAudio, false, cancellationToken).ConfigureAwait(false);

            return new List<MediaSourceInfo>
            {
                stream
            };
        }

        public async Task CloseLiveStream(string id, CancellationToken cancellationToken)
        {
            // Ignore the consumer id
            //id = id.Substring(id.IndexOf('_') + 1);

            await _liveStreamsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var stream = _liveStreams.FirstOrDefault(i => i.SharedStreamIds.Contains(id));
                if (stream != null)
                {
                    stream.SharedStreamIds.Remove(id);

                    _logger.Info("Live stream {0} consumer count is now {1}", id, stream.ConsumerCount);

                    if (stream.ConsumerCount <= 0)
                    {
                        _liveStreams.Remove(stream);

                        _logger.Info("Closing live stream {0}", id);

                        stream.Close();
                        _logger.Info("Live stream {0} closed successfully", id);
                    }
                }
                else
                {
                    _logger.Warn("Live stream not found: {0}, unable to close", id);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error closing live stream", ex);
            }
            finally
            {
                _liveStreamsSemaphore.Release();
            }
        }

        public Task RecordLiveStream(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task ResetTuner(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        async void _timerProvider_TimerFired(object sender, GenericEventArgs<TimerInfo> e)
        {
            var timer = e.Argument;

            _logger.Info("Recording timer fired.");

            try
            {
                var recordingEndDate = timer.EndDate.AddSeconds(timer.PostPaddingSeconds);

                if (recordingEndDate <= DateTime.UtcNow)
                {
                    _logger.Warn("Recording timer fired for updatedTimer {0}, Id: {1}, but the program has already ended.", timer.Name, timer.Id);
                    OnTimerOutOfDate(timer);
                    return;
                }

                var registration = await _liveTvManager.GetRegistrationInfo("dvr").ConfigureAwait(false);
                if (!registration.IsValid)
                {
                    _logger.Warn("Emby Premiere required to use Emby DVR.");
                    OnTimerOutOfDate(timer);
                    return;
                }

                var activeRecordingInfo = new ActiveRecordingInfo
                {
                    CancellationTokenSource = new CancellationTokenSource(),
                    Timer = timer,
                    Id = timer.Id
                };

                if (!_activeRecordings.ContainsKey(timer.Id))
                {
                    await RecordStream(timer, recordingEndDate, activeRecordingInfo).ConfigureAwait(false);
                }
                else
                {
                    _logger.Info("Skipping RecordStream because it's already in progress.");
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error recording stream", ex);
            }
        }

        private string GetRecordingPath(TimerInfo timer, RemoteSearchResult metadata, out string seriesPath)
        {
            var recordPath = RecordingPath;
            var config = GetConfiguration();
            seriesPath = null;

            if (timer.IsProgramSeries)
            {
                var customRecordingPath = config.SeriesRecordingPath;
                var allowSubfolder = true;
                if (!string.IsNullOrWhiteSpace(customRecordingPath))
                {
                    allowSubfolder = string.Equals(customRecordingPath, recordPath, StringComparison.OrdinalIgnoreCase);
                    recordPath = customRecordingPath;
                }

                if (allowSubfolder && config.EnableRecordingSubfolders)
                {
                    recordPath = Path.Combine(recordPath, "Series");
                }

                // trim trailing period from the folder name
                var folderName = _fileSystem.GetValidFilename(timer.Name).Trim().TrimEnd('.').Trim();

                if (metadata != null && metadata.ProductionYear.HasValue)
                {
                    folderName += " (" + metadata.ProductionYear.Value.ToString(CultureInfo.InvariantCulture) + ")";
                }

                // Can't use the year here in the folder name because it is the year of the episode, not the series.
                recordPath = Path.Combine(recordPath, folderName);

                seriesPath = recordPath;

                if (timer.SeasonNumber.HasValue)
                {
                    folderName = string.Format("Season {0}", timer.SeasonNumber.Value.ToString(CultureInfo.InvariantCulture));
                    recordPath = Path.Combine(recordPath, folderName);
                }
            }
            else if (timer.IsMovie)
            {
                var customRecordingPath = config.MovieRecordingPath;
                var allowSubfolder = true;
                if (!string.IsNullOrWhiteSpace(customRecordingPath))
                {
                    allowSubfolder = string.Equals(customRecordingPath, recordPath, StringComparison.OrdinalIgnoreCase);
                    recordPath = customRecordingPath;
                }

                if (allowSubfolder && config.EnableRecordingSubfolders)
                {
                    recordPath = Path.Combine(recordPath, "Movies");
                }

                var folderName = _fileSystem.GetValidFilename(timer.Name).Trim();
                if (timer.ProductionYear.HasValue)
                {
                    folderName += " (" + timer.ProductionYear.Value.ToString(CultureInfo.InvariantCulture) + ")";
                }

                // trim trailing period from the folder name
                folderName = folderName.TrimEnd('.').Trim();

                recordPath = Path.Combine(recordPath, folderName);
            }
            else if (timer.IsKids)
            {
                if (config.EnableRecordingSubfolders)
                {
                    recordPath = Path.Combine(recordPath, "Kids");
                }

                var folderName = _fileSystem.GetValidFilename(timer.Name).Trim();
                if (timer.ProductionYear.HasValue)
                {
                    folderName += " (" + timer.ProductionYear.Value.ToString(CultureInfo.InvariantCulture) + ")";
                }

                // trim trailing period from the folder name
                folderName = folderName.TrimEnd('.').Trim();

                recordPath = Path.Combine(recordPath, folderName);
            }
            else if (timer.IsSports)
            {
                if (config.EnableRecordingSubfolders)
                {
                    recordPath = Path.Combine(recordPath, "Sports");
                }
                recordPath = Path.Combine(recordPath, _fileSystem.GetValidFilename(timer.Name).Trim());
            }
            else
            {
                if (config.EnableRecordingSubfolders)
                {
                    recordPath = Path.Combine(recordPath, "Other");
                }
                recordPath = Path.Combine(recordPath, _fileSystem.GetValidFilename(timer.Name).Trim());
            }

            var recordingFileName = _fileSystem.GetValidFilename(RecordingHelper.GetRecordingName(timer)).Trim() + ".ts";

            return Path.Combine(recordPath, recordingFileName);
        }

        private async Task RecordStream(TimerInfo timer, DateTime recordingEndDate, ActiveRecordingInfo activeRecordingInfo)
        {
            if (timer == null)
            {
                throw new ArgumentNullException("timer");
            }

            LiveTvProgram programInfo = null;

            if (!string.IsNullOrWhiteSpace(timer.ProgramId))
            {
                programInfo = GetProgramInfoFromCache(timer);
            }
            if (programInfo == null)
            {
                _logger.Info("Unable to find program with Id {0}. Will search using start date", timer.ProgramId);
                programInfo = GetProgramInfoFromCache(timer.ChannelId, timer.StartDate);
            }

            if (programInfo != null)
            {
                CopyProgramInfoToTimerInfo(programInfo, timer);
            }

            string seriesPath = null;
            var remoteMetadata = await FetchInternetMetadata(timer, CancellationToken.None).ConfigureAwait(false);
            var recordPath = GetRecordingPath(timer, remoteMetadata, out seriesPath);
            var recordingStatus = RecordingStatus.New;

            string liveStreamId = null;

            try
            {
                var allMediaSources = await GetChannelStreamMediaSources(timer.ChannelId, CancellationToken.None).ConfigureAwait(false);

                _logger.Info("Opening recording stream from tuner provider");
                var liveStreamInfo = await GetChannelStreamInternal(timer.ChannelId, allMediaSources[0].Id, CancellationToken.None)
                            .ConfigureAwait(false);

                var mediaStreamInfo = liveStreamInfo.Item2;
                liveStreamId = mediaStreamInfo.Id;

                var recorder = GetRecorder(liveStreamInfo.Item1, mediaStreamInfo);

                recordPath = recorder.GetOutputPath(mediaStreamInfo, recordPath);
                recordPath = EnsureFileUnique(recordPath, timer.Id);

                _libraryMonitor.ReportFileSystemChangeBeginning(recordPath);

                var duration = recordingEndDate - DateTime.UtcNow;

                _logger.Info("Beginning recording. Will record for {0} minutes.", duration.TotalMinutes.ToString(CultureInfo.InvariantCulture));

                _logger.Info("Writing file to path: " + recordPath);

                Action onStarted = async () =>
                {
                    activeRecordingInfo.Path = recordPath;

                    _activeRecordings.TryAdd(timer.Id, activeRecordingInfo);

                    timer.Status = RecordingStatus.InProgress;
                    _timerProvider.AddOrUpdate(timer, false);

                    await SaveRecordingMetadata(timer, recordPath, seriesPath).ConfigureAwait(false);

                    CreateRecordingFolders();

                    TriggerRefresh(recordPath);
                    EnforceKeepUpTo(timer, seriesPath);
                };

                await recorder.Record(liveStreamInfo.Item1 as IDirectStreamProvider, mediaStreamInfo, recordPath, duration, onStarted, activeRecordingInfo.CancellationTokenSource.Token).ConfigureAwait(false);

                recordingStatus = RecordingStatus.Completed;
                _logger.Info("Recording completed: {0}", recordPath);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Recording stopped: {0}", recordPath);
                recordingStatus = RecordingStatus.Completed;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error recording to {0}", ex, recordPath);
                recordingStatus = RecordingStatus.Error;
            }

            if (!string.IsNullOrWhiteSpace(liveStreamId))
            {
                try
                {
                    await CloseLiveStream(liveStreamId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error closing live stream", ex);
                }
            }

            DeleteFileIfEmpty(recordPath);

            TriggerRefresh(recordPath);
            _libraryMonitor.ReportFileSystemChangeComplete(recordPath, false);

            ActiveRecordingInfo removed;
            _activeRecordings.TryRemove(timer.Id, out removed);

            if (recordingStatus != RecordingStatus.Completed && DateTime.UtcNow < timer.EndDate && timer.RetryCount < 10)
            {
                const int retryIntervalSeconds = 60;
                _logger.Info("Retrying recording in {0} seconds.", retryIntervalSeconds);

                timer.Status = RecordingStatus.New;
                timer.PrePaddingSeconds = 0;
                timer.StartDate = DateTime.UtcNow.AddSeconds(retryIntervalSeconds);
                timer.RetryCount++;
                _timerProvider.AddOrUpdate(timer);
            }
            else if (_fileSystem.FileExists(recordPath))
            {
                timer.RecordingPath = recordPath;
                timer.Status = RecordingStatus.Completed;
                _timerProvider.AddOrUpdate(timer, false);
                OnSuccessfulRecording(timer, recordPath);
            }
            else
            {
                _timerProvider.Delete(timer);
            }
        }

        private async Task<RemoteSearchResult> FetchInternetMetadata(TimerInfo timer, CancellationToken cancellationToken)
        {
            if (timer.IsSeries)
            {
                if (timer.SeriesProviderIds.Count == 0)
                {
                    return null;
                }

                var query = new RemoteSearchQuery<SeriesInfo>()
                {
                    SearchInfo = new SeriesInfo
                    {
                        ProviderIds = timer.SeriesProviderIds,
                        Name = timer.Name,
                        MetadataCountryCode = _config.Configuration.MetadataCountryCode,
                        MetadataLanguage = _config.Configuration.PreferredMetadataLanguage
                    }
                };

                var results = await _providerManager.GetRemoteSearchResults<Series, SeriesInfo>(query, cancellationToken).ConfigureAwait(false);

                return results.FirstOrDefault();
            }

            return null;
        }

        private void DeleteFileIfEmpty(string path)
        {
            var file = _fileSystem.GetFileInfo(path);

            if (file.Exists && file.Length == 0)
            {
                try
                {
                    _fileSystem.DeleteFile(path);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error deleting 0-byte failed recording file {0}", ex, path);
                }
            }
        }

        private void TriggerRefresh(string path)
        {
            _logger.Info("Triggering refresh on {0}", path);

            var item = GetAffectedBaseItem(_fileSystem.GetDirectoryName(path));

            if (item != null)
            {
                _logger.Info("Refreshing recording parent {0}", item.Path);

                _providerManager.QueueRefresh(item.Id, new MetadataRefreshOptions(_fileSystem)
                {
                    RefreshPaths = new string[]
                    {
                        path,
                        _fileSystem.GetDirectoryName(path),
                        _fileSystem.GetDirectoryName(_fileSystem.GetDirectoryName(path))
                    }

                }, RefreshPriority.High);
            }
        }

        private BaseItem GetAffectedBaseItem(string path)
        {
            BaseItem item = null;

            var parentPath = _fileSystem.GetDirectoryName(path);

            while (item == null && !string.IsNullOrEmpty(path))
            {
                item = _libraryManager.FindByPath(path, null);

                path = _fileSystem.GetDirectoryName(path);
            }

            if (item != null)
            {
                if (item.GetType() == typeof(Folder) && string.Equals(item.Path, parentPath, StringComparison.OrdinalIgnoreCase))
                {
                    var parentItem = item.GetParent();
                    if (parentItem != null && !(parentItem is AggregateFolder))
                    {
                        item = parentItem;
                    }
                }
            }

            return item;
        }

        private async void EnforceKeepUpTo(TimerInfo timer, string seriesPath)
        {
            if (string.IsNullOrWhiteSpace(timer.SeriesTimerId))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(seriesPath))
            {
                return;
            }

            var seriesTimerId = timer.SeriesTimerId;
            var seriesTimer = _seriesTimerProvider.GetAll().FirstOrDefault(i => string.Equals(i.Id, seriesTimerId, StringComparison.OrdinalIgnoreCase));

            if (seriesTimer == null || seriesTimer.KeepUpTo <= 0)
            {
                return;
            }

            if (_disposed)
            {
                return;
            }

            await _recordingDeleteSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_disposed)
                {
                    return;
                }

                var timersToDelete = _timerProvider.GetAll()
                    .Where(i => i.Status == RecordingStatus.Completed && !string.IsNullOrWhiteSpace(i.RecordingPath))
                    .Where(i => string.Equals(i.SeriesTimerId, seriesTimerId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.EndDate)
                    .Where(i => _fileSystem.FileExists(i.RecordingPath))
                    .Skip(seriesTimer.KeepUpTo - 1)
                    .ToList();

                DeleteLibraryItemsForTimers(timersToDelete);

                var librarySeries = _libraryManager.FindByPath(seriesPath, true) as Folder;

                if (librarySeries == null)
                {
                    return;
                }

                var episodesToDelete = (librarySeries.GetItemList(new InternalItemsQuery
                {
                    OrderBy = new[] { new Tuple<string, SortOrder>(ItemSortBy.DateCreated, SortOrder.Descending) },
                    IsVirtualItem = false,
                    IsFolder = false,
                    Recursive = true,
                    DtoOptions = new DtoOptions(true)

                }))
                    .Where(i => i.IsFileProtocol && _fileSystem.FileExists(i.Path))
                    .Skip(seriesTimer.KeepUpTo - 1)
                    .ToList();

                foreach (var item in episodesToDelete)
                {
                    try
                    {
                        _libraryManager.DeleteItem(item, new DeleteOptions
                        {
                            DeleteFileLocation = true

                        }, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error deleting item", ex);
                    }
                }
            }
            finally
            {
                _recordingDeleteSemaphore.Release();
            }
        }

        private readonly SemaphoreSlim _recordingDeleteSemaphore = new SemaphoreSlim(1, 1);
        private void DeleteLibraryItemsForTimers(List<TimerInfo> timers)
        {
            foreach (var timer in timers)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    DeleteLibraryItemForTimer(timer);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error deleting recording", ex);
                }
            }
        }

        private void DeleteLibraryItemForTimer(TimerInfo timer)
        {
            var libraryItem = _libraryManager.FindByPath(timer.RecordingPath, false);

            if (libraryItem != null)
            {
                _libraryManager.DeleteItem(libraryItem, new DeleteOptions
                {
                    DeleteFileLocation = true

                }, true);
            }
            else
            {
                try
                {
                    _fileSystem.DeleteFile(timer.RecordingPath);
                }
                catch (IOException)
                {

                }
            }

            _timerProvider.Delete(timer);
        }

        private string EnsureFileUnique(string path, string timerId)
        {
            var originalPath = path;
            var index = 1;

            while (FileExists(path, timerId))
            {
                var parent = _fileSystem.GetDirectoryName(originalPath);
                var name = Path.GetFileNameWithoutExtension(originalPath);
                name += " - " + index.ToString(CultureInfo.InvariantCulture);

                path = Path.ChangeExtension(Path.Combine(parent, name), Path.GetExtension(originalPath));
                index++;
            }

            return path;
        }

        private bool FileExists(string path, string timerId)
        {
            if (_fileSystem.FileExists(path))
            {
                return true;
            }

            var hasRecordingAtPath = _activeRecordings
                .Values
                .ToList()
                .Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase) && !string.Equals(i.Timer.Id, timerId, StringComparison.OrdinalIgnoreCase));

            if (hasRecordingAtPath)
            {
                return true;
            }
            return false;
        }

        private IRecorder GetRecorder(ILiveStream liveStream, MediaSourceInfo mediaSource)
        {
            if (mediaSource.RequiresLooping || !(mediaSource.Container ?? string.Empty).EndsWith("ts", StringComparison.OrdinalIgnoreCase) || (mediaSource.Protocol != MediaProtocol.File && mediaSource.Protocol != MediaProtocol.Http))
            {
                return new EncodedRecorder(_logger, _fileSystem, _mediaEncoder, _config.ApplicationPaths, _jsonSerializer, _httpClient, _processFactory, _config, _assemblyInfo);
            }

            return new DirectRecorder(_logger, _httpClient, _fileSystem);
        }

        private void OnSuccessfulRecording(TimerInfo timer, string path)
        {
            PostProcessRecording(timer, path);
        }

        private void PostProcessRecording(TimerInfo timer, string path)
        {
            var options = GetConfiguration();
            if (string.IsNullOrWhiteSpace(options.RecordingPostProcessor))
            {
                return;
            }

            try
            {
                var process = _processFactory.Create(new ProcessOptions
                {
                    Arguments = GetPostProcessArguments(path, options.RecordingPostProcessorArguments),
                    CreateNoWindow = true,
                    EnableRaisingEvents = true,
                    ErrorDialog = false,
                    FileName = options.RecordingPostProcessor,
                    IsHidden = true,
                    UseShellExecute = false
                });

                _logger.Info("Running recording post processor {0} {1}", process.StartInfo.FileName, process.StartInfo.Arguments);

                process.Exited += Process_Exited;
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error running recording post processor", ex);
            }
        }

        private string GetPostProcessArguments(string path, string arguments)
        {
            return arguments.Replace("{path}", path, StringComparison.OrdinalIgnoreCase);
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            var process = (IProcess)sender;
            try
            {
                _logger.Info("Recording post-processing script completed with exit code {0}", process.ExitCode);
            }
            catch
            {

            }

            process.Dispose();
        }

        private async Task SaveRecordingImage(string recordingPath, LiveTvProgram program, ItemImageInfo image)
        {
            if (!image.IsLocalFile)
            {
                image = await _libraryManager.ConvertImageToLocal(program, image, 0).ConfigureAwait(false);
            }

            string imageSaveFilenameWithoutExtension = null;

            switch (image.Type)
            {
                case ImageType.Primary:

                    if (program.IsSeries)
                    {
                        imageSaveFilenameWithoutExtension = Path.GetFileNameWithoutExtension(recordingPath) + "-thumb";
                    }
                    else
                    {
                        imageSaveFilenameWithoutExtension = "poster";
                    }

                    break;
                case ImageType.Logo:
                    imageSaveFilenameWithoutExtension = "logo";
                    break;
                case ImageType.Thumb:
                    if (program.IsSeries)
                    {
                        imageSaveFilenameWithoutExtension = Path.GetFileNameWithoutExtension(recordingPath) + "-thumb";
                    }
                    else
                    {
                        imageSaveFilenameWithoutExtension = "landscape";
                    }

                    break;
                case ImageType.Backdrop:
                    imageSaveFilenameWithoutExtension = "fanart";
                    break;
                default:
                    break;
            }

            if (string.IsNullOrWhiteSpace(imageSaveFilenameWithoutExtension))
            {
                return;
            }

            var imageSavePath = Path.Combine(_fileSystem.GetDirectoryName(recordingPath), imageSaveFilenameWithoutExtension);

            // preserve original image extension
            imageSavePath = Path.ChangeExtension(imageSavePath, Path.GetExtension(image.Path));

            _fileSystem.CopyFile(image.Path, imageSavePath, true);
        }

        private async Task SaveRecordingImages(string recordingPath, LiveTvProgram program)
        {
            var image = program.IsSeries ?
                (program.GetImageInfo(ImageType.Thumb, 0) ?? program.GetImageInfo(ImageType.Primary, 0)) :
                (program.GetImageInfo(ImageType.Primary, 0) ?? program.GetImageInfo(ImageType.Thumb, 0));

            if (image != null)
            {
                try
                {
                    await SaveRecordingImage(recordingPath, program, image).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error saving recording image", ex);
                }
            }

            if (!program.IsSeries)
            {
                image = program.GetImageInfo(ImageType.Backdrop, 0);
                if (image != null)
                {
                    try
                    {
                        await SaveRecordingImage(recordingPath, program, image).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error saving recording image", ex);
                    }
                }

                image = program.GetImageInfo(ImageType.Thumb, 0);
                if (image != null)
                {
                    try
                    {
                        await SaveRecordingImage(recordingPath, program, image).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error saving recording image", ex);
                    }
                }

                image = program.GetImageInfo(ImageType.Logo, 0);
                if (image != null)
                {
                    try
                    {
                        await SaveRecordingImage(recordingPath, program, image).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error saving recording image", ex);
                    }
                }
            }
        }

        private async Task SaveRecordingMetadata(TimerInfo timer, string recordingPath, string seriesPath)
        {
            try
            {
                var program = string.IsNullOrWhiteSpace(timer.ProgramId) ? null : _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { typeof(LiveTvProgram).Name },
                    Limit = 1,
                    ExternalId = timer.ProgramId,
                    DtoOptions = new DtoOptions(true)

                }).FirstOrDefault() as LiveTvProgram;

                // dummy this up
                if (program == null)
                {
                    program = new LiveTvProgram
                    {
                        Name = timer.Name,
                        HomePageUrl = timer.HomePageUrl,
                        Overview = timer.Overview,
                        Genres = timer.Genres,
                        CommunityRating = timer.CommunityRating,
                        OfficialRating = timer.OfficialRating,
                        ProductionYear = timer.ProductionYear,
                        PremiereDate = timer.OriginalAirDate,
                        IndexNumber = timer.EpisodeNumber,
                        ParentIndexNumber = timer.SeasonNumber
                    };
                }

                if (timer.IsSports)
                {
                    AddGenre(program.Genres, "Sports");
                }
                if (timer.IsKids)
                {
                    AddGenre(program.Genres, "Kids");
                    AddGenre(program.Genres, "Children");
                }
                if (timer.IsNews)
                {
                    AddGenre(program.Genres, "News");
                }

                if (timer.IsProgramSeries)
                {
                    SaveSeriesNfo(timer, seriesPath);
                    SaveVideoNfo(timer, recordingPath, program, false);
                }
                else if (!timer.IsMovie || timer.IsSports || timer.IsNews)
                {
                    SaveVideoNfo(timer, recordingPath, program, true);
                }
                else
                {
                    SaveVideoNfo(timer, recordingPath, program, false);
                }

                await SaveRecordingImages(recordingPath, program).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error saving nfo", ex);
            }
        }

        private void SaveSeriesNfo(TimerInfo timer, string seriesPath)
        {
            var nfoPath = Path.Combine(seriesPath, "tvshow.nfo");

            if (_fileSystem.FileExists(nfoPath))
            {
                return;
            }

            using (var stream = _fileSystem.GetFileStream(nfoPath, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read))
            {
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = Encoding.UTF8,
                    CloseOutput = false
                };

                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument(true);
                    writer.WriteStartElement("tvshow");

                    string id;
                    if (timer.SeriesProviderIds.TryGetValue(MetadataProviders.Tvdb.ToString(), out id))
                    {
                        writer.WriteElementString("id", id);
                    }
                    if (timer.SeriesProviderIds.TryGetValue(MetadataProviders.Imdb.ToString(), out id))
                    {
                        writer.WriteElementString("imdb_id", id);
                    }
                    if (timer.SeriesProviderIds.TryGetValue(MetadataProviders.Tmdb.ToString(), out id))
                    {
                        writer.WriteElementString("tmdbid", id);
                    }
                    if (timer.SeriesProviderIds.TryGetValue(MetadataProviders.Zap2It.ToString(), out id))
                    {
                        writer.WriteElementString("zap2itid", id);
                    }

                    if (!string.IsNullOrWhiteSpace(timer.Name))
                    {
                        writer.WriteElementString("title", timer.Name);
                    }

                    if (!string.IsNullOrWhiteSpace(timer.OfficialRating))
                    {
                        writer.WriteElementString("mpaa", timer.OfficialRating);
                    }

                    foreach (var genre in timer.Genres)
                    {
                        writer.WriteElementString("genre", genre);
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
        }

        public const string DateAddedFormat = "yyyy-MM-dd HH:mm:ss";
        private void SaveVideoNfo(TimerInfo timer, string recordingPath, BaseItem item, bool lockData)
        {
            var nfoPath = Path.ChangeExtension(recordingPath, ".nfo");

            if (_fileSystem.FileExists(nfoPath))
            {
                return;
            }

            using (var stream = _fileSystem.GetFileStream(nfoPath, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read))
            {
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = Encoding.UTF8,
                    CloseOutput = false
                };

                var options = _config.GetNfoConfiguration();

                var isSeriesEpisode = timer.IsProgramSeries;

                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument(true);

                    if (isSeriesEpisode)
                    {
                        writer.WriteStartElement("episodedetails");

                        if (!string.IsNullOrWhiteSpace(timer.EpisodeTitle))
                        {
                            writer.WriteElementString("title", timer.EpisodeTitle);
                        }

                        var premiereDate = item.PremiereDate ?? (!timer.IsRepeat ? DateTime.UtcNow : (DateTime?)null);

                        if (premiereDate.HasValue)
                        {
                            var formatString = options.ReleaseDateFormat;

                            writer.WriteElementString("aired", premiereDate.Value.ToLocalTime().ToString(formatString));
                        }

                        if (item.IndexNumber.HasValue)
                        {
                            writer.WriteElementString("episode", item.IndexNumber.Value.ToString(CultureInfo.InvariantCulture));
                        }

                        if (item.ParentIndexNumber.HasValue)
                        {
                            writer.WriteElementString("season", item.ParentIndexNumber.Value.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    else
                    {
                        writer.WriteStartElement("movie");

                        if (!string.IsNullOrWhiteSpace(item.Name))
                        {
                            writer.WriteElementString("title", item.Name);
                        }

                        if (!string.IsNullOrWhiteSpace(item.OriginalTitle))
                        {
                            writer.WriteElementString("originaltitle", item.OriginalTitle);
                        }

                        if (item.PremiereDate.HasValue)
                        {
                            var formatString = options.ReleaseDateFormat;

                            writer.WriteElementString("premiered", item.PremiereDate.Value.ToLocalTime().ToString(formatString));
                            writer.WriteElementString("releasedate", item.PremiereDate.Value.ToLocalTime().ToString(formatString));
                        }
                    }

                    writer.WriteElementString("dateadded", DateTime.UtcNow.ToLocalTime().ToString(DateAddedFormat));

                    if (item.ProductionYear.HasValue)
                    {
                        writer.WriteElementString("year", item.ProductionYear.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    if (!string.IsNullOrEmpty(item.OfficialRating))
                    {
                        writer.WriteElementString("mpaa", item.OfficialRating);
                    }

                    var overview = (item.Overview ?? string.Empty)
                        .StripHtml()
                        .Replace("&quot;", "'");

                    writer.WriteElementString("plot", overview);

                    if (item.CommunityRating.HasValue)
                    {
                        writer.WriteElementString("rating", item.CommunityRating.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    foreach (var genre in item.Genres)
                    {
                        writer.WriteElementString("genre", genre);
                    }

                    if (!string.IsNullOrWhiteSpace(item.HomePageUrl))
                    {
                        writer.WriteElementString("website", item.HomePageUrl);
                    }

                    var people = item.Id.Equals(Guid.Empty) ? new List<PersonInfo>() : _libraryManager.GetPeople(item);

                    var directors = people
                        .Where(i => IsPersonType(i, PersonType.Director))
                        .Select(i => i.Name)
                        .ToList();

                    foreach (var person in directors)
                    {
                        writer.WriteElementString("director", person);
                    }

                    var writers = people
                        .Where(i => IsPersonType(i, PersonType.Writer))
                        .Select(i => i.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var person in writers)
                    {
                        writer.WriteElementString("writer", person);
                    }

                    foreach (var person in writers)
                    {
                        writer.WriteElementString("credits", person);
                    }

                    var tmdbCollection = item.GetProviderId(MetadataProviders.TmdbCollection);

                    if (!string.IsNullOrEmpty(tmdbCollection))
                    {
                        writer.WriteElementString("collectionnumber", tmdbCollection);
                    }

                    var imdb = item.GetProviderId(MetadataProviders.Imdb);
                    if (!string.IsNullOrEmpty(imdb))
                    {
                        if (!isSeriesEpisode)
                        {
                            writer.WriteElementString("id", imdb);
                        }

                        writer.WriteElementString("imdbid", imdb);

                        // No need to lock if we have identified the content already
                        lockData = false;
                    }

                    var tvdb = item.GetProviderId(MetadataProviders.Tvdb);
                    if (!string.IsNullOrEmpty(tvdb))
                    {
                        writer.WriteElementString("tvdbid", tvdb);

                        // No need to lock if we have identified the content already
                        lockData = false;
                    }

                    var tmdb = item.GetProviderId(MetadataProviders.Tmdb);
                    if (!string.IsNullOrEmpty(tmdb))
                    {
                        writer.WriteElementString("tmdbid", tmdb);

                        // No need to lock if we have identified the content already
                        lockData = false;
                    }

                    if (lockData)
                    {
                        writer.WriteElementString("lockdata", true.ToString().ToLower());
                    }

                    if (item.CriticRating.HasValue)
                    {
                        writer.WriteElementString("criticrating", item.CriticRating.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    if (!string.IsNullOrWhiteSpace(item.Tagline))
                    {
                        writer.WriteElementString("tagline", item.Tagline);
                    }

                    foreach (var studio in item.Studios)
                    {
                        writer.WriteElementString("studio", studio);
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
        }

        private static bool IsPersonType(PersonInfo person, string type)
        {
            return string.Equals(person.Type, type, StringComparison.OrdinalIgnoreCase) || string.Equals(person.Role, type, StringComparison.OrdinalIgnoreCase);
        }

        private void AddGenre(List<string> genres, string genre)
        {
            if (!genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            {
                genres.Add(genre);
            }
        }

        private LiveTvProgram GetProgramInfoFromCache(string programId)
        {
            var query = new InternalItemsQuery
            {
                ItemIds = new[] { _liveTvManager.GetInternalProgramId(Name, programId) },
                Limit = 1,
                DtoOptions = new DtoOptions()
            };

            return _libraryManager.GetItemList(query).Cast<LiveTvProgram>().FirstOrDefault();
        }

        private LiveTvProgram GetProgramInfoFromCache(TimerInfo timer)
        {
            return GetProgramInfoFromCache(timer.ProgramId, timer.ChannelId);
        }

        private LiveTvProgram GetProgramInfoFromCache(string programId, string channelId)
        {
            return GetProgramInfoFromCache(programId);
        }

        private LiveTvProgram GetProgramInfoFromCache(string channelId, DateTime startDateUtc)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new string[] { typeof(LiveTvProgram).Name },
                Limit = 1,
                DtoOptions = new DtoOptions(true)
                {
                    EnableImages = false
                },
                MinStartDate = startDateUtc.AddMinutes(-3),
                MaxStartDate = startDateUtc.AddMinutes(3),
                OrderBy = new[] { new Tuple<string, SortOrder>(ItemSortBy.StartDate, SortOrder.Ascending) }
            };

            if (!string.IsNullOrWhiteSpace(channelId))
            {
                query.ChannelIds = new[] { _liveTvManager.GetInternalChannelId(Name, channelId) };
            }

            return _libraryManager.GetItemList(query).Cast<LiveTvProgram>().FirstOrDefault();
        }

        private LiveTvOptions GetConfiguration()
        {
            return _config.GetConfiguration<LiveTvOptions>("livetv");
        }

        private bool ShouldCancelTimerForSeriesTimer(SeriesTimerInfo seriesTimer, TimerInfo timer)
        {
            if (timer.IsManual)
            {
                return false;
            }

            if (!seriesTimer.RecordAnyTime)
            {
                if (Math.Abs(seriesTimer.StartDate.TimeOfDay.Ticks - timer.StartDate.TimeOfDay.Ticks) >= TimeSpan.FromMinutes(10).Ticks)
                {
                    return true;
                }
            }

            //if (!seriesTimer.Days.Contains(timer.StartDate.ToLocalTime().DayOfWeek))
            //{
            //    return true;
            //}

            if (seriesTimer.RecordNewOnly && timer.IsRepeat)
            {
                return true;
            }

            if (!seriesTimer.RecordAnyChannel && !string.Equals(timer.ChannelId, seriesTimer.ChannelId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return seriesTimer.SkipEpisodesInLibrary && IsProgramAlreadyInLibrary(timer);
        }

        private void HandleDuplicateShowIds(List<TimerInfo> timers)
        {
            foreach (var timer in timers.Skip(1))
            {
                // TODO: Get smarter, prefer HD, etc

                timer.Status = RecordingStatus.Cancelled;
                _timerProvider.Update(timer);
            }
        }

        private void SearchForDuplicateShowIds(List<TimerInfo> timers)
        {
            var groups = timers.ToLookup(i => i.ShowId ?? string.Empty).ToList();

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                {
                    continue;
                }

                var groupTimers = group.ToList();

                if (groupTimers.Count < 2)
                {
                    continue;
                }

                HandleDuplicateShowIds(groupTimers);
            }
        }

        private async Task UpdateTimersForSeriesTimer(SeriesTimerInfo seriesTimer, bool updateTimerSettings, bool deleteInvalidTimers)
        {
            var allTimers = GetTimersForSeries(seriesTimer)
                .ToList();

            var registration = await _liveTvManager.GetRegistrationInfo("seriesrecordings").ConfigureAwait(false);

            var enabledTimersForSeries = new List<TimerInfo>();

            if (registration.IsValid)
            {
                foreach (var timer in allTimers)
                {
                    var existingTimer = _timerProvider.GetTimer(timer.Id);

                    if (existingTimer == null)
                    {
                        existingTimer = string.IsNullOrWhiteSpace(timer.ProgramId)
                            ? null
                            : _timerProvider.GetTimerByProgramId(timer.ProgramId);
                    }

                    if (existingTimer == null)
                    {
                        if (ShouldCancelTimerForSeriesTimer(seriesTimer, timer))
                        {
                            timer.Status = RecordingStatus.Cancelled;
                        }
                        else
                        {
                            enabledTimersForSeries.Add(timer);
                        }
                        _timerProvider.Add(timer);
                    }
                    else
                    {
                        // Only update if not currently active - test both new timer and existing in case Id's are different
                        // Id's could be different if the timer was created manually prior to series timer creation
                        ActiveRecordingInfo activeRecordingInfo;
                        if (!_activeRecordings.TryGetValue(timer.Id, out activeRecordingInfo) && !_activeRecordings.TryGetValue(existingTimer.Id, out activeRecordingInfo))
                        {
                            UpdateExistingTimerWithNewMetadata(existingTimer, timer);

                            // Needed by ShouldCancelTimerForSeriesTimer
                            timer.IsManual = existingTimer.IsManual;

                            if (ShouldCancelTimerForSeriesTimer(seriesTimer, timer))
                            {
                                existingTimer.Status = RecordingStatus.Cancelled;
                            }
                            else if (!existingTimer.IsManual)
                            {
                                existingTimer.Status = RecordingStatus.New;
                            }

                            if (existingTimer.Status != RecordingStatus.Cancelled)
                            {
                                enabledTimersForSeries.Add(existingTimer);
                            }

                            if (updateTimerSettings)
                            {
                                existingTimer.KeepUntil = seriesTimer.KeepUntil;
                                existingTimer.IsPostPaddingRequired = seriesTimer.IsPostPaddingRequired;
                                existingTimer.IsPrePaddingRequired = seriesTimer.IsPrePaddingRequired;
                                existingTimer.PostPaddingSeconds = seriesTimer.PostPaddingSeconds;
                                existingTimer.PrePaddingSeconds = seriesTimer.PrePaddingSeconds;
                                existingTimer.Priority = seriesTimer.Priority;
                            }

                            existingTimer.SeriesTimerId = seriesTimer.Id;
                            _timerProvider.Update(existingTimer);
                        }
                    }
                }
            }

            SearchForDuplicateShowIds(enabledTimersForSeries);

            if (deleteInvalidTimers)
            {
                var allTimerIds = allTimers
                    .Select(i => i.Id)
                    .ToList();

                var deleteStatuses = new []
                {
                    RecordingStatus.New
                };

                var deletes = _timerProvider.GetAll()
                    .Where(i => string.Equals(i.SeriesTimerId, seriesTimer.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(i => !allTimerIds.Contains(i.Id, StringComparer.OrdinalIgnoreCase) && i.StartDate > DateTime.UtcNow)
                    .Where(i => deleteStatuses.Contains(i.Status))
                    .ToList();

                foreach (var timer in deletes)
                {
                    CancelTimerInternal(timer.Id, false, false);
                }
            }
        }

        private IEnumerable<TimerInfo> GetTimersForSeries(SeriesTimerInfo seriesTimer)
        {
            if (seriesTimer == null)
            {
                throw new ArgumentNullException("seriesTimer");
            }

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new string[] { typeof(LiveTvProgram).Name },
                ExternalSeriesId = seriesTimer.SeriesId,
                DtoOptions = new DtoOptions(true)
                {
                    EnableImages = false
                },
                MinEndDate = DateTime.UtcNow
            };

            if (string.IsNullOrEmpty(seriesTimer.SeriesId))
            {
                query.Name = seriesTimer.Name;
            }

            if (!seriesTimer.RecordAnyChannel)
            {
                query.ChannelIds = new[] { _liveTvManager.GetInternalChannelId(Name, seriesTimer.ChannelId) };
            }

            var tempChannelCache = new Dictionary<string, LiveTvChannel>();

            return _libraryManager.GetItemList(query).Cast<LiveTvProgram>().Select(i => CreateTimer(i, seriesTimer, tempChannelCache));
        }

        private TimerInfo CreateTimer(LiveTvProgram parent, SeriesTimerInfo seriesTimer, Dictionary<string, LiveTvChannel> tempChannelCache)
        {
            string channelId = seriesTimer.RecordAnyChannel ? null : seriesTimer.ChannelId;

            if (string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(parent.ChannelId))
            {
                LiveTvChannel channel;

                if (!tempChannelCache.TryGetValue(parent.ChannelId, out channel))
                {
                    channel = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new string[] { typeof(LiveTvChannel).Name },
                        ItemIds = new[] { new Guid(parent.ChannelId) },
                        DtoOptions = new DtoOptions()

                    }).Cast<LiveTvChannel>().FirstOrDefault();

                    if (channel != null && !string.IsNullOrWhiteSpace(channel.ExternalId))
                    {
                        tempChannelCache[parent.ChannelId] = channel;
                    }
                }

                if (channel != null || tempChannelCache.TryGetValue(parent.ChannelId, out channel))
                {
                    channelId = channel.ExternalId;
                }
            }

            var timer = new TimerInfo
            {
                ChannelId = channelId,
                Id = (seriesTimer.Id + parent.ExternalId).GetMD5().ToString("N"),
                StartDate = parent.StartDate,
                EndDate = parent.EndDate.Value,
                ProgramId = parent.ExternalId,
                PrePaddingSeconds = seriesTimer.PrePaddingSeconds,
                PostPaddingSeconds = seriesTimer.PostPaddingSeconds,
                IsPostPaddingRequired = seriesTimer.IsPostPaddingRequired,
                IsPrePaddingRequired = seriesTimer.IsPrePaddingRequired,
                KeepUntil = seriesTimer.KeepUntil,
                Priority = seriesTimer.Priority,
                Name = parent.Name,
                Overview = parent.Overview,
                SeriesId = parent.ExternalSeriesId,
                SeriesTimerId = seriesTimer.Id,
                ShowId = parent.ShowId
            };

            CopyProgramInfoToTimerInfo(parent, timer, tempChannelCache);

            return timer;
        }

        private void CopyProgramInfoToTimerInfo(LiveTvProgram programInfo, TimerInfo timerInfo)
        {
            var tempChannelCache = new Dictionary<string, LiveTvChannel>();
            CopyProgramInfoToTimerInfo(programInfo, timerInfo, tempChannelCache);
        }

        private void CopyProgramInfoToTimerInfo(LiveTvProgram programInfo, TimerInfo timerInfo, Dictionary<string, LiveTvChannel> tempChannelCache)
        {
            string channelId = null;

            if (!string.IsNullOrWhiteSpace(programInfo.ChannelId))
            {
                LiveTvChannel channel;

                if (!tempChannelCache.TryGetValue(programInfo.ChannelId, out channel))
                {
                    channel = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new string[] { typeof(LiveTvChannel).Name },
                        ItemIds = new[] { new Guid(programInfo.ChannelId) },
                        DtoOptions = new DtoOptions()

                    }).Cast<LiveTvChannel>().FirstOrDefault();

                    if (channel != null && !string.IsNullOrWhiteSpace(channel.ExternalId))
                    {
                        tempChannelCache[programInfo.ChannelId] = channel;
                    }
                }

                if (channel != null || tempChannelCache.TryGetValue(programInfo.ChannelId, out channel))
                {
                    channelId = channel.ExternalId;
                }
            }

            timerInfo.Name = programInfo.Name;
            timerInfo.StartDate = programInfo.StartDate;
            timerInfo.EndDate = programInfo.EndDate.Value;

            if (!string.IsNullOrWhiteSpace(channelId))
            {
                timerInfo.ChannelId = channelId;
            }

            timerInfo.SeasonNumber = programInfo.ParentIndexNumber;
            timerInfo.EpisodeNumber = programInfo.IndexNumber;
            timerInfo.IsMovie = programInfo.IsMovie;
            timerInfo.IsKids = programInfo.IsKids;
            timerInfo.IsNews = programInfo.IsNews;
            timerInfo.IsSports = programInfo.IsSports;
            timerInfo.ProductionYear = programInfo.ProductionYear;
            timerInfo.EpisodeTitle = programInfo.EpisodeTitle;
            timerInfo.OriginalAirDate = programInfo.PremiereDate;
            timerInfo.IsProgramSeries = programInfo.IsSeries;

            timerInfo.IsSeries = programInfo.IsSeries;

            timerInfo.HomePageUrl = programInfo.HomePageUrl;
            timerInfo.CommunityRating = programInfo.CommunityRating;
            timerInfo.Overview = programInfo.Overview;
            timerInfo.OfficialRating = programInfo.OfficialRating;
            timerInfo.IsRepeat = programInfo.IsRepeat;
            timerInfo.SeriesId = programInfo.ExternalSeriesId;
            timerInfo.ProviderIds = programInfo.ProviderIds;
            timerInfo.Tags = programInfo.Tags;

            var seriesProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var providerId in timerInfo.ProviderIds)
            {
                var srch = "Series";
                if (providerId.Key.StartsWith(srch, StringComparison.OrdinalIgnoreCase))
                {
                    seriesProviderIds[providerId.Key.Substring(srch.Length)] = providerId.Value;
                }
            }

            timerInfo.SeriesProviderIds = seriesProviderIds;
        }

        private bool IsProgramAlreadyInLibrary(TimerInfo program)
        {
            if ((program.EpisodeNumber.HasValue && program.SeasonNumber.HasValue) || !string.IsNullOrWhiteSpace(program.EpisodeTitle))
            {
                var seriesIds = _libraryManager.GetItemIds(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { typeof(Series).Name },
                    Name = program.Name

                }).ToArray();

                if (seriesIds.Length == 0)
                {
                    return false;
                }

                if (program.EpisodeNumber.HasValue && program.SeasonNumber.HasValue)
                {
                    var result = _libraryManager.GetItemIds(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { typeof(Episode).Name },
                        ParentIndexNumber = program.SeasonNumber.Value,
                        IndexNumber = program.EpisodeNumber.Value,
                        AncestorIds = seriesIds,
                        IsVirtualItem = false,
                        Limit = 1
                    });

                    if (result.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool _disposed;
        public void Dispose()
        {
            _disposed = true;
            foreach (var pair in _activeRecordings.ToList())
            {
                pair.Value.CancellationTokenSource.Cancel();
            }
        }

        public List<VirtualFolderInfo> GetRecordingFolders()
        {
            var list = new List<VirtualFolderInfo>();

            var defaultFolder = RecordingPath;
            var defaultName = "Recordings";

            if (_fileSystem.DirectoryExists(defaultFolder))
            {
                list.Add(new VirtualFolderInfo
                {
                    Locations = new string[] { defaultFolder },
                    Name = defaultName
                });
            }

            var customPath = GetConfiguration().MovieRecordingPath;
            if ((!string.IsNullOrWhiteSpace(customPath) && !string.Equals(customPath, defaultFolder, StringComparison.OrdinalIgnoreCase)) && _fileSystem.DirectoryExists(customPath))
            {
                list.Add(new VirtualFolderInfo
                {
                    Locations = new string[] { customPath },
                    Name = "Recorded Movies",
                    CollectionType = CollectionType.Movies
                });
            }

            customPath = GetConfiguration().SeriesRecordingPath;
            if ((!string.IsNullOrWhiteSpace(customPath) && !string.Equals(customPath, defaultFolder, StringComparison.OrdinalIgnoreCase)) && _fileSystem.DirectoryExists(customPath))
            {
                list.Add(new VirtualFolderInfo
                {
                    Locations = new string[] { customPath },
                    Name = "Recorded Shows",
                    CollectionType = CollectionType.TvShows
                });
            }

            return list;
        }

        private const int TunerDiscoveryDurationMs = 3000;

        public async Task<List<TunerHostInfo>> DiscoverTuners(bool newDevicesOnly, CancellationToken cancellationToken)
        {
            var list = new List<TunerHostInfo>();

            var configuredDeviceIds = GetConfiguration().TunerHosts
               .Where(i => !string.IsNullOrWhiteSpace(i.DeviceId))
               .Select(i => i.DeviceId)
               .ToList();

            foreach (var host in _liveTvManager.TunerHosts)
            {
                var discoveredDevices = await DiscoverDevices(host, TunerDiscoveryDurationMs, cancellationToken).ConfigureAwait(false);

                if (newDevicesOnly)
                {
                    discoveredDevices = discoveredDevices.Where(d => !configuredDeviceIds.Contains(d.DeviceId, StringComparer.OrdinalIgnoreCase))
                            .ToList();
                }
                list.AddRange(discoveredDevices);
            }

            return list;
        }

        public async Task ScanForTunerDeviceChanges(CancellationToken cancellationToken)
        {
            foreach (var host in _liveTvManager.TunerHosts)
            {
                await ScanForTunerDeviceChanges(host, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ScanForTunerDeviceChanges(ITunerHost host, CancellationToken cancellationToken)
        {
            var discoveredDevices = await DiscoverDevices(host, TunerDiscoveryDurationMs, cancellationToken).ConfigureAwait(false);

            var configuredDevices = GetConfiguration().TunerHosts
                .Where(i => string.Equals(i.Type, host.Type, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var device in discoveredDevices)
            {
                var configuredDevice = configuredDevices.FirstOrDefault(i => string.Equals(i.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase));

                if (configuredDevice != null)
                {
                    if (!string.Equals(device.Url, configuredDevice.Url, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Info("Tuner url has changed from {0} to {1}", configuredDevice.Url, device.Url);

                        configuredDevice.Url = device.Url;
                        await _liveTvManager.SaveTunerHost(configuredDevice).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<List<TunerHostInfo>> DiscoverDevices(ITunerHost host, int discoveryDuationMs, CancellationToken cancellationToken)
        {
            try
            {
                var discoveredDevices = await host.DiscoverDevices(discoveryDuationMs, cancellationToken).ConfigureAwait(false);

                foreach (var device in discoveredDevices)
                {
                    _logger.Info("Discovered tuner device {0} at {1}", host.Name, device.Url);
                }

                return discoveredDevices;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error discovering tuner devices", ex);

                return new List<TunerHostInfo>();
            }
        }
    }
    public static class ConfigurationExtension
    {
        public static XbmcMetadataOptions GetNfoConfiguration(this IConfigurationManager manager)
        {
            return manager.GetConfiguration<XbmcMetadataOptions>("xbmcmetadata");
        }
    }
}