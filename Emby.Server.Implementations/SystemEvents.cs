﻿using System;
using MediaBrowser.Common.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.System;

namespace Emby.Server.Implementations
{
    public class SystemEvents : ISystemEvents
    {
        public event EventHandler Resume;
        public event EventHandler Suspend;
        public event EventHandler SessionLogoff;
        public event EventHandler SystemShutdown;

        private readonly ILogger _logger;

        public SystemEvents(ILogger logger)
        {
            _logger = logger;
        }
    }
}
