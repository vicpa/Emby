﻿using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MediaBrowser.Model.Dlna
{
    public class StreamBuilder
    {
        private readonly ILogger _logger;
        private readonly ITranscoderSupport _transcoderSupport;

        public StreamBuilder(ITranscoderSupport transcoderSupport, ILogger logger)
        {
            _transcoderSupport = transcoderSupport;
            _logger = logger;
        }

        public StreamBuilder(ILogger logger)
            : this(new FullTranscoderSupport(), logger)
        {
        }

        public StreamInfo BuildAudioItem(AudioOptions options)
        {
            ValidateAudioInput(options);

            var mediaSources = new List<MediaSourceInfo>();
            foreach (MediaSourceInfo i in options.MediaSources)
            {
                if (string.IsNullOrEmpty(options.MediaSourceId) ||
                    StringHelper.EqualsIgnoreCase(i.Id, options.MediaSourceId))
                {
                    mediaSources.Add(i);
                }
            }

            var streams = new List<StreamInfo>();
            foreach (MediaSourceInfo i in mediaSources)
            {
                StreamInfo streamInfo = BuildAudioItem(i, options);
                if (streamInfo != null)
                {
                    streams.Add(streamInfo);
                }
            }

            foreach (StreamInfo stream in streams)
            {
                stream.DeviceId = options.DeviceId;
                stream.DeviceProfileId = options.Profile.Id;
            }

            return GetOptimalStream(streams, options.GetMaxBitrate(true));
        }

        public StreamInfo BuildVideoItem(VideoOptions options)
        {
            ValidateInput(options);

            var mediaSources = new List<MediaSourceInfo>();
            foreach (MediaSourceInfo i in options.MediaSources)
            {
                if (string.IsNullOrEmpty(options.MediaSourceId) ||
                    StringHelper.EqualsIgnoreCase(i.Id, options.MediaSourceId))
                {
                    mediaSources.Add(i);
                }
            }

            var streams = new List<StreamInfo>();
            foreach (MediaSourceInfo i in mediaSources)
            {
                StreamInfo streamInfo = BuildVideoItem(i, options);
                if (streamInfo != null)
                {
                    streams.Add(streamInfo);
                }
            }

            foreach (StreamInfo stream in streams)
            {
                stream.DeviceId = options.DeviceId;
                stream.DeviceProfileId = options.Profile.Id;
            }

            return GetOptimalStream(streams, options.GetMaxBitrate(false));
        }

        private StreamInfo GetOptimalStream(List<StreamInfo> streams, long? maxBitrate)
        {
            var sorted = SortMediaSources(streams, maxBitrate);

            foreach (StreamInfo stream in sorted)
            {
                return stream;
            }

            return null;
        }

        private StreamInfo[] SortMediaSources(List<StreamInfo> streams, long? maxBitrate)
        {
            return streams.OrderBy(i =>
            {
                // Nothing beats direct playing a file
                if (i.PlayMethod == PlayMethod.DirectPlay && i.MediaSource.Protocol == MediaProtocol.File)
                {
                    return 0;
                }

                return 1;

            }).ThenBy(i =>
            {
                switch (i.PlayMethod)
                {
                    // Let's assume direct streaming a file is just as desirable as direct playing a remote url
                    case PlayMethod.DirectStream:
                    case PlayMethod.DirectPlay:
                        return 0;
                    default:
                        return 1;
                }

            }).ThenBy(i =>
            {
                switch (i.MediaSource.Protocol)
                {
                    case MediaProtocol.File:
                        return 0;
                    default:
                        return 1;
                }

            }).ThenBy(i =>
            {
                if (maxBitrate.HasValue)
                {
                    if (i.MediaSource.Bitrate.HasValue)
                    {
                        return Math.Abs(i.MediaSource.Bitrate.Value - maxBitrate.Value);
                    }
                }

                return 0;

            }).ThenBy(streams.IndexOf).ToArray();
        }

        private TranscodeReason? GetTranscodeReasonForFailedCondition(ProfileCondition condition)
        {
            switch (condition.Property)
            {
                case ProfileConditionValue.AudioBitrate:
                    if (condition.Condition == ProfileConditionType.LessThanEqual)
                    {
                        return TranscodeReason.AudioBitrateNotSupported;
                    }
                    return TranscodeReason.AudioBitrateNotSupported;

                case ProfileConditionValue.AudioChannels:
                    if (condition.Condition == ProfileConditionType.LessThanEqual)
                    {
                        return TranscodeReason.AudioChannelsNotSupported;
                    }
                    return TranscodeReason.AudioChannelsNotSupported;

                case ProfileConditionValue.AudioProfile:
                    return TranscodeReason.AudioProfileNotSupported;

                case ProfileConditionValue.AudioSampleRate:
                    return TranscodeReason.AudioSampleRateNotSupported;

                case ProfileConditionValue.Has64BitOffsets:
                    // TODO
                    return null;

                case ProfileConditionValue.Height:
                    return TranscodeReason.VideoResolutionNotSupported;

                case ProfileConditionValue.IsAnamorphic:
                    return TranscodeReason.AnamorphicVideoNotSupported;

                case ProfileConditionValue.IsAvc:
                    // TODO
                    return null;

                case ProfileConditionValue.IsInterlaced:
                    return TranscodeReason.InterlacedVideoNotSupported;

                case ProfileConditionValue.IsSecondaryAudio:
                    return TranscodeReason.SecondaryAudioNotSupported;

                case ProfileConditionValue.NumAudioStreams:
                    // TODO
                    return null;

                case ProfileConditionValue.NumVideoStreams:
                    // TODO
                    return null;

                case ProfileConditionValue.PacketLength:
                    // TODO
                    return null;

                case ProfileConditionValue.RefFrames:
                    return TranscodeReason.RefFramesNotSupported;

                case ProfileConditionValue.VideoBitDepth:
                    return TranscodeReason.VideoBitDepthNotSupported;

                case ProfileConditionValue.AudioBitDepth:
                    return TranscodeReason.AudioBitDepthNotSupported;

                case ProfileConditionValue.VideoBitrate:
                    return TranscodeReason.VideoBitrateNotSupported;

                case ProfileConditionValue.VideoCodecTag:
                    return TranscodeReason.VideoCodecNotSupported;

                case ProfileConditionValue.VideoFramerate:
                    return TranscodeReason.VideoFramerateNotSupported;

                case ProfileConditionValue.VideoLevel:
                    return TranscodeReason.VideoLevelNotSupported;

                case ProfileConditionValue.VideoProfile:
                    return TranscodeReason.VideoProfileNotSupported;

                case ProfileConditionValue.VideoTimestamp:
                    // TODO
                    return null;

                case ProfileConditionValue.Width:
                    return TranscodeReason.VideoResolutionNotSupported;

                default:
                    return null;
            }
        }

        public static string NormalizeMediaSourceFormatIntoSingleContainer(string inputContainer, DeviceProfile profile, DlnaProfileType type)
        {
            if (string.IsNullOrWhiteSpace(inputContainer))
            {
                return null;
            }

            var formats = ContainerProfile.SplitValue(inputContainer);

            if (formats.Length == 1)
            {
                return formats[0];
            }

            if (profile != null)
            {
                foreach (var format in formats)
                {
                    foreach (var directPlayProfile in profile.DirectPlayProfiles)
                    {
                        if (directPlayProfile.Type == type)
                        {
                            if (directPlayProfile.SupportsContainer(format))
                            {
                                return format;
                            }
                        }
                    }
                }
            }

            return formats[0];
        }

        private StreamInfo BuildAudioItem(MediaSourceInfo item, AudioOptions options)
        {
            var transcodeReasons = new List<TranscodeReason>();

            StreamInfo playlistItem = new StreamInfo
            {
                ItemId = options.ItemId,
                MediaType = DlnaProfileType.Audio,
                MediaSource = item,
                RunTimeTicks = item.RunTimeTicks,
                Context = options.Context,
                DeviceProfile = options.Profile
            };

            if (options.ForceDirectPlay)
            {
                playlistItem.PlayMethod = PlayMethod.DirectPlay;
                playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Audio);
                return playlistItem;
            }

            if (options.ForceDirectStream)
            {
                playlistItem.PlayMethod = PlayMethod.DirectStream;
                playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Audio);
                return playlistItem;
            }

            MediaStream audioStream = item.GetDefaultAudioStream(null);

            var directPlayInfo = GetAudioDirectPlayMethods(item, audioStream, options);

            var directPlayMethods = directPlayInfo.Item1;
            transcodeReasons.AddRange(directPlayInfo.Item2);

            ConditionProcessor conditionProcessor = new ConditionProcessor();

            int? inputAudioChannels = audioStream == null ? null : audioStream.Channels;
            int? inputAudioBitrate = audioStream == null ? null : audioStream.BitDepth;
            int? inputAudioSampleRate = audioStream == null ? null : audioStream.SampleRate;
            int? inputAudioBitDepth = audioStream == null ? null : audioStream.BitDepth;

            if (directPlayMethods.Count > 0)
            {
                string audioCodec = audioStream == null ? null : audioStream.Codec;

                // Make sure audio codec profiles are satisfied
                var conditions = new List<ProfileCondition>();
                foreach (CodecProfile i in options.Profile.CodecProfiles)
                {
                    if (i.Type == CodecType.Audio && i.ContainsAnyCodec(audioCodec, item.Container))
                    {
                        bool applyConditions = true;
                        foreach (ProfileCondition applyCondition in i.ApplyConditions)
                        {
                            if (!conditionProcessor.IsAudioConditionSatisfied(applyCondition, inputAudioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth))
                            {
                                LogConditionFailure(options.Profile, "AudioCodecProfile", applyCondition, item);
                                applyConditions = false;
                                break;
                            }
                        }

                        if (applyConditions)
                        {
                            foreach (ProfileCondition c in i.Conditions)
                            {
                                conditions.Add(c);
                            }
                        }
                    }
                }

                bool all = true;
                foreach (ProfileCondition c in conditions)
                {
                    if (!conditionProcessor.IsAudioConditionSatisfied(c, inputAudioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth))
                    {
                        LogConditionFailure(options.Profile, "AudioCodecProfile", c, item);
                        var transcodeReason = GetTranscodeReasonForFailedCondition(c);
                        if (transcodeReason.HasValue)
                        {
                            transcodeReasons.Add(transcodeReason.Value);
                        }
                        all = false;
                        break;
                    }
                }

                if (all)
                {
                    if (directPlayMethods.Contains(PlayMethod.DirectStream))
                    {
                        playlistItem.PlayMethod = PlayMethod.DirectStream;
                    }

                    playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Audio);

                    return playlistItem;
                }
            }

            TranscodingProfile transcodingProfile = null;
            foreach (TranscodingProfile i in options.Profile.TranscodingProfiles)
            {
                if (i.Type == playlistItem.MediaType && i.Context == options.Context)
                {
                    if (_transcoderSupport.CanEncodeToAudioCodec(i.AudioCodec ?? i.Container))
                    {
                        transcodingProfile = i;
                        break;
                    }
                }
            }

            if (transcodingProfile != null)
            {
                if (!item.SupportsTranscoding)
                {
                    return null;
                }

                playlistItem.PlayMethod = PlayMethod.Transcode;
                playlistItem.TranscodeSeekInfo = transcodingProfile.TranscodeSeekInfo;
                playlistItem.EstimateContentLength = transcodingProfile.EstimateContentLength;
                playlistItem.Container = transcodingProfile.Container;

                if (string.IsNullOrEmpty(transcodingProfile.AudioCodec))
                {
                    playlistItem.AudioCodecs = new string[] { };
                }
                else
                {
                    playlistItem.AudioCodecs = transcodingProfile.AudioCodec.Split(',');
                }

                playlistItem.SubProtocol = transcodingProfile.Protocol;

                var audioCodecProfiles = new List<CodecProfile>();
                foreach (CodecProfile i in options.Profile.CodecProfiles)
                {
                    if (i.Type == CodecType.Audio && i.ContainsAnyCodec(transcodingProfile.AudioCodec, transcodingProfile.Container))
                    {
                        audioCodecProfiles.Add(i);
                    }

                    if (audioCodecProfiles.Count >= 1) break;
                }

                var audioTranscodingConditions = new List<ProfileCondition>();
                foreach (CodecProfile i in audioCodecProfiles)
                {
                    bool applyConditions = true;
                    foreach (ProfileCondition applyCondition in i.ApplyConditions)
                    {
                        if (!conditionProcessor.IsAudioConditionSatisfied(applyCondition, inputAudioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth))
                        {
                            LogConditionFailure(options.Profile, "AudioCodecProfile", applyCondition, item);
                            applyConditions = false;
                            break;
                        }
                    }

                    if (applyConditions)
                    {
                        foreach (ProfileCondition c in i.Conditions)
                        {
                            audioTranscodingConditions.Add(c);
                        }
                    }
                }

                ApplyTranscodingConditions(playlistItem, audioTranscodingConditions, null, false);

                // Honor requested max channels
                if (options.MaxAudioChannels.HasValue)
                {
                    int currentValue = playlistItem.MaxAudioChannels ?? options.MaxAudioChannels.Value;

                    playlistItem.MaxAudioChannels = Math.Min(options.MaxAudioChannels.Value, currentValue);
                }

                long transcodingBitrate = options.AudioTranscodingBitrate ??
                    options.Profile.MusicStreamingTranscodingBitrate ??
                    128000;

                var configuredBitrate = options.GetMaxBitrate(true);

                if (configuredBitrate.HasValue)
                {
                    transcodingBitrate = Math.Min(configuredBitrate.Value, transcodingBitrate);
                }

                var longBitrate = Math.Min(transcodingBitrate, playlistItem.AudioBitrate ?? transcodingBitrate);
                playlistItem.AudioBitrate = longBitrate > int.MaxValue ? int.MaxValue : Convert.ToInt32(longBitrate);
            }

            playlistItem.TranscodeReasons = transcodeReasons;
            return playlistItem;
        }

        private long? GetBitrateForDirectPlayCheck(MediaSourceInfo item, AudioOptions options, bool isAudio)
        {
            if (item.Protocol == MediaProtocol.File)
            {
                return options.Profile.MaxStaticBitrate;
            }

            return options.GetMaxBitrate(isAudio);
        }

        private Tuple<List<PlayMethod>, List<TranscodeReason>> GetAudioDirectPlayMethods(MediaSourceInfo item, MediaStream audioStream, AudioOptions options)
        {
            var transcodeReasons = new List<TranscodeReason>();

            DirectPlayProfile directPlayProfile = null;
            foreach (DirectPlayProfile i in options.Profile.DirectPlayProfiles)
            {
                if (i.Type == DlnaProfileType.Audio && IsAudioDirectPlaySupported(i, item, audioStream))
                {
                    directPlayProfile = i;
                    break;
                }
            }

            var playMethods = new List<PlayMethod>();

            if (directPlayProfile != null)
            {
                // While options takes the network and other factors into account. Only applies to direct stream
                if (item.SupportsDirectStream)
                {
                    if (IsAudioEligibleForDirectPlay(item, options.GetMaxBitrate(true), PlayMethod.DirectStream))
                    {
                        if (options.EnableDirectStream)
                        {
                            playMethods.Add(PlayMethod.DirectStream);
                        }
                    }
                    else
                    {
                        transcodeReasons.Add(TranscodeReason.ContainerBitrateExceedsLimit);
                    }
                }

                // The profile describes what the device supports
                // If device requirements are satisfied then allow both direct stream and direct play
                if (item.SupportsDirectPlay)
                {
                    if (IsAudioEligibleForDirectPlay(item, GetBitrateForDirectPlayCheck(item, options, true), PlayMethod.DirectPlay))
                    {
                        if (options.EnableDirectPlay)
                        {
                            playMethods.Add(PlayMethod.DirectPlay);
                        }
                    }
                    else
                    {
                        transcodeReasons.Add(TranscodeReason.ContainerBitrateExceedsLimit);
                    }
                }
            }
            else
            {
                transcodeReasons.InsertRange(0, GetTranscodeReasonsFromDirectPlayProfile(item, null, audioStream, options.Profile.DirectPlayProfiles));

                _logger.Info("Profile: {0}, No direct play profiles found for Path: {1}",
                    options.Profile.Name ?? "Unknown Profile",
                    item.Path ?? "Unknown path");
            }

            if (playMethods.Count > 0)
            {
                transcodeReasons.Clear();
            }
            else
            {
                transcodeReasons = transcodeReasons.Distinct().ToList();
            }

            return new Tuple<List<PlayMethod>, List<TranscodeReason>>(playMethods, transcodeReasons);
        }

        private List<TranscodeReason> GetTranscodeReasonsFromDirectPlayProfile(MediaSourceInfo item, MediaStream videoStream, MediaStream audioStream, IEnumerable<DirectPlayProfile> directPlayProfiles)
        {
            var list = new List<TranscodeReason>();
            var containerSupported = false;
            var audioSupported = false;
            var videoSupported = false;

            foreach (var profile in directPlayProfiles)
            {
                // Check container type
                if (profile.SupportsContainer(item.Container))
                {
                    containerSupported = true;

                    if (videoStream != null)
                    {
                        // Check video codec
                        var videoCodecs = profile.GetVideoCodecs();
                        if (videoCodecs.Length > 0)
                        {
                            string videoCodec = videoStream.Codec;
                            if (!string.IsNullOrEmpty(videoCodec) && ListHelper.ContainsIgnoreCase(videoCodecs, videoCodec))
                            {
                                videoSupported = true;
                            }
                        }
                        else
                        {
                            videoSupported = true;
                        }
                    }

                    if (audioStream != null)
                    {
                        // Check audio codec
                        var audioCodecs = profile.GetAudioCodecs();
                        if (audioCodecs.Length > 0)
                        {
                            string audioCodec = audioStream.Codec;
                            if (!string.IsNullOrEmpty(audioCodec) && ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec))
                            {
                                audioSupported = true;
                            }
                        }
                        else
                        {
                            audioSupported = true;
                        }
                    }
                }
            }

            if (!containerSupported)
            {
                list.Add(TranscodeReason.ContainerNotSupported);
            }

            if (videoStream != null && !videoSupported)
            {
                list.Add(TranscodeReason.VideoCodecNotSupported);
            }

            if (audioStream != null && !audioSupported)
            {
                list.Add(TranscodeReason.AudioCodecNotSupported);
            }

            return list;
        }

        private int? GetDefaultSubtitleStreamIndex(MediaSourceInfo item, SubtitleProfile[] subtitleProfiles)
        {
            int highestScore = -1;

            foreach (MediaStream stream in item.MediaStreams)
            {
                if (stream.Type == MediaStreamType.Subtitle && stream.Score.HasValue)
                {
                    if (stream.Score.Value > highestScore)
                    {
                        highestScore = stream.Score.Value;
                    }
                }
            }

            var topStreams = new List<MediaStream>();
            foreach (MediaStream stream in item.MediaStreams)
            {
                if (stream.Type == MediaStreamType.Subtitle && stream.Score.HasValue && stream.Score.Value == highestScore)
                {
                    topStreams.Add(stream);
                }
            }

            // If multiple streams have an equal score, try to pick the most efficient one
            if (topStreams.Count > 1)
            {
                foreach (MediaStream stream in topStreams)
                {
                    foreach (SubtitleProfile profile in subtitleProfiles)
                    {
                        if (profile.Method == SubtitleDeliveryMethod.External && StringHelper.EqualsIgnoreCase(profile.Format, stream.Codec))
                        {
                            return stream.Index;
                        }
                    }
                }
            }

            // If no optimization panned out, just use the original default
            return item.DefaultSubtitleStreamIndex;
        }

        private StreamInfo BuildVideoItem(MediaSourceInfo item, VideoOptions options)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            var transcodeReasons = new List<TranscodeReason>();

            StreamInfo playlistItem = new StreamInfo
            {
                ItemId = options.ItemId,
                MediaType = DlnaProfileType.Video,
                MediaSource = item,
                RunTimeTicks = item.RunTimeTicks,
                Context = options.Context,
                DeviceProfile = options.Profile
            };

            playlistItem.SubtitleStreamIndex = options.SubtitleStreamIndex ?? GetDefaultSubtitleStreamIndex(item, options.Profile.SubtitleProfiles);
            MediaStream subtitleStream = playlistItem.SubtitleStreamIndex.HasValue ? item.GetMediaStream(MediaStreamType.Subtitle, playlistItem.SubtitleStreamIndex.Value) : null;

            MediaStream audioStream = item.GetDefaultAudioStream(options.AudioStreamIndex ?? item.DefaultAudioStreamIndex);
            if (audioStream != null)
            {
                playlistItem.AudioStreamIndex = audioStream.Index;
            }

            MediaStream videoStream = item.VideoStream;

            // TODO: This doesn't accout for situation of device being able to handle media bitrate, but wifi connection not fast enough
            var directPlayEligibilityResult = IsEligibleForDirectPlay(item, GetBitrateForDirectPlayCheck(item, options, true), subtitleStream, options, PlayMethod.DirectPlay);
            var directStreamEligibilityResult = IsEligibleForDirectPlay(item, options.GetMaxBitrate(false), subtitleStream, options, PlayMethod.DirectStream);
            bool isEligibleForDirectPlay = options.EnableDirectPlay && (options.ForceDirectPlay || directPlayEligibilityResult.Item1);
            bool isEligibleForDirectStream = options.EnableDirectStream && (options.ForceDirectStream || directStreamEligibilityResult.Item1);

            _logger.Info("Profile: {0}, Path: {1}, isEligibleForDirectPlay: {2}, isEligibleForDirectStream: {3}",
                options.Profile.Name ?? "Unknown Profile",
                item.Path ?? "Unknown path",
                isEligibleForDirectPlay,
                isEligibleForDirectStream);

            if (isEligibleForDirectPlay || isEligibleForDirectStream)
            {
                // See if it can be direct played
                var directPlayInfo = GetVideoDirectPlayProfile(options, item, videoStream, audioStream, isEligibleForDirectPlay, isEligibleForDirectStream);
                var directPlay = directPlayInfo.Item1;

                if (directPlay != null)
                {
                    playlistItem.PlayMethod = directPlay.Value;
                    playlistItem.Container = NormalizeMediaSourceFormatIntoSingleContainer(item.Container, options.Profile, DlnaProfileType.Video);

                    if (subtitleStream != null)
                    {
                        SubtitleProfile subtitleProfile = GetSubtitleProfile(item, subtitleStream, options.Profile.SubtitleProfiles, directPlay.Value, _transcoderSupport, null, null);

                        playlistItem.SubtitleDeliveryMethod = subtitleProfile.Method;
                        playlistItem.SubtitleFormat = subtitleProfile.Format;
                    }

                    return playlistItem;
                }

                transcodeReasons.AddRange(directPlayInfo.Item2);
            }

            if (directPlayEligibilityResult.Item2.HasValue)
            {
                transcodeReasons.Add(directPlayEligibilityResult.Item2.Value);
            }

            if (directStreamEligibilityResult.Item2.HasValue)
            {
                transcodeReasons.Add(directStreamEligibilityResult.Item2.Value);
            }

            // Can't direct play, find the transcoding profile
            TranscodingProfile transcodingProfile = null;
            foreach (TranscodingProfile i in options.Profile.TranscodingProfiles)
            {
                if (i.Type == playlistItem.MediaType && i.Context == options.Context)
                {
                    transcodingProfile = i;
                    break;
                }
            }

            if (transcodingProfile != null)
            {
                if (!item.SupportsTranscoding)
                {
                    return null;
                }

                if (subtitleStream != null)
                {
                    SubtitleProfile subtitleProfile = GetSubtitleProfile(item, subtitleStream, options.Profile.SubtitleProfiles, PlayMethod.Transcode, _transcoderSupport, transcodingProfile.Protocol, transcodingProfile.Container);

                    playlistItem.SubtitleDeliveryMethod = subtitleProfile.Method;
                    playlistItem.SubtitleFormat = subtitleProfile.Format;
                    playlistItem.SubtitleCodecs = new[] { subtitleProfile.Format };
                }

                playlistItem.PlayMethod = PlayMethod.Transcode;
                playlistItem.Container = transcodingProfile.Container;
                playlistItem.EstimateContentLength = transcodingProfile.EstimateContentLength;
                playlistItem.TranscodeSeekInfo = transcodingProfile.TranscodeSeekInfo;

                playlistItem.AudioCodecs = transcodingProfile.AudioCodec.Split(',');

                playlistItem.VideoCodecs = transcodingProfile.VideoCodec.Split(',');
                playlistItem.CopyTimestamps = transcodingProfile.CopyTimestamps;
                playlistItem.EnableSubtitlesInManifest = transcodingProfile.EnableSubtitlesInManifest;

                playlistItem.BreakOnNonKeyFrames = transcodingProfile.BreakOnNonKeyFrames;

                if (transcodingProfile.MinSegments > 0)
                {
                    playlistItem.MinSegments = transcodingProfile.MinSegments;
                }
                if (transcodingProfile.SegmentLength > 0)
                {
                    playlistItem.SegmentLength = transcodingProfile.SegmentLength;
                }

                if (!string.IsNullOrEmpty(transcodingProfile.MaxAudioChannels))
                {
                    int transcodingMaxAudioChannels;
                    if (int.TryParse(transcodingProfile.MaxAudioChannels, NumberStyles.Any, CultureInfo.InvariantCulture, out transcodingMaxAudioChannels))
                    {
                        playlistItem.TranscodingMaxAudioChannels = transcodingMaxAudioChannels;
                    }
                }
                playlistItem.SubProtocol = transcodingProfile.Protocol;
                ConditionProcessor conditionProcessor = new ConditionProcessor();

                var isFirstAppliedCodecProfile = true;
                foreach (CodecProfile i in options.Profile.CodecProfiles)
                {
                    if (i.Type == CodecType.Video && i.ContainsAnyCodec(transcodingProfile.VideoCodec, transcodingProfile.Container))
                    {
                        bool applyConditions = true;
                        foreach (ProfileCondition applyCondition in i.ApplyConditions)
                        {
                            int? width = videoStream == null ? null : videoStream.Width;
                            int? height = videoStream == null ? null : videoStream.Height;
                            int? bitDepth = videoStream == null ? null : videoStream.BitDepth;
                            int? videoBitrate = videoStream == null ? null : videoStream.BitRate;
                            double? videoLevel = videoStream == null ? null : videoStream.Level;
                            string videoProfile = videoStream == null ? null : videoStream.Profile;
                            float? videoFramerate = videoStream == null ? null : videoStream.AverageFrameRate ?? videoStream.AverageFrameRate;
                            bool? isAnamorphic = videoStream == null ? null : videoStream.IsAnamorphic;
                            bool? isInterlaced = videoStream == null ? (bool?)null : videoStream.IsInterlaced;
                            string videoCodecTag = videoStream == null ? null : videoStream.CodecTag;
                            bool? isAvc = videoStream == null ? null : videoStream.IsAVC;

                            TransportStreamTimestamp? timestamp = videoStream == null ? TransportStreamTimestamp.None : item.Timestamp;
                            int? packetLength = videoStream == null ? null : videoStream.PacketLength;
                            int? refFrames = videoStream == null ? null : videoStream.RefFrames;

                            int? numAudioStreams = item.GetStreamCount(MediaStreamType.Audio);
                            int? numVideoStreams = item.GetStreamCount(MediaStreamType.Video);

                            if (!conditionProcessor.IsVideoConditionSatisfied(applyCondition, width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc))
                            {
                                //LogConditionFailure(options.Profile, "VideoCodecProfile.ApplyConditions", applyCondition, item);
                                applyConditions = false;
                                break;
                            }
                        }

                        if (applyConditions)
                        {
                            var transcodingVideoCodecs = ContainerProfile.SplitValue(transcodingProfile.VideoCodec);
                            foreach (var transcodingVideoCodec in transcodingVideoCodecs)
                            {
                                if (i.ContainsAnyCodec(transcodingVideoCodec, transcodingProfile.Container))
                                {
                                    ApplyTranscodingConditions(playlistItem, i.Conditions, transcodingVideoCodec, !isFirstAppliedCodecProfile);
                                    isFirstAppliedCodecProfile = false;
                                }
                            }
                        }
                    }
                }

                var audioTranscodingConditions = new List<ProfileCondition>();
                foreach (CodecProfile i in options.Profile.CodecProfiles)
                {
                    if (i.Type == CodecType.VideoAudio && i.ContainsAnyCodec(playlistItem.TargetAudioCodec, transcodingProfile.Container))
                    {
                        bool applyConditions = true;
                        foreach (ProfileCondition applyCondition in i.ApplyConditions)
                        {
                            bool? isSecondaryAudio = audioStream == null ? null : item.IsSecondaryAudio(audioStream);
                            int? inputAudioBitrate = audioStream == null ? null : audioStream.BitRate;
                            int? audioChannels = audioStream == null ? null : audioStream.Channels;
                            string audioProfile = audioStream == null ? null : audioStream.Profile;
                            int? inputAudioSampleRate = audioStream == null ? null : audioStream.SampleRate;
                            int? inputAudioBitDepth = audioStream == null ? null : audioStream.BitDepth;

                            if (!conditionProcessor.IsVideoAudioConditionSatisfied(applyCondition, audioChannels, inputAudioBitrate, inputAudioSampleRate, inputAudioBitDepth, audioProfile, isSecondaryAudio))
                            {
                                //LogConditionFailure(options.Profile, "VideoCodecProfile.ApplyConditions", applyCondition, item);
                                applyConditions = false;
                                break;
                            }
                        }

                        if (applyConditions)
                        {
                            foreach (ProfileCondition c in i.Conditions)
                            {
                                audioTranscodingConditions.Add(c);
                            }
                            break;
                        }
                    }
                }
                // Honor requested max channels
                if (options.MaxAudioChannels.HasValue)
                {
                    int currentValue = playlistItem.MaxAudioChannels ?? options.MaxAudioChannels.Value;

                    playlistItem.MaxAudioChannels = Math.Min(options.MaxAudioChannels.Value, currentValue);
                }

                int audioBitrate = GetAudioBitrate(playlistItem.SubProtocol, options.GetMaxBitrate(false), playlistItem.TargetAudioChannels, playlistItem.TargetAudioCodec, audioStream);
                playlistItem.AudioBitrate = Math.Min(playlistItem.AudioBitrate ?? audioBitrate, audioBitrate);

                var maxBitrateSetting = options.GetMaxBitrate(false);
                // Honor max rate
                if (maxBitrateSetting.HasValue)
                {
                    var videoBitrate = maxBitrateSetting.Value;

                    if (playlistItem.AudioBitrate.HasValue)
                    {
                        videoBitrate -= playlistItem.AudioBitrate.Value;
                    }

                    // Make sure the video bitrate is lower than bitrate settings but at least 64k
                    long currentValue = playlistItem.VideoBitrate ?? videoBitrate;
                    var longBitrate = Math.Max(Math.Min(videoBitrate, currentValue), 64000);
                    playlistItem.VideoBitrate = longBitrate > int.MaxValue ? int.MaxValue : Convert.ToInt32(longBitrate);
                }

                // Do this after initial values are set to account for greater than/less than conditions
                ApplyTranscodingConditions(playlistItem, audioTranscodingConditions, null, false);
            }

            playlistItem.TranscodeReasons = transcodeReasons;

            return playlistItem;
        }

        private int GetDefaultAudioBitrateIfUnknown(MediaStream audioStream)
        {
            if ((audioStream.Channels ?? 0) >= 6)
            {
                return 384000;
            }

            return 192000;
        }

        private int GetAudioBitrate(string subProtocol, long? maxTotalBitrate, int? targetAudioChannels, string[] targetAudioCodecs, MediaStream audioStream)
        {
            var targetAudioCodec = targetAudioCodecs.Length == 0 ? null : targetAudioCodecs[0];

            int defaultBitrate = audioStream == null ? 192000 : audioStream.BitRate ?? GetDefaultAudioBitrateIfUnknown(audioStream);

            // Reduce the bitrate if we're downmixing
            if (targetAudioChannels.HasValue && audioStream != null && audioStream.Channels.HasValue && targetAudioChannels.Value < audioStream.Channels.Value)
            {
                defaultBitrate = StringHelper.EqualsIgnoreCase(targetAudioCodec, "ac3") ? 192000 : 128000;
            }

            if (StringHelper.EqualsIgnoreCase(subProtocol, "hls"))
            {
                defaultBitrate = Math.Min(384000, defaultBitrate);
            }
            else
            {
                defaultBitrate = Math.Min(448000, defaultBitrate);
            }

            int encoderAudioBitrateLimit = int.MaxValue;

            if (audioStream != null)
            {
                // Seeing webm encoding failures when source has 1 audio channel and 22k bitrate. 
                // Any attempts to transcode over 64k will fail
                if (audioStream.Channels.HasValue &&
                    audioStream.Channels.Value == 1)
                {
                    if ((audioStream.BitRate ?? 0) < 64000)
                    {
                        encoderAudioBitrateLimit = 64000;
                    }
                }
            }

            if (maxTotalBitrate.HasValue)
            {
                if (maxTotalBitrate.Value < 640000)
                {
                    defaultBitrate = Math.Min(128000, defaultBitrate);
                }
            }

            return Math.Min(defaultBitrate, encoderAudioBitrateLimit);
        }

        private Tuple<PlayMethod?, List<TranscodeReason>> GetVideoDirectPlayProfile(VideoOptions options,
            MediaSourceInfo mediaSource,
            MediaStream videoStream,
            MediaStream audioStream,
            bool isEligibleForDirectPlay,
            bool isEligibleForDirectStream)
        {
            DeviceProfile profile = options.Profile;

            if (options.ForceDirectPlay)
            {
                return new Tuple<PlayMethod?, List<TranscodeReason>>(PlayMethod.DirectPlay, new List<TranscodeReason>());
            }
            if (options.ForceDirectStream)
            {
                return new Tuple<PlayMethod?, List<TranscodeReason>>(PlayMethod.DirectStream, new List<TranscodeReason>());
            }

            // See if it can be direct played
            DirectPlayProfile directPlay = null;
            foreach (DirectPlayProfile i in profile.DirectPlayProfiles)
            {
                if (i.Type == DlnaProfileType.Video && IsVideoDirectPlaySupported(i, mediaSource, videoStream, audioStream))
                {
                    directPlay = i;
                    break;
                }
            }

            if (directPlay == null)
            {
                _logger.Info("Profile: {0}, No direct play profiles found for Path: {1}",
                    profile.Name ?? "Unknown Profile",
                    mediaSource.Path ?? "Unknown path");

                return new Tuple<PlayMethod?, List<TranscodeReason>>(null, GetTranscodeReasonsFromDirectPlayProfile(mediaSource, videoStream, audioStream, profile.DirectPlayProfiles));
            }

            string container = mediaSource.Container;

            var conditions = new List<ProfileCondition>();
            foreach (ContainerProfile i in profile.ContainerProfiles)
            {
                if (i.Type == DlnaProfileType.Video &&
                    i.ContainsContainer(container))
                {
                    foreach (ProfileCondition c in i.Conditions)
                    {
                        conditions.Add(c);
                    }
                }
            }

            ConditionProcessor conditionProcessor = new ConditionProcessor();

            int? width = videoStream == null ? null : videoStream.Width;
            int? height = videoStream == null ? null : videoStream.Height;
            int? bitDepth = videoStream == null ? null : videoStream.BitDepth;
            int? videoBitrate = videoStream == null ? null : videoStream.BitRate;
            double? videoLevel = videoStream == null ? null : videoStream.Level;
            string videoProfile = videoStream == null ? null : videoStream.Profile;
            float? videoFramerate = videoStream == null ? null : videoStream.AverageFrameRate ?? videoStream.AverageFrameRate;
            bool? isAnamorphic = videoStream == null ? null : videoStream.IsAnamorphic;
            bool? isInterlaced = videoStream == null ? (bool?)null : videoStream.IsInterlaced;
            string videoCodecTag = videoStream == null ? null : videoStream.CodecTag;
            bool? isAvc = videoStream == null ? null : videoStream.IsAVC;

            int? audioBitrate = audioStream == null ? null : audioStream.BitRate;
            int? audioChannels = audioStream == null ? null : audioStream.Channels;
            string audioProfile = audioStream == null ? null : audioStream.Profile;
            int? audioSampleRate = audioStream == null ? null : audioStream.SampleRate;
            int? audioBitDepth = audioStream == null ? null : audioStream.BitDepth;

            TransportStreamTimestamp? timestamp = videoStream == null ? TransportStreamTimestamp.None : mediaSource.Timestamp;
            int? packetLength = videoStream == null ? null : videoStream.PacketLength;
            int? refFrames = videoStream == null ? null : videoStream.RefFrames;

            int? numAudioStreams = mediaSource.GetStreamCount(MediaStreamType.Audio);
            int? numVideoStreams = mediaSource.GetStreamCount(MediaStreamType.Video);

            // Check container conditions
            foreach (ProfileCondition i in conditions)
            {
                if (!conditionProcessor.IsVideoConditionSatisfied(i, width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc))
                {
                    LogConditionFailure(profile, "VideoContainerProfile", i, mediaSource);

                    var transcodeReason = GetTranscodeReasonForFailedCondition(i);
                    var transcodeReasons = transcodeReason.HasValue
                        ? new List<TranscodeReason> { transcodeReason.Value }
                        : new List<TranscodeReason> { };

                    return new Tuple<PlayMethod?, List<TranscodeReason>>(null, transcodeReasons);
                }
            }

            string videoCodec = videoStream == null ? null : videoStream.Codec;

            conditions = new List<ProfileCondition>();
            foreach (CodecProfile i in profile.CodecProfiles)
            {
                if (i.Type == CodecType.Video && i.ContainsAnyCodec(videoCodec, container))
                {
                    bool applyConditions = true;
                    foreach (ProfileCondition applyCondition in i.ApplyConditions)
                    {
                        if (!conditionProcessor.IsVideoConditionSatisfied(applyCondition, width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc))
                        {
                            //LogConditionFailure(profile, "VideoCodecProfile.ApplyConditions", applyCondition, mediaSource);
                            applyConditions = false;
                            break;
                        }
                    }

                    if (applyConditions)
                    {
                        foreach (ProfileCondition c in i.Conditions)
                        {
                            conditions.Add(c);
                        }
                    }
                }
            }

            foreach (ProfileCondition i in conditions)
            {
                if (!conditionProcessor.IsVideoConditionSatisfied(i, width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc))
                {
                    LogConditionFailure(profile, "VideoCodecProfile", i, mediaSource);

                    var transcodeReason = GetTranscodeReasonForFailedCondition(i);
                    var transcodeReasons = transcodeReason.HasValue
                        ? new List<TranscodeReason> { transcodeReason.Value }
                        : new List<TranscodeReason> { };

                    return new Tuple<PlayMethod?, List<TranscodeReason>>(null, transcodeReasons);
                }
            }

            if (audioStream != null)
            {
                string audioCodec = audioStream.Codec;

                conditions = new List<ProfileCondition>();
                bool? isSecondaryAudio = audioStream == null ? null : mediaSource.IsSecondaryAudio(audioStream);

                foreach (CodecProfile i in profile.CodecProfiles)
                {
                    if (i.Type == CodecType.VideoAudio && i.ContainsAnyCodec(audioCodec, container))
                    {
                        bool applyConditions = true;
                        foreach (ProfileCondition applyCondition in i.ApplyConditions)
                        {
                            if (!conditionProcessor.IsVideoAudioConditionSatisfied(applyCondition, audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryAudio))
                            {
                                //LogConditionFailure(profile, "VideoAudioCodecProfile.ApplyConditions", applyCondition, mediaSource);
                                applyConditions = false;
                                break;
                            }
                        }

                        if (applyConditions)
                        {
                            foreach (ProfileCondition c in i.Conditions)
                            {
                                conditions.Add(c);
                            }
                        }
                    }
                }

                foreach (ProfileCondition i in conditions)
                {
                    if (!conditionProcessor.IsVideoAudioConditionSatisfied(i, audioChannels, audioBitrate, audioSampleRate, audioBitDepth, audioProfile, isSecondaryAudio))
                    {
                        LogConditionFailure(profile, "VideoAudioCodecProfile", i, mediaSource);

                        var transcodeReason = GetTranscodeReasonForFailedCondition(i);
                        var transcodeReasons = transcodeReason.HasValue
                            ? new List<TranscodeReason> { transcodeReason.Value }
                            : new List<TranscodeReason> { };

                        return new Tuple<PlayMethod?, List<TranscodeReason>>(null, transcodeReasons);
                    }
                }
            }

            if (isEligibleForDirectStream && mediaSource.SupportsDirectStream)
            {
                return new Tuple<PlayMethod?, List<TranscodeReason>>(PlayMethod.DirectStream, new List<TranscodeReason>());
            }

            return new Tuple<PlayMethod?, List<TranscodeReason>>(null, new List<TranscodeReason> { TranscodeReason.ContainerBitrateExceedsLimit });
        }

        private void LogConditionFailure(DeviceProfile profile, string type, ProfileCondition condition, MediaSourceInfo mediaSource)
        {
            _logger.Info("Profile: {0}, DirectPlay=false. Reason={1}.{2} Condition: {3}. ConditionValue: {4}. IsRequired: {5}. Path: {6}",
                type,
                profile.Name ?? "Unknown Profile",
                condition.Property,
                condition.Condition,
                condition.Value ?? string.Empty,
                condition.IsRequired,
                mediaSource.Path ?? "Unknown path");
        }

        private Tuple<bool, TranscodeReason?> IsEligibleForDirectPlay(MediaSourceInfo item,
            long? maxBitrate,
            MediaStream subtitleStream,
            VideoOptions options,
            PlayMethod playMethod)
        {
            if (subtitleStream != null)
            {
                SubtitleProfile subtitleProfile = GetSubtitleProfile(item, subtitleStream, options.Profile.SubtitleProfiles, playMethod, _transcoderSupport, null, null);

                if (subtitleProfile.Method != SubtitleDeliveryMethod.External && subtitleProfile.Method != SubtitleDeliveryMethod.Embed)
                {
                    _logger.Info("Not eligible for {0} due to unsupported subtitles", playMethod);
                    return new Tuple<bool, TranscodeReason?>(false, TranscodeReason.SubtitleCodecNotSupported);
                }
            }

            var result = IsAudioEligibleForDirectPlay(item, maxBitrate, playMethod);

            if (result)
            {
                return new Tuple<bool, TranscodeReason?>(result, null);
            }

            return new Tuple<bool, TranscodeReason?>(result, TranscodeReason.ContainerBitrateExceedsLimit);
        }

        public static SubtitleProfile GetSubtitleProfile(MediaSourceInfo mediaSource, MediaStream subtitleStream, SubtitleProfile[] subtitleProfiles, PlayMethod playMethod, ITranscoderSupport transcoderSupport, string transcodingSubProtocol, string transcodingContainer)
        {
            if (!subtitleStream.IsExternal && (playMethod != PlayMethod.Transcode || !string.Equals(transcodingSubProtocol, "hls", StringComparison.OrdinalIgnoreCase)))
            {
                // Look for supported embedded subs of the same format
                foreach (SubtitleProfile profile in subtitleProfiles)
                {
                    if (!profile.SupportsLanguage(subtitleStream.Language))
                    {
                        continue;
                    }

                    if (profile.Method != SubtitleDeliveryMethod.Embed)
                    {
                        continue;
                    }

                    if (playMethod == PlayMethod.Transcode && !IsSubtitleEmbedSupported(subtitleStream, profile, transcodingSubProtocol, transcodingContainer))
                    {
                        continue;
                    }

                    if (subtitleStream.IsTextSubtitleStream == MediaStream.IsTextFormat(profile.Format) && StringHelper.EqualsIgnoreCase(profile.Format, subtitleStream.Codec))
                    {
                        return profile;
                    }
                }

                // Look for supported embedded subs of a convertible format
                foreach (SubtitleProfile profile in subtitleProfiles)
                {
                    if (!profile.SupportsLanguage(subtitleStream.Language))
                    {
                        continue;
                    }

                    if (profile.Method != SubtitleDeliveryMethod.Embed)
                    {
                        continue;
                    }

                    if (playMethod == PlayMethod.Transcode && !IsSubtitleEmbedSupported(subtitleStream, profile, transcodingSubProtocol, transcodingContainer))
                    {
                        continue;
                    }

                    if (subtitleStream.IsTextSubtitleStream && subtitleStream.SupportsSubtitleConversionTo(profile.Format))
                    {
                        return profile;
                    }
                }
            }

            // Look for an external or hls profile that matches the stream type (text/graphical) and doesn't require conversion
            return GetExternalSubtitleProfile(mediaSource, subtitleStream, subtitleProfiles, playMethod, transcoderSupport, false) ??
                GetExternalSubtitleProfile(mediaSource, subtitleStream, subtitleProfiles, playMethod, transcoderSupport, true) ??
                new SubtitleProfile
                {
                    Method = SubtitleDeliveryMethod.Encode,
                    Format = subtitleStream.Codec
                };
        }

        private static bool IsSubtitleEmbedSupported(MediaStream subtitleStream, SubtitleProfile subtitleProfile, string transcodingSubProtocol, string transcodingContainer)
        {
            if (!string.IsNullOrWhiteSpace(transcodingContainer))
            {
                var normalizedContainers = ContainerProfile.SplitValue(transcodingContainer);

                if (ContainerProfile.ContainsContainer(normalizedContainers, "ts"))
                {
                    return false;
                }
                if (ContainerProfile.ContainsContainer(normalizedContainers, "mpegts"))
                {
                    return false;
                }
                if (ContainerProfile.ContainsContainer(normalizedContainers, "mp4"))
                {
                    return false;
                }
                if (ContainerProfile.ContainsContainer(normalizedContainers, "mkv") ||
                    ContainerProfile.ContainsContainer(normalizedContainers, "matroska"))
                {
                    return true;
                }
            }

            return false;
        }

        private static SubtitleProfile GetExternalSubtitleProfile(MediaSourceInfo mediaSource, MediaStream subtitleStream, SubtitleProfile[] subtitleProfiles, PlayMethod playMethod, ITranscoderSupport transcoderSupport, bool allowConversion)
        {
            foreach (SubtitleProfile profile in subtitleProfiles)
            {
                if (profile.Method != SubtitleDeliveryMethod.External && profile.Method != SubtitleDeliveryMethod.Hls)
                {
                    continue;
                }

                if (profile.Method == SubtitleDeliveryMethod.Hls && playMethod != PlayMethod.Transcode)
                {
                    continue;
                }

                if (!profile.SupportsLanguage(subtitleStream.Language))
                {
                    continue;
                }

                if (!subtitleStream.IsExternal && !transcoderSupport.CanExtractSubtitles(subtitleStream.Codec))
                {
                    continue;
                }

                if ((profile.Method == SubtitleDeliveryMethod.External && subtitleStream.IsTextSubtitleStream == MediaStream.IsTextFormat(profile.Format)) ||
                    (profile.Method == SubtitleDeliveryMethod.Hls && subtitleStream.IsTextSubtitleStream))
                {
                    bool requiresConversion = !StringHelper.EqualsIgnoreCase(subtitleStream.Codec, profile.Format);

                    if (!requiresConversion)
                    {
                        return profile;
                    }

                    if (!allowConversion)
                    {
                        continue;
                    }

                    // TODO: Build this into subtitleStream.SupportsExternalStream
                    if (mediaSource.IsInfiniteStream)
                    {
                        continue;
                    }

                    if (subtitleStream.IsTextSubtitleStream && subtitleStream.SupportsExternalStream && subtitleStream.SupportsSubtitleConversionTo(profile.Format))
                    {
                        return profile;
                    }
                }
            }

            return null;
        }

        private bool IsAudioEligibleForDirectPlay(MediaSourceInfo item, long? maxBitrate, PlayMethod playMethod)
        {
            // Don't restrict by bitrate if coming from an external domain
            if (item.IsRemote)
            {
                return true;
            }

            var requestedMaxBitrate = maxBitrate ?? 1000000;

            // If we don't know the bitrate, then force a transcode if requested max bitrate is under 40 mbps
            var itemBitrate = item.Bitrate ??
                40000000;

            if (itemBitrate > requestedMaxBitrate)
            {
                _logger.Info("Bitrate exceeds " + playMethod + " limit: media bitrate: {0}, max bitrate: {1}", itemBitrate.ToString(CultureInfo.InvariantCulture), requestedMaxBitrate.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            return true;
        }

        private void ValidateInput(VideoOptions options)
        {
            ValidateAudioInput(options);

            if (options.AudioStreamIndex.HasValue && string.IsNullOrEmpty(options.MediaSourceId))
            {
                throw new ArgumentException("MediaSourceId is required when a specific audio stream is requested");
            }

            if (options.SubtitleStreamIndex.HasValue && string.IsNullOrEmpty(options.MediaSourceId))
            {
                throw new ArgumentException("MediaSourceId is required when a specific subtitle stream is requested");
            }
        }

        private void ValidateAudioInput(AudioOptions options)
        {
            if (string.IsNullOrEmpty(options.ItemId))
            {
                throw new ArgumentException("ItemId is required");
            }
            if (string.IsNullOrEmpty(options.DeviceId))
            {
                throw new ArgumentException("DeviceId is required");
            }
            if (options.Profile == null)
            {
                throw new ArgumentException("Profile is required");
            }
            if (options.MediaSources == null)
            {
                throw new ArgumentException("MediaSources is required");
            }
        }

        private void ApplyTranscodingConditions(StreamInfo item, IEnumerable<ProfileCondition> conditions, string qualifier, bool qualifiedOnly)
        {
            foreach (ProfileCondition condition in conditions)
            {
                string value = condition.Value;

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                // No way to express this
                if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                {
                    continue;
                }

                switch (condition.Property)
                {
                    case ProfileConditionValue.AudioBitrate:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.AudioBitrate = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.AudioBitrate = Math.Min(num, item.AudioBitrate ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.AudioBitrate = Math.Max(num, item.AudioBitrate ?? num);
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.AudioChannels:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.MaxAudioChannels = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.MaxAudioChannels = Math.Min(num, item.MaxAudioChannels ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.MaxAudioChannels = Math.Max(num, item.MaxAudioChannels ?? num);
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.IsAvc:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            bool isAvc;
                            if (bool.TryParse(value, out isAvc))
                            {
                                if (isAvc && condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.RequireAvc = true;
                                }
                                else if (!isAvc && condition.Condition == ProfileConditionType.NotEquals)
                                {
                                    item.RequireAvc = true;
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.IsAnamorphic:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            bool isAnamorphic;
                            if (bool.TryParse(value, out isAnamorphic))
                            {
                                if (isAnamorphic && condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.RequireNonAnamorphic = true;
                                }
                                else if (!isAnamorphic && condition.Condition == ProfileConditionType.NotEquals)
                                {
                                    item.RequireNonAnamorphic = true;
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.IsInterlaced:
                        {
                            if (string.IsNullOrWhiteSpace(qualifier))
                            {
                                continue;
                            }

                            bool isInterlaced;
                            if (bool.TryParse(value, out isInterlaced))
                            {
                                if (!isInterlaced && condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.SetOption(qualifier, "deinterlace", "true");
                                }
                                else if (isInterlaced && condition.Condition == ProfileConditionType.NotEquals)
                                {
                                    item.SetOption(qualifier, "deinterlace", "true");
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.AudioProfile:
                    case ProfileConditionValue.Has64BitOffsets:
                    case ProfileConditionValue.PacketLength:
                    case ProfileConditionValue.NumAudioStreams:
                    case ProfileConditionValue.NumVideoStreams:
                    case ProfileConditionValue.IsSecondaryAudio:
                    case ProfileConditionValue.VideoTimestamp:
                        {
                            // Not supported yet
                            break;
                        }
                    case ProfileConditionValue.RefFrames:
                        {
                            if (string.IsNullOrWhiteSpace(qualifier))
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.SetOption(qualifier, "maxrefframes", num.ToString(CultureInfo.InvariantCulture));
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.SetOption(qualifier, "maxrefframes", Math.Min(num, item.GetTargetRefFrames(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.SetOption(qualifier, "maxrefframes", Math.Max(num, item.GetTargetRefFrames(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.VideoBitDepth:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.MaxVideoBitDepth = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.MaxVideoBitDepth = Math.Min(num, item.MaxVideoBitDepth ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.MaxVideoBitDepth = Math.Max(num, item.MaxVideoBitDepth ?? num);
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.VideoProfile:
                        {
                            if (string.IsNullOrWhiteSpace(qualifier))
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                // change from split by | to comma

                                // strip spaces to avoid having to encode
                                var values = value
                                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

                                if (condition.Condition == ProfileConditionType.Equals || condition.Condition == ProfileConditionType.EqualsAny)
                                {
                                    item.SetOption(qualifier, "profile", string.Join(",", values));
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.Height:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.MaxHeight = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.MaxHeight = Math.Min(num, item.MaxHeight ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.MaxHeight = Math.Max(num, item.MaxHeight ?? num);
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.VideoBitrate:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.VideoBitrate = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.VideoBitrate = Math.Min(num, item.VideoBitrate ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.VideoBitrate = Math.Max(num, item.VideoBitrate ?? num);
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.VideoFramerate:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            float num;
                            if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.MaxFramerate = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.MaxFramerate = Math.Min(num, item.MaxFramerate ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.MaxFramerate = Math.Max(num, item.MaxFramerate ?? num);
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.VideoLevel:
                        {
                            if (string.IsNullOrWhiteSpace(qualifier))
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.SetOption(qualifier, "level", num.ToString(CultureInfo.InvariantCulture));
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.SetOption(qualifier, "level", Math.Min(num, item.GetTargetVideoLevel(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.SetOption(qualifier, "level", Math.Max(num, item.GetTargetVideoLevel(qualifier) ?? num).ToString(CultureInfo.InvariantCulture));
                                }
                            }
                            break;
                        }
                    case ProfileConditionValue.Width:
                        {
                            if (qualifiedOnly)
                            {
                                continue;
                            }

                            int num;
                            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                            {
                                if (condition.Condition == ProfileConditionType.Equals)
                                {
                                    item.MaxWidth = num;
                                }
                                else if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    item.MaxWidth = Math.Min(num, item.MaxWidth ?? num);
                                }
                                else if (condition.Condition == ProfileConditionType.GreaterThanEqual)
                                {
                                    item.MaxWidth = Math.Max(num, item.MaxWidth ?? num);
                                }
                            }
                            break;
                        }
                    default:
                        break;
                }
            }
        }

        private bool IsAudioDirectPlaySupported(DirectPlayProfile profile, MediaSourceInfo item, MediaStream audioStream)
        {
            // Check container type
            if (!profile.SupportsContainer(item.Container))
            {
                return false;
            }

            // Check audio codec
            var audioCodecs = profile.GetAudioCodecs();
            if (audioCodecs.Length > 0)
            {
                // Check audio codecs
                string audioCodec = audioStream == null ? null : audioStream.Codec;
                if (string.IsNullOrEmpty(audioCodec) || !ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsVideoDirectPlaySupported(DirectPlayProfile profile, MediaSourceInfo item, MediaStream videoStream, MediaStream audioStream)
        {
            // Check container type
            if (!profile.SupportsContainer(item.Container))
            {
                return false;
            }

            // Check video codec
            var videoCodecs = profile.GetVideoCodecs();
            if (videoCodecs.Length > 0)
            {
                string videoCodec = videoStream == null ? null : videoStream.Codec;
                if (string.IsNullOrEmpty(videoCodec) || !ListHelper.ContainsIgnoreCase(videoCodecs, videoCodec))
                {
                    return false;
                }
            }

            // Check audio codec
            if (audioStream != null)
            {
                var audioCodecs = profile.GetAudioCodecs();
                if (audioCodecs.Length > 0)
                {
                    // Check audio codecs
                    string audioCodec = audioStream == null ? null : audioStream.Codec;
                    if (string.IsNullOrEmpty(audioCodec) || !ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}