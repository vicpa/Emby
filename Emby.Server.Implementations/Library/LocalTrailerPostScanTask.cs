﻿using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Emby.Server.Implementations.Library
{
    public class LocalTrailerPostScanTask : ILibraryPostScanTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IChannelManager _channelManager;

        public LocalTrailerPostScanTask(ILibraryManager libraryManager, IChannelManager channelManager)
        {
            _libraryManager = libraryManager;
            _channelManager = channelManager;
        }

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name, typeof(Game).Name, typeof(Movie).Name, typeof(Series).Name },
                Recursive = true,
                DtoOptions = new DtoOptions(true)

            }).OfType<IHasTrailers>().ToList();

            var trailerTypes = Enum.GetNames(typeof(TrailerType))
                    .Select(i => (TrailerType)Enum.Parse(typeof(TrailerType), i, true))
                    .Except(new[] { TrailerType.LocalTrailer })
                    .ToArray();

            var trailers = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Trailer).Name },
                TrailerTypes = trailerTypes,
                Recursive = true,
                DtoOptions = new DtoOptions(false)
            });

            var numComplete = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AssignTrailers(item, trailers);

                numComplete++;
                double percent = numComplete;
                percent /= items.Count;
                progress.Report(percent * 100);
            }

            progress.Report(100);

            return Task.CompletedTask;
        }

        private void AssignTrailers(IHasTrailers item, IEnumerable<BaseItem> channelTrailers)
        {
            if (item is Game)
            {
                return;
            }

            var imdbId = item.GetProviderId(MetadataProviders.Imdb);
            var tmdbId = item.GetProviderId(MetadataProviders.Tmdb);

            var trailers = channelTrailers.Where(i =>
            {
                if (!string.IsNullOrWhiteSpace(imdbId) &&
                    string.Equals(imdbId, i.GetProviderId(MetadataProviders.Imdb), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.IsNullOrWhiteSpace(tmdbId) &&
                    string.Equals(tmdbId, i.GetProviderId(MetadataProviders.Tmdb), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            });

            var trailerIds = trailers.Select(i => i.Id)
                .ToArray();

            if (!trailerIds.SequenceEqual(item.RemoteTrailerIds))
            {
                item.RemoteTrailerIds = trailerIds;

                var baseItem = (BaseItem)item;
                baseItem.UpdateToRepository(ItemUpdateType.MetadataImport, CancellationToken.None);
            }
        }
    }
}
