﻿using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Collections
{
    public class CollectionCreationOptions : IHasProviderIds
    {
        public string Name { get; set; }

        public Guid? ParentId { get; set; }

        public bool IsLocked { get; set; }

        public Dictionary<string, string> ProviderIds { get; set; }

        public string[] ItemIdList { get; set; }
        public string[] UserIds { get; set; }

        public CollectionCreationOptions()
        {
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ItemIdList = new string[] {};
            UserIds = new string[] {};
        }
    }
}
