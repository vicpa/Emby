﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Emby.Server.Implementations.Images;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Extensions;

namespace Emby.Server.Implementations.Collections
{
    public class CollectionImageProvider : BaseDynamicImageProvider<BoxSet>
    {
        public CollectionImageProvider(IFileSystem fileSystem, IProviderManager providerManager, IApplicationPaths applicationPaths, IImageProcessor imageProcessor) : base(fileSystem, providerManager, applicationPaths, imageProcessor)
        {
        }

        protected override bool Supports(BaseItem item)
        {
            // Right now this is the only way to prevent this image from getting created ahead of internet image providers
            if (!item.IsLocked)
            {
                return false;
            }

            return base.Supports(item);
        }

        protected override List<BaseItem> GetItemsWithImages(BaseItem item)
        {
            var playlist = (BoxSet)item;

            var items = playlist.Children.Concat(playlist.GetLinkedChildren())
                .Select(i =>
                {
                    var subItem = i;

                    var episode = subItem as Episode;

                    if (episode != null)
                    {
                        var series = episode.Series;
                        if (series != null && series.HasImage(ImageType.Primary))
                        {
                            return series;
                        }
                    }

                    if (subItem.HasImage(ImageType.Primary))
                    {
                        return subItem;
                    }

                    var parent = subItem.IsOwnedItem ? subItem.GetOwner() : subItem.GetParent();

                    if (parent != null && parent.HasImage(ImageType.Primary))
                    {
                        if (parent is MusicAlbum)
                        {
                            return parent;
                        }
                    }

                    return null;
                })
                .Where(i => i != null)
                .DistinctBy(i => i.Id)
                .ToList();

            return GetFinalItems(items, 2);
        }

        protected override string CreateImage(BaseItem item, List<BaseItem> itemsWithImages, string outputPathWithoutExtension, ImageType imageType, int imageIndex)
        {
            return CreateSingleImage(itemsWithImages, outputPathWithoutExtension, ImageType.Primary);
        }
    }
}
