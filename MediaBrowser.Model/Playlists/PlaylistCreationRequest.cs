﻿using System.Collections.Generic;
using System;

namespace MediaBrowser.Model.Playlists
{
    public class PlaylistCreationRequest
    {
         public string Name { get; set; }

        public string[] ItemIdList { get; set; }

        public string MediaType { get; set; }

        public string UserId { get; set; }

        public PlaylistCreationRequest()
        {
            ItemIdList = new string[] {};
        }
   }
}
