﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Serialization;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Providers;

namespace MediaBrowser.Controller.Playlists
{
    public class Playlist : Folder, IHasShares
    {
        public string OwnerUserId { get; set; }

        public List<Share> Shares { get; set; }

        public Playlist()
        {
            Shares = new List<Share>();
        }

        [IgnoreDataMember]
        protected override bool FilterLinkedChildrenPerUser
        {
            get
            {
                return true;
            }
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
                return string.Equals(MediaType, "Video", StringComparison.OrdinalIgnoreCase);
            }
        }

        [IgnoreDataMember]
        public override bool AlwaysScanInternalMetadataPath
        {
            get
            {
                return true;
            }
        }

        [IgnoreDataMember]
        public override bool SupportsCumulativeRunTimeTicks
        {
            get
            {
                return true;
            }
        }

        public override double? GetDefaultPrimaryImageAspectRatio()
        {
            return 1;
        }

        public override bool IsAuthorizedToDelete(User user)
        {
            return true;
        }

        public override bool IsSaveLocalMetadataEnabled()
        {
            return true;
        }

        protected override List<BaseItem> LoadChildren()
        {
            // Save a trip to the database
            return new List<BaseItem>();
        }

        public override List<BaseItem> GetChildren(User user, bool includeLinkedChildren)
        {
            return GetPlayableItems(user, new DtoOptions(true));
        }

        protected override IEnumerable<BaseItem> GetNonCachedChildren(IDirectoryService directoryService)
        {
            return new List<BaseItem>();
        }

        public override IEnumerable<BaseItem> GetRecursiveChildren(User user, InternalItemsQuery query)
        {
            var items = GetPlayableItems(user, query.DtoOptions);

            if (query != null)
            {
                items = items.Where(i => UserViewBuilder.FilterItem(i, query)).ToList();
            }

            return items;
        }

        public IEnumerable<Tuple<LinkedChild, BaseItem>> GetManageableItems()
        {
            return GetLinkedChildrenInfos();
        }

        private List<BaseItem> GetPlayableItems(User user, DtoOptions options)
        {
            return GetPlaylistItems(MediaType, base.GetChildren(user, true), user, options);
        }

        public static List<BaseItem> GetPlaylistItems(string playlistMediaType, IEnumerable<BaseItem> inputItems, User user, DtoOptions options)
        {
            if (user != null)
            {
                inputItems = inputItems.Where(i => i.IsVisible(user));
            }

            var list = new List<BaseItem>();

            foreach (var item in inputItems)
            {
                var playlistItems = GetPlaylistItems(item, user, playlistMediaType, options);
                list.AddRange(playlistItems);
            }

            return list;
        }

        private static IEnumerable<BaseItem> GetPlaylistItems(BaseItem item, User user, string mediaType, DtoOptions options)
        {
            var musicGenre = item as MusicGenre;
            if (musicGenre != null)
            {
                return LibraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { typeof(Audio).Name },
                    GenreIds = new[] { musicGenre.Id.ToString("N") },
                    SortBy = new[] { ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.SortName },
                    SortOrder = SortOrder.Ascending,
                    DtoOptions = options
                });
            }

            var musicArtist = item as MusicArtist;
            if (musicArtist != null)
            {
                return LibraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { typeof(Audio).Name },
                    ArtistIds = new[] { musicArtist.Id.ToString("N") },
                    SortBy = new[] { ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.SortName },
                    SortOrder = SortOrder.Ascending,
                    DtoOptions = options
                });
            }

            var folder = item as Folder;
            if (folder != null)
            {
                var query = new InternalItemsQuery(user)
                {
                    Recursive = true,
                    IsFolder = false,
                    SortBy = new[] { ItemSortBy.SortName },
                    MediaTypes = new[] { mediaType },
                    EnableTotalRecordCount = false,
                    DtoOptions = options
                };

                return folder.GetItemList(query);
            }

            return new[] { item };
        }

        [IgnoreDataMember]
        public override bool IsPreSorted
        {
            get
            {
                return true;
            }
        }

        public string PlaylistMediaType { get; set; }

        [IgnoreDataMember]
        public override string MediaType
        {
            get
            {
                return PlaylistMediaType;
            }
        }

        public void SetMediaType(string value)
        {
            PlaylistMediaType = value;
        }

        public override bool IsVisible(User user)
        {
            var userId = user.Id.ToString("N");

            return Shares.Any(i => string.Equals(userId, i.UserId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(OwnerUserId, userId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool IsVisibleStandalone(User user)
        {
            return IsVisible(user);
        }
    }
}
