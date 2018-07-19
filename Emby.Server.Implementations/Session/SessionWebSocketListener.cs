﻿using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace Emby.Server.Implementations.Session
{
    /// <summary>
    /// Class SessionWebSocketListener
    /// </summary>
    public class SessionWebSocketListener : IWebSocketListener, IDisposable
    {
        /// <summary>
        /// The _session manager
        /// </summary>
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The _dto service
        /// </summary>
        private readonly IJsonSerializer _json;

        private readonly IHttpServer _httpServer;


        /// <summary>
        /// Initializes a new instance of the <see cref="SessionWebSocketListener" /> class.
        /// </summary>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="logManager">The log manager.</param>
        /// <param name="json">The json.</param>
        /// <param name="httpServer">The HTTP server.</param>
        /// <param name="serverManager">The server manager.</param>
        public SessionWebSocketListener(ISessionManager sessionManager, ILogManager logManager, IJsonSerializer json, IHttpServer httpServer)
        {
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger(GetType().Name);
            _json = json;
            _httpServer = httpServer;
            httpServer.WebSocketConnected += _serverManager_WebSocketConnected;
        }

        void _serverManager_WebSocketConnected(object sender, GenericEventArgs<IWebSocketConnection> e)
        {
            var session = GetSession(e.Argument.QueryString, e.Argument.RemoteEndPoint);

            if (session != null)
            {
                EnsureController(session, e.Argument);
            }
            else
            {
                _logger.Warn("Unable to determine session based on url: {0}", e.Argument.Url);
            }
        }

        private SessionInfo GetSession(QueryParamCollection queryString, string remoteEndpoint)
        {
            if (queryString == null)
            {
                throw new ArgumentNullException("queryString");
            }

            var token = queryString["api_key"];
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }
            var deviceId = queryString["deviceId"];
            return _sessionManager.GetSessionByAuthenticationToken(token, deviceId, remoteEndpoint);
        }

        public void Dispose()
        {
            _httpServer.WebSocketConnected -= _serverManager_WebSocketConnected;
        }

        /// <summary>
        /// Processes the message.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns>Task.</returns>
        public Task ProcessMessage(WebSocketMessageInfo message)
        {
            return Task.CompletedTask;
        }

        private void EnsureController(SessionInfo session, IWebSocketConnection connection)
        {
            var controllerInfo = session.EnsureController<WebSocketController>(s => new WebSocketController(s, _logger, _sessionManager));

            var controller = (WebSocketController)controllerInfo.Item1;
            controller.AddWebSocket(connection);
        }
    }
}
