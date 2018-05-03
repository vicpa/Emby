﻿using System;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;

namespace MediaBrowser.Model.Configuration
{
    public class LibraryOptions
    {
        public bool EnableArchiveMediaFiles { get; set; }
        public bool EnablePhotos { get; set; }
        public bool EnableRealtimeMonitor { get; set; }
        public bool EnableChapterImageExtraction { get; set; }
        public bool ExtractChapterImagesDuringLibraryScan { get; set; }
        public bool DownloadImagesInAdvance { get; set; }
        public MediaPathInfo[] PathInfos { get; set; }

        public bool SaveLocalMetadata { get; set; }
        public bool EnableInternetProviders { get; set; }
        public bool ImportMissingEpisodes { get; set; }
        public bool EnableAutomaticSeriesGrouping { get; set; }
        public bool EnableEmbeddedTitles { get; set; }

        public int AutomaticRefreshIntervalDays { get; set; }

        /// <summary>
        /// Gets or sets the preferred metadata language.
        /// </summary>
        /// <value>The preferred metadata language.</value>
        public string PreferredMetadataLanguage { get; set; }

        /// <summary>
        /// Gets or sets the metadata country code.
        /// </summary>
        /// <value>The metadata country code.</value>
        public string MetadataCountryCode { get; set; }

        public string SeasonZeroDisplayName { get; set; }
        public string[] MetadataSavers { get; set; }
        public string[] DisabledLocalMetadataReaders { get; set; }
        public string[] LocalMetadataReaderOrder { get; set; }

        public string[] DisabledSubtitleFetchers { get; set; }
        public string[] SubtitleFetcherOrder { get; set; }

        public bool SkipSubtitlesIfEmbeddedSubtitlesPresent { get; set; }
        public bool SkipSubtitlesIfAudioTrackMatches { get; set; }
        public string[] SubtitleDownloadLanguages { get; set; }
        public bool RequirePerfectSubtitleMatch { get; set; }
        public bool SaveSubtitlesWithMedia { get; set; }

        public TypeOptions[] TypeOptions { get; set; }

        public TypeOptions GetTypeOptions(string type)
        {
            foreach (var options in TypeOptions)
            {
                if (string.Equals(options.Type, type, StringComparison.OrdinalIgnoreCase))
                {
                    return options;
                }
            }

            return null;
        }

        public LibraryOptions()
        {
            TypeOptions = new TypeOptions[] { };
            DisabledSubtitleFetchers = new string[] { };
            SubtitleFetcherOrder = new string[] { };
            DisabledLocalMetadataReaders = new string[] { };

            SkipSubtitlesIfAudioTrackMatches = true;
            RequirePerfectSubtitleMatch = true;

            EnablePhotos = true;
            SaveSubtitlesWithMedia = true;
            EnableRealtimeMonitor = true;
            PathInfos = new MediaPathInfo[] { };
            EnableInternetProviders = true;
            EnableAutomaticSeriesGrouping = true;
            SeasonZeroDisplayName = "Specials";
        }
    }

    public class MediaPathInfo
    {
        public string Path { get; set; }
        public string NetworkPath { get; set; }
    }

    public class TypeOptions
    {
        public string Type { get; set; }
        public string[] MetadataFetchers { get; set; }
        public string[] MetadataFetcherOrder { get; set; }

        public string[] ImageFetchers { get; set; }
        public string[] ImageFetcherOrder { get; set; }
        public ImageOption[] ImageOptions { get; set; }

        public ImageOption GetImageOptions(ImageType type)
        {
            foreach (ImageOption i in ImageOptions)
            {
                if (i.Type == type)
                {
                    return i;
                }
            }

            ImageOption[] options;
            if (DefaultImageOptions.TryGetValue(Type, out options))
            {
                foreach (ImageOption i in options)
                {
                    if (i.Type == type)
                    {
                        return i;
                    }
                }
            }

            return DefaultInstance;
        }

        public int GetLimit(ImageType type)
        {
            return GetImageOptions(type).Limit;
        }

        public int GetMinWidth(ImageType type)
        {
            return GetImageOptions(type).MinWidth;
        }

        public bool IsEnabled(ImageType type)
        {
            return GetLimit(type) > 0;
        }

        public TypeOptions()
        {
            MetadataFetchers = new string[] { };
            MetadataFetcherOrder = new string[] { };
            ImageFetchers = new string[] { };
            ImageFetcherOrder = new string[] { };
            ImageOptions = new ImageOption[] { };
        }

        public static Dictionary<string, ImageOption[]> DefaultImageOptions = new Dictionary<string, ImageOption[]>
        {
            {
                "Movie", new []
                {
                    new ImageOption
                    {
                        Limit = 1,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Art
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Disc
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Primary
                    },

                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Banner
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Thumb
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Logo
                    }
                }
            },
            {
                "MusicVideo", new []
                {
                    new ImageOption
                    {
                        Limit = 1,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Art
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Disc
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Primary
                    },

                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Banner
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Thumb
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Logo
                    }
                }
            },
            {
                "Series", new []
                {
                    new ImageOption
                    {
                        Limit = 1,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Art
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Primary
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Banner
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Thumb
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Logo
                    }
                }
            },
            {
                "MusicAlbum", new []
                {
                    new ImageOption
                    {
                        Limit = 0,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Disc
                    }
                }
            },
            {
                "MusicArtist", new []
                {
                    new ImageOption
                    {
                        Limit = 1,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    // Don't download this by default
                    // They do look great, but most artists won't have them, which means a banner view isn't really possible
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Banner
                    },

                    // Don't download this by default
                    // Generally not used
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Art
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Logo
                    }
                }
            },
            {
                "BoxSet", new []
                {
                    new ImageOption
                    {
                        Limit = 1,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Primary
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Thumb
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Logo
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Art
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Disc
                    },

                    // Don't download this by default as it's rarely used.
                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Banner
                    }
                }
            },
            {
                "Season", new []
                {
                    new ImageOption
                    {
                        Limit = 0,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Primary
                    },

                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Banner
                    },

                    new ImageOption
                    {
                        Limit = 0,
                        Type = ImageType.Thumb
                    }
                }
            },
            {
                "Episode", new []
                {
                    new ImageOption
                    {
                        Limit = 0,
                        MinWidth = 1280,
                        Type = ImageType.Backdrop
                    },

                    new ImageOption
                    {
                        Limit = 1,
                        Type = ImageType.Primary
                    }
                }
            }
        };

        public static ImageOption DefaultInstance = new ImageOption();
    }
}
