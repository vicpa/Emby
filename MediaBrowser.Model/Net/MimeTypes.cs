﻿using MediaBrowser.Model.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaBrowser.Model.Net
{
    /// <summary>
    /// Class MimeTypes
    /// </summary>
    public static class MimeTypes
    {
        /// <summary>
        /// Any extension in this list is considered a video file - can be added to at runtime for extensibility
        /// </summary>
        private static readonly string[] VideoFileExtensions = new string[]
            {
                ".mkv",
                ".m2t",
                ".m2ts",
                ".img",
                ".iso",
                ".mk3d",
                ".ts",
                ".rmvb",
                ".mov",
                ".avi",
                ".mpg",
                ".mpeg",
                ".wmv",
                ".mp4",
                ".divx",
                ".dvr-ms",
                ".wtv",
                ".ogm",
                ".ogv",
                ".asf",
                ".m4v",
                ".flv",
                ".f4v",
                ".3gp",
                ".webm",
                ".mts",
                ".m2v",
                ".rec"
        };

        private static Dictionary<string, string> GetVideoFileExtensionsDictionary()
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string ext in VideoFileExtensions)
            {
                dict[ext] = ext;
            }

            return dict;
        }

        private static readonly Dictionary<string, string> VideoFileExtensionsDictionary = GetVideoFileExtensionsDictionary();

        // http://en.wikipedia.org/wiki/Internet_media_type
        // Add more as needed

        private static Dictionary<string, string> GetMimeTypeLookup()
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            dict.Add(".jpg", "image/jpeg");
            dict.Add(".jpeg", "image/jpeg");
            dict.Add(".tbn", "image/jpeg");
            dict.Add(".png", "image/png");
            dict.Add(".gif", "image/gif");
            dict.Add(".tiff", "image/tiff");
            dict.Add(".webp", "image/webp");
            dict.Add(".ico", "image/vnd.microsoft.icon");
            dict.Add(".mpg", "video/mpeg");
            dict.Add(".mpeg", "video/mpeg");
            dict.Add(".ogv", "video/ogg");
            dict.Add(".mov", "video/quicktime");
            dict.Add(".webm", "video/webm");
            dict.Add(".mkv", "video/x-matroska");
            dict.Add(".wmv", "video/x-ms-wmv");
            dict.Add(".flv", "video/x-flv");
            dict.Add(".avi", "video/x-msvideo");
            dict.Add(".asf", "video/x-ms-asf");
            dict.Add(".m4v", "video/x-m4v");
            dict.Add(".cbz", "application/epub+zip");
            dict.Add(".cbr", "application/epub+zip");
            dict.Add(".epub", "application/epub+zip");
            dict.Add(".pdf", "application/pdf");
            dict.Add(".mobi", "application/x-mobipocket-ebook");

            dict.Add(".ass", "text/x-ssa");
            dict.Add(".ssa", "text/x-ssa");

            return dict;
        }

        private static readonly Dictionary<string, string> MimeTypeLookup = GetMimeTypeLookup();

        private static readonly Dictionary<string, string> ExtensionLookup = CreateExtensionLookup();

        private static Dictionary<string, string> CreateExtensionLookup()
        {
            var dict = MimeTypeLookup
                .GroupBy(i => i.Value)
                .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.OrdinalIgnoreCase);

            dict["image/jpg"] = ".jpg";
            dict["image/x-png"] = ".png";

            return dict;
        }

        public static string GetMimeType(string path)
        {
            return GetMimeType(path, true);
        }

        /// <summary>
        /// Gets the type of the MIME.
        /// </summary>
        public static string GetMimeType(string path, bool enableStreamDefault)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }

            var ext = Path.GetExtension(path) ?? string.Empty;

            string result;
            if (MimeTypeLookup.TryGetValue(ext, out result))
            {
                return result;
            }

            // Type video
            if (StringHelper.EqualsIgnoreCase(ext, ".3gp"))
            {
                return "video/3gpp";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".3g2"))
            {
                return "video/3gpp2";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".ts"))
            {
                return "video/mp2t";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".mpd"))
            {
                return "video/vnd.mpeg.dash.mpd";
            }

            // Catch-all for all video types that don't require specific mime types
            if (VideoFileExtensionsDictionary.ContainsKey(ext))
            {
                return "video/" + ext.TrimStart('.').ToLower();
            }

            // Type text
            if (StringHelper.EqualsIgnoreCase(ext, ".css"))
            {
                return "text/css";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".csv"))
            {
                return "text/csv";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".html"))
            {
                return "text/html; charset=UTF-8";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".htm"))
            {
                return "text/html; charset=UTF-8";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".txt"))
            {
                return "text/plain";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".xml"))
            {
                return "application/xml";
            }

            // Type audio
            if (StringHelper.EqualsIgnoreCase(ext, ".mp3"))
            {
                return "audio/mpeg";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".m4a"))
            {
                return "audio/mp4";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".aac"))
            {
                return "audio/mp4";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".webma"))
            {
                return "audio/webm";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".wav"))
            {
                return "audio/wav";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".wma"))
            {
                return "audio/x-ms-wma";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".flac"))
            {
                return "audio/flac";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".aac"))
            {
                return "audio/x-aac";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".ogg"))
            {
                return "audio/ogg";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".oga"))
            {
                return "audio/ogg";
			}
			if (StringHelper.EqualsIgnoreCase(ext, ".opus"))
			{
				return "audio/ogg";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".ac3"))
            {
                return "audio/ac3";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".dsf"))
            {
                return "audio/dsf";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".m4b"))
            {
                return "audio/m4b";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".xsp"))
            {
                return "audio/xsp";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".dsp"))
            {
                return "audio/dsp";
            }

            // Playlists
            if (StringHelper.EqualsIgnoreCase(ext, ".m3u8"))
            {
                return "application/x-mpegURL";
            }

            // Misc
            if (StringHelper.EqualsIgnoreCase(ext, ".dll"))
            {
                return "application/octet-stream";
            }

            // Web
            if (StringHelper.EqualsIgnoreCase(ext, ".js"))
            {
                return "application/x-javascript";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".json"))
            {
                return "application/json";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".map"))
            {
                return "application/x-javascript";
            }

            if (StringHelper.EqualsIgnoreCase(ext, ".woff"))
            {
                return "font/woff";
            }

            if (StringHelper.EqualsIgnoreCase(ext, ".ttf"))
            {
                return "font/ttf";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".eot"))
            {
                return "application/vnd.ms-fontobject";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".svg"))
            {
                return "image/svg+xml";
            }
            if (StringHelper.EqualsIgnoreCase(ext, ".svgz"))
            {
                return "image/svg+xml";
            }

            if (StringHelper.EqualsIgnoreCase(ext, ".srt"))
            {
                return "text/plain";
            }

            if (StringHelper.EqualsIgnoreCase(ext, ".vtt"))
            {
                return "text/vtt";
            }

            if (StringHelper.EqualsIgnoreCase(ext, ".ttml"))
            {
                return "application/ttml+xml";
            }

            if (enableStreamDefault)
            {
                return "application/octet-stream";
            }

            return null;
        }

        public static string ToExtension(string mimeType)
        {
            if (string.IsNullOrEmpty(mimeType))
            {
                throw new ArgumentNullException("mimeType");
            }

            // handle text/html; charset=UTF-8
            mimeType = mimeType.Split(';')[0];

            string result;
            if (ExtensionLookup.TryGetValue(mimeType, out result))
            {
                return result;
            }
            return null;
        }
    }
}
