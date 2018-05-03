﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Extensions;

namespace MediaBrowser.Providers.MediaInfo
{
    public class SubtitleScheduledTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IServerConfigurationManager _config;
        private readonly ISubtitleManager _subtitleManager;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;

        public SubtitleScheduledTask(ILibraryManager libraryManager, IJsonSerializer json, IServerConfigurationManager config, ISubtitleManager subtitleManager, ILogger logger, IMediaSourceManager mediaSourceManager)
        {
            _libraryManager = libraryManager;
            _config = config;
            _subtitleManager = subtitleManager;
            _logger = logger;
            _mediaSourceManager = mediaSourceManager;
            _json = json;
        }

        public string Name
        {
            get { return "Download missing subtitles"; }
        }

        public string Description
        {
            get { return "Searches the internet for missing subtitles based on metadata configuration."; }
        }

        public string Category
        {
            get { return "Library"; }
        }

        private SubtitleOptions GetOptions()
        {
            return _config.GetConfiguration<SubtitleOptions>("subtitles");
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = GetOptions();

            var types = new[] { "Episode", "Movie" };

            var dict = new Dictionary<Guid, BaseItem>();

            foreach (var library in _libraryManager.RootFolder.Children.ToList())
            {
                var libraryOptions = _libraryManager.GetLibraryOptions(library);

                string[] subtitleDownloadLanguages;
                bool SkipIfEmbeddedSubtitlesPresent;
                bool SkipIfAudioTrackMatches;
                bool RequirePerfectMatch;

                if (libraryOptions.SubtitleDownloadLanguages == null)
                {
                    subtitleDownloadLanguages = options.DownloadLanguages;
                    SkipIfEmbeddedSubtitlesPresent = options.SkipIfEmbeddedSubtitlesPresent;
                    SkipIfAudioTrackMatches = options.SkipIfAudioTrackMatches;
                    RequirePerfectMatch = options.RequirePerfectMatch;
                }
                else
                {
                    subtitleDownloadLanguages = libraryOptions.SubtitleDownloadLanguages;
                    SkipIfEmbeddedSubtitlesPresent = libraryOptions.SkipSubtitlesIfEmbeddedSubtitlesPresent;
                    SkipIfAudioTrackMatches = libraryOptions.SkipSubtitlesIfAudioTrackMatches;
                    RequirePerfectMatch = libraryOptions.RequirePerfectSubtitleMatch;
                }

                foreach (var lang in subtitleDownloadLanguages)
                {
                    var query = new InternalItemsQuery
                    {
                        MediaTypes = new string[] { MediaType.Video },
                        IsVirtualItem = false,
                        IncludeItemTypes = types,
                        DtoOptions = new DtoOptions(true),
                        SourceTypes = new[] { SourceType.Library },
                        Parent = library,
                        Recursive = true
                    };

                    if (SkipIfAudioTrackMatches)
                    {
                        query.HasNoAudioTrackWithLanguage = lang;
                    }

                    if (SkipIfEmbeddedSubtitlesPresent)
                    {
                        // Exclude if it already has any subtitles of the same language
                        query.HasNoSubtitleTrackWithLanguage = lang;
                    }
                    else
                    {
                        // Exclude if it already has external subtitles of the same language
                        query.HasNoExternalSubtitleTrackWithLanguage = lang;
                    }

                    var videosByLanguage = _libraryManager.GetItemList(query);

                    foreach (var video in videosByLanguage)
                    {
                        dict[video.Id] = video;
                    }
                }
            }

            var videos = dict.Values.ToList();
            if (videos.Count == 0)
            {
                return;
            }

            var numComplete = 0;

            foreach (var video in videos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await DownloadSubtitles(video as Video, options, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error downloading subtitles for {0}", ex, video.Path);
                }

                // Update progress
                numComplete++;
                double percent = numComplete;
                percent /= videos.Count;

                progress.Report(100 * percent);
            }
        }

        private async Task<bool> DownloadSubtitles(Video video, SubtitleOptions options, CancellationToken cancellationToken)
        {
            var mediaStreams = video.GetMediaStreams();

            var libraryOptions = _libraryManager.GetLibraryOptions(video);

            string[] subtitleDownloadLanguages;
            bool SkipIfEmbeddedSubtitlesPresent;
            bool SkipIfAudioTrackMatches;
            bool RequirePerfectMatch;

            if (libraryOptions.SubtitleDownloadLanguages == null)
            {
                subtitleDownloadLanguages = options.DownloadLanguages;
                SkipIfEmbeddedSubtitlesPresent = options.SkipIfEmbeddedSubtitlesPresent;
                SkipIfAudioTrackMatches = options.SkipIfAudioTrackMatches;
                RequirePerfectMatch = options.RequirePerfectMatch;
            }
            else
            {
                subtitleDownloadLanguages = libraryOptions.SubtitleDownloadLanguages;
                SkipIfEmbeddedSubtitlesPresent = libraryOptions.SkipSubtitlesIfEmbeddedSubtitlesPresent;
                SkipIfAudioTrackMatches = libraryOptions.SkipSubtitlesIfAudioTrackMatches;
                RequirePerfectMatch = libraryOptions.RequirePerfectSubtitleMatch;
            }

            var downloadedLanguages = await new SubtitleDownloader(_logger,
                _subtitleManager)
                .DownloadSubtitles(video,
                mediaStreams,
                SkipIfEmbeddedSubtitlesPresent,
                SkipIfAudioTrackMatches,
                RequirePerfectMatch,
                subtitleDownloadLanguages,
                libraryOptions.DisabledSubtitleFetchers,
                libraryOptions.SubtitleFetcherOrder,
                cancellationToken).ConfigureAwait(false);

            // Rescan
            if (downloadedLanguages.Count > 0)
            {
                await video.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[] { 
            
                // Every so often
                new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(24).Ticks}
            };
        }

        public string Key
        {
            get { return "DownloadSubtitles"; }
        }
    }
}
