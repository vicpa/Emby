﻿using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.XmlTv.Classes;
using Emby.XmlTv.Entities;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Progress;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Emby.Server.Implementations.LiveTv.Listings
{
    public class XmlTvProvider : IListingsProvider
    {
        private readonly IServerConfigurationManager _config;
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IZipClient _zipClient;

        public XmlTvProvider(IServerConfigurationManager config, IHttpClient httpClient, ILogger logger, IFileSystem fileSystem, IZipClient zipClient)
        {
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
            _fileSystem = fileSystem;
            _zipClient = zipClient;
        }

        public string Name
        {
            get { return "XmlTV"; }
        }

        public string Type
        {
            get { return "xmltv"; }
        }

        private string GetLanguage(ListingsProviderInfo info)
        {
            if (!string.IsNullOrEmpty(info.PreferredLanguage))
            {
                return info.PreferredLanguage;
            }

            return _config.Configuration.PreferredMetadataLanguage;
        }

        private async Task<string> GetXml(ListingsProviderInfo info, string path, CancellationToken cancellationToken)
        {
            _logger.Info("xmltv path: {0}", path);

            if (!path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return UnzipIfNeeded(path, path);
            }

            var cacheFilename = DateTime.UtcNow.DayOfYear.ToString(CultureInfo.InvariantCulture) + "-" + DateTime.UtcNow.Hour.ToString(CultureInfo.InvariantCulture) + ".xml";
            var cacheFile = Path.Combine(_config.ApplicationPaths.CachePath, "xmltv", cacheFilename);
            if (_fileSystem.FileExists(cacheFile))
            {
                return UnzipIfNeeded(path, cacheFile);
            }

            _logger.Info("Downloading xmltv listings from {0}", path);

            var tempFile = await _httpClient.GetTempFile(new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = path,
                Progress = new SimpleProgress<Double>(),
                DecompressionMethod = CompressionMethod.Gzip,

                // It's going to come back gzipped regardless of this value
                // So we need to make sure the decompression method is set to gzip
                EnableHttpCompression = true,

                UserAgent = string.IsNullOrEmpty(info.UserAgent) ? "Emby/3.0" : info.UserAgent

            }).ConfigureAwait(false);

            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(cacheFile));

            _fileSystem.CopyFile(tempFile, cacheFile, true);

            return UnzipIfNeeded(path, cacheFile);
        }

        private string UnzipIfNeeded(string originalUrl, string file)
        {
            var ext = Path.GetExtension(originalUrl.Split('?')[0]);

            //if (string.Equals(ext, ".gz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var tempFolder = ExtractGz(file);
                    return FindXmlFile(tempFolder);
                }
                catch (Exception ex)
                {
                    //_logger.ErrorException("Error extracting from gz file {0}", ex, file);
                }

                try
                {
                    var tempFolder = ExtractFirstFileFromGz(file);
                    return FindXmlFile(tempFolder);
                }
                catch (Exception ex)
                {
                    //_logger.ErrorException("Error extracting from zip file {0}", ex, file);
                }
            }

            return file;
        }

        private string ExtractFirstFileFromGz(string file)
        {
            using (var stream = _fileSystem.OpenRead(file))
            {
                var tempFolder = Path.Combine(_config.ApplicationPaths.TempDirectory, Guid.NewGuid().ToString());
                _fileSystem.CreateDirectory(tempFolder);

                _zipClient.ExtractFirstFileFromGz(stream, tempFolder, "data.xml");

                return tempFolder;
            }
        }

        private string ExtractGz(string file)
        {
            using (var stream = _fileSystem.OpenRead(file))
            {
                var tempFolder = Path.Combine(_config.ApplicationPaths.TempDirectory, Guid.NewGuid().ToString());
                _fileSystem.CreateDirectory(tempFolder);

                _zipClient.ExtractAllFromGz(stream, tempFolder, true);

                return tempFolder;
            }
        }

        private string FindXmlFile(string directory)
        {
            return _fileSystem.GetFiles(directory, true)
                .Where(i => string.Equals(i.Extension, ".xml", StringComparison.OrdinalIgnoreCase))
                .Select(i => i.FullName)
                .FirstOrDefault();
        }

        public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                throw new ArgumentNullException("channelId");
            }

            if (!await EmbyTV.EmbyTVRegistration.Instance.EnableXmlTv().ConfigureAwait(false))
            {
                var length = endDateUtc - startDateUtc;
                if (length.TotalDays > 1)
                {
                    endDateUtc = startDateUtc.AddDays(1);
                }
            }

            _logger.Debug("Getting xmltv programs for channel {0}", channelId);

            var path = await GetXml(info, info.Path, cancellationToken).ConfigureAwait(false);

            _logger.Debug("Opening XmlTvReader for {0}", path);

            return new XmlTvReader(path, GetLanguage(info))
                .GetProgrammes(channelId, startDateUtc, endDateUtc, cancellationToken)
                .Select(p => GetProgramInfo(p, info));
        }

        private ProgramInfo GetProgramInfo(XmlTvProgram p, ListingsProviderInfo info)
        {
            var episodeTitle = p.Episode == null ? null : p.Episode.Title;

            var programInfo = new ProgramInfo
            {
                ChannelId = p.ChannelId,
                EndDate = GetDate(p.EndDate),
                EpisodeNumber = p.Episode == null ? null : p.Episode.Episode,
                EpisodeTitle = episodeTitle,
                Genres = p.Categories,
                StartDate = GetDate(p.StartDate),
                Name = p.Title,
                Overview = p.Description,
                ProductionYear = !p.CopyrightDate.HasValue ? (int?)null : p.CopyrightDate.Value.Year,
                SeasonNumber = p.Episode == null ? null : p.Episode.Series,
                IsSeries = p.Episode != null,
                IsRepeat = p.IsPreviouslyShown && !p.IsNew,
                IsPremiere = p.Premiere != null,
                IsKids = p.Categories.Any(c => info.KidsCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
                IsMovie = p.Categories.Any(c => info.MovieCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
                IsNews = p.Categories.Any(c => info.NewsCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
                IsSports = p.Categories.Any(c => info.SportsCategories.Contains(c, StringComparer.OrdinalIgnoreCase)),
                ImageUrl = p.Icon != null && !String.IsNullOrEmpty(p.Icon.Source) ? p.Icon.Source : null,
                HasImage = p.Icon != null && !String.IsNullOrEmpty(p.Icon.Source),
                OfficialRating = p.Rating != null && !String.IsNullOrEmpty(p.Rating.Value) ? p.Rating.Value : null,
                CommunityRating = p.StarRating.HasValue ? p.StarRating.Value : (float?)null,
                SeriesId = p.Episode != null ? p.Title.GetMD5().ToString("N") : null,
                ProviderIds = p.ProviderIds,
                SeriesProviderIds = p.ProviderIds
            };

            if (!string.IsNullOrWhiteSpace(p.ProgramId))
            {
                programInfo.ShowId = p.ProgramId;
            }
            else
            {
                string iceTvEpisodeNumber;
                p.ProviderIds.TryGetValue("icetv", out iceTvEpisodeNumber);

                var uniqueString = (p.Title ?? string.Empty) + (episodeTitle ?? string.Empty) + (iceTvEpisodeNumber ?? string.Empty);

                if (programInfo.SeasonNumber.HasValue)
                {
                    uniqueString = "-" + programInfo.SeasonNumber.Value.ToString(CultureInfo.InvariantCulture);
                }
                if (programInfo.EpisodeNumber.HasValue)
                {
                    uniqueString = "-" + programInfo.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture);
                }

                programInfo.ShowId = uniqueString.GetMD5().ToString("N");

                // If we don't have valid episode info, assume it's a unique program, otherwise recordings might be skipped
                if (programInfo.IsSeries && !programInfo.IsRepeat)
                {
                    if ((programInfo.EpisodeNumber ?? 0) == 0)
                    {
                        programInfo.ShowId = programInfo.ShowId + programInfo.StartDate.Ticks.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }

            // Construct an id from the channel and start date
            programInfo.Id = String.Format("{0}_{1:O}", p.ChannelId, p.StartDate);

            if (programInfo.IsMovie)
            {
                programInfo.IsSeries = false;
                programInfo.EpisodeNumber = null;
                programInfo.EpisodeTitle = null;
            }

            return programInfo;
        }

        private DateTime GetDate(DateTime date)
        {
            if (date.Kind != DateTimeKind.Utc)
            {
                date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            }
            return date;
        }

        public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
        {
            // Assume all urls are valid. check files for existence
            if (!info.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !_fileSystem.FileExists(info.Path))
            {
                throw new FileNotFoundException("Could not find the XmlTv file specified:", info.Path);
            }

            return Task.FromResult(true);
        }

        public async Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
        {
            // In theory this should never be called because there is always only one lineup
            var channels = await GetChannels(info, CancellationToken.None);

            return channels.Select(i => new NameIdPair
            {
                Name = i.Name,
                Id = i.Id

            }).ToList();
        }

        public async Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
        {
            var path = await GetXml(info, info.Path, cancellationToken).ConfigureAwait(false);

            _logger.Debug("Opening XmlTvReader for {0}", path);

            var xmlChannels = new XmlTvReader(path, GetLanguage(info)).GetChannels();

            var list = new List<ChannelInfo>();

            foreach (var xmlChannel in xmlChannels)
            {
                var channel = new ChannelInfo
                {
                    Id = xmlChannel.Id,
                    Name = xmlChannel.DisplayName,
                    ImageUrl = xmlChannel.Icon != null && !string.IsNullOrWhiteSpace(xmlChannel.Icon.Source) ? xmlChannel.Icon.Source : null,
                    Number = string.IsNullOrWhiteSpace(xmlChannel.Number) ? xmlChannel.Id : xmlChannel.Number
                };

                list.Add(channel);
            }

            return list;
        }
    }
}