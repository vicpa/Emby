﻿using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Providers.Manager;
using System.Collections.Generic;

using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;

namespace MediaBrowser.Providers.LiveTv
{
    public class ProgramMetadataService : MetadataService<LiveTvProgram, ItemLookupInfo>
    {
        protected override void MergeData(MetadataResult<LiveTvProgram> source, MetadataResult<LiveTvProgram> target, MetadataFields[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            ProviderUtils.MergeBaseItemData(source, target, lockedFields, replaceData, mergeMetadataSettings);
        }

        public ProgramMetadataService(IServerConfigurationManager serverConfigurationManager, ILogger logger, IProviderManager providerManager, IFileSystem fileSystem, IUserDataManager userDataManager, ILibraryManager libraryManager) : base(serverConfigurationManager, logger, providerManager, fileSystem, userDataManager, libraryManager)
        {
        }
    }
    public class LiveTvMetadataService : MetadataService<LiveTvChannel, ItemLookupInfo>
    {
        protected override void MergeData(MetadataResult<LiveTvChannel> source, MetadataResult<LiveTvChannel> target, MetadataFields[] lockedFields, bool replaceData, bool mergeMetadataSettings)
        {
            ProviderUtils.MergeBaseItemData(source, target, lockedFields, replaceData, mergeMetadataSettings);
        }

        public LiveTvMetadataService(IServerConfigurationManager serverConfigurationManager, ILogger logger, IProviderManager providerManager, IFileSystem fileSystem, IUserDataManager userDataManager, ILibraryManager libraryManager) : base(serverConfigurationManager, logger, providerManager, fileSystem, userDataManager, libraryManager)
        {
        }
    }
}
