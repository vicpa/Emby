﻿using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.System;

namespace Emby.Server.Implementations.LiveTv.TunerHosts
{
    public class M3UTunerHost : BaseTunerHost, ITunerHost, IConfigurableTunerHost
    {
        private readonly IHttpClient _httpClient;
        private readonly IServerApplicationHost _appHost;
        private readonly IEnvironmentInfo _environment;
        private readonly INetworkManager _networkManager;

        public M3UTunerHost(IServerConfigurationManager config, ILogger logger, IJsonSerializer jsonSerializer, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IHttpClient httpClient, IServerApplicationHost appHost, IEnvironmentInfo environment, INetworkManager networkManager) : base(config, logger, jsonSerializer, mediaEncoder, fileSystem)
        {
            _httpClient = httpClient;
            _appHost = appHost;
            _environment = environment;
            _networkManager = networkManager;
        }

        public override string Type
        {
            get { return "m3u"; }
        }

        public virtual string Name
        {
            get { return "M3U Tuner"; }
        }

        private string GetFullChannelIdPrefix(TunerHostInfo info)
        {
            return ChannelIdPrefix + info.Url.GetMD5().ToString("N");
        }

        protected override async Task<List<ChannelInfo>> GetChannelsInternal(TunerHostInfo info, CancellationToken cancellationToken)
        {
            var channelIdPrefix = GetFullChannelIdPrefix(info);

            var result = await new M3uParser(Logger, FileSystem, _httpClient, _appHost).Parse(info.Url, channelIdPrefix, info.Id, cancellationToken).ConfigureAwait(false);

            return result.Cast<ChannelInfo>().ToList();
        }

        public Task<List<LiveTvTunerInfo>> GetTunerInfos(CancellationToken cancellationToken)
        {
            var list = GetTunerHosts()
            .Select(i => new LiveTvTunerInfo()
            {
                Name = Name,
                SourceType = Type,
                Status = LiveTvTunerStatus.Available,
                Id = i.Url.GetMD5().ToString("N"),
                Url = i.Url
            })
            .ToList();

            return Task.FromResult(list);
        }

        protected override async Task<LiveStream> GetChannelStream(TunerHostInfo info, string channelId, string streamId, CancellationToken cancellationToken)
        {
            var sources = await GetChannelStreamMediaSources(info, channelId, cancellationToken).ConfigureAwait(false);

            var liveStream = new LiveStream(sources.First(), _environment, FileSystem);
            return liveStream;
        }

        public async Task Validate(TunerHostInfo info)
        {
            using (var stream = await new M3uParser(Logger, FileSystem, _httpClient, _appHost).GetListingsStream(info.Url, CancellationToken.None).ConfigureAwait(false))
            {

            }
        }

        protected override async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(TunerHostInfo info, string channelId, CancellationToken cancellationToken)
        {
            var channelIdPrefix = GetFullChannelIdPrefix(info);

            if (!channelId.StartsWith(channelIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var channels = await GetChannels(info, true, cancellationToken).ConfigureAwait(false);
            var channel = channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                return new List<MediaSourceInfo> { CreateMediaSourceInfo(info, channel) };
            }
            return new List<MediaSourceInfo>();
        }

        protected virtual MediaSourceInfo CreateMediaSourceInfo(TunerHostInfo info, ChannelInfo channel)
        {
            var path = channel.Path;
            MediaProtocol protocol = MediaProtocol.File;
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                protocol = MediaProtocol.Http;
            }
            else if (path.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase))
            {
                protocol = MediaProtocol.Rtmp;
            }
            else if (path.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase))
            {
                protocol = MediaProtocol.Rtsp;
            }
            else if (path.StartsWith("udp", StringComparison.OrdinalIgnoreCase))
            {
                protocol = MediaProtocol.Udp;
            }
            else if (path.StartsWith("rtp", StringComparison.OrdinalIgnoreCase))
            {
                protocol = MediaProtocol.Rtmp;
            }

            Uri uri;
            var isRemote = true;
            if (Uri.TryCreate(path, UriKind.Absolute, out uri))
            {
                isRemote = !_networkManager.IsInLocalNetwork(uri.Host);
            }

            var mediaSource = new MediaSourceInfo
            {
                Path = path,
                Protocol = protocol,
                MediaStreams = new List<MediaStream>
                {
                    new MediaStream
                    {
                        Type = MediaStreamType.Video,
                        // Set the index to -1 because we don't know the exact index of the video stream within the container
                        Index = -1,
                        IsInterlaced = true
                    },
                    new MediaStream
                    {
                        Type = MediaStreamType.Audio,
                        // Set the index to -1 because we don't know the exact index of the audio stream within the container
                        Index = -1

                    }
                },
                RequiresOpening = true,
                RequiresClosing = true,
                RequiresLooping = info.EnableStreamLooping,
                EnableMpDecimate = info.EnableMpDecimate,

                ReadAtNativeFramerate = false,

                Id = channel.Path.GetMD5().ToString("N"),
                IsInfiniteStream = true,
                IsRemote = isRemote,

                IgnoreDts = true
            };

            mediaSource.InferTotalBitrate();

            return mediaSource;
        }

        protected override Task<bool> IsAvailableInternal(TunerHostInfo tuner, string channelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
        {
            return Task.FromResult(new List<TunerHostInfo>());
        }
    }
}