﻿using System;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Providers.Manager;
using System.Collections.Generic;
using System.Threading.Tasks;

using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Providers.TV
{
    public class EpisodeMetadataService : MetadataService<Episode, EpisodeInfo>
    {
        protected override ItemUpdateType BeforeSaveInternal(Episode item, bool isFullRefresh, ItemUpdateType currentUpdateType)
        {
            var updateType = base.BeforeSaveInternal(item, isFullRefresh, currentUpdateType);

            var seriesName = item.FindSeriesName();
            if (!string.Equals(item.SeriesName, seriesName, StringComparison.Ordinal))
            {
                item.SeriesName = seriesName;
                updateType |= ItemUpdateType.MetadataImport;
            }

            var seasonName = item.FindSeasonName();
            if (!string.Equals(item.SeasonName, seasonName, StringComparison.Ordinal))
            {
                item.SeasonName = seasonName;
                updateType |= ItemUpdateType.MetadataImport;
            }

            var seriesId = item.FindSeriesId();
            if (item.SeriesId != seriesId)
            {
                item.SeriesId = seriesId;
                updateType |= ItemUpdateType.MetadataImport;
            }

            var seasonId = item.FindSeasonId();
            if (item.SeasonId != seasonId)
            {
                item.SeasonId = seasonId;
                updateType |= ItemUpdateType.MetadataImport;
            }

            var seriesPresentationUniqueKey = item.FindSeriesPresentationUniqueKey();
            if (!string.Equals(item.SeriesPresentationUniqueKey, seriesPresentationUniqueKey, StringComparison.Ordinal))
            {
                item.SeriesPresentationUniqueKey = seriesPresentationUniqueKey;
                updateType |= ItemUpdateType.MetadataImport;
            }

            return updateType;
        }

        protected override void MergeData(MetadataResult<Episode> source, MetadataResult<Episode> target, MetadataFields[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            ProviderUtils.MergeBaseItemData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            var sourceItem = source.Item;
            var targetItem = target.Item;

            if (replaceData || !targetItem.AirsBeforeSeasonNumber.HasValue)
            {
                targetItem.AirsBeforeSeasonNumber = sourceItem.AirsBeforeSeasonNumber;
            }

            if (replaceData || !targetItem.AirsAfterSeasonNumber.HasValue)
            {
                targetItem.AirsAfterSeasonNumber = sourceItem.AirsAfterSeasonNumber;
            }

            if (replaceData || !targetItem.AirsBeforeEpisodeNumber.HasValue)
            {
                targetItem.AirsBeforeEpisodeNumber = sourceItem.AirsBeforeEpisodeNumber;
            }

            if (replaceData || !targetItem.IndexNumberEnd.HasValue)
            {
                targetItem.IndexNumberEnd = sourceItem.IndexNumberEnd;
            }
        }

        public EpisodeMetadataService(IServerConfigurationManager serverConfigurationManager, ILogger logger, IProviderManager providerManager, IFileSystem fileSystem, IUserDataManager userDataManager, ILibraryManager libraryManager) : base(serverConfigurationManager, logger, providerManager, fileSystem, userDataManager, libraryManager)
        {
        }
    }
}
