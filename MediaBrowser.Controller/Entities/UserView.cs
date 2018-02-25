﻿using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Serialization;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;

namespace MediaBrowser.Controller.Entities
{
    public class UserView : Folder, IHasCollectionType
    {
        public string ViewType { get; set; }
        public Guid DisplayParentId { get; set; }

        public Guid? UserId { get; set; }

        public static ITVSeriesManager TVSeriesManager;
        public static IPlaylistManager PlaylistManager;

        [IgnoreDataMember]
        public string CollectionType
        {
            get
            {
                return ViewType;
            }
        }

        public override IEnumerable<Guid> GetIdsForAncestorQuery()
        {
            var list = new List<Guid>();

            if (DisplayParentId != Guid.Empty)
            {
                list.Add(DisplayParentId);
            }
            else if (ParentId != Guid.Empty)
            {
                list.Add(ParentId);
            }
            else
            {
                list.Add(Id);
            }
            return list;
        }

        [IgnoreDataMember]
        public override bool SupportsInheritedParentImages
        {
            get
            {
                return false;
            }
        }

        [IgnoreDataMember]
        public override bool SupportsPlayedStatus
        {
            get
            {
                return false;
            }
        }

        //public override double? GetDefaultPrimaryImageAspectRatio()
        //{
        //    double value = 16;
        //    value /= 9;

        //    return value;
        //}

        public override int GetChildCount(User user)
        {
            return GetChildren(user, true).Count;
        }

        protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
        {
            var parent = this as Folder;

            if (DisplayParentId != Guid.Empty)
            {
                parent = LibraryManager.GetItemById(DisplayParentId) as Folder ?? parent;
            }
            else if (ParentId != Guid.Empty)
            {
                parent = LibraryManager.GetItemById(ParentId) as Folder ?? parent;
            }

            return new UserViewBuilder(UserViewManager, LibraryManager, Logger, UserDataManager, TVSeriesManager, ConfigurationManager, PlaylistManager)
                .GetUserItems(parent, this, CollectionType, query);
        }

        public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren)
        {
            var result = GetItemList(new InternalItemsQuery
            {
                User = user,
                EnableTotalRecordCount = false,
                DtoOptions = new DtoOptions(true)

            });

            return result.ToList();
        }

        public override bool CanDelete()
        {
            return false;
        }

        public override bool IsSaveLocalMetadataEnabled()
        {
            return true;
        }

        public override IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
        {
            query.SetUser(user);
            query.Recursive = true;
            query.EnableTotalRecordCount = false;
            query.ForceDirect = true;

            return GetItemList(query);
        }

        protected override IEnumerable<BaseItem> GetEligibleChildrenForRecursiveChildren(User user)
        {
            return GetChildren(user, false);
        }

        private static string[] UserSpecificViewTypes = new string[]
            {
                MediaBrowser.Model.Entities.CollectionType.Playlists
            };

        public static bool IsUserSpecific(Folder folder)
        {
            var collectionFolder = folder as ICollectionFolder;

            if (collectionFolder == null)
            {
                return false;
            }

            var supportsUserSpecific = folder as ISupportsUserSpecificView;
            if (supportsUserSpecific != null && supportsUserSpecific.EnableUserSpecificView)
            {
                return true;
            }

            return UserSpecificViewTypes.Contains(collectionFolder.CollectionType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsEligibleForGrouping(Folder folder)
        {
            var collectionFolder = folder as ICollectionFolder;
            return collectionFolder != null && IsEligibleForGrouping(collectionFolder.CollectionType);
        }

        private static string[] ViewTypesEligibleForGrouping = new string[]
            {
                MediaBrowser.Model.Entities.CollectionType.Movies,
                MediaBrowser.Model.Entities.CollectionType.TvShows,
                string.Empty
            };

        public static bool IsEligibleForGrouping(string viewType)
        {
            return ViewTypesEligibleForGrouping.Contains(viewType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private static string[] OriginalFolderViewTypes = new string[]
            {
                MediaBrowser.Model.Entities.CollectionType.Games,
                MediaBrowser.Model.Entities.CollectionType.Books,
                MediaBrowser.Model.Entities.CollectionType.MusicVideos,
                MediaBrowser.Model.Entities.CollectionType.HomeVideos,
                MediaBrowser.Model.Entities.CollectionType.Photos,
                MediaBrowser.Model.Entities.CollectionType.Music,
                MediaBrowser.Model.Entities.CollectionType.BoxSets
            };

        public static bool EnableOriginalFolder(string viewType)
        {
            return OriginalFolderViewTypes.Contains(viewType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        protected override Task ValidateChildrenInternal(IProgress<double> progress, System.Threading.CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, Providers.MetadataRefreshOptions refreshOptions, Providers.IDirectoryService directoryService)
        {
            return Task.FromResult(true);
        }

        [IgnoreDataMember]
        public override bool SupportsPeople
        {
            get
            {
                return false;
            }
        }
    }
}
