﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.Implementations.LiveTv.TunerHosts
{
    public abstract class BaseTunerHost
    {
        protected readonly IServerConfigurationManager Config;
        protected readonly ILogger Logger;
        protected IJsonSerializer JsonSerializer;
        protected readonly IMediaEncoder MediaEncoder;
        protected readonly IFileSystem FileSystem;

        private readonly ConcurrentDictionary<string, ChannelCache> _channelCache =
            new ConcurrentDictionary<string, ChannelCache>(StringComparer.OrdinalIgnoreCase);

        protected BaseTunerHost(IServerConfigurationManager config, ILogger logger, IJsonSerializer jsonSerializer, IMediaEncoder mediaEncoder, IFileSystem fileSystem)
        {
            Config = config;
            Logger = logger;
            JsonSerializer = jsonSerializer;
            MediaEncoder = mediaEncoder;
            FileSystem = fileSystem;
        }

        public virtual bool IsSupported
        {
            get
            {
                return true;
            }
        }

        protected abstract Task<List<ChannelInfo>> GetChannelsInternal(TunerHostInfo tuner, CancellationToken cancellationToken);
        public abstract string Type { get; }

        public async Task<List<ChannelInfo>> GetChannels(TunerHostInfo tuner, bool enableCache, CancellationToken cancellationToken)
        {
            ChannelCache cache = null;
            var key = tuner.Id;

            if (enableCache && !string.IsNullOrWhiteSpace(key) && _channelCache.TryGetValue(key, out cache))
            {
                if (DateTime.UtcNow - cache.Date < TimeSpan.FromMinutes(60))
                {
                    return cache.Channels.ToList();
                }
            }

            var result = await GetChannelsInternal(tuner, cancellationToken).ConfigureAwait(false);
            var list = result.ToList();
            Logger.Info("Channels from {0}: {1}", tuner.Url, JsonSerializer.SerializeToString(list));

            if (!string.IsNullOrWhiteSpace(key) && list.Count > 0)
            {
                cache = cache ?? new ChannelCache();
                cache.Date = DateTime.UtcNow;
                cache.Channels = list;
                _channelCache.AddOrUpdate(key, cache, (k, v) => cache);
            }

            return list;
        }

        protected virtual List<TunerHostInfo> GetTunerHosts()
        {
            return GetConfiguration().TunerHosts
                .Where(i => string.Equals(i.Type, Type, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
        {
            var list = new List<ChannelInfo>();

            var hosts = GetTunerHosts();

            foreach (var host in hosts)
            {
                var channelCacheFile = Path.Combine(Config.ApplicationPaths.CachePath, host.Id + "_channels");

                try
                {
                    var channels = await GetChannels(host, enableCache, cancellationToken).ConfigureAwait(false);
                    var newChannels = channels.Where(i => !list.Any(l => string.Equals(i.Id, l.Id, StringComparison.OrdinalIgnoreCase))).ToList();

                    list.AddRange(newChannels);

                    if (!enableCache)
                    {
                        try
                        {
                            FileSystem.CreateDirectory(FileSystem.GetDirectoryName(channelCacheFile));
                            JsonSerializer.SerializeToFile(channels, channelCacheFile);
                        }
                        catch (IOException)
                        {

                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error getting channel list", ex);

                    if (enableCache)
                    {
                        try
                        {
                            var channels = JsonSerializer.DeserializeFromFile<List<ChannelInfo>>(channelCacheFile);
                            list.AddRange(channels);
                        }
                        catch (IOException)
                        {

                        }
                    }
                }
            }

            return list;
        }

        protected abstract Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(TunerHostInfo tuner, string channelId, CancellationToken cancellationToken);

        public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentNullException("channelId");
            }

            if (IsValidChannelId(channelId))
            {
                var hosts = GetTunerHosts();

                foreach (var host in hosts)
                {
                    try
                    {
                        var channels = await GetChannels(host, true, cancellationToken).ConfigureAwait(false);

                        if (channels.Any(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase)))
                        {
                            return await GetChannelStreamMediaSources(host, channelId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error getting channels", ex);
                    }
                }
            }

            return new List<MediaSourceInfo>();
        }

        protected abstract Task<ILiveStream> GetChannelStream(TunerHostInfo tuner, string channelId, string streamId, CancellationToken cancellationToken);

        public async Task<ILiveStream> GetChannelStream(string channelId, string streamId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentNullException("channelId");
            }

            if (!IsValidChannelId(channelId))
            {
                throw new FileNotFoundException();
            }

            var hosts = GetTunerHosts();

            var hostsWithChannel = new List<TunerHostInfo>();

            foreach (var host in hosts)
            {
                try
                {
                    var channels = await GetChannels(host, true, cancellationToken).ConfigureAwait(false);

                    if (channels.Any(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase)))
                    {
                        hostsWithChannel.Add(host);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Error getting channels", ex);
                }
            }

            foreach (var host in hostsWithChannel)
            {
                if (!channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var liveStream = await GetChannelStream(host, channelId, streamId, cancellationToken).ConfigureAwait(false);
                    var startTime = DateTime.UtcNow;
                    await liveStream.Open(cancellationToken).ConfigureAwait(false);
                    var endTime = DateTime.UtcNow;
                    Logger.Info("Live stream opened after {0}ms", (endTime - startTime).TotalMilliseconds);
                    return liveStream;
                }
                catch (Exception ex)
                {
                    Logger.Error("Error opening tuner", ex);
                }
            }

            throw new LiveTvConflictException();
        }

        protected virtual string ChannelIdPrefix
        {
            get
            {
                return Type + "_";
            }
        }
        protected virtual bool IsValidChannelId(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                throw new ArgumentNullException("channelId");
            }

            return channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase);
        }

        protected LiveTvOptions GetConfiguration()
        {
            return Config.GetConfiguration<LiveTvOptions>("livetv");
        }

        private class ChannelCache
        {
            public DateTime Date;
            public List<ChannelInfo> Channels;
        }
    }
}
