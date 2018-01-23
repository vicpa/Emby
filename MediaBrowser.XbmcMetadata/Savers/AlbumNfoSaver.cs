﻿using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

using MediaBrowser.Controller.IO;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Xml;

namespace MediaBrowser.XbmcMetadata.Savers
{
    public class AlbumNfoSaver : BaseNfoSaver
    {
        protected override string GetLocalSavePath(BaseItem item)
        {
            return Path.Combine(item.Path, "album.nfo");
        }

        protected override string GetRootElementName(BaseItem item)
        {
            return "album";
        }

        public override bool IsEnabledFor(BaseItem item, ItemUpdateType updateType)
        {
            if (!item.SupportsLocalMetadata)
            {
                return false;
            }

            return item is MusicAlbum && updateType >= MinimumUpdateType;
        }

        protected override void WriteCustomElements(BaseItem item, XmlWriter writer)
        {
            var album = (MusicAlbum)item;
            
            foreach (var artist in album.Artists)
            {
                writer.WriteElementString("artist", artist);
            }

            foreach (var artist in album.AlbumArtists)
            {
                writer.WriteElementString("albumartist", artist);
            }

            AddTracks(album.Tracks, writer);
        }        
        
        private readonly CultureInfo UsCulture = new CultureInfo("en-US");

        private void AddTracks(IEnumerable<BaseItem> tracks, XmlWriter writer)
        {
            foreach (var track in tracks.OrderBy(i => i.ParentIndexNumber ?? 0).ThenBy(i => i.IndexNumber ?? 0))
            {
                writer.WriteStartElement("track");

                if (track.IndexNumber.HasValue)
                {
                    writer.WriteElementString("position", track.IndexNumber.Value.ToString(UsCulture));
                }

                if (!string.IsNullOrEmpty(track.Name))
                {
                    writer.WriteElementString("title", track.Name);
                }

                if (track.RunTimeTicks.HasValue)
                {
                    var time = TimeSpan.FromTicks(track.RunTimeTicks.Value).ToString(@"mm\:ss");

                    writer.WriteElementString("duration", time);
                }

                writer.WriteEndElement();
            }
        }

        protected override List<string> GetTagsUsed(BaseItem item)
        {
            var list = base.GetTagsUsed(item);
            list.AddRange(new string[]
            {
                "track",
                "artist",
                "albumartist"
            });
            return list;
        }

        public AlbumNfoSaver(IFileSystem fileSystem, IServerConfigurationManager configurationManager, ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, ILogger logger, IXmlReaderSettingsFactory xmlReaderSettingsFactory) : base(fileSystem, configurationManager, libraryManager, userManager, userDataManager, logger, xmlReaderSettingsFactory)
        {
        }
    }
}
