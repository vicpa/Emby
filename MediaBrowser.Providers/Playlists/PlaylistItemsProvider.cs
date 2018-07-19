﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Playlists;
using System.IO;
using PlaylistsNET;
using PlaylistsNET.Content;
using System.Collections.Generic;

namespace MediaBrowser.Providers.Playlists
{
    public class PlaylistItemsProvider : ICustomMetadataProvider<Playlist>,
        IHasOrder,
        IForcedProvider,
        IPreRefreshProvider,
        IHasItemChangeMonitor
    {
        private ILogger _logger;
        private IFileSystem _fileSystem;

        public PlaylistItemsProvider(IFileSystem fileSystem, ILogger logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public string Name
        {
            get { return "Playlist Reader"; }
        }

        public Task<ItemUpdateType> FetchAsync(Playlist item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            var path = item.Path;
            if (!Playlist.IsPlaylistFile(path))
            {
                return Task.FromResult(ItemUpdateType.None);
            }

            var extension = Path.GetExtension(path);
            if (!Playlist.SupportedExtensions.Contains(extension ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(ItemUpdateType.None);
            }

            using (var stream = _fileSystem.OpenRead(path))
            {
                var items = GetItems(stream, extension).ToArray();

                item.LinkedChildren = items;
            }

            return Task.FromResult(ItemUpdateType.None);
        }

        private IEnumerable<LinkedChild> GetItems(Stream stream, string extension)
        {
            if (string.Equals(".wpl", extension, StringComparison.OrdinalIgnoreCase))
            {
                return GetWplItems(stream);
            }
            if (string.Equals(".zpl", extension, StringComparison.OrdinalIgnoreCase))
            {
                return GetZplItems(stream);
            }
            if (string.Equals(".m3u", extension, StringComparison.OrdinalIgnoreCase))
            {
                return GetM3uItems(stream);
            }
            if (string.Equals(".m3u8", extension, StringComparison.OrdinalIgnoreCase))
            {
                return GetM3u8Items(stream);
            }
            if (string.Equals(".pls", extension, StringComparison.OrdinalIgnoreCase))
            {
                return GetPlsItems(stream);
            }

            return new List<LinkedChild>();
        }

        private IEnumerable<LinkedChild> GetPlsItems(Stream stream)
        {
            var content = new PlsContent();
            var playlist = content.GetFromStream(stream);

            return playlist.PlaylistEntries.Select(i => new LinkedChild
            {
                Path = i.Path,
                Type = LinkedChildType.Manual
            });
        }

        private IEnumerable<LinkedChild> GetM3u8Items(Stream stream)
        {
            var content = new M3u8Content();
            var playlist = content.GetFromStream(stream);

            return playlist.PlaylistEntries.Select(i => new LinkedChild
            {
                Path = i.Path,
                Type = LinkedChildType.Manual
            });
        }

        private IEnumerable<LinkedChild> GetM3uItems(Stream stream)
        {
            var content = new M3uContent();
            var playlist = content.GetFromStream(stream);

            return playlist.PlaylistEntries.Select(i => new LinkedChild
            {
                Path = i.Path,
                Type = LinkedChildType.Manual
            });
        }

        private IEnumerable<LinkedChild> GetZplItems(Stream stream)
        {
            var content = new ZplContent();
            var playlist = content.GetFromStream(stream);

            return playlist.PlaylistEntries.Select(i => new LinkedChild
            {
                Path = i.Path,
                Type = LinkedChildType.Manual
            });
        }

        private IEnumerable<LinkedChild> GetWplItems(Stream stream)
        {
            WplContent content = new WplContent();
            var playlist = content.GetFromStream(stream);

            return playlist.PlaylistEntries.Select(i => new LinkedChild
            {
                Path = i.Path,
                Type = LinkedChildType.Manual
            });
        }

        public bool HasChanged(BaseItem item, IDirectoryService directoryService)
        {
            var path = item.Path;

            if (!string.IsNullOrWhiteSpace(path) && item.IsFileProtocol)
            {
                var file = directoryService.GetFile(path);
                if (file != null && file.LastWriteTimeUtc != item.DateModified)
                {
                    _logger.Debug("Refreshing {0} due to date modified timestamp change.", path);
                    return true;
                }
            }

            return false;
        }

        public int Order
        {
            get
            {
                // Run last
                return 100;
            }
        }
    }
}
