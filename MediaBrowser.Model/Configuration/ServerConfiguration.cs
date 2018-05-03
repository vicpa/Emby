﻿using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Represents the server configuration.
    /// </summary>
    public class ServerConfiguration : BaseApplicationConfiguration
    {
        public const int DefaultHttpPort = 8096;
        public const int DefaultHttpsPort = 8920;

        /// <summary>
        /// Gets or sets a value indicating whether [enable u pn p].
        /// </summary>
        /// <value><c>true</c> if [enable u pn p]; otherwise, <c>false</c>.</value>
        public bool EnableUPnP { get; set; }

        /// <summary>
        /// Gets or sets the public mapped port.
        /// </summary>
        /// <value>The public mapped port.</value>
        public int PublicPort { get; set; }

        /// <summary>
        /// Gets or sets the public HTTPS port.
        /// </summary>
        /// <value>The public HTTPS port.</value>
        public int PublicHttpsPort { get; set; }

        /// <summary>
        /// Gets or sets the HTTP server port number.
        /// </summary>
        /// <value>The HTTP server port number.</value>
        public int HttpServerPortNumber { get; set; }

        /// <summary>
        /// Gets or sets the HTTPS server port number.
        /// </summary>
        /// <value>The HTTPS server port number.</value>
        public int HttpsPortNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [use HTTPS].
        /// </summary>
        /// <value><c>true</c> if [use HTTPS]; otherwise, <c>false</c>.</value>
        public bool EnableHttps { get; set; }
        public bool EnableNormalizedItemByNameIds { get; set; }

        /// <summary>
        /// Gets or sets the value pointing to the file system where the ssl certiifcate is located..
        /// </summary>
        /// <value>The value pointing to the file system where the ssl certiifcate is located..</value>
        public string CertificatePath { get; set; }
        public string CertificatePassword { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is port authorized.
        /// </summary>
        /// <value><c>true</c> if this instance is port authorized; otherwise, <c>false</c>.</value>
        public bool IsPortAuthorized { get; set; }

        public bool AutoRunWebApp { get; set; }
        public bool EnableRemoteAccess { get; set; }
        public bool CameraUploadUpgraded { get; set; }
        public bool CollectionsUpgraded { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [enable case sensitive item ids].
        /// </summary>
        /// <value><c>true</c> if [enable case sensitive item ids]; otherwise, <c>false</c>.</value>
        public bool EnableCaseSensitiveItemIds { get; set; }

        public bool DisableLiveTvChannelUserDataName { get; set; }

        /// <summary>
        /// Gets or sets the metadata path.
        /// </summary>
        /// <value>The metadata path.</value>
        public string MetadataPath { get; set; }
        public string MetadataNetworkPath { get; set; }

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

        /// <summary>
        /// Characters to be replaced with a ' ' in strings to create a sort name
        /// </summary>
        /// <value>The sort replace characters.</value>
        public string[] SortReplaceCharacters { get; set; }

        /// <summary>
        /// Characters to be removed from strings to create a sort name
        /// </summary>
        /// <value>The sort remove characters.</value>
        public string[] SortRemoveCharacters { get; set; }

        /// <summary>
        /// Words to be removed from strings to create a sort name
        /// </summary>
        /// <value>The sort remove words.</value>
        public string[] SortRemoveWords { get; set; }

        /// <summary>
        /// Gets or sets the minimum percentage of an item that must be played in order for playstate to be updated.
        /// </summary>
        /// <value>The min resume PCT.</value>
        public int MinResumePct { get; set; }

        /// <summary>
        /// Gets or sets the maximum percentage of an item that can be played while still saving playstate. If this percentage is crossed playstate will be reset to the beginning and the item will be marked watched.
        /// </summary>
        /// <value>The max resume PCT.</value>
        public int MaxResumePct { get; set; }

        /// <summary>
        /// Gets or sets the minimum duration that an item must have in order to be eligible for playstate updates..
        /// </summary>
        /// <value>The min resume duration seconds.</value>
        public int MinResumeDurationSeconds { get; set; }

        /// <summary>
        /// The delay in seconds that we will wait after a file system change to try and discover what has been added/removed
        /// Some delay is necessary with some items because their creation is not atomic.  It involves the creation of several
        /// different directories and files.
        /// </summary>
        /// <value>The file watcher delay.</value>
        public int LibraryMonitorDelay { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [enable dashboard response caching].
        /// Allows potential contributors without visual studio to modify production dashboard code and test changes.
        /// </summary>
        /// <value><c>true</c> if [enable dashboard response caching]; otherwise, <c>false</c>.</value>
        public bool EnableDashboardResponseCaching { get; set; }

        /// <summary>
        /// Allows the dashboard to be served from a custom path.
        /// </summary>
        /// <value>The dashboard source path.</value>
        public string DashboardSourcePath { get; set; }

        /// <summary>
        /// Gets or sets the image saving convention.
        /// </summary>
        /// <value>The image saving convention.</value>
        public ImageSavingConvention ImageSavingConvention { get; set; }

        public MetadataOptions[] MetadataOptions { get; set; }

        public bool EnableAutomaticRestart { get; set; }
        public bool SkipDeserializationForBasicTypes { get; set; }

        public string ServerName { get; set; }
        public string WanDdns { get; set; }

        public string UICulture { get; set; }

        public bool SaveMetadataHidden { get; set; }

        public NameValuePair[] ContentTypes { get; set; }

        public int RemoteClientBitrateLimit { get; set; }

        public int SchemaVersion { get; set; }

        public bool EnableAnonymousUsageReporting { get; set; }
        public bool EnableFolderView { get; set; }
        public bool EnableGroupingIntoCollections { get; set; }
        public bool DisplaySpecialsWithinSeasons { get; set; }
        public string[] LocalNetworkSubnets { get; set; }
        public string[] LocalNetworkAddresses { get; set; }
        public string[] CodecsUsed { get; set; }
        public bool EnableExternalContentInSuggestions { get; set; }
        public bool RequireHttps { get; set; }
        public bool IsBehindProxy { get; set; }
        public bool EnableNewOmdbSupport { get; set; }

        public string[] RemoteIPFilter { get; set; }
        public bool IsRemoteIPFilterBlacklist { get; set; }

        public int ImageExtractionTimeoutMs { get; set; }

        public PathSubstitution[] PathSubstitutions { get; set; }
        public bool EnableSimpleArtistDetection { get; set; }

        public string[] UninstalledPlugins { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerConfiguration" /> class.
        /// </summary>
        public ServerConfiguration()
        {
            UninstalledPlugins = new string[] { };
            RemoteIPFilter = new string[] { };
            LocalNetworkSubnets = new string[] { };
            LocalNetworkAddresses = new string[] { };
            CodecsUsed = new string[] { };
            ImageExtractionTimeoutMs = 0;
            PathSubstitutions = new PathSubstitution[] { };
            EnableSimpleArtistDetection = true;

            DisplaySpecialsWithinSeasons = true;
            EnableExternalContentInSuggestions = true;

            ImageSavingConvention = ImageSavingConvention.Compatible;
            PublicPort = DefaultHttpPort;
            PublicHttpsPort = DefaultHttpsPort;
            HttpServerPortNumber = DefaultHttpPort;
            HttpsPortNumber = DefaultHttpsPort;
            EnableHttps = true;
            EnableDashboardResponseCaching = true;
            EnableAnonymousUsageReporting = true;
            EnableCaseSensitiveItemIds = true;

            EnableAutomaticRestart = true;
            AutoRunWebApp = true;
            EnableRemoteAccess = true;

            EnableUPnP = true;
            MinResumePct = 5;
            MaxResumePct = 90;

            // 5 minutes
            MinResumeDurationSeconds = 300;

            LibraryMonitorDelay = 60;

            ContentTypes = new NameValuePair[] { };

            PreferredMetadataLanguage = "en";
            MetadataCountryCode = "US";

            SortReplaceCharacters = new[] { ".", "+", "%" };
            SortRemoveCharacters = new[] { ",", "&", "-", "{", "}", "'" };
            SortRemoveWords = new[] { "the", "a", "an" };

            UICulture = "en-us";

            MetadataOptions = new[]
            {
                new MetadataOptions {ItemType = "Book"},

                new MetadataOptions
                {
                    ItemType = "Movie"
                },

                new MetadataOptions
                {
                    ItemType = "MusicVideo",
                    DisabledMetadataFetchers = new []{ "The Open Movie Database" },
                    DisabledImageFetchers = new []{ "The Open Movie Database", "FanArt" }
                },

                new MetadataOptions
                {
                    ItemType = "Series",
                    DisabledMetadataFetchers = new []{ "TheMovieDb" },
                    DisabledImageFetchers = new []{ "TheMovieDb" }
                },

                new MetadataOptions
                {
                    ItemType = "MusicAlbum",
                    DisabledMetadataFetchers = new []{ "TheAudioDB" }
                },

                new MetadataOptions
                {
                    ItemType = "MusicArtist",
                    DisabledMetadataFetchers = new []{ "TheAudioDB" }
                },

                new MetadataOptions
                {
                    ItemType = "BoxSet"
                },

                new MetadataOptions
                {
                    ItemType = "Season",
                    DisabledMetadataFetchers = new []{ "TheMovieDb" },
                    DisabledImageFetchers = new [] { "FanArt" }
                },

                new MetadataOptions
                {
                    ItemType = "Episode",
                    DisabledMetadataFetchers = new []{ "The Open Movie Database", "TheMovieDb" },
                    DisabledImageFetchers = new []{ "The Open Movie Database", "TheMovieDb" }
                }
            };
        }
    }

    public class PathSubstitution
    {
        public string From { get; set; }
        public string To { get; set; }
    }
}