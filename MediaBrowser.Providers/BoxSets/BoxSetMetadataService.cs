﻿using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Providers.Manager;
using System.Linq;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Extensions;
using System.Collections.Generic;

namespace MediaBrowser.Providers.BoxSets
{
    public class BoxSetMetadataService : MetadataService<BoxSet, BoxSetInfo>
    {
        protected override IList<BaseItem> GetChildrenForMetadataUpdates(BoxSet item)
        {
            return item.GetLinkedChildren();
        }

        protected override void MergeData(MetadataResult<BoxSet> source, MetadataResult<BoxSet> target, MetadataFields[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            ProviderUtils.MergeBaseItemData(source, target, lockedFields, replaceData, mergeMetadataSettings);

            var sourceItem = source.Item;
            var targetItem = target.Item;

            if (mergeMetadataSettings)
            {
                targetItem.LinkedChildren = sourceItem.LinkedChildren;
            }
        }

        public BoxSetMetadataService(IServerConfigurationManager serverConfigurationManager, ILogger logger, IProviderManager providerManager, IFileSystem fileSystem, IUserDataManager userDataManager, ILibraryManager libraryManager) : base(serverConfigurationManager, logger, providerManager, fileSystem, userDataManager, libraryManager)
        {
        }

        protected override bool EnableUpdatingGenresFromChildren
        {
            get
            {
                return true;
            }
        }

        protected override bool EnableUpdatingOfficialRatingFromChildren
        {
            get
            {
                return true;
            }
        }

        protected override bool EnableUpdatingStudiosFromChildren
        {
            get
            {
                return true;
            }
        }
    }
}
