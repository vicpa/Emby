﻿using System;
using System.Linq;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Entities
{
    public class Book : BaseItem, IHasLookupInfo<BookInfo>, IHasSeries
    {
        [IgnoreDataMember]
        public override string MediaType
        {
            get
            {
                return Model.Entities.MediaType.Book;
            }
        }

        [IgnoreDataMember]
        public string SeriesPresentationUniqueKey { get; set; }
        [IgnoreDataMember]
        public string SeriesName { get; set; }
        [IgnoreDataMember]
        public Guid? SeriesId { get; set; }

        public string FindSeriesSortName()
        {
            return SeriesName;
        }
        public string FindSeriesName()
        {
            return SeriesName;
        }
        public string FindSeriesPresentationUniqueKey()
        {
            return SeriesPresentationUniqueKey;
        }

        public Guid? FindSeriesId()
        {
            return SeriesId;
        }

        public override bool CanDownload()
        {
            return IsFileProtocol;
        }

        public override UnratedItem GetBlockUnratedType()
        {
            return UnratedItem.Book;
        }

        public BookInfo GetLookupInfo()
        {
            var info = GetItemLookupInfo<BookInfo>();

            if (string.IsNullOrEmpty(SeriesName))
            {
                info.SeriesName = GetParents().Select(i => i.Name).FirstOrDefault();
            }
            else
            {
                info.SeriesName = SeriesName;
            }

            return info;
        }
    }
}
