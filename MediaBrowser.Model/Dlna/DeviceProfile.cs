﻿using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.MediaInfo;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MediaBrowser.Model.Dlna
{
    [XmlRoot("Profile")]
    public class DeviceProfile
    {
        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name { get; set; }

        [XmlIgnore]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the identification.
        /// </summary>
        /// <value>The identification.</value>
        public MediaBrowser.Model.Dlna.DeviceIdentification Identification { get; set; }

        public string FriendlyName { get; set; }
        public string Manufacturer { get; set; }
        public string ManufacturerUrl { get; set; }
        public string ModelName { get; set; }
        public string ModelDescription { get; set; }
        public string ModelNumber { get; set; }
        public string ModelUrl { get; set; }
        public string SerialNumber { get; set; }

        public bool EnableAlbumArtInDidl { get; set; }
        public bool EnableSingleAlbumArtLimit { get; set; }
        public bool EnableSingleSubtitleLimit { get; set; }

        public string SupportedMediaTypes { get; set; }

        public string UserId { get; set; }

        public string AlbumArtPn { get; set; }

        public int MaxAlbumArtWidth { get; set; }
        public int MaxAlbumArtHeight { get; set; }

        public int? MaxIconWidth { get; set; }
        public int? MaxIconHeight { get; set; }

        public long? MaxStreamingBitrate { get; set; }
        public long? MaxStaticBitrate { get; set; }

        public int? MusicStreamingTranscodingBitrate { get; set; }
        public int? MaxStaticMusicBitrate { get; set; }

        /// <summary>
        /// Controls the content of the X_DLNADOC element in the urn:schemas-dlna-org:device-1-0 namespace.
        /// </summary>
        public string XDlnaDoc { get; set; }
        /// <summary>
        /// Controls the content of the X_DLNACAP element in the urn:schemas-dlna-org:device-1-0 namespace.
        /// </summary>
        public string XDlnaCap { get; set; }
        /// <summary>
        /// Controls the content of the aggregationFlags element in the urn:schemas-sonycom:av namespace.
        /// </summary>
        public string SonyAggregationFlags { get; set; }

        public string ProtocolInfo { get; set; }

        public int TimelineOffsetSeconds { get; set; }
        public bool RequiresPlainVideoItems { get; set; }
        public bool RequiresPlainFolders { get; set; }

        public bool EnableMSMediaReceiverRegistrar { get; set; }
        public bool IgnoreTranscodeByteRangeRequests { get; set; }

        public XmlAttribute[] XmlRootAttributes { get; set; }

        /// <summary>
        /// Gets or sets the direct play profiles.
        /// </summary>
        /// <value>The direct play profiles.</value>
        public DirectPlayProfile[] DirectPlayProfiles { get; set; }

        /// <summary>
        /// Gets or sets the transcoding profiles.
        /// </summary>
        /// <value>The transcoding profiles.</value>
        public TranscodingProfile[] TranscodingProfiles { get; set; }

        public ContainerProfile[] ContainerProfiles { get; set; }

        public CodecProfile[] CodecProfiles { get; set; }
        public ResponseProfile[] ResponseProfiles { get; set; }

        public SubtitleProfile[] SubtitleProfiles { get; set; }

        public DeviceProfile()
        {
            DirectPlayProfiles = new DirectPlayProfile[] { };
            TranscodingProfiles = new TranscodingProfile[] { };
            ResponseProfiles = new ResponseProfile[] { };
            CodecProfiles = new CodecProfile[] { };
            ContainerProfiles = new ContainerProfile[] { };
            SubtitleProfiles = new SubtitleProfile[] { };

            XmlRootAttributes = new XmlAttribute[] { };

            SupportedMediaTypes = "Audio,Photo,Video";
            MaxStreamingBitrate = 8000000;
            MaxStaticBitrate = 8000000;
            MusicStreamingTranscodingBitrate = 128000;
        }

        public string[] GetSupportedMediaTypes()
        {
            return ContainerProfile.SplitValue(SupportedMediaTypes);
        }

        public TranscodingProfile GetAudioTranscodingProfile(string container, string audioCodec)
        {
            container = (container ?? string.Empty).TrimStart('.');

            foreach (var i in TranscodingProfiles)
            {
                if (i.Type != MediaBrowser.Model.Dlna.DlnaProfileType.Audio)
                {
                    continue;
                }

                if (!StringHelper.EqualsIgnoreCase(container, i.Container))
                {
                    continue;
                }

                if (!ListHelper.ContainsIgnoreCase(i.GetAudioCodecs(), audioCodec ?? string.Empty))
                {
                    continue;
                }

                return i;
            }
            return null;
        }

        public TranscodingProfile GetVideoTranscodingProfile(string container, string audioCodec, string videoCodec)
        {
            container = (container ?? string.Empty).TrimStart('.');

            foreach (var i in TranscodingProfiles)
            {
                if (i.Type != MediaBrowser.Model.Dlna.DlnaProfileType.Video)
                {
                    continue;
                }

                if (!StringHelper.EqualsIgnoreCase(container, i.Container))
                {
                    continue;
                }

                if (!ListHelper.ContainsIgnoreCase(i.GetAudioCodecs(), audioCodec ?? string.Empty))
                {
                    continue;
                }

                if (!StringHelper.EqualsIgnoreCase(videoCodec, i.VideoCodec ?? string.Empty))
                {
                    continue;
                }

                return i;
            }
            return null;
        }

        public ResponseProfile GetAudioMediaProfile(string container, string audioCodec, int? audioChannels, int? audioBitrate, int? audioSampleRate, int? audioBitDepth)
        {
            foreach (var i in ResponseProfiles)
            {
                if (i.Type != DlnaProfileType.Audio)
                {
                    continue;
                }

                if (!ContainerProfile.ContainsContainer(i.GetContainers(), container))
                {
                    continue;
                }

                var audioCodecs = i.GetAudioCodecs();
                if (audioCodecs.Length > 0 && !ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec ?? string.Empty))
                {
                    continue;
                }

                var conditionProcessor = new ConditionProcessor();

                var anyOff = false;
                foreach (ProfileCondition c in i.Conditions)
                {
                    if (!conditionProcessor.IsAudioConditionSatisfied(GetModelProfileCondition(c), audioChannels, audioBitrate, audioSampleRate, audioBitDepth))
                    {
                        anyOff = true;
                        break;
                    }
                }

                if (anyOff)
                {
                    continue;
                }

                return i;
            }
            return null;
        }

        private ProfileCondition GetModelProfileCondition(ProfileCondition c)
        {
            return new ProfileCondition
            {
                Condition = c.Condition,
                IsRequired = c.IsRequired,
                Property = c.Property,
                Value = c.Value
            };
        }

        public ResponseProfile GetImageMediaProfile(string container, int? width, int? height)
        {
            foreach (var i in ResponseProfiles)
            {
                if (i.Type != DlnaProfileType.Photo)
                {
                    continue;
                }

                if (!ContainerProfile.ContainsContainer(i.GetContainers(), container))
                {
                    continue;
                }

                var conditionProcessor = new ConditionProcessor();

                var anyOff = false;
                foreach (ProfileCondition c in i.Conditions)
                {
                    if (!conditionProcessor.IsImageConditionSatisfied(GetModelProfileCondition(c), width, height))
                    {
                        anyOff = true;
                        break;
                    }
                }

                if (anyOff)
                {
                    continue;
                }

                return i;
            }
            return null;
        }

        public ResponseProfile GetVideoMediaProfile(string container,
            string audioCodec,
            string videoCodec,
            int? width,
            int? height,
            int? bitDepth,
            int? videoBitrate,
            string videoProfile,
            double? videoLevel,
            float? videoFramerate,
            int? packetLength,
            TransportStreamTimestamp timestamp,
            bool? isAnamorphic,
            bool? isInterlaced,
            int? refFrames,
            int? numVideoStreams,
            int? numAudioStreams,
            string videoCodecTag,
            bool? isAvc)
        {
            foreach (var i in ResponseProfiles)
            {
                if (i.Type != DlnaProfileType.Video)
                {
                    continue;
                }

                if (!ContainerProfile.ContainsContainer(i.GetContainers(), container))
                {
                    continue;
                }

                var audioCodecs = i.GetAudioCodecs();
                if (audioCodecs.Length > 0 && !ListHelper.ContainsIgnoreCase(audioCodecs, audioCodec ?? string.Empty))
                {
                    continue;
                }

                var videoCodecs = i.GetVideoCodecs();
                if (videoCodecs.Length > 0 && !ListHelper.ContainsIgnoreCase(videoCodecs, videoCodec ?? string.Empty))
                {
                    continue;
                }

                var conditionProcessor = new ConditionProcessor();

                var anyOff = false;
                foreach (ProfileCondition c in i.Conditions)
                {
                    if (!conditionProcessor.IsVideoConditionSatisfied(GetModelProfileCondition(c), width, height, bitDepth, videoBitrate, videoProfile, videoLevel, videoFramerate, packetLength, timestamp, isAnamorphic, isInterlaced, refFrames, numVideoStreams, numAudioStreams, videoCodecTag, isAvc))
                    {
                        anyOff = true;
                        break;
                    }
                }

                if (anyOff)
                {
                    continue;
                }

                return i;
            }
            return null;
        }
    }
}
