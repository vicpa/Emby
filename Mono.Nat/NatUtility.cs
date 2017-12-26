//
// Authors:
//   Ben Motmans <ben.motmans@gmail.com>
//   Nicholas Terry <nick.i.terry@gmail.com>
//
// Copyright (C) 2007 Ben Motmans
// Copyright (C) 2014 Nicholas Terry
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Logging;

namespace Mono.Nat
{
    public static class NatUtility
    {
        public static event EventHandler<DeviceEventArgs> DeviceFound;
        public static event EventHandler<DeviceEventArgs> DeviceLost;

        private static List<ISearcher> controllers;
        private static bool verbose;

        public static List<NatProtocol> EnabledProtocols { get; set; }

        public static ILogger Logger { get; set; }
        public static IHttpClient HttpClient { get; set; }

        static NatUtility()
        {
            EnabledProtocols = new List<NatProtocol>
            {
                NatProtocol.Pmp
            };

            controllers = new List<ISearcher>();
            controllers.Add(PmpSearcher.Instance);

            controllers.ForEach(searcher =>
                {
                    searcher.DeviceFound += (sender, args) =>
                    {
                        if (DeviceFound != null)
                            DeviceFound(sender, args);
                    };
                    searcher.DeviceLost += (sender, args) =>
                    {
                        if (DeviceLost != null)
                            DeviceLost(sender, args);
                    };
                });
        }

        internal static void Log(string format, params object[] args)
        {
            var logger = Logger;
            if (logger != null)
                logger.Debug(format, args);
        }

        private static CancellationTokenSource _currentCancellationTokenSource;
        private static object _runSyncLock = new object();
        public static void StartDiscovery()
        {
            lock (_runSyncLock)
            {
                if (_currentCancellationTokenSource == null)
                {
                    return;
                }

                var tokenSource = new CancellationTokenSource();

                _currentCancellationTokenSource = tokenSource;
                //Task.Factory.StartNew(() => SearchAndListen(tokenSource.Token), tokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        public static void StopDiscovery()
        {
            lock (_runSyncLock)
            {
                var tokenSource = _currentCancellationTokenSource;

                if (tokenSource != null)
                {
                    try
                    {
                        tokenSource.Cancel();
                        tokenSource.Dispose();
                    }
                    catch
                    {

                    }

                    _currentCancellationTokenSource = null;
                }
            }
        }

        public static async Task Handle(IPAddress localAddress, UpnpDeviceInfo deviceInfo, IPEndPoint endpoint, NatProtocol protocol)
        {
            switch (protocol)
            {
                case NatProtocol.Upnp:
                    var searcher = new UpnpSearcher(Logger, HttpClient);
                    searcher.DeviceFound += Searcher_DeviceFound;
                    await searcher.Handle(localAddress, deviceInfo, endpoint).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentException("Unexpected protocol: " + protocol);
            }
        }

        private static void Searcher_DeviceFound(object sender, DeviceEventArgs e)
        {
            if (DeviceFound != null)
            {
                DeviceFound(sender, e);
            }
        }
    }
}
