﻿using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Drawing
{
    /// <summary>
    /// Interface IImageProcessor
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// Gets the supported input formats.
        /// </summary>
        /// <value>The supported input formats.</value>
        string[] SupportedInputFormats { get; }

        /// <summary>
        /// Gets the image enhancers.
        /// </summary>
        /// <value>The image enhancers.</value>
        IImageEnhancer[] ImageEnhancers { get; }

        ImageSize GetImageSize(string path);

        /// <summary>
        /// Gets the size of the image.
        /// </summary>
        /// <param name="info">The information.</param>
        /// <returns>ImageSize.</returns>
        ImageSize GetImageSize(BaseItem item, ItemImageInfo info);

        ImageSize GetImageSize(BaseItem item, ItemImageInfo info, bool allowSlowMethods, bool updateItem);

        /// <summary>
        /// Adds the parts.
        /// </summary>
        /// <param name="enhancers">The enhancers.</param>
        void AddParts(IEnumerable<IImageEnhancer> enhancers);

        /// <summary>
        /// Gets the supported enhancers.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="imageType">Type of the image.</param>
        /// <returns>IEnumerable{IImageEnhancer}.</returns>
        List<IImageEnhancer> GetSupportedEnhancers(BaseItem item, ImageType imageType);

        /// <summary>
        /// Gets the image cache tag.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="image">The image.</param>
        /// <returns>Guid.</returns>
        string GetImageCacheTag(BaseItem item, ItemImageInfo image);

        /// <summary>
        /// Gets the image cache tag.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="image">The image.</param>
        /// <param name="imageEnhancers">The image enhancers.</param>
        /// <returns>Guid.</returns>
        string GetImageCacheTag(BaseItem item, ItemImageInfo image, List<IImageEnhancer> imageEnhancers);

        /// <summary>
        /// Processes the image.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="toStream">To stream.</param>
        /// <returns>Task.</returns>
        Task ProcessImage(ImageProcessingOptions options, Stream toStream);

        /// <summary>
        /// Processes the image.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>Task.</returns>
        Task<Tuple<string, string, DateTime>> ProcessImage(ImageProcessingOptions options);

        /// <summary>
        /// Gets the enhanced image.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="imageType">Type of the image.</param>
        /// <param name="imageIndex">Index of the image.</param>
        /// <returns>Task{System.String}.</returns>
        Task<string> GetEnhancedImage(BaseItem item, ImageType imageType, int imageIndex);

        /// <summary>
        /// Gets the supported image output formats.
        /// </summary>
        /// <returns>ImageOutputFormat[].</returns>
        ImageFormat[] GetSupportedImageOutputFormats();

        /// <summary>
        /// Creates the image collage.
        /// </summary>
        /// <param name="options">The options.</param>
        void CreateImageCollage(ImageCollageOptions options);

        /// <summary>
        /// Gets a value indicating whether [supports image collage creation].
        /// </summary>
        /// <value><c>true</c> if [supports image collage creation]; otherwise, <c>false</c>.</value>
        bool SupportsImageCollageCreation { get; }

        IImageEncoder ImageEncoder { get; set; }

        bool SupportsTransparency(string path);
    }
}
