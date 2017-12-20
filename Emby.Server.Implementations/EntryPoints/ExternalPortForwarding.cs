﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Threading;
using Mono.Nat;
using MediaBrowser.Model.Extensions;
using System.Threading;

namespace Emby.Server.Implementations.EntryPoints
{
    public class ExternalPortForwarding : IServerEntryPoint
    {
        private readonly IServerApplicationHost _appHost;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;
        private readonly IServerConfigurationManager _config;
        private readonly IDeviceDiscovery _deviceDiscovery;

        private ITimer _timer;
        private bool _isStarted;
        private readonly ITimerFactory _timerFactory;

        public ExternalPortForwarding(ILogManager logmanager, IServerApplicationHost appHost, IServerConfigurationManager config, IDeviceDiscovery deviceDiscovery, IHttpClient httpClient, ITimerFactory timerFactory)
        {
            _logger = logmanager.GetLogger("PortMapper");
            _appHost = appHost;
            _config = config;
            _deviceDiscovery = deviceDiscovery;
            _httpClient = httpClient;
            _timerFactory = timerFactory;
        }

        private string _lastConfigIdentifier;
        private string GetConfigIdentifier()
        {
            var values = new List<string>();
            var config = _config.Configuration;

            values.Add(config.EnableUPnP.ToString());
            values.Add(config.PublicPort.ToString(CultureInfo.InvariantCulture));
            values.Add(_appHost.HttpPort.ToString(CultureInfo.InvariantCulture));
            values.Add(_appHost.HttpsPort.ToString(CultureInfo.InvariantCulture));
            values.Add((config.EnableHttps || config.RequireHttps).ToString());
            values.Add(_appHost.EnableHttps.ToString());

            return string.Join("|", values.ToArray(values.Count));
        }

        void _config_ConfigurationUpdated(object sender, EventArgs e)
        {
            if (!string.Equals(_lastConfigIdentifier, GetConfigIdentifier(), StringComparison.OrdinalIgnoreCase))
            {
                if (_isStarted)
                {
                    DisposeNat();
                }

                Run();
            }
        }

        public void Run()
        {
            NatUtility.Logger = _logger;
            NatUtility.HttpClient = _httpClient;

            if (_config.Configuration.EnableUPnP)
            {
                Start();
            }

            _config.ConfigurationUpdated -= _config_ConfigurationUpdated;
            _config.ConfigurationUpdated += _config_ConfigurationUpdated;
        }

        private void Start()
        {
            _logger.Debug("Starting NAT discovery");
            NatUtility.EnabledProtocols = new List<NatProtocol>
            {
                NatProtocol.Pmp
            };
            NatUtility.DeviceFound += NatUtility_DeviceFound;

            NatUtility.DeviceLost += NatUtility_DeviceLost;


            NatUtility.StartDiscovery();

            _timer = _timerFactory.Create(ClearCreatedRules, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

            _deviceDiscovery.DeviceDiscovered += _deviceDiscovery_DeviceDiscovered;

            _lastConfigIdentifier = GetConfigIdentifier();

            _isStarted = true;
        }

        private async void _deviceDiscovery_DeviceDiscovered(object sender, GenericEventArgs<UpnpDeviceInfo> e)
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

            // Filter device type
            if (usn.IndexOf("WANIPConnection:", StringComparison.OrdinalIgnoreCase) == -1 &&
                     nt.IndexOf("WANIPConnection:", StringComparison.OrdinalIgnoreCase) == -1 &&
                     usn.IndexOf("WANPPPConnection:", StringComparison.OrdinalIgnoreCase) == -1 &&
                     nt.IndexOf("WANPPPConnection:", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return;
            }

            var identifier = string.IsNullOrWhiteSpace(usn) ? nt : usn;

            if (info.Location == null)
            {
                return;
            }

            lock (_usnsHandled)
            {
                if (_usnsHandled.Contains(identifier))
                {
                    return;
                }
                _usnsHandled.Add(identifier);
            }

            _logger.Debug("Found NAT device: " + identifier);

            IPAddress address;
            if (IPAddress.TryParse(info.Location.Host, out address))
            {
                // The Handle method doesn't need the port
                var endpoint = new IPEndPoint(address, info.Location.Port);

                IPAddress localAddress = null;

                try
                {
                    var localAddressString = await _appHost.GetLocalApiUrl(CancellationToken.None).ConfigureAwait(false);

                    Uri uri;
                    if (Uri.TryCreate(localAddressString, UriKind.Absolute, out uri))
                    {
                        localAddressString = uri.Host;

                        if (!IPAddress.TryParse(localAddressString, out localAddress))
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    return;
                }

                if (_disposed)
                {
                    return;
                }

                _logger.Debug("Calling Nat.Handle on " + identifier);
                NatUtility.Handle(localAddress, info, endpoint, NatProtocol.Upnp);
            }
        }

        private void ClearCreatedRules(object state)
        {
            lock (_createdRules)
            {
                _createdRules.Clear();
            }
            lock (_usnsHandled)
            {
                _usnsHandled.Clear();
            }
        }

        void NatUtility_DeviceFound(object sender, DeviceEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var device = e.Device;
                _logger.Debug("NAT device found: {0}", device.LocalAddress.ToString());

                CreateRules(device);
            }
            catch
            {
                // Commenting out because users are reporting problems out of our control
                //_logger.ErrorException("Error creating port forwarding rules", ex);
            }
        }

        private List<string> _createdRules = new List<string>();
        private List<string> _usnsHandled = new List<string>();
        private async void CreateRules(INatDevice device)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("PortMapper");
            }

            // On some systems the device discovered event seems to fire repeatedly
            // This check will help ensure we're not trying to port map the same device over and over
            var address = device.LocalAddress.ToString();

            lock (_createdRules)
            {
                if (!_createdRules.Contains(address))
                {
                    _createdRules.Add(address);
                }
                else
                {
                    return;
                }
            }

            var success = await CreatePortMap(device, _appHost.HttpPort, _config.Configuration.PublicPort).ConfigureAwait(false);

            if (success)
            {
                await CreatePortMap(device, _appHost.HttpsPort, _config.Configuration.PublicHttpsPort).ConfigureAwait(false);
            }
        }

        private async Task<bool> CreatePortMap(INatDevice device, int privatePort, int publicPort)
        {
            _logger.Debug("Creating port map on port {0}", privatePort);

            try
            {
                await device.CreatePortMap(new Mapping(Protocol.Tcp, privatePort, publicPort)
                {
                    Description = _appHost.Name

                }).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Error creating port map: " + ex.Message);

                return false;
            }
        }

        void NatUtility_DeviceLost(object sender, DeviceEventArgs e)
        {
            var device = e.Device;
            _logger.Debug("NAT device lost: {0}", device.LocalAddress.ToString());
        }

        private bool _disposed = false;
        public void Dispose()
        {
            _disposed = true;
            DisposeNat();
            GC.SuppressFinalize(this);
        }

        private void DisposeNat()
        {
            _logger.Debug("Stopping NAT discovery");

            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }

            _deviceDiscovery.DeviceDiscovered -= _deviceDiscovery_DeviceDiscovered;

            try
            {
                NatUtility.StopDiscovery();
                NatUtility.DeviceFound -= NatUtility_DeviceFound;
                NatUtility.DeviceLost -= NatUtility_DeviceLost;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error stopping NAT Discovery", ex);
            }
            finally
            {
                _isStarted = false;
            }
        }
    }
}