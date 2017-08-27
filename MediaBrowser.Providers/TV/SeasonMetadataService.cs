﻿using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Providers.Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Providers.TV
{
    public class SeasonMetadataService : MetadataService<Season, SeasonInfo>
    {
        protected override ItemUpdateType BeforeSave(Season item, bool isFullRefresh, ItemUpdateType currentUpdateType)
        {
            var updateType = base.BeforeSave(item, isFullRefresh, currentUpdateType);

            if (item.IndexNumber.HasValue && item.IndexNumber.Value == 0)
            {
                if (!string.Equals(item.Name, ServerConfigurationManager.Configuration.SeasonZeroDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = ServerConfigurationManager.Configuration.SeasonZeroDisplayName;
                    updateType = updateType | ItemUpdateType.MetadataEdit;
                }
            }

            if (isFullRefresh || currentUpdateType > ItemUpdateType.None)
            {
                var episodes = item.GetEpisodes();
                updateType |= SavePremiereDate(item, episodes);
                updateType |= SaveIsVirtualItem(item, episodes);
            }

            var seriesName = item.FindSeriesName();
            if (!string.Equals(item.SeriesName, seriesName, StringComparison.Ordinal))
            {
                item.SeriesName = seriesName;
                updateType |= ItemUpdateType.MetadataImport;
            }

            var seriesPresentationUniqueKey = item.FindSeriesPresentationUniqueKey();
            if (!string.Equals(item.SeriesPresentationUniqueKey, seriesPresentationUniqueKey, StringComparison.Ordinal))
            {
                item.SeriesPresentationUniqueKey = seriesPresentationUniqueKey;
                updateType |= ItemUpdateType.MetadataImport;
            }

            var seriesId = item.FindSeriesId();
            if (item.SeriesId != seriesId)
            {
                item.SeriesId = seriesId;
                updateType |= ItemUpdateType.MetadataImport;
            }

            return updateType;
        }

        protected override void MergeData(MetadataResult<Season> source, MetadataResult<Season> target, MetadataFields[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            ProviderUtils.MergeBaseItemData(source, target, lockedFields, replaceData, mergeMetadataSettings);
        }

        private ItemUpdateType SavePremiereDate(Season item, List<BaseItem> episodes)
        {
            var dates = episodes.Where(i => i.PremiereDate.HasValue).Select(i => i.PremiereDate.Value).ToList();

            DateTime? premiereDate = null;

            if (dates.Count > 0)
            {
                premiereDate = dates.Min();
            }

            if (item.PremiereDate != premiereDate)
            {
                item.PremiereDate = premiereDate;
                return ItemUpdateType.MetadataEdit;
            }

            return ItemUpdateType.None;
        }

        private ItemUpdateType SaveIsVirtualItem(Season item, List<BaseItem> episodes)
        {
            var isVirtualItem = item.LocationType == LocationType.Virtual && (episodes.Count == 0 || episodes.All(i => i.LocationType == LocationType.Virtual));

            if (item.IsVirtualItem != isVirtualItem)
            {
                item.IsVirtualItem = isVirtualItem;
                return ItemUpdateType.MetadataEdit;
            }

            return ItemUpdateType.None;
        }

        public SeasonMetadataService(IServerConfigurationManager serverConfigurationManager, ILogger logger, IProviderManager providerManager, IFileSystem fileSystem, IUserDataManager userDataManager, ILibraryManager libraryManager) : base(serverConfigurationManager, logger, providerManager, fileSystem, userDataManager, libraryManager)
        {
        }
    }
}
