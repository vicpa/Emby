﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.Server.Implementations.Images;
using MediaBrowser.Model.IO;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Querying;

namespace Emby.Server.Implementations.UserViews
{
    public class CollectionFolderImageProvider : BaseDynamicImageProvider<CollectionFolder>
    {
        public CollectionFolderImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor) : base(fileSystem, providerManager, applicationPaths, imageProcessor)
        {
        }

        protected override List<BaseItem> GetItemsWithImages(BaseItem item)
        {
            var view = (CollectionFolder)item;
            var viewType = view.CollectionType;

            string[] includeItemTypes;

            if (string.Equals(viewType, CollectionType.Movies))
            {
                includeItemTypes = new string[] { "Movie" };
            }
            else if (string.Equals(viewType, CollectionType.TvShows))
            {
                includeItemTypes = new string[] { "Series" };
            }
            else if (string.Equals(viewType, CollectionType.Music))
            {
                includeItemTypes = new string[] { "MusicAlbum" };
            }
            else if (string.Equals(viewType, CollectionType.Books))
            {
                includeItemTypes = new string[] { "Book", "AudioBook" };
            }
            else if (string.Equals(viewType, CollectionType.Games))
            {
                includeItemTypes = new string[] { "Game" };
            }
            else
            {
                includeItemTypes = new string[] { "Video", "Audio", "Photo", "Movie", "Series" };
            }

            var recursive = !new[] { CollectionType.Playlists }.Contains(view.CollectionType ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            return view.GetItemList(new InternalItemsQuery
            {
                CollapseBoxSetItems = false,
                Recursive = recursive,
                DtoOptions = new DtoOptions(false),
                ImageTypes = new ImageType[] { ImageType.Primary },
                Limit = 4,
                OrderBy = new Tuple<string, SortOrder>[]
                {
                    new Tuple<string, SortOrder>(ItemSortBy.Random, SortOrder.Ascending)
                },
                IncludeItemTypes = includeItemTypes

            }).ToList();
        }

        protected override bool Supports(BaseItem item)
        {
            return item is CollectionFolder;
        }

        protected override string CreateImage(BaseItem item, List<BaseItem> itemsWithImages, string outputPathWithoutExtension, ImageType imageType, int imageIndex)
        {
            var outputPath = Path.ChangeExtension(outputPathWithoutExtension, ".png");

            if (imageType == ImageType.Primary)
            {
                if (itemsWithImages.Count == 0)
                {
                    return null;
                }

                return CreateThumbCollage(item, itemsWithImages, outputPath, 960, 540);
            }

            return base.CreateImage(item, itemsWithImages, outputPath, imageType, imageIndex);
        }
    }

    public class ManualCollectionFolderImageProvider : BaseDynamicImageProvider<ManualCollectionsFolder>
    {
        private readonly ILibraryManager _libraryManager;

        public ManualCollectionFolderImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor, ILibraryManager libraryManager) : base(fileSystem, providerManager, applicationPaths, imageProcessor)
        {
            _libraryManager = libraryManager;
        }

        protected override List<BaseItem> GetItemsWithImages(BaseItem item)
        {
            var view = (ManualCollectionsFolder)item;

            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(BoxSet).Name },
                Limit = 4,
                OrderBy = new[] { new Tuple<string, SortOrder>(ItemSortBy.Random, SortOrder.Ascending) },
                ImageTypes = new ImageType[] { ImageType.Primary },
                DtoOptions = new DtoOptions(false)
            });
        }

        protected override bool Supports(BaseItem item)
        {
            return item is ManualCollectionsFolder;
        }

        protected override string CreateImage(BaseItem item, List<BaseItem> itemsWithImages, string outputPathWithoutExtension, ImageType imageType, int imageIndex)
        {
            var outputPath = Path.ChangeExtension(outputPathWithoutExtension, ".png");

            if (imageType == ImageType.Primary)
            {
                if (itemsWithImages.Count == 0)
                {
                    return null;
                }

                return CreateThumbCollage(item, itemsWithImages, outputPath, 960, 540);
            }

            return base.CreateImage(item, itemsWithImages, outputPath, imageType, imageIndex);
        }
    }

}
