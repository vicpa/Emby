using System.Collections.Generic;
using MediaBrowser.Model.Entities;
using System;
using System.Linq;

namespace MediaBrowser.Controller.Providers
{
    public class ImageRefreshOptions
    {
        public MetadataRefreshMode ImageRefreshMode { get; set; }
        public IDirectoryService DirectoryService { get; private set; }

        public bool ReplaceAllImages { get; set; }

        public ImageType[] ReplaceImages { get; set; }
        public bool IsAutomated { get; set; }

        public ImageRefreshOptions(IDirectoryService directoryService)
        {
            ImageRefreshMode = MetadataRefreshMode.Default;
            DirectoryService = directoryService;

            ReplaceImages = new ImageType[] { };
            IsAutomated = true;
        }

        public bool IsReplacingImage(ImageType type)
        {
            return ImageRefreshMode == MetadataRefreshMode.FullRefresh &&
                   (ReplaceAllImages || ReplaceImages.Contains(type));
        }
    }
}