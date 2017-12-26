﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.System;

namespace Emby.Server.Implementations.LiveTv.TunerHosts.HdHomerun
{
    public class HdHomerunHost : BaseTunerHost, ITunerHost, IConfigurableTunerHost
    {
        private readonly IHttpClient _httpClient;
        private readonly IServerApplicationHost _appHost;
        private readonly ISocketFactory _socketFactory;
        private readonly INetworkManager _networkManager;
        private readonly IEnvironmentInfo _environment;

        public HdHomerunHost(IServerConfigurationManager config, ILogger logger, IJsonSerializer jsonSerializer, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IHttpClient httpClient, IServerApplicationHost appHost, ISocketFactory socketFactory, INetworkManager networkManager, IEnvironmentInfo environment) : base(config, logger, jsonSerializer, mediaEncoder, fileSystem)
        {
            _httpClient = httpClient;
            _appHost = appHost;
            _socketFactory = socketFactory;
            _networkManager = networkManager;
            _environment = environment;
        }

        public string Name
        {
            get { return "HD Homerun"; }
        }

        public override string Type
        {
            get { return DeviceType; }
        }

        public static string DeviceType
        {
            get { return "hdhomerun"; }
        }

        protected override string ChannelIdPrefix
        {
            get
            {
                return "hdhr_";
            }
        }

        private string GetChannelId(TunerHostInfo info, Channels i)
        {
            var id = ChannelIdPrefix + i.GuideNumber;

            if (!info.EnableNewHdhrChannelIds)
            {
                id += '_' + (i.GuideName ?? string.Empty).GetMD5().ToString("N");
            }

            return id;
        }

        private async Task<List<Channels>> GetLineup(TunerHostInfo info, CancellationToken cancellationToken)
        {
            var model = await GetModelInfo(info, false, cancellationToken).ConfigureAwait(false);

            var options = new HttpRequestOptions
            {
                Url = model.LineupURL,
                CancellationToken = cancellationToken,
                BufferContent = false
            };
            using (var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false))
            {
                using (var stream = response.Content)
                {
                    var lineup = JsonSerializer.DeserializeFromStream<List<Channels>>(stream) ?? new List<Channels>();

                    if (info.ImportFavoritesOnly)
                    {
                        lineup = lineup.Where(i => i.Favorite).ToList();
                    }

                    return lineup.Where(i => !i.DRM).ToList();
                }
            }
        }

        private class HdHomerunChannelInfo : ChannelInfo
        {
            public bool IsLegacyTuner { get; set; }
        }

        protected override async Task<List<ChannelInfo>> GetChannelsInternal(TunerHostInfo info, CancellationToken cancellationToken)
        {
            var lineup = await GetLineup(info, cancellationToken).ConfigureAwait(false);

            return lineup.Select(i => new HdHomerunChannelInfo
            {
                Name = i.GuideName,
                Number = i.GuideNumber,
                Id = GetChannelId(info, i),
                IsFavorite = i.Favorite,
                TunerHostId = info.Id,
                IsHD = i.HD == 1,
                AudioCodec = i.AudioCodec,
                VideoCodec = i.VideoCodec,
                ChannelType = ChannelType.TV,
                IsLegacyTuner = (i.URL ?? string.Empty).StartsWith("hdhomerun", StringComparison.OrdinalIgnoreCase),
                Path = i.URL

            }).Cast<ChannelInfo>().ToList();
        }

        private readonly Dictionary<string, DiscoverResponse> _modelCache = new Dictionary<string, DiscoverResponse>();
        private async Task<DiscoverResponse> GetModelInfo(TunerHostInfo info, bool throwAllExceptions, CancellationToken cancellationToken)
        {
            var cacheKey = info.Id;

            lock (_modelCache)
            {
                if (!string.IsNullOrWhiteSpace(cacheKey))
                {
                    DiscoverResponse response;
                    if (_modelCache.TryGetValue(cacheKey, out response))
                    {
                        return response;
                    }
                }
            }

            try
            {
                using (var response = await _httpClient.SendAsync(new HttpRequestOptions()
                {
                    Url = string.Format("{0}/discover.json", GetApiUrl(info)),
                    CancellationToken = cancellationToken,
                    TimeoutMs = Convert.ToInt32(TimeSpan.FromSeconds(10).TotalMilliseconds),
                    BufferContent = false

                }, "GET").ConfigureAwait(false))
                {
                    using (var stream = response.Content)
                    {
                        var discoverResponse = JsonSerializer.DeserializeFromStream<DiscoverResponse>(stream);

                        if (!string.IsNullOrWhiteSpace(cacheKey))
                        {
                            lock (_modelCache)
                            {
                                _modelCache[cacheKey] = discoverResponse;
                            }
                        }

                        return discoverResponse;
                    }
                }
            }
            catch (HttpException ex)
            {
                if (!throwAllExceptions && ex.StatusCode.HasValue && ex.StatusCode.Value == System.Net.HttpStatusCode.NotFound)
                {
                    var defaultValue = "HDHR";
                    var response = new DiscoverResponse
                    {
                        ModelNumber = defaultValue
                    };
                    if (!string.IsNullOrWhiteSpace(cacheKey))
                    {
                        // HDHR4 doesn't have this api
                        lock (_modelCache)
                        {
                            _modelCache[cacheKey] = response;
                        }
                    }
                    return response;
                }

                throw;
            }
        }

        private async Task<List<LiveTvTunerInfo>> GetTunerInfosHttp(TunerHostInfo info, CancellationToken cancellationToken)
        {
            var model = await GetModelInfo(info, false, cancellationToken).ConfigureAwait(false);

            using (var stream = await _httpClient.Get(new HttpRequestOptions()
            {
                Url = string.Format("{0}/tuners.html", GetApiUrl(info)),
                CancellationToken = cancellationToken,
                TimeoutMs = Convert.ToInt32(TimeSpan.FromSeconds(5).TotalMilliseconds),
                BufferContent = false
            }))
            {
                var tuners = new List<LiveTvTunerInfo>();
                using (var sr = new StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    while (!sr.EndOfStream)
                    {
                        string line = StripXML(sr.ReadLine());
                        if (line.Contains("Channel"))
                        {
                            LiveTvTunerStatus status;
                            var index = line.IndexOf("Channel", StringComparison.OrdinalIgnoreCase);
                            var name = line.Substring(0, index - 1);
                            var currentChannel = line.Substring(index + 7);
                            if (currentChannel != "none") { status = LiveTvTunerStatus.LiveTv; } else { status = LiveTvTunerStatus.Available; }
                            tuners.Add(new LiveTvTunerInfo
                            {
                                Name = name,
                                SourceType = string.IsNullOrWhiteSpace(model.ModelNumber) ? Name : model.ModelNumber,
                                ProgramName = currentChannel,
                                Status = status
                            });
                        }
                    }
                }
                return tuners;
            }
        }

        private static string StripXML(string source)
        {
            char[] buffer = new char[source.Length];
            int bufferIndex = 0;
            bool inside = false;

            for (int i = 0; i < source.Length; i++)
            {
                char let = source[i];
                if (let == '<')
                {
                    inside = true;
                    continue;
                }
                if (let == '>')
                {
                    inside = false;
                    continue;
                }
                if (!inside)
                {
                    buffer[bufferIndex] = let;
                    bufferIndex++;
                }
            }
            return new string(buffer, 0, bufferIndex);
        }

        private async Task<List<LiveTvTunerInfo>> GetTunerInfosUdp(TunerHostInfo info, CancellationToken cancellationToken)
        {
            var model = await GetModelInfo(info, false, cancellationToken).ConfigureAwait(false);

            var tuners = new List<LiveTvTunerInfo>();

            var uri = new Uri(GetApiUrl(info));

            using (var manager = new HdHomerunManager(_socketFactory, Logger))
            {
                // Legacy HdHomeruns are IPv4 only
                var ipInfo = _networkManager.ParseIpAddress(uri.Host);

                for (int i = 0; i < model.TunerCount; ++i)
                {
                    var name = String.Format("Tuner {0}", i + 1);
                    var currentChannel = "none"; /// @todo Get current channel and map back to Station Id      
                    var isAvailable = await manager.CheckTunerAvailability(ipInfo, i, cancellationToken).ConfigureAwait(false);
                    LiveTvTunerStatus status = isAvailable ? LiveTvTunerStatus.Available : LiveTvTunerStatus.LiveTv;
                    tuners.Add(new LiveTvTunerInfo
                    {
                        Name = name,
                        SourceType = string.IsNullOrWhiteSpace(model.ModelNumber) ? Name : model.ModelNumber,
                        ProgramName = currentChannel,
                        Status = status
                    });
                }
            }
            return tuners;
        }

        public async Task<List<LiveTvTunerInfo>> GetTunerInfos(CancellationToken cancellationToken)
        {
            var list = new List<LiveTvTunerInfo>();

            foreach (var host in GetConfiguration().TunerHosts
                .Where(i => string.Equals(i.Type, Type, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    list.AddRange(await GetTunerInfos(host, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error getting tuner info", ex);
                }
            }

            return list;
        }

        public async Task<List<LiveTvTunerInfo>> GetTunerInfos(TunerHostInfo info, CancellationToken cancellationToken)
        {
            // TODO Need faster way to determine UDP vs HTTP
            var channels = await GetChannels(info, true, cancellationToken);

            var hdHomerunChannelInfo = channels.FirstOrDefault() as HdHomerunChannelInfo;

            if (hdHomerunChannelInfo == null || hdHomerunChannelInfo.IsLegacyTuner)
            {
                return await GetTunerInfosUdp(info, cancellationToken).ConfigureAwait(false);
            }

            return await GetTunerInfosHttp(info, cancellationToken).ConfigureAwait(false);
        }

        private string GetApiUrl(TunerHostInfo info)
        {
            var url = info.Url;

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Invalid tuner info");
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            return new Uri(url).AbsoluteUri.TrimEnd('/');
        }

        private class Channels
        {
            public string GuideNumber { get; set; }
            public string GuideName { get; set; }
            public string VideoCodec { get; set; }
            public string AudioCodec { get; set; }
            public string URL { get; set; }
            public bool Favorite { get; set; }
            public bool DRM { get; set; }
            public int HD { get; set; }
        }

        protected EncodingOptions GetEncodingOptions()
        {
            return Config.GetConfiguration<EncodingOptions>("encoding");
        }

        private string GetHdHrIdFromChannelId(string channelId)
        {
            return channelId.Split('_')[1];
        }

        private MediaSourceInfo GetMediaSource(TunerHostInfo info, string channelId, ChannelInfo channelInfo, string profile)
        {
            int? width = null;
            int? height = null;
            bool isInterlaced = true;
            string videoCodec = null;
            string audioCodec = null;

            int? videoBitrate = null;
            int? audioBitrate = null;

            var isHd = channelInfo.IsHD ?? true;

            if (string.Equals(profile, "mobile", StringComparison.OrdinalIgnoreCase))
            {
                width = 1280;
                height = 720;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 2000000;
            }
            else if (string.Equals(profile, "heavy", StringComparison.OrdinalIgnoreCase))
            {
                width = 1920;
                height = 1080;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 15000000;
            }
            else if (string.Equals(profile, "internet720", StringComparison.OrdinalIgnoreCase))
            {
                width = 1280;
                height = 720;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 8000000;
            }
            else if (string.Equals(profile, "internet540", StringComparison.OrdinalIgnoreCase))
            {
                width = 960;
                height = 540;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 2500000;
            }
            else if (string.Equals(profile, "internet480", StringComparison.OrdinalIgnoreCase))
            {
                width = 848;
                height = 480;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 2000000;
            }
            else if (string.Equals(profile, "internet360", StringComparison.OrdinalIgnoreCase))
            {
                width = 640;
                height = 360;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 1500000;
            }
            else if (string.Equals(profile, "internet240", StringComparison.OrdinalIgnoreCase))
            {
                width = 432;
                height = 240;
                isInterlaced = false;
                videoCodec = "h264";
                videoBitrate = 1000000;
            }
            else
            {
                // This is for android tv's 1200 condition. Remove once not needed anymore so that we can avoid possible side effects of dummying up this data
                if (isHd)
                {
                    width = 1920;
                    height = 1080;
                }
            }

            if (channelInfo != null)
            {
                if (string.IsNullOrWhiteSpace(videoCodec))
                {
                    videoCodec = channelInfo.VideoCodec;
                }
                audioCodec = channelInfo.AudioCodec;

                if (!videoBitrate.HasValue)
                {
                    videoBitrate = isHd ? 15000000 : 2000000;
                }
                audioBitrate = isHd ? 448000 : 192000;
            }

            // normalize
            if (string.Equals(videoCodec, "mpeg2", StringComparison.OrdinalIgnoreCase))
            {
                videoCodec = "mpeg2video";
            }

            string nal = null;
            if (string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                nal = "0";
            }

            var url = GetApiUrl(info);

            var id = profile;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "native";
            }
            id += "_" + channelId.GetMD5().ToString("N") + "_" + url.GetMD5().ToString("N");

            var mediaSource = new MediaSourceInfo
            {
                Path = url,
                Protocol = MediaProtocol.Udp,
                MediaStreams = new List<MediaStream>
                        {
                            new MediaStream
                            {
                                Type = MediaStreamType.Video,
                                // Set the index to -1 because we don't know the exact index of the video stream within the container
                                Index = -1,
                                IsInterlaced = isInterlaced,
                                Codec = videoCodec,
                                Width = width,
                                Height = height,
                                BitRate = videoBitrate,
                                NalLengthSize = nal

                            },
                            new MediaStream
                            {
                                Type = MediaStreamType.Audio,
                                // Set the index to -1 because we don't know the exact index of the audio stream within the container
                                Index = -1,
                                Codec = audioCodec,
                                BitRate = audioBitrate
                            }
                        },
                RequiresOpening = true,
                RequiresClosing = true,
                BufferMs = 0,
                Container = "ts",
                Id = id,
                SupportsDirectPlay = false,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
                IsInfiniteStream = true,
                IgnoreDts = true,
                //AnalyzeDurationMs = 2000000
                //IgnoreIndex = true,
                //ReadAtNativeFramerate = true
            };

            mediaSource.InferTotalBitrate();

            return mediaSource;
        }

        protected override async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(TunerHostInfo info, string channelId, CancellationToken cancellationToken)
        {
            var list = new List<MediaSourceInfo>();

            if (!channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return list;
            }
            var hdhrId = GetHdHrIdFromChannelId(channelId);

            var channels = await GetChannels(info, true, CancellationToken.None).ConfigureAwait(false);
            var channelInfo = channels.FirstOrDefault(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase));

            var hdHomerunChannelInfo = channelInfo as HdHomerunChannelInfo;

            var isLegacyTuner = hdHomerunChannelInfo != null && hdHomerunChannelInfo.IsLegacyTuner;

            if (isLegacyTuner)
            {
                list.Add(GetMediaSource(info, hdhrId, channelInfo, "native"));
            }
            else
            {
                try
                {
                    var modelInfo = await GetModelInfo(info, false, cancellationToken).ConfigureAwait(false);

                    if (modelInfo != null && modelInfo.SupportsTranscoding)
                    {
                        list.Add(GetMediaSource(info, hdhrId, channelInfo, "native"));

                        if (info.AllowHWTranscoding)
                        {
                            list.Add(GetMediaSource(info, hdhrId, channelInfo, "heavy"));

                            list.Add(GetMediaSource(info, hdhrId, channelInfo, "internet540"));
                            list.Add(GetMediaSource(info, hdhrId, channelInfo, "internet480"));
                            list.Add(GetMediaSource(info, hdhrId, channelInfo, "internet360"));
                            list.Add(GetMediaSource(info, hdhrId, channelInfo, "internet240"));
                            list.Add(GetMediaSource(info, hdhrId, channelInfo, "mobile"));
                        }
                    }
                }
                catch
                {

                }

                if (list.Count == 0)
                {
                    list.Add(GetMediaSource(info, hdhrId, channelInfo, "native"));
                }
            }

            return list;
        }

        protected override async Task<ILiveStream> GetChannelStream(TunerHostInfo info, string channelId, string streamId, CancellationToken cancellationToken)
        {
            var profile = streamId.Split('_')[0];

            Logger.Info("GetChannelStream: channel id: {0}. stream id: {1} profile: {2}", channelId, streamId, profile);

            var hdhrId = GetHdHrIdFromChannelId(channelId);

            var channels = await GetChannels(info, true, CancellationToken.None).ConfigureAwait(false);
            var channelInfo = channels.FirstOrDefault(i => string.Equals(i.Id, channelId, StringComparison.OrdinalIgnoreCase));

            var hdhomerunChannel = channelInfo as HdHomerunChannelInfo;

            var modelInfo = await GetModelInfo(info, false, cancellationToken).ConfigureAwait(false);

            if (!modelInfo.SupportsTranscoding)
            {
                profile = "native";
            }

            var mediaSource = GetMediaSource(info, hdhrId, channelInfo, profile);

            if (hdhomerunChannel != null && hdhomerunChannel.IsLegacyTuner)
            {
                return new HdHomerunUdpStream(mediaSource, info, streamId, new LegacyHdHomerunChannelCommands(hdhomerunChannel.Path), modelInfo.TunerCount, FileSystem, _httpClient, Logger, Config.ApplicationPaths, _appHost, _socketFactory, _networkManager, _environment);
            }

            // The UDP method is not working reliably on OSX, and on BSD it hasn't been tested yet
            var enableHttpStream = _environment.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.OSX
                || _environment.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.BSD;
            enableHttpStream = true;
            if (enableHttpStream)
            {
                mediaSource.Protocol = MediaProtocol.Http;

                var httpUrl = channelInfo.Path;

                // If raw was used, the tuner doesn't support params
                if (!string.IsNullOrWhiteSpace(profile) && !string.Equals(profile, "native", StringComparison.OrdinalIgnoreCase))
                {
                    httpUrl += "?transcode=" + profile;
                }
                mediaSource.Path = httpUrl;

                return new SharedHttpStream(mediaSource, info, streamId, FileSystem, _httpClient, Logger, Config.ApplicationPaths, _appHost, _environment);
            }

            return new HdHomerunUdpStream(mediaSource, info, streamId, new HdHomerunChannelCommands(hdhomerunChannel.Number, profile), modelInfo.TunerCount, FileSystem, _httpClient, Logger, Config.ApplicationPaths, _appHost, _socketFactory, _networkManager, _environment);
        }

        public async Task Validate(TunerHostInfo info)
        {
            lock (_modelCache)
            {
                _modelCache.Clear();
            }

            try
            {
                // Test it by pulling down the lineup
                var modelInfo = await GetModelInfo(info, true, CancellationToken.None).ConfigureAwait(false);
                info.DeviceId = modelInfo.DeviceID;
            }
            catch (HttpException ex)
            {
                if (ex.StatusCode.HasValue && ex.StatusCode.Value == System.Net.HttpStatusCode.NotFound)
                {
                    // HDHR4 doesn't have this api
                    return;
                }

                throw;
            }
        }

        protected override async Task<bool> IsAvailableInternal(TunerHostInfo tuner, string channelId, CancellationToken cancellationToken)
        {
            var info = await GetTunerInfos(tuner, cancellationToken).ConfigureAwait(false);

            return info.Any(i => i.Status == LiveTvTunerStatus.Available);
        }

        public class DiscoverResponse
        {
            public string FriendlyName { get; set; }
            public string ModelNumber { get; set; }
            public string FirmwareName { get; set; }
            public string FirmwareVersion { get; set; }
            public string DeviceID { get; set; }
            public string DeviceAuth { get; set; }
            public string BaseURL { get; set; }
            public string LineupURL { get; set; }
            public int TunerCount { get; set; }

            public bool SupportsTranscoding
            {
                get
                {
                    var model = ModelNumber ?? string.Empty;

                    if ((model.IndexOf("hdtc", StringComparison.OrdinalIgnoreCase) != -1))
                    {
                        return true;
                    }

                    return false;
                }
            }
        }

        public async Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
        {
            lock (_modelCache)
            {
                _modelCache.Clear();
            }

            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(new CancellationTokenSource(discoveryDurationMs).Token, cancellationToken).Token;
            var list = new List<TunerHostInfo>();

            // Create udp broadcast discovery message
            byte[] discBytes = { 0, 2, 0, 12, 1, 4, 255, 255, 255, 255, 2, 4, 255, 255, 255, 255, 115, 204, 125, 143 };
            using (var udpClient = _socketFactory.CreateUdpBroadcastSocket(0))
            {
                // Need a way to set the Receive timeout on the socket otherwise this might never timeout?
                try
                {
                    await udpClient.SendToAsync(discBytes, 0, discBytes.Length, new IpEndPointInfo(new IpAddressInfo("255.255.255.255", IpAddressFamily.InterNetwork), 65001), cancellationToken);
                    var receiveBuffer = new byte[8192];

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var response = await udpClient.ReceiveAsync(receiveBuffer, 0, receiveBuffer.Length, cancellationToken).ConfigureAwait(false);
                        var deviceIp = response.RemoteEndPoint.IpAddress.Address;

                        // check to make sure we have enough bytes received to be a valid message and make sure the 2nd byte is the discover reply byte
                        if (response.ReceivedBytes > 13 && response.Buffer[1] == 3)
                        {
                            var deviceAddress = "http://" + deviceIp;

                            var info = await TryGetTunerHostInfo(deviceAddress, cancellationToken).ConfigureAwait(false);

                            if (info != null)
                            {
                                list.Add(info);
                            }
                        }
                    }

                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    // Socket timeout indicates all messages have been received.
                }
            }

            return list;
        }

        private async Task<TunerHostInfo> TryGetTunerHostInfo(string url, CancellationToken cancellationToken)
        {
            var hostInfo = new TunerHostInfo
            {
                Type = Type,
                Url = url
            };

            try
            {
                var modelInfo = await GetModelInfo(hostInfo, false, cancellationToken).ConfigureAwait(false);

                hostInfo.DeviceId = modelInfo.DeviceID;
                hostInfo.FriendlyName = modelInfo.FriendlyName;

                return hostInfo;
            }
            catch
            {
                // logged at lower levels
            }

            return null;
        }
    }
}
