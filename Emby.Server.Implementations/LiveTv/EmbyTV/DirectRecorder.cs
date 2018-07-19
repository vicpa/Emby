﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;

namespace Emby.Server.Implementations.LiveTv.EmbyTV
{
    public class DirectRecorder : IRecorder
    {
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly IFileSystem _fileSystem;
        private readonly IStreamHelper _streamHelper;

        public DirectRecorder(ILogger logger, IHttpClient httpClient, IFileSystem fileSystem, IStreamHelper streamHelper)
        {
            _logger = logger;
            _httpClient = httpClient;
            _fileSystem = fileSystem;
            _streamHelper = streamHelper;
        }

        public string GetOutputPath(MediaSourceInfo mediaSource, string targetFile)
        {
            return targetFile;
        }

        public Task Record(IDirectStreamProvider directStreamProvider, MediaSourceInfo mediaSource, string targetFile, TimeSpan duration, Action onStarted, CancellationToken cancellationToken)
        {
            if (directStreamProvider != null)
            {
                return RecordFromDirectStreamProvider(directStreamProvider, targetFile, duration, onStarted, cancellationToken);
            }

            return RecordFromMediaSource(mediaSource, targetFile, duration, onStarted, cancellationToken);
        }

        private async Task RecordFromDirectStreamProvider(IDirectStreamProvider directStreamProvider, string targetFile, TimeSpan duration, Action onStarted, CancellationToken cancellationToken)
        {
            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(targetFile));

            using (var output = _fileSystem.GetFileStream(targetFile, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read))
            {
                onStarted();

                _logger.Info("Copying recording stream to file {0}", targetFile);

                // The media source is infinite so we need to handle stopping ourselves
                var durationToken = new CancellationTokenSource(duration);
                cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, durationToken.Token).Token;

                await directStreamProvider.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            _logger.Info("Recording completed to file {0}", targetFile);
        }

        private async Task RecordFromMediaSource(MediaSourceInfo mediaSource, string targetFile, TimeSpan duration, Action onStarted, CancellationToken cancellationToken)
        {
            var httpRequestOptions = new HttpRequestOptions
            {
                Url = mediaSource.Path,
                BufferContent = false,

                // Some remote urls will expect a user agent to be supplied
                UserAgent = "Emby/3.0",

                // Shouldn't matter but may cause issues
                EnableHttpCompression = false
            };

            using (var response = await _httpClient.SendAsync(httpRequestOptions, "GET").ConfigureAwait(false))
            {
                _logger.Info("Opened recording stream from tuner provider");

                _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(targetFile));

                using (var output = _fileSystem.GetFileStream(targetFile, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read))
                {
                    onStarted();

                    _logger.Info("Copying recording stream to file {0}", targetFile);

                    // The media source if infinite so we need to handle stopping ourselves
                    var durationToken = new CancellationTokenSource(duration);
                    cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, durationToken.Token).Token;

                    await _streamHelper.CopyUntilCancelled(response.Content, output, 81920, cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.Info("Recording completed to file {0}", targetFile);
        }
    }
}
