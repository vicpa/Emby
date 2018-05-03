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
using System.Threading;

namespace MediaBrowser.Controller.Playlists
{
    public class Playlist : Folder, IHasShares
    {
        public static string[] SupportedExtensions = new string[] {

            ".m3u",
            ".m3u8",
            ".pls",
            ".wpl",
            ".zpl"
        };

        public Guid OwnerUserId { get; set; }

        public Share[] Shares { get; set; }

        public Playlist()
        {
            Shares = new Share[] { };
        }

        [IgnoreDataMember]
        public bool IsFile
        {
            get
            {
                return IsPlaylistFile(Path);
            }
        }

        public static bool IsPlaylistFile(string path)
        {
            return System.IO.Path.HasExtension(path);
        }

        [IgnoreDataMember]
        public override string ContainingFolderPath
        {
            get
            {
                var path = Path;

                if (IsPlaylistFile(path))
                {
                    return FileSystem.GetDirectoryName(path);
                }

                return path;
            }
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

        public override bool IsAuthorizedToDelete(User user, List<Folder> allCollectionFolders)
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

        protected override Task ValidateChildrenInternal(IProgress<double> progress, CancellationToken cancellationToken, bool recursive, bool refreshChildMetadata, MetadataRefreshOptions refreshOptions, IDirectoryService directoryService)
        {
            return Task.FromResult(true);
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
                    GenreIds = new[] { musicGenre.Id },
                    OrderBy = new[] { ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.SortName }.Select(i => new Tuple<string, SortOrder>(i, SortOrder.Ascending)).ToArray(),
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
                    ArtistIds = new[] { musicArtist.Id },
                    OrderBy = new[] { ItemSortBy.AlbumArtist, ItemSortBy.Album, ItemSortBy.SortName }.Select(i => new Tuple<string, SortOrder>(i, SortOrder.Ascending)).ToArray(),
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
                    OrderBy = new[] { ItemSortBy.SortName }.Select(i => new Tuple<string, SortOrder>(i, SortOrder.Ascending)).ToArray(),
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
            if (user.Id == OwnerUserId)
            {
                return true;
            }

            var shares = Shares;
            if (shares.Length == 0)
            {
                return base.IsVisible(user);
            }

            var userId = user.Id.ToString("N");
            foreach (var share in shares)
            {
                if (string.Equals(share.UserId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public override bool IsVisibleStandalone(User user)
        {
            return IsVisible(user);
        }
    }
}
