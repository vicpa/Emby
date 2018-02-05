﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Threading;
using System.Threading;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Devices;

namespace Emby.Dlna.PlayTo
{
    class PlayToManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ISessionManager _sessionManager;

        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IDlnaManager _dlnaManager;
        private readonly IServerApplicationHost _appHost;
        private readonly IImageProcessor _imageProcessor;
        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _config;
        private readonly IUserDataManager _userDataManager;
        private readonly ILocalizationManager _localization;

        private readonly IDeviceDiscovery _deviceDiscovery;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly ITimerFactory _timerFactory;

        private bool _disposed;
        private SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _disposeCancellationTokenSource = new CancellationTokenSource();

        public PlayToManager(ILogger logger, ISessionManager sessionManager, ILibraryManager libraryManager, IUserManager userManager, IDlnaManager dlnaManager, IServerApplicationHost appHost, IImageProcessor imageProcessor, IDeviceDiscovery deviceDiscovery, IHttpClient httpClient, IServerConfigurationManager config, IUserDataManager userDataManager, ILocalizationManager localization, IMediaSourceManager mediaSourceManager, IMediaEncoder mediaEncoder, ITimerFactory timerFactory)
        {
            _logger = logger;
            _sessionManager = sessionManager;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _dlnaManager = dlnaManager;
            _appHost = appHost;
            _imageProcessor = imageProcessor;
            _deviceDiscovery = deviceDiscovery;
            _httpClient = httpClient;
            _config = config;
            _userDataManager = userDataManager;
            _localization = localization;
            _mediaSourceManager = mediaSourceManager;
            _mediaEncoder = mediaEncoder;
            _timerFactory = timerFactory;
        }

        public void Start()
        {
            _deviceDiscovery.DeviceDiscovered += _deviceDiscovery_DeviceDiscovered;
        }

        async void _deviceDiscovery_DeviceDiscovered(object sender, GenericEventArgs<UpnpDeviceInfo> e)
        {
            if (_disposed)
            {
                return;
            }

            var info = e.Argument;

            string usn;
            if (!info.Headers.TryGetValue("USN", out usn)) usn = string.Empty;

            string nt;
            if (!info.Headers.TryGetValue("NT", out nt)) nt = string.Empty;

            string location = info.Location.ToString();

            // It has to report that it's a media renderer
            if (usn.IndexOf("MediaRenderer:", StringComparison.OrdinalIgnoreCase) == -1 &&
                     nt.IndexOf("MediaRenderer:", StringComparison.OrdinalIgnoreCase) == -1)
            {
                //_logger.Debug("Upnp device {0} does not contain a MediaRenderer device (0).", location);
                return;
            }

            if (_sessionManager.Sessions.Any(i => usn.IndexOf(i.DeviceId, StringComparison.OrdinalIgnoreCase) != -1))
            {
                return;
            }

            var cancellationToken = _disposeCancellationTokenSource.Token;

            await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_disposed)
                {
                    return;
                }

                await AddDevice(info, location, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error creating PlayTo device.", ex);
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        private string GetUuid(string usn)
        {
            var found = false;
            var index = usn.IndexOf("uuid:", StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                usn = usn.Substring(index);
                found = true;
            }
            index = usn.IndexOf("::", StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                usn = usn.Substring(0, index);
            }

            if (found)
            {
                return usn;
            }

            return usn.GetMD5().ToString("N");
        }

        private async Task AddDevice(UpnpDeviceInfo info, string location, CancellationToken cancellationToken)
        {
            var uri = info.Location;
            _logger.Debug("Attempting to create PlayToController from location {0}", location);

            _logger.Debug("Logging session activity from location {0}", location);
            string uuid;
            if (info.Headers.TryGetValue("USN", out uuid))
            {
                uuid = GetUuid(uuid);
            }
            else
            {
                uuid = location.GetMD5().ToString("N");
            }

            string deviceName = null;

            var sessionInfo = _sessionManager.LogSessionActivity("DLNA", _appHost.ApplicationVersion.ToString(), uuid, deviceName, uri.OriginalString, null);

            var controller = sessionInfo.SessionController as PlayToController;

            if (controller == null)
            {
                var device = await Device.CreateuPnpDeviceAsync(uri, _httpClient, _config, _logger, _timerFactory, cancellationToken).ConfigureAwait(false);

                deviceName = device.Properties.Name;

                _sessionManager.UpdateDeviceName(sessionInfo.Id, deviceName);

                string serverAddress;
                if (info.LocalIpAddress == null || info.LocalIpAddress.Equals(IpAddressInfo.Any) || info.LocalIpAddress.Equals(IpAddressInfo.IPv6Any))
                {
                    serverAddress = await _appHost.GetLocalApiUrl(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    serverAddress = _appHost.GetLocalApiUrl(info.LocalIpAddress);
                }

                string accessToken = null;

                sessionInfo.SessionController = controller = new PlayToController(sessionInfo,
                    _sessionManager,
                    _libraryManager,
                    _logger,
                    _dlnaManager,
                    _userManager,
                    _imageProcessor,
                    serverAddress,
                    accessToken,
                    _deviceDiscovery,
                    _userDataManager,
                    _localization,
                    _mediaSourceManager,
                    _config,
                    _mediaEncoder);

                controller.Init(device);

                var profile = _dlnaManager.GetProfile(device.Properties.ToDeviceIdentification()) ??
                              _dlnaManager.GetDefaultProfile();

                _sessionManager.ReportCapabilities(sessionInfo.Id, new ClientCapabilities
                {
                    PlayableMediaTypes = profile.GetSupportedMediaTypes(),

                    SupportedCommands = new string[]
                    {
                            GeneralCommandType.VolumeDown.ToString(),
                            GeneralCommandType.VolumeUp.ToString(),
                            GeneralCommandType.Mute.ToString(),
                            GeneralCommandType.Unmute.ToString(),
                            GeneralCommandType.ToggleMute.ToString(),
                            GeneralCommandType.SetVolume.ToString(),
                            GeneralCommandType.SetAudioStreamIndex.ToString(),
                            GeneralCommandType.SetSubtitleStreamIndex.ToString()
                    },

                    SupportsMediaControl = true,

                    // xbox one creates a new uuid everytime it restarts
                    SupportsPersistentIdentifier = (device.Properties.ModelName ?? string.Empty).IndexOf("xbox", StringComparison.OrdinalIgnoreCase) == -1
                });

                _logger.Info("DLNA Session created for {0} - {1}", device.Properties.Name, device.Properties.ModelName);
            }
        }

        public void Dispose()
        {
            _deviceDiscovery.DeviceDiscovered -= _deviceDiscovery_DeviceDiscovered;

            try
            {
                _disposeCancellationTokenSource.Cancel();
            }
            catch
            {

            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
