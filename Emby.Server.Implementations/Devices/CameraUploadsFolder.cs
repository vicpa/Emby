﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Serialization;

namespace Emby.Server.Implementations.Devices
{
    public class CameraUploadsFolder : BasePluginFolder, ISupportsUserSpecificView
    {
        public CameraUploadsFolder()
        {
            Name = "Camera Uploads";
        }

        public override bool IsVisible(User user)
        {
            if (!user.Policy.EnableAllFolders && !user.Policy.EnabledFolders.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            return base.IsVisible(user) && HasChildren();
        }

        [IgnoreDataMember]
        public override string CollectionType
        {
            get { return MediaBrowser.Model.Entities.CollectionType.Photos; }
        }

        [IgnoreDataMember]
        public override bool SupportsInheritedParentImages
        {
            get
            {
                return false;
            }
        }

        public override string GetClientTypeName()
        {
            return typeof(CollectionFolder).Name;
        }

        private bool? _hasChildren;
        private bool HasChildren()
        {
            if (!_hasChildren.HasValue)
            {
                _hasChildren = LibraryManager.GetItemIds(new InternalItemsQuery { Parent = this }).Count > 0;
            }

            return _hasChildren.Value;
        }

        protected override Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
        {
            _hasChildren = null;
            return base.ValidateChildrenInternal(progress, cancellationToken, recursive, refreshChildMetadata, refreshOptions, directoryService);
        }

        [IgnoreDataMember]
        public bool EnableUserSpecificView
        {
            get { return true; }
        }
    }
}
