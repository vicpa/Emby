﻿using System;
using Emby.Drawing;
using Emby.Drawing.Skia;
using Emby.Server.Core;
using Emby.Server.Implementations;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace MediaBrowser.Server.Startup.Common
{
    public class ImageEncoderHelper
    {
        public static IImageEncoder GetImageEncoder(ILogger logger,
            ILogManager logManager,
            IFileSystem fileSystem,
            StartupOptions startupOptions,
            Func<IHttpClient> httpClient,
            IApplicationPaths appPaths)
        {
            try
            {
                return new SkiaEncoder(logManager.GetLogger("Skia"), appPaths, httpClient, fileSystem);
            }
            catch
            {
                logger.Error("Skia not available. Will try next image processor.");
            }

            return new NullImageEncoder();
        }
    }
}
