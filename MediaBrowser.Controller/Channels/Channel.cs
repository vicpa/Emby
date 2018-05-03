﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Querying;
using System;
using System.Linq;
using MediaBrowser.Model.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Progress;

namespace MediaBrowser.Controller.Channels
{
    public class Channel : Folder
    {
        public override bool IsVisible(User user)
        {
            if (user.Policy.BlockedChannels != null)
            {
                if (user.Policy.BlockedChannels.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                if (!user.Policy.EnableAllChannels && !user.Policy.EnabledChannels.Contains(Id.ToString("N"), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return base.IsVisible(user);
        }

        [IgnoreDataMember]
        public override bool SupportsInheritedParentImages
        {
            get
            {
                return false;
            }
        }

        [IgnoreDataMember]
        public override SourceType SourceType
        {
            get { return SourceType.Channel; }
        }

        protected override QueryResult<BaseItem> GetItemsInternal(InternalItemsQuery query)
        {
            try
            {
                query.Parent = this;
                query.ChannelIds = new Guid[] { Id };

                // Don't blow up here because it could cause parent screens with other content to fail
                return ChannelManager.GetChannelItemsInternal(query, new SimpleProgress<double>(), CancellationToken.None).Result;
            }
            catch
            {
                // Already logged at lower levels
                return new QueryResult<BaseItem>();
            }
        }

        protected override string GetInternalMetadataPath(string basePath)
        {
            return GetInternalMetadataPath(basePath, Id);
        }

        public static string GetInternalMetadataPath(string basePath, Guid id)
        {
            return System.IO.Path.Combine(basePath, "channels", id.ToString("N"), "metadata");
        }

        public override bool CanDelete()
        {
            return false;
        }

        protected override bool IsAllowTagFilterEnforced()
        {
            return false;
        }

        internal static bool IsChannelVisible(BaseItem channelItem, User user)
        {
            var channel = ChannelManager.GetChannel(channelItem.ChannelId);

            return channel.IsVisible(user);
        }
    }
}
