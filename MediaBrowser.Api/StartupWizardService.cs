﻿using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Connect;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Services;
using MediaBrowser.Common.Net;
using System.Threading;

namespace MediaBrowser.Api
{
    [Route("/Startup/Complete", "POST", Summary = "Reports that the startup wizard has been completed")]
    public class ReportStartupWizardComplete : IReturnVoid
    {
    }

    [Route("/Startup/Info", "GET", Summary = "Gets initial server info")]
    public class GetStartupInfo : IReturn<StartupInfo>
    {
    }

    [Route("/Startup/Configuration", "GET", Summary = "Gets initial server configuration")]
    public class GetStartupConfiguration : IReturn<StartupConfiguration>
    {
    }

    [Route("/Startup/Configuration", "POST", Summary = "Updates initial server configuration")]
    public class UpdateStartupConfiguration : StartupConfiguration, IReturnVoid
    {
    }

    [Route("/Startup/RemoteAccess", "POST", Summary = "Updates initial server configuration")]
    public class UpdateRemoteAccessConfiguration : IReturnVoid
    {
        public bool EnableRemoteAccess { get; set; }
        public bool EnableAutomaticPortMapping { get; set; }
    }

    [Route("/Startup/User", "GET", Summary = "Gets initial user info")]
    public class GetStartupUser : IReturn<StartupUser>
    {
    }

    [Route("/Startup/User", "POST", Summary = "Updates initial user info")]
    public class UpdateStartupUser : StartupUser, IReturn<UpdateStartupUserResult>
    {
    }

    [Authenticated(AllowBeforeStartupWizard = true, Roles = "Admin")]
    public class StartupWizardService : BaseApiService
    {
        private readonly IServerConfigurationManager _config;
        private readonly IServerApplicationHost _appHost;
        private readonly IUserManager _userManager;
        private readonly IConnectManager _connectManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IHttpClient _httpClient;

        public StartupWizardService(IServerConfigurationManager config, IHttpClient httpClient, IServerApplicationHost appHost, IUserManager userManager, IConnectManager connectManager, IMediaEncoder mediaEncoder)
        {
            _config = config;
            _appHost = appHost;
            _userManager = userManager;
            _connectManager = connectManager;
            _mediaEncoder = mediaEncoder;
            _httpClient = httpClient;
        }

        public void Post(ReportStartupWizardComplete request)
        {
            _config.Configuration.IsStartupWizardCompleted = true;
            _config.SetOptimalValues();
            _config.SaveConfiguration();

            Task.Run(UpdateStats);
        }

        private async Task UpdateStats()
        {
            try
            {
                var url = string.Format("http://www.mb3admin.com/admin/service/package/installed?mac={0}&product=MBServer&operation=Install&version={1}",
                    _appHost.SystemId,
                    _appHost.ApplicationVersion.ToString());

                using (var response = await _httpClient.SendAsync(new HttpRequestOptions
                {

                    Url = url,
                    CancellationToken = CancellationToken.None,
                    LogErrors = false,
                    LogRequest = false

                }, "GET").ConfigureAwait(false))
                {

                }
            }
            catch
            {

            }
        }

        public object Get(GetStartupInfo request)
        {
            return new StartupInfo
            {
                HasMediaEncoder = !string.IsNullOrWhiteSpace(_mediaEncoder.EncoderPath)
            };
        }

        public object Get(GetStartupConfiguration request)
        {
            var result = new StartupConfiguration
            {
                UICulture = _config.Configuration.UICulture,
                MetadataCountryCode = _config.Configuration.MetadataCountryCode,
                PreferredMetadataLanguage = _config.Configuration.PreferredMetadataLanguage
            };

            return result;
        }

        public void Post(UpdateStartupConfiguration request)
        {
            _config.Configuration.UICulture = request.UICulture;
            _config.Configuration.MetadataCountryCode = request.MetadataCountryCode;
            _config.Configuration.PreferredMetadataLanguage = request.PreferredMetadataLanguage;
            _config.SaveConfiguration();
        }

        public void Post(UpdateRemoteAccessConfiguration request)
        {
            _config.Configuration.EnableRemoteAccess = request.EnableRemoteAccess;
            _config.Configuration.EnableUPnP = request.EnableAutomaticPortMapping;
            _config.SaveConfiguration();
        }

        public object Get(GetStartupUser request)
        {
            var user = _userManager.Users.First();

            return new StartupUser
            {
                Name = user.Name,
                ConnectUserName = user.ConnectUserName
            };
        }

        public async Task<object> Post(UpdateStartupUser request)
        {
            var user = _userManager.Users.First();

            user.Name = request.Name;
            _userManager.UpdateUser(user);

            var result = new UpdateStartupUserResult();

            if (!string.IsNullOrWhiteSpace(user.ConnectUserName) &&
                string.IsNullOrWhiteSpace(request.ConnectUserName))
            {
                await _connectManager.RemoveConnect(user.Id.ToString("N")).ConfigureAwait(false);
            }
            else if (!string.Equals(user.ConnectUserName, request.ConnectUserName, StringComparison.OrdinalIgnoreCase))
            {
                result.UserLinkResult = await _connectManager.LinkUser(user.Id.ToString("N"), request.ConnectUserName).ConfigureAwait(false);
            }

            return result;
        }
    }

    public class StartupConfiguration
    {
        public string UICulture { get; set; }
        public string MetadataCountryCode { get; set; }
        public string PreferredMetadataLanguage { get; set; }
    }

    public class StartupInfo
    {
        public bool HasMediaEncoder { get; set; }
    }

    public class StartupUser
    {
        public string Name { get; set; }
        public string ConnectUserName { get; set; }
    }

    public class UpdateStartupUserResult
    {
        public UserLinkResult UserLinkResult { get; set; }
    }
}
