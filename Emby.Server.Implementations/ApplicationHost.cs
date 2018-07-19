﻿using Emby.Common.Implementations.Serialization;
using Emby.Drawing;
using Emby.Photos;
using Emby.Dlna;
using Emby.Dlna.Main;
using Emby.Dlna.Ssdp;
using Emby.Server.Implementations.Activity;
using Emby.Server.Implementations.Archiving;
using Emby.Server.Implementations.Channels;
using Emby.Server.Implementations.Collections;
using Emby.Server.Implementations.Configuration;
using Emby.Server.Implementations.Cryptography;
using Emby.Server.Implementations.Data;
using Emby.Server.Implementations.Devices;
using Emby.Server.Implementations.Diagnostics;
using Emby.Server.Implementations.Dto;
using Emby.Server.Implementations.FFMpeg;
using Emby.Server.Implementations.HttpServer;
using Emby.Server.Implementations.HttpServer.Security;
using Emby.Server.Implementations.IO;
using Emby.Server.Implementations.Library;
using Emby.Server.Implementations.LiveTv;
using Emby.Server.Implementations.Localization;
using Emby.Server.Implementations.MediaEncoder;
using Emby.Server.Implementations.Net;
using Emby.Notifications;
using Emby.Server.Implementations.Playlists;
using Emby.Server.Implementations.Reflection;
using Emby.Server.Implementations.ScheduledTasks;
using Emby.Server.Implementations.Security;
using Emby.Server.Implementations.Serialization;
using Emby.Server.Implementations.Session;
using Emby.Server.Implementations.Threading;
using Emby.Server.Implementations.TV;
using Emby.Server.Implementations.Updates;
using Emby.Server.Implementations.Xml;
using Emby.Server.MediaEncoding.Subtitles;
using MediaBrowser.Api;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Events;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Common.Security;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Connect;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Dlna;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Controller.Security;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Controller.TV;
using MediaBrowser.LocalMetadata.Savers;
using MediaBrowser.MediaEncoding.BdInfo;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.Diagnostics;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.News;
using MediaBrowser.Model.Reflection;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Text;
using MediaBrowser.Model.Threading;
using MediaBrowser.Model.Updates;
using MediaBrowser.Model.Xml;
using MediaBrowser.Providers.Chapters;
using MediaBrowser.Providers.Manager;
using MediaBrowser.Providers.Subtitles;
using MediaBrowser.WebDashboard.Api;
using MediaBrowser.XbmcMetadata.Providers;
using ServiceStack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StringExtensions = MediaBrowser.Controller.Extensions.StringExtensions;
using X509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;
using MediaBrowser.Controller.Authentication;
using System.Diagnostics;
using ServiceStack.Text.Jsv;

namespace Emby.Server.Implementations
{
    /// <summary>
    /// Class CompositionRoot
    /// </summary>
    public abstract class ApplicationHost : IServerApplicationHost, IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether this instance can self restart.
        /// </summary>
        /// <value><c>true</c> if this instance can self restart; otherwise, <c>false</c>.</value>
        public abstract bool CanSelfRestart { get; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance can self update.
        /// </summary>
        /// <value><c>true</c> if this instance can self update; otherwise, <c>false</c>.</value>
        public virtual bool CanSelfUpdate
        {
            get
            {
                return false;
            }
        }

        public virtual bool CanLaunchWebBrowser
        {
            get
            {
                if (!Environment.UserInteractive)
                {
                    return false;
                }

                if (StartupOptions.ContainsOption("-service"))
                {
                    return false;
                }

                if (EnvironmentInfo.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.Windows)
                {
                    return true;
                }

                if (EnvironmentInfo.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.OSX)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Occurs when [has pending restart changed].
        /// </summary>
        public event EventHandler HasPendingRestartChanged;

        /// <summary>
        /// Occurs when [application updated].
        /// </summary>
        public event EventHandler<GenericEventArgs<PackageVersionInfo>> ApplicationUpdated;

        /// <summary>
        /// Gets or sets a value indicating whether this instance has changes that require the entire application to restart.
        /// </summary>
        /// <value><c>true</c> if this instance has pending application restart; otherwise, <c>false</c>.</value>
        public bool HasPendingRestart { get; private set; }

        public bool IsShuttingDown { get; private set; }

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        /// <value>The logger.</value>
        protected ILogger Logger { get; set; }

        /// <summary>
        /// Gets or sets the plugins.
        /// </summary>
        /// <value>The plugins.</value>
        public IPlugin[] Plugins { get; protected set; }

        /// <summary>
        /// Gets or sets the log manager.
        /// </summary>
        /// <value>The log manager.</value>
        public ILogManager LogManager { get; protected set; }

        /// <summary>
        /// Gets the application paths.
        /// </summary>
        /// <value>The application paths.</value>
        protected ServerApplicationPaths ApplicationPaths { get; set; }

        /// <summary>
        /// Gets all concrete types.
        /// </summary>
        /// <value>All concrete types.</value>
        public Tuple<Type, string>[] AllConcreteTypes { get; protected set; }

        /// <summary>
        /// The disposable parts
        /// </summary>
        protected readonly List<IDisposable> DisposableParts = new List<IDisposable>();

        /// <summary>
        /// Gets the configuration manager.
        /// </summary>
        /// <value>The configuration manager.</value>
        protected IConfigurationManager ConfigurationManager { get; set; }

        public IFileSystem FileSystemManager { get; set; }

        protected IEnvironmentInfo EnvironmentInfo { get; set; }

        private IBlurayExaminer BlurayExaminer { get; set; }

        public PackageVersionClass SystemUpdateLevel
        {
            get
            {

#if BETA
                return PackageVersionClass.Beta;
#endif
                return PackageVersionClass.Release;
            }
        }

        public virtual string OperatingSystemDisplayName
        {
            get { return EnvironmentInfo.OperatingSystemName; }
        }

        /// <summary>
        /// The container
        /// </summary>
        protected readonly SimpleInjector.Container Container = new SimpleInjector.Container();

        protected ISystemEvents SystemEvents { get; set; }

        /// <summary>
        /// Gets the server configuration manager.
        /// </summary>
        /// <value>The server configuration manager.</value>
        public IServerConfigurationManager ServerConfigurationManager
        {
            get { return (IServerConfigurationManager)ConfigurationManager; }
        }

        /// <summary>
        /// Gets the configuration manager.
        /// </summary>
        /// <returns>IConfigurationManager.</returns>
        protected IConfigurationManager GetConfigurationManager()
        {
            return new ServerConfigurationManager(ApplicationPaths, LogManager, XmlSerializer, FileSystemManager);
        }

        protected virtual IResourceFileManager CreateResourceFileManager()
        {
            return new ResourceFileManager(HttpResultFactory, LogManager.GetLogger("ResourceManager"), FileSystemManager);
        }

        /// <summary>
        /// Gets or sets the user manager.
        /// </summary>
        /// <value>The user manager.</value>
        public IUserManager UserManager { get; set; }
        /// <summary>
        /// Gets or sets the library manager.
        /// </summary>
        /// <value>The library manager.</value>
        internal ILibraryManager LibraryManager { get; set; }
        /// <summary>
        /// Gets or sets the directory watchers.
        /// </summary>
        /// <value>The directory watchers.</value>
        private ILibraryMonitor LibraryMonitor { get; set; }
        /// <summary>
        /// Gets or sets the provider manager.
        /// </summary>
        /// <value>The provider manager.</value>
        private IProviderManager ProviderManager { get; set; }
        /// <summary>
        /// Gets or sets the HTTP server.
        /// </summary>
        /// <value>The HTTP server.</value>
        private IHttpServer HttpServer { get; set; }
        private IDtoService DtoService { get; set; }
        public IImageProcessor ImageProcessor { get; set; }

        /// <summary>
        /// Gets or sets the media encoder.
        /// </summary>
        /// <value>The media encoder.</value>
        private IMediaEncoder MediaEncoder { get; set; }
        private ISubtitleEncoder SubtitleEncoder { get; set; }

        private IConnectManager ConnectManager { get; set; }
        private ISessionManager SessionManager { get; set; }

        private ILiveTvManager LiveTvManager { get; set; }

        public ILocalizationManager LocalizationManager { get; set; }

        private IEncodingManager EncodingManager { get; set; }
        private IChannelManager ChannelManager { get; set; }
        protected ITextEncoding TextEncoding { get; private set; }

        /// <summary>
        /// Gets or sets the user data repository.
        /// </summary>
        /// <value>The user data repository.</value>
        private IUserDataManager UserDataManager { get; set; }
        private IUserRepository UserRepository { get; set; }
        internal IDisplayPreferencesRepository DisplayPreferencesRepository { get; set; }
        internal IItemRepository ItemRepository { get; set; }

        private INotificationManager NotificationManager { get; set; }
        private ISubtitleManager SubtitleManager { get; set; }
        private IChapterManager ChapterManager { get; set; }
        private IDeviceManager DeviceManager { get; set; }

        internal IUserViewManager UserViewManager { get; set; }

        private IAuthenticationRepository AuthenticationRepository { get; set; }
        private ITVSeriesManager TVSeriesManager { get; set; }
        private ICollectionManager CollectionManager { get; set; }
        private IMediaSourceManager MediaSourceManager { get; set; }
        private IPlaylistManager PlaylistManager { get; set; }

        /// <summary>
        /// Gets or sets the installation manager.
        /// </summary>
        /// <value>The installation manager.</value>
        protected IInstallationManager InstallationManager { get; private set; }
        /// <summary>
        /// Gets the security manager.
        /// </summary>
        /// <value>The security manager.</value>
        protected ISecurityManager SecurityManager { get; private set; }

        /// <summary>
        /// Gets or sets the zip client.
        /// </summary>
        /// <value>The zip client.</value>
        protected IZipClient ZipClient { get; private set; }
        protected IHttpResultFactory HttpResultFactory { get; private set; }
        protected IAuthService AuthService { get; private set; }

        public StartupOptions StartupOptions { get; private set; }
        protected readonly string ReleaseAssetFilename;

        internal IPowerManagement PowerManagement { get; private set; }
        internal IImageEncoder ImageEncoder { get; private set; }

        protected IProcessFactory ProcessFactory { get; private set; }
        protected ITimerFactory TimerFactory { get; private set; }
        protected ICryptoProvider CryptographyProvider = new CryptographyProvider();
        protected readonly IXmlSerializer XmlSerializer;

        protected ISocketFactory SocketFactory { get; private set; }
        protected ITaskManager TaskManager { get; private set; }
        public IHttpClient HttpClient { get; private set; }
        protected INetworkManager NetworkManager { get; set; }
        public IJsonSerializer JsonSerializer { get; private set; }
        protected IIsoManager IsoManager { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationHost" /> class.
        /// </summary>
        public ApplicationHost(ServerApplicationPaths applicationPaths,
            ILogManager logManager,
            StartupOptions options,
            IFileSystem fileSystem,
            IPowerManagement powerManagement,
            string releaseAssetFilename,
            IEnvironmentInfo environmentInfo,
            IImageEncoder imageEncoder,
            ISystemEvents systemEvents,
            INetworkManager networkManager)
        {

            // hack alert, until common can target .net core
            BaseExtensions.CryptographyProvider = CryptographyProvider;

            XmlSerializer = new MyXmlSerializer(fileSystem, logManager.GetLogger("XmlSerializer"));

            NetworkManager = networkManager;
            networkManager.LocalSubnetsFn = GetConfiguredLocalSubnets;
            EnvironmentInfo = environmentInfo;
            SystemEvents = systemEvents;

            ApplicationPaths = applicationPaths;
            LogManager = logManager;
            FileSystemManager = fileSystem;

            ConfigurationManager = GetConfigurationManager();

            // Initialize this early in case the -v command line option is used
            Logger = LogManager.GetLogger("App");

            StartupOptions = options;
            ReleaseAssetFilename = releaseAssetFilename;
            PowerManagement = powerManagement;

            ImageEncoder = imageEncoder;

            SetBaseExceptionMessage();

            fileSystem.AddShortcutHandler(new MbLinkShortcutHandler(fileSystem));

            NetworkManager.NetworkChanged += NetworkManager_NetworkChanged;
        }

        public string ExpandVirtualPath(string path)
        {
            var appPaths = ApplicationPaths;

            return path.Replace(appPaths.VirtualDataPath, appPaths.DataPath, StringComparison.OrdinalIgnoreCase)
                .Replace(appPaths.VirtualInternalMetadataPath, appPaths.InternalMetadataPath, StringComparison.OrdinalIgnoreCase);
        }

        public string ReverseVirtualPath(string path)
        {
            var appPaths = ApplicationPaths;

            return path.Replace(appPaths.DataPath, appPaths.VirtualDataPath, StringComparison.OrdinalIgnoreCase)
                .Replace(appPaths.InternalMetadataPath, appPaths.VirtualInternalMetadataPath, StringComparison.OrdinalIgnoreCase);
        }

        private string[] GetConfiguredLocalSubnets()
        {
            return ServerConfigurationManager.Configuration.LocalNetworkSubnets;
        }

        private void NetworkManager_NetworkChanged(object sender, EventArgs e)
        {
            _validAddressResults.Clear();
        }

        private Version _version;
        /// <summary>
        /// Gets the current application version
        /// </summary>
        /// <value>The application version.</value>
        public Version ApplicationVersion
        {
            get
            {
                return _version ?? (_version = GetType().GetTypeInfo().Assembly.GetName().Version);
            }
        }

        private DeviceId _deviceId;
        public string SystemId
        {
            get
            {
                if (_deviceId == null)
                {
                    _deviceId = new DeviceId(ApplicationPaths, LogManager.GetLogger("SystemId"), FileSystemManager);
                }

                return _deviceId.Value;
            }
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get
            {
                return "Emby Server";
            }
        }

        private Tuple<Assembly, string> GetAssembly(Type type)
        {
            var assembly = type.GetTypeInfo().Assembly;
            string path = null;

            return new Tuple<Assembly, string>(assembly, path);
        }

        public virtual IStreamHelper CreateStreamHelper()
        {
            return new StreamHelper();
        }

        public virtual bool SupportsAutoRunAtStartup
        {
            get
            {
                return EnvironmentInfo.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.Windows;
            }
        }

        /// <summary>
        /// Creates an instance of type and resolves all constructor dependancies
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>System.Object.</returns>
        public object CreateInstance(Type type)
        {
            return Container.GetInstance(type);
        }

        /// <summary>
        /// Creates the instance safe.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>System.Object.</returns>
        protected object CreateInstanceSafe(Tuple<Type, string> typeInfo)
        {
            var type = typeInfo.Item1;

            try
            {
                return Container.GetInstance(type);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error creating {0}", ex, type.FullName);
                // Don't blow up in release mode
                return null;
            }
        }

        /// <summary>
        /// Registers the specified obj.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj">The obj.</param>
        /// <param name="manageLifetime">if set to <c>true</c> [manage lifetime].</param>
        protected void RegisterSingleInstance<T>(T obj, bool manageLifetime = true)
            where T : class
        {
            Container.RegisterSingleton(obj);

            if (manageLifetime)
            {
                var disposable = obj as IDisposable;

                if (disposable != null)
                {
                    DisposableParts.Add(disposable);
                }
            }
        }

        /// <summary>
        /// Registers the single instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="func">The func.</param>
        protected void RegisterSingleInstance<T>(Func<T> func)
            where T : class
        {
            Container.RegisterSingleton(func);
        }

        /// <summary>
        /// Resolves this instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>``0.</returns>
        public T Resolve<T>()
        {
            return (T)Container.GetRegistration(typeof(T), true).GetInstance();
        }

        /// <summary>
        /// Resolves this instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>``0.</returns>
        public T TryResolve<T>()
        {
            var result = Container.GetRegistration(typeof(T), false);

            if (result == null)
            {
                return default(T);
            }
            return (T)result.GetInstance();
        }

        /// <summary>
        /// Loads the assembly.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <returns>Assembly.</returns>
        protected Tuple<Assembly, string> LoadAssembly(string file)
        {
            try
            {
                var assembly = Assembly.Load(File.ReadAllBytes(file));

                return new Tuple<Assembly, string>(assembly, file);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error loading assembly {0}", ex, file);
                return null;
            }
        }

        /// <summary>
        /// Gets the export types.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>IEnumerable{Type}.</returns>
        public IEnumerable<Tuple<Type, string>> GetExportTypes<T>()
        {
            var currentType = typeof(T);

            return AllConcreteTypes.Where(i => currentType.IsAssignableFrom(i.Item1));
        }

        /// <summary>
        /// Gets the exports.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="manageLiftime">if set to <c>true</c> [manage liftime].</param>
        /// <returns>IEnumerable{``0}.</returns>
        public IEnumerable<T> GetExports<T>(bool manageLiftime = true)
        {
            var parts = GetExportTypes<T>()
                .Select(CreateInstanceSafe)
                .Where(i => i != null)
                .Cast<T>()
                .ToList();

            if (manageLiftime)
            {
                lock (DisposableParts)
                {
                    DisposableParts.AddRange(parts.OfType<IDisposable>());
                }
            }

            return parts;
        }

        public List<Tuple<T, string>> GetExportsWithInfo<T>(bool manageLiftime = true)
        {
            var parts = GetExportTypes<T>()
                .Select(i =>
                {
                    var obj = CreateInstanceSafe(i);

                    if (obj == null)
                    {
                        return null;
                    }
                    return new Tuple<T, string>((T)obj, i.Item2);
                })
                .Where(i => i != null)
                .ToList();

            if (manageLiftime)
            {
                lock (DisposableParts)
                {
                    DisposableParts.AddRange(parts.Select(i => i.Item1).OfType<IDisposable>());
                }
            }

            return parts;
        }

        private void SetBaseExceptionMessage()
        {
            var builder = GetBaseExceptionMessage(ApplicationPaths);

            builder.Insert(0, string.Format("Version: {0}{1}", ApplicationVersion, Environment.NewLine));
            builder.Insert(0, "*** Error Report ***" + Environment.NewLine);

            LogManager.ExceptionMessagePrefix = builder.ToString();
        }

        /// <summary>
        /// Runs the startup tasks.
        /// </summary>
        public async Task RunStartupTasks()
        {
            Resolve<ITaskManager>().AddTasks(GetExports<IScheduledTask>(false));

            ConfigureAutorun();

            ConfigurationManager.ConfigurationUpdated += OnConfigurationUpdated;

            MediaEncoder.Init();

            //if (string.IsNullOrWhiteSpace(MediaEncoder.EncoderPath))
            //{
            //    if (ServerConfigurationManager.Configuration.IsStartupWizardCompleted)
            //    {
            //        ServerConfigurationManager.Configuration.IsStartupWizardCompleted = false;
            //        ServerConfigurationManager.SaveConfiguration();
            //    }
            //}

            Logger.Info("ServerId: {0}", SystemId);

            var entryPoints = GetExports<IServerEntryPoint>().ToList();
            RunEntryPoints(entryPoints, true);

            Logger.Info("Core startup complete");
            HttpServer.GlobalResponse = null;

            Logger.Info("Post-init migrations complete");

            RunEntryPoints(entryPoints, false);
            Logger.Info("All entry points have started");

            LogManager.RemoveConsoleOutput();
        }

        private void RunEntryPoints(IEnumerable<IServerEntryPoint> entryPoints, bool isBeforeStartup)
        {
            foreach (var entryPoint in entryPoints)
            {
                if (isBeforeStartup != (entryPoint is IRunBeforeStartup))
                {
                    continue;
                }

                var name = entryPoint.GetType().FullName;
                Logger.Info("Starting entry point {0}", name);
                var now = DateTime.UtcNow;
                try
                {
                    entryPoint.Run();
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error in {0}", ex, name);
                }
                Logger.Info("Entry point completed: {0}. Duration: {1} seconds", name, (DateTime.UtcNow - now).TotalSeconds.ToString(CultureInfo.InvariantCulture), "ImageInfos");
            }
        }

        /// <summary>
        /// Configures the autorun.
        /// </summary>
        private void ConfigureAutorun()
        {
            try
            {
                ConfigureAutoRunAtStartup(ConfigurationManager.CommonConfiguration.RunAtStartup);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error configuring autorun", ex);
            }
        }

        private IJsonSerializer CreateJsonSerializer()
        {
            return new JsonSerializer(FileSystemManager, LogManager.GetLogger("JsonSerializer"));
        }

        public void Init()
        {
            HttpPort = ServerConfigurationManager.Configuration.HttpServerPortNumber;
            HttpsPort = ServerConfigurationManager.Configuration.HttpsPortNumber;

            // Safeguard against invalid configuration
            if (HttpPort == HttpsPort)
            {
                HttpPort = ServerConfiguration.DefaultHttpPort;
                HttpsPort = ServerConfiguration.DefaultHttpsPort;
            }

            JsonSerializer = CreateJsonSerializer();

            OnLoggerLoaded(true);
            LogManager.LoggerLoaded += (s, e) => OnLoggerLoaded(false);

            LogManager.LogSeverity = ConfigurationManager.CommonConfiguration.EnableDebugLevelLogging
                ? LogSeverity.Debug
                : LogSeverity.Info;

            DiscoverTypes();

            SetHttpLimit();

            RegisterResources();

            FindParts();
        }

        protected virtual void OnLoggerLoaded(bool isFirstLoad)
        {
            Logger.Info("Application version: {0}", ApplicationVersion);

            if (!isFirstLoad)
            {
                LogEnvironmentInfo(Logger, ApplicationPaths, false);
            }

            if (Plugins != null)
            {
                var pluginBuilder = new StringBuilder();

                foreach (var plugin in Plugins)
                {
                    pluginBuilder.AppendLine(string.Format("{0} {1}", plugin.Name, plugin.Version));
                }

                Logger.LogMultiline("Plugins:", LogSeverity.Info, pluginBuilder);
            }
        }

        protected abstract IConnectManager CreateConnectManager();

        protected virtual IHttpClient CreateHttpClient()
        {
            return new HttpClientManager.HttpClientManager(ApplicationPaths, LogManager.GetLogger("HttpClient"), FileSystemManager, GetDefaultUserAgent);
        }

        public static IStreamHelper StreamHelper { get; set; }

        /// <summary>
        /// Registers resources that classes will depend on
        /// </summary>
        protected void RegisterResources()
        {
            RegisterSingleInstance(ConfigurationManager);
            RegisterSingleInstance<IApplicationHost>(this);

            RegisterSingleInstance<IApplicationPaths>(ApplicationPaths);

            RegisterSingleInstance(JsonSerializer);
            RegisterSingleInstance(SystemEvents);

            RegisterSingleInstance(LogManager, false);
            RegisterSingleInstance(Logger);

            RegisterSingleInstance(EnvironmentInfo);

            RegisterSingleInstance(FileSystemManager);

            HttpClient = CreateHttpClient();
            RegisterSingleInstance(HttpClient);

            RegisterSingleInstance(NetworkManager);

            IsoManager = new IsoManager();
            RegisterSingleInstance(IsoManager);

            TaskManager = new TaskManager(ApplicationPaths, JsonSerializer, LogManager.GetLogger("TaskManager"), FileSystemManager, SystemEvents);
            RegisterSingleInstance(TaskManager);

            RegisterSingleInstance(XmlSerializer);

            ProcessFactory = new ProcessFactory();
            RegisterSingleInstance(ProcessFactory);

            TimerFactory = new TimerFactory();
            RegisterSingleInstance(TimerFactory);

            var streamHelper = CreateStreamHelper();
            ApplicationHost.StreamHelper = streamHelper;
            RegisterSingleInstance(streamHelper);

            RegisterSingleInstance(CryptographyProvider);

            SocketFactory = new SocketFactory(LogManager.GetLogger("SocketFactory"));
            RegisterSingleInstance(SocketFactory);

            RegisterSingleInstance(PowerManagement);

            SecurityManager = new PluginSecurityManager(this, HttpClient, JsonSerializer, ApplicationPaths, LogManager, FileSystemManager, CryptographyProvider);
            RegisterSingleInstance(SecurityManager);

            InstallationManager = new InstallationManager(LogManager.GetLogger("InstallationManager"), this, ApplicationPaths, HttpClient, JsonSerializer, SecurityManager, ServerConfigurationManager, FileSystemManager, CryptographyProvider, PackageRuntime);
            RegisterSingleInstance(InstallationManager);

            ZipClient = new ZipClient(FileSystemManager);
            RegisterSingleInstance(ZipClient);

            HttpResultFactory = new HttpResultFactory(LogManager, FileSystemManager, JsonSerializer, CreateBrotliCompressor());
            RegisterSingleInstance(HttpResultFactory);

            RegisterSingleInstance<IServerApplicationHost>(this);
            RegisterSingleInstance<IServerApplicationPaths>(ApplicationPaths);

            RegisterSingleInstance(ServerConfigurationManager);

            IAssemblyInfo assemblyInfo = new AssemblyInfo();
            RegisterSingleInstance<IAssemblyInfo>(assemblyInfo);

            LocalizationManager = new LocalizationManager(ServerConfigurationManager, FileSystemManager, JsonSerializer, LogManager.GetLogger("LocalizationManager"), assemblyInfo, new TextLocalizer());
            StringExtensions.LocalizationManager = LocalizationManager;
            RegisterSingleInstance(LocalizationManager);

            TextEncoding = new TextEncoding.TextEncoding(FileSystemManager, LogManager.GetLogger("TextEncoding"), JsonSerializer);
            RegisterSingleInstance(TextEncoding);
            BlurayExaminer = new BdInfoExaminer(FileSystemManager, TextEncoding);
            RegisterSingleInstance(BlurayExaminer);

            RegisterSingleInstance<IXmlReaderSettingsFactory>(new XmlReaderSettingsFactory());

            UserDataManager = new UserDataManager(LogManager, ServerConfigurationManager, ()=> UserManager);
            RegisterSingleInstance(UserDataManager);

            UserRepository = GetUserRepository();
            // This is only needed for disposal purposes. If removing this, make sure to have the manager handle disposing it
            RegisterSingleInstance(UserRepository);

            var displayPreferencesRepo = new SqliteDisplayPreferencesRepository(LogManager.GetLogger("SqliteDisplayPreferencesRepository"), JsonSerializer, ApplicationPaths, FileSystemManager);
            DisplayPreferencesRepository = displayPreferencesRepo;
            RegisterSingleInstance(DisplayPreferencesRepository);

            var itemRepo = new SqliteItemRepository(ServerConfigurationManager, this, JsonSerializer, LogManager.GetLogger("SqliteItemRepository"), assemblyInfo, FileSystemManager, EnvironmentInfo, TimerFactory);
            ItemRepository = itemRepo;
            RegisterSingleInstance(ItemRepository);

            AuthenticationRepository = GetAuthenticationRepository();
            RegisterSingleInstance(AuthenticationRepository);

            UserManager = new UserManager(LogManager.GetLogger("UserManager"), ServerConfigurationManager, UserRepository, XmlSerializer, NetworkManager, () => ImageProcessor, () => DtoService, () => ConnectManager, this, JsonSerializer, FileSystemManager, CryptographyProvider);
            RegisterSingleInstance(UserManager);

            LibraryManager = new LibraryManager(this, Logger, TaskManager, UserManager, ServerConfigurationManager, UserDataManager, () => LibraryMonitor, FileSystemManager, () => ProviderManager, () => UserViewManager);
            RegisterSingleInstance(LibraryManager);

            var musicManager = new MusicManager(LibraryManager);
            RegisterSingleInstance<IMusicManager>(new MusicManager(LibraryManager));

            LibraryMonitor = new LibraryMonitor(LogManager, TaskManager, LibraryManager, ServerConfigurationManager, FileSystemManager, TimerFactory, SystemEvents, EnvironmentInfo);
            RegisterSingleInstance(LibraryMonitor);

            RegisterSingleInstance<ISearchEngine>(() => new SearchEngine(LogManager, LibraryManager, UserManager));

            CertificateInfo = GetCertificateInfo(true);
            Certificate = GetCertificate(CertificateInfo);

            HttpServer = new HttpListenerHost(this,
                LogManager.GetLogger("HttpServer"),
                ServerConfigurationManager,
                "web/index.html",
                NetworkManager,
                TextEncoding,
                JsonSerializer,
                XmlSerializer,
                GetParseFn);

            HttpServer.GlobalResponse = LocalizationManager.GetLocalizedString("StartupEmbyServerIsLoading");
            RegisterSingleInstance(HttpServer);

            ImageProcessor = GetImageProcessor();
            RegisterSingleInstance(ImageProcessor);

            TVSeriesManager = new TVSeriesManager(UserManager, UserDataManager, LibraryManager, ServerConfigurationManager);
            RegisterSingleInstance(TVSeriesManager);

            var encryptionManager = new EncryptionManager();
            RegisterSingleInstance<IEncryptionManager>(encryptionManager);

            ConnectManager = CreateConnectManager();
            RegisterSingleInstance(ConnectManager);

            DeviceManager = new DeviceManager(AuthenticationRepository, JsonSerializer, LibraryManager, LocalizationManager, UserManager, FileSystemManager, LibraryMonitor, ServerConfigurationManager, LogManager.GetLogger("DeviceManager"), NetworkManager);
            RegisterSingleInstance(DeviceManager);

            var newsService = new Emby.Server.Implementations.News.NewsService(ApplicationPaths, JsonSerializer);
            RegisterSingleInstance<INewsService>(newsService);

            MediaSourceManager = new MediaSourceManager(ItemRepository, ApplicationPaths, LocalizationManager, UserManager, LibraryManager, LogManager.GetLogger("MediaSourceManager"), JsonSerializer, FileSystemManager, UserDataManager, TimerFactory, () => MediaEncoder);
            RegisterSingleInstance(MediaSourceManager);

            SubtitleManager = new SubtitleManager(LogManager.GetLogger("SubtitleManager"), FileSystemManager, LibraryMonitor, MediaSourceManager, ServerConfigurationManager, LocalizationManager);
            RegisterSingleInstance(SubtitleManager);

            ProviderManager = new ProviderManager(HttpClient, SubtitleManager, ServerConfigurationManager, LibraryMonitor, LogManager, FileSystemManager, ApplicationPaths, () => LibraryManager, JsonSerializer);
            RegisterSingleInstance(ProviderManager);

            DtoService = new DtoService(LogManager.GetLogger("DtoService"), LibraryManager, UserDataManager, ItemRepository, ImageProcessor, ServerConfigurationManager, FileSystemManager, ProviderManager, () => ChannelManager, this, () => DeviceManager, () => MediaSourceManager, () => LiveTvManager);
            RegisterSingleInstance(DtoService);

            ChannelManager = new ChannelManager(UserManager, DtoService, LibraryManager, LogManager.GetLogger("ChannelManager"), ServerConfigurationManager, FileSystemManager, UserDataManager, JsonSerializer, LocalizationManager, HttpClient, ProviderManager);
            RegisterSingleInstance(ChannelManager);

            SessionManager = new SessionManager(UserDataManager, LogManager.GetLogger("SessionManager"), LibraryManager, UserManager, musicManager, DtoService, ImageProcessor, JsonSerializer, this, HttpClient, AuthenticationRepository, DeviceManager, MediaSourceManager, TimerFactory);
            RegisterSingleInstance(SessionManager);

            var dlnaManager = new DlnaManager(XmlSerializer, FileSystemManager, ApplicationPaths, LogManager.GetLogger("Dlna"), JsonSerializer, this, assemblyInfo);
            RegisterSingleInstance<IDlnaManager>(dlnaManager);

            CollectionManager = new CollectionManager(LibraryManager, ApplicationPaths, LocalizationManager, FileSystemManager, LibraryMonitor, LogManager.GetLogger("CollectionManager"), ProviderManager);
            RegisterSingleInstance(CollectionManager);

            PlaylistManager = new PlaylistManager(LibraryManager, FileSystemManager, LibraryMonitor, LogManager.GetLogger("PlaylistManager"), UserManager, ProviderManager);
            RegisterSingleInstance<IPlaylistManager>(PlaylistManager);

            LiveTvManager = new LiveTvManager(this, HttpClient, ServerConfigurationManager, Logger, ItemRepository, ImageProcessor, UserDataManager, DtoService, UserManager, LibraryManager, TaskManager, LocalizationManager, JsonSerializer, ProviderManager, FileSystemManager, SecurityManager, () => ChannelManager);
            RegisterSingleInstance(LiveTvManager);

            UserViewManager = new UserViewManager(LibraryManager, LocalizationManager, UserManager, ChannelManager, LiveTvManager, ServerConfigurationManager);
            RegisterSingleInstance(UserViewManager);

            NotificationManager = new NotificationManager(LogManager, UserManager, ServerConfigurationManager);
            RegisterSingleInstance(NotificationManager);

            RegisterSingleInstance<IDeviceDiscovery>(new DeviceDiscovery(LogManager.GetLogger("IDeviceDiscovery"), ServerConfigurationManager, SocketFactory, TimerFactory));

            ChapterManager = new ChapterManager(LibraryManager, LogManager.GetLogger("ChapterManager"), ServerConfigurationManager, ItemRepository);
            RegisterSingleInstance(ChapterManager);

            RegisterMediaEncoder(assemblyInfo);

            EncodingManager = new EncodingManager(FileSystemManager, Logger, MediaEncoder, ChapterManager, LibraryManager);
            RegisterSingleInstance(EncodingManager);

            var activityLogRepo = GetActivityLogRepository();
            RegisterSingleInstance(activityLogRepo);
            RegisterSingleInstance<IActivityManager>(new ActivityManager(LogManager.GetLogger("ActivityManager"), activityLogRepo, UserManager));

            var authContext = new AuthorizationContext(AuthenticationRepository, ConnectManager, UserManager);
            RegisterSingleInstance<IAuthorizationContext>(authContext);
            RegisterSingleInstance<ISessionContext>(new SessionContext(UserManager, authContext, SessionManager));

            AuthService = new AuthService(UserManager, authContext, ServerConfigurationManager, ConnectManager, SessionManager, NetworkManager);
            RegisterSingleInstance<IAuthService>(AuthService);

            SubtitleEncoder = new SubtitleEncoder(LibraryManager, LogManager.GetLogger("SubtitleEncoder"), ApplicationPaths, FileSystemManager, MediaEncoder, JsonSerializer, HttpClient, MediaSourceManager, ProcessFactory, TextEncoding);
            RegisterSingleInstance(SubtitleEncoder);

            RegisterSingleInstance(CreateResourceFileManager());

            displayPreferencesRepo.Initialize();

            var userDataRepo = new SqliteUserDataRepository(LogManager.GetLogger("SqliteUserDataRepository"), ApplicationPaths, FileSystemManager);

            SetStaticProperties();

            ((UserManager)UserManager).Initialize();

            ((UserDataManager)UserDataManager).Repository = userDataRepo;
            itemRepo.Initialize(userDataRepo, UserManager);
            ((LibraryManager)LibraryManager).ItemRepository = ItemRepository;
        }

        protected virtual IBrotliCompressor CreateBrotliCompressor()
        {
            return null;
        }

        private static Func<string, object> GetParseFn(Type propertyType)
        {
            return s => JsvReader.GetParseFn(propertyType)(s);
        }

        public virtual string PackageRuntime
        {
            get
            {
                return "netframework";
            }
        }

        public static void LogEnvironmentInfo(ILogger logger, IApplicationPaths appPaths, bool isStartup)
        {
            logger.LogMultiline("Emby", LogSeverity.Info, GetBaseExceptionMessage(appPaths));
        }

        protected static StringBuilder GetBaseExceptionMessage(IApplicationPaths appPaths)
        {
            var builder = new StringBuilder();

            // Distinct these to prevent users from reporting problems that aren't actually problems
            var commandLineArgs = Environment
                .GetCommandLineArgs()
                .Distinct()
                .ToArray();

            builder.AppendLine(string.Format("Command line: {0}", string.Join(" ", commandLineArgs)));

            builder.AppendLine(string.Format("Operating system: {0}", Environment.OSVersion));
            builder.AppendLine(string.Format("64-Bit OS: {0}", Environment.Is64BitOperatingSystem));
            builder.AppendLine(string.Format("64-Bit Process: {0}", Environment.Is64BitProcess));
            builder.AppendLine(string.Format("User Interactive: {0}", Environment.UserInteractive));

            Type type = Type.GetType("Mono.Runtime");
            if (type != null)
            {
                MethodInfo displayName = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                if (displayName != null)
                {
                    builder.AppendLine("Mono: " + displayName.Invoke(null, null));
                }
            }

            builder.AppendLine(string.Format("Processor count: {0}", Environment.ProcessorCount));
            builder.AppendLine(string.Format("Program data path: {0}", appPaths.ProgramDataPath));
            builder.AppendLine(string.Format("Application directory: {0}", appPaths.ProgramSystemPath));

            return builder;
        }

        private void SetHttpLimit()
        {
            try
            {
                // Increase the max http request limit
                ServicePointManager.DefaultConnectionLimit = Math.Max(96, ServicePointManager.DefaultConnectionLimit);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error setting http limit", ex);
            }
        }

        protected string GetDefaultUserAgent()
        {
            var name = FormatAttribute(Name);

            return name + "/" + ApplicationVersion;
        }

        private string FormatAttribute(string str)
        {
            var arr = str.ToCharArray();

            arr = Array.FindAll<char>(arr, (c => (char.IsLetterOrDigit(c)
                                                  || char.IsWhiteSpace(c))));

            var result = new string(arr);

            if (string.IsNullOrWhiteSpace(result))
            {
                result = "Emby";
            }

            return result;
        }

        protected virtual bool SupportsDualModeSockets
        {
            get
            {
                return true;
            }
        }

        private X509Certificate GetCertificate(CertificateInfo info)
        {
            var certificateLocation = info == null ? null : info.Path;

            if (string.IsNullOrWhiteSpace(certificateLocation))
            {
                return null;
            }

            try
            {
                if (!FileSystemManager.FileExists(certificateLocation))
                {
                    return null;
                }

                // Don't use an empty string password
                var password = string.IsNullOrWhiteSpace(info.Password) ? null : info.Password;

                X509Certificate2 localCert = new X509Certificate2(certificateLocation, password);
                //localCert.PrivateKey = PrivateKey.CreateFromFile(pvk_file).RSA;
                if (!localCert.HasPrivateKey)
                {
                    Logger.Error("No private key included in SSL cert {0}.", certificateLocation);
                    return null;
                }

                return localCert;
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error loading cert from {0}", ex, certificateLocation);
                return null;
            }
        }

        private IImageProcessor GetImageProcessor()
        {
            return new ImageProcessor(LogManager.GetLogger("ImageProcessor"), ServerConfigurationManager.ApplicationPaths, FileSystemManager, JsonSerializer, ImageEncoder, () => LibraryManager, TimerFactory, () => MediaEncoder);
        }

        protected virtual FFMpegInstallInfo GetFfmpegInstallInfo()
        {
            var info = new FFMpegInstallInfo();

            // Windows builds: http://ffmpeg.zeranoe.com/builds/
            // Linux builds: http://johnvansickle.com/ffmpeg/
            // OS X builds: http://ffmpegmac.net/
            // OS X x64: http://www.evermeet.cx/ffmpeg/

            if (EnvironmentInfo.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.Linux)
            {
                info.FFMpegFilename = "ffmpeg";
                info.FFProbeFilename = "ffprobe";
                info.ArchiveType = "7z";
                info.Version = "20170308";
            }
            else if (EnvironmentInfo.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.Windows)
            {
                info.FFMpegFilename = "ffmpeg.exe";
                info.FFProbeFilename = "ffprobe.exe";
                info.Version = "20170308";
                info.ArchiveType = "7z";
            }
            else if (EnvironmentInfo.OperatingSystem == MediaBrowser.Model.System.OperatingSystem.OSX)
            {
                info.FFMpegFilename = "ffmpeg";
                info.FFProbeFilename = "ffprobe";
                info.ArchiveType = "7z";
                info.Version = "20170308";
            }

            return info;
        }

        protected virtual FFMpegInfo GetFFMpegInfo()
        {
            return new FFMpegLoader(Logger, ApplicationPaths, HttpClient, ZipClient, FileSystemManager, GetFfmpegInstallInfo())
                .GetFFMpegInfo(StartupOptions);
        }

        /// <summary>
        /// Registers the media encoder.
        /// </summary>
        /// <returns>Task.</returns>
        private void RegisterMediaEncoder(IAssemblyInfo assemblyInfo)
        {
            string encoderPath = null;
            string probePath = null;

            var info = GetFFMpegInfo();

            encoderPath = info.EncoderPath;
            probePath = info.ProbePath;
            var hasExternalEncoder = string.Equals(info.Version, "external", StringComparison.OrdinalIgnoreCase);

            var mediaEncoder = new MediaEncoding.Encoder.MediaEncoder(LogManager.GetLogger("MediaEncoder"),
                JsonSerializer,
                encoderPath,
                probePath,
                hasExternalEncoder,
                ServerConfigurationManager,
                FileSystemManager,
                LiveTvManager,
                IsoManager,
                LibraryManager,
                ChannelManager,
                SessionManager,
                () => SubtitleEncoder,
                () => MediaSourceManager,
                HttpClient,
                ZipClient,
                ProcessFactory,
                EnvironmentInfo,
                BlurayExaminer,
                assemblyInfo,
                this);

            MediaEncoder = mediaEncoder;
            RegisterSingleInstance(MediaEncoder);
        }

        /// <summary>
        /// Gets the user repository.
        /// </summary>
        /// <returns>Task{IUserRepository}.</returns>
        private IUserRepository GetUserRepository()
        {
            var repo = new SqliteUserRepository(LogManager.GetLogger("SqliteUserRepository"), ApplicationPaths, JsonSerializer);

            repo.Initialize();

            return repo;
        }

        private IAuthenticationRepository GetAuthenticationRepository()
        {
            var repo = new AuthenticationRepository(LogManager.GetLogger("AuthenticationRepository"), ServerConfigurationManager);

            repo.Initialize();

            return repo;
        }

        private IActivityRepository GetActivityLogRepository()
        {
            var repo = new ActivityRepository(LogManager.GetLogger("ActivityRepository"), ServerConfigurationManager.ApplicationPaths, FileSystemManager);

            repo.Initialize();

            return repo;
        }

        /// <summary>
        /// Dirty hacks
        /// </summary>
        private void SetStaticProperties()
        {
            ((SqliteItemRepository)ItemRepository).ImageProcessor = ImageProcessor;

            // For now there's no real way to inject these properly
            BaseItem.Logger = LogManager.GetLogger("BaseItem");
            BaseItem.ConfigurationManager = ServerConfigurationManager;
            BaseItem.LibraryManager = LibraryManager;
            BaseItem.ProviderManager = ProviderManager;
            BaseItem.LocalizationManager = LocalizationManager;
            BaseItem.ItemRepository = ItemRepository;
            User.UserManager = UserManager;
            BaseItem.FileSystem = FileSystemManager;
            BaseItem.UserDataManager = UserDataManager;
            BaseItem.ChannelManager = ChannelManager;
            Video.LiveTvManager = LiveTvManager;
            Folder.UserViewManager = UserViewManager;
            UserView.TVSeriesManager = TVSeriesManager;
            UserView.PlaylistManager = PlaylistManager;
            UserView.CollectionManager = CollectionManager;
            BaseItem.MediaSourceManager = MediaSourceManager;
            CollectionFolder.XmlSerializer = XmlSerializer;
            CollectionFolder.JsonSerializer = JsonSerializer;
            CollectionFolder.ApplicationHost = this;
            AuthenticatedAttribute.AuthService = AuthService;
        }

        /// <summary>
        /// Finds the parts.
        /// </summary>
        protected void FindParts()
        {
            if (!ServerConfigurationManager.Configuration.IsPortAuthorized)
            {
                RegisterServerWithAdministratorAccess();
                ServerConfigurationManager.Configuration.IsPortAuthorized = true;
                ConfigurationManager.SaveConfiguration();
            }

            ConfigurationManager.AddParts(GetExports<IConfigurationFactory>());
            Plugins = GetExportsWithInfo<IPlugin>().Select(LoadPlugin).Where(i => i != null).ToArray();

            HttpServer.Init(GetExports<IService>(false), GetExports<IWebSocketListener>());

            StartServer();

            LibraryManager.AddParts(GetExports<IResolverIgnoreRule>(),
                GetExports<IItemResolver>(),
                GetExports<IIntroProvider>(),
                GetExports<IBaseItemComparer>(),
                GetExports<ILibraryPostScanTask>());

            ProviderManager.AddParts(GetExports<IImageProvider>(),
                GetExports<IMetadataService>(),
                GetExports<IMetadataProvider>(),
                GetExports<IMetadataSaver>(),
                GetExports<IExternalId>());

            ImageProcessor.AddParts(GetExports<IImageEnhancer>());

            LiveTvManager.AddParts(GetExports<ILiveTvService>(), GetExports<ITunerHost>(), GetExports<IListingsProvider>());

            SubtitleManager.AddParts(GetExports<ISubtitleProvider>());

            ChannelManager.AddParts(GetExports<IChannel>());

            MediaSourceManager.AddParts(GetExports<IMediaSourceProvider>());

            NotificationManager.AddParts(GetExports<INotificationService>(), GetExports<INotificationTypeFactory>());
            UserManager.AddParts(GetExports<IAuthenticationProvider>());

            IsoManager.AddParts(GetExports<IIsoMounter>());
        }

        private IPlugin LoadPlugin(Tuple<IPlugin, string> info)
        {
            var plugin = info.Item1;
            var assemblyFilePath = info.Item2;

            try
            {
                var assemblyPlugin = plugin as IPluginAssembly;

                if (assemblyPlugin != null)
                {
                    var assembly = plugin.GetType().Assembly;
                    var assemblyName = assembly.GetName();

                    var dataFolderPath = Path.Combine(ApplicationPaths.PluginsPath, Path.GetFileNameWithoutExtension(assemblyFilePath));

                    assemblyPlugin.SetAttributes(assemblyFilePath, dataFolderPath, assemblyName.Version);

                    try
                    {
                        var idAttributes = assembly.GetCustomAttributes(typeof(GuidAttribute), true);
                        if (idAttributes.Length > 0)
                        {
                            var attribute = (GuidAttribute)idAttributes[0];
                            var assemblyId = new Guid(attribute.Value);

                            assemblyPlugin.SetId(assemblyId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorException("Error getting plugin Id from {0}.", ex, plugin.GetType().FullName);
                    }
                }

                var hasPluginConfiguration = plugin as IHasPluginConfiguration;
                if (hasPluginConfiguration != null)
                {
                    hasPluginConfiguration.SetStartupInfo(s => Directory.CreateDirectory(s));
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error loading plugin {0}", ex, plugin.GetType().FullName);
                return null;
            }

            return plugin;
        }

        /// <summary>
        /// Discovers the types.
        /// </summary>
        protected void DiscoverTypes()
        {
            Logger.Info("Loading assemblies");

            var assemblyInfos = GetComposablePartAssemblies();

            foreach (var assemblyInfo in assemblyInfos)
            {
                var assembly = assemblyInfo.Item1;
                var path = assemblyInfo.Item2;

                if (path == null)
                {
                    Logger.Info("Loading {0}", assembly.FullName);
                }
                else
                {
                    Logger.Info("Loading {0} from {1}", assembly.FullName, path);
                }
            }

            AllConcreteTypes = assemblyInfos
                .SelectMany(GetTypes)
                .Where(info =>
                {
                    var t = info.Item1;
                    return t.IsClass && !t.IsAbstract && !t.IsInterface && !t.IsGenericType;
                })
                .ToArray();
        }

        /// <summary>
        /// Gets a list of types within an assembly
        /// This will handle situations that would normally throw an exception - such as a type within the assembly that depends on some other non-existant reference
        /// </summary>
        protected List<Tuple<Type, string>> GetTypes(Tuple<Assembly, string> assemblyInfo)
        {
            if (assemblyInfo == null)
            {
                return new List<Tuple<Type, string>>();
            }

            var assembly = assemblyInfo.Item1;

            try
            {
                // This null checking really shouldn't be needed but adding it due to some
                // unhandled exceptions in mono 5.0 that are a little hard to hunt down
                var types = assembly.GetTypes() ?? new Type[] { };
                return types.Where(t => t != null).Select(i => new Tuple<Type, string>(i, assemblyInfo.Item2)).ToList();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (ex.LoaderExceptions != null)
                {
                    foreach (var loaderException in ex.LoaderExceptions)
                    {
                        if (loaderException != null)
                        {
                            Logger.Error("LoaderException: " + loaderException.Message);
                        }
                    }
                }

                // If it fails we can still get a list of the Types it was able to resolve
                var types = ex.Types ?? new Type[] { };
                return types.Where(t => t != null).Select(i => new Tuple<Type, string>(i, assemblyInfo.Item2)).ToList();
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error loading types from assembly", ex);

                return new List<Tuple<Type, string>>();
            }
        }

        private CertificateInfo CertificateInfo { get; set; }
        protected X509Certificate Certificate { get; private set; }

        private IEnumerable<string> GetUrlPrefixes()
        {
            var hosts = new[] { "+" };

            return hosts.SelectMany(i =>
            {
                var prefixes = new List<string>
                {
                    "http://"+i+":" + HttpPort + "/"
                };

                if (CertificateInfo != null)
                {
                    prefixes.Add("https://" + i + ":" + HttpsPort + "/");
                }

                return prefixes;
            });
        }

        protected abstract IHttpListener CreateHttpListener();

        /// <summary>
        /// Starts the server.
        /// </summary>
        private void StartServer()
        {
            try
            {
                ((HttpListenerHost)HttpServer).StartServer(GetUrlPrefixes().ToArray(), CreateHttpListener());
                return;
            }
            catch (Exception ex)
            {
                var msg = string.Equals(ex.GetType().Name, "SocketException", StringComparison.OrdinalIgnoreCase)
                  ? "The http server is unable to start due to a Socket error. This can occasionally happen when the operating system takes longer than usual to release the IP bindings from the previous session. This can take up to five minutes. Please try waiting or rebooting the system."
                  : "Error starting Http Server";

                Logger.ErrorException(msg, ex);

                if (HttpPort == ServerConfiguration.DefaultHttpPort)
                {
                    throw;
                }
            }

            HttpPort = ServerConfiguration.DefaultHttpPort;

            try
            {
                ((HttpListenerHost)HttpServer).StartServer(GetUrlPrefixes().ToArray(), CreateHttpListener());
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error starting http server", ex);

                throw;
            }
        }

        private CertificateInfo GetCertificateInfo(bool generateCertificate)
        {
            if (!string.IsNullOrWhiteSpace(ServerConfigurationManager.Configuration.CertificatePath))
            {
                // Custom cert
                return new CertificateInfo
                {
                    Path = ServerConfigurationManager.Configuration.CertificatePath,
                    Password = ServerConfigurationManager.Configuration.CertificatePassword
                };
            }

            // Generate self-signed cert
            var certHost = GetHostnameFromExternalDns(ServerConfigurationManager.Configuration.WanDdns);
            var certPath = Path.Combine(ServerConfigurationManager.ApplicationPaths.ProgramDataPath, "ssl", "cert_" + (certHost + "2").GetMD5().ToString("N") + ".pfx");
            var password = "embycert";

            //if (generateCertificate)
            //{
            //    if (!FileSystemManager.FileExists(certPath))
            //    {
            //        FileSystemManager.CreateDirectory(FileSystemManager.GetDirectoryName(certPath));

            //        try
            //        {
            //            CertificateGenerator.CreateSelfSignCertificatePfx(certPath, certHost, password, Logger);
            //        }
            //        catch (Exception ex)
            //        {
            //            Logger.ErrorException("Error creating ssl cert", ex);
            //            return null;
            //        }
            //    }
            //}

            return new CertificateInfo
            {
                Path = certPath,
                Password = password
            };
        }

        /// <summary>
        /// Called when [configuration updated].
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void OnConfigurationUpdated(object sender, EventArgs e)
        {
            ConfigureAutorun();

            var requiresRestart = false;

            // Don't do anything if these haven't been set yet
            if (HttpPort != 0 && HttpsPort != 0)
            {
                // Need to restart if ports have changed
                if (ServerConfigurationManager.Configuration.HttpServerPortNumber != HttpPort ||
                    ServerConfigurationManager.Configuration.HttpsPortNumber != HttpsPort)
                {
                    if (ServerConfigurationManager.Configuration.IsPortAuthorized)
                    {
                        ServerConfigurationManager.Configuration.IsPortAuthorized = false;
                        ServerConfigurationManager.SaveConfiguration();

                        requiresRestart = true;
                    }
                }
            }

            if (!HttpServer.UrlPrefixes.SequenceEqual(GetUrlPrefixes(), StringComparer.OrdinalIgnoreCase))
            {
                requiresRestart = true;
            }

            var currentCertPath = CertificateInfo == null ? null : CertificateInfo.Path;
            var newCertInfo = GetCertificateInfo(false);
            var newCertPath = newCertInfo == null ? null : newCertInfo.Path;

            if (!string.Equals(currentCertPath, newCertPath, StringComparison.OrdinalIgnoreCase))
            {
                requiresRestart = true;
            }

            if (requiresRestart)
            {
                Logger.Info("App needs to be restarted due to configuration change.");

                NotifyPendingRestart();
            }
        }

        /// <summary>
        /// Notifies that the kernel that a change has been made that requires a restart
        /// </summary>
        public void NotifyPendingRestart()
        {
            Logger.Info("App needs to be restarted.");

            var changed = !HasPendingRestart;

            HasPendingRestart = true;

            if (changed)
            {
                EventHelper.QueueEventIfNotNull(HasPendingRestartChanged, this, EventArgs.Empty, Logger);
            }
        }

        /// <summary>
        /// Restarts this instance.
        /// </summary>
        public void Restart()
        {
            if (!CanSelfRestart)
            {
                throw new PlatformNotSupportedException("The server is unable to self-restart. Please restart manually.");
            }

            if (IsShuttingDown)
            {
                return;
            }

            IsShuttingDown = true;

            Task.Run(async () =>
            {
                try
                {
                    await SessionManager.SendServerRestartNotification(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException("Error sending server restart notification", ex);
                }

                Logger.Info("Calling RestartInternal");

                RestartInternal();
            });
        }

        protected abstract void RestartInternal();

        /// <summary>
        /// Gets the composable part assemblies.
        /// </summary>
        /// <returns>IEnumerable{Assembly}.</returns>
        protected List<Tuple<Assembly, string>> GetComposablePartAssemblies()
        {
            var list = GetPluginAssemblies();

            // Gets all plugin assemblies by first reading all bytes of the .dll and calling Assembly.Load against that
            // This will prevent the .dll file from getting locked, and allow us to replace it when needed

            // Include composable parts in the Api assembly 
            list.Add(GetAssembly(typeof(ApiEntryPoint)));

            // Include composable parts in the Dashboard assembly 
            list.Add(GetAssembly(typeof(DashboardService)));

            // Include composable parts in the Model assembly 
            list.Add(GetAssembly(typeof(SystemInfo)));

            // Include composable parts in the Common assembly 
            list.Add(GetAssembly(typeof(IApplicationHost)));

            // Include composable parts in the Controller assembly 
            list.Add(GetAssembly(typeof(IServerApplicationHost)));

            // Include composable parts in the Providers assembly 
            list.Add(GetAssembly(typeof(ProviderUtils)));

            // Include composable parts in the Photos assembly 
            list.Add(GetAssembly(typeof(PhotoProvider)));

            // Emby.Server implementations
            list.Add(GetAssembly(typeof(InstallationManager)));

            // MediaEncoding
            list.Add(GetAssembly(typeof(MediaEncoding.Encoder.MediaEncoder)));

            // Dlna 
            list.Add(GetAssembly(typeof(DlnaEntryPoint)));

            // Local metadata 
            list.Add(GetAssembly(typeof(BoxSetXmlSaver)));

            // Notifications
            list.Add(GetAssembly(typeof(NotificationManager)));

            // Xbmc 
            list.Add(GetAssembly(typeof(ArtistNfoProvider)));

            list.AddRange(GetAssembliesWithPartsInternal().Select(i => new Tuple<Assembly, string>(i, null)));

            return list.ToList();
        }

        protected abstract List<Assembly> GetAssembliesWithPartsInternal();

        /// <summary>
        /// Gets the plugin assemblies.
        /// </summary>
        /// <returns>IEnumerable{Assembly}.</returns>
        private List<Tuple<Assembly, string>> GetPluginAssemblies()
        {
            // Copy pre-installed plugins
            var sourcePath = Path.Combine(ApplicationPaths.ApplicationResourcesPath, "plugins");
            CopyPlugins(sourcePath, ApplicationPaths.PluginsPath);

            return GetPluginAssemblies(ApplicationPaths.PluginsPath);
        }

        private void CopyPlugins(string source, string target)
        {
            List<string> files;

            try
            {
                files = Directory.EnumerateFiles(source, "*.dll", SearchOption.TopDirectoryOnly)
                   .ToList();

            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            if (files.Count == 0)
            {
                return;
            }

            foreach (var sourceFile in files)
            {
                var filename = Path.GetFileName(sourceFile);
                var targetFile = Path.Combine(target, filename);

                var targetFileExists = File.Exists(targetFile);

                if (!targetFileExists && ServerConfigurationManager.Configuration.UninstalledPlugins.Contains(filename, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (targetFileExists && GetDllVersion(targetFile) >= GetDllVersion(sourceFile))
                {
                    continue;
                }

                Directory.CreateDirectory(target);
                File.Copy(sourceFile, targetFile, true);
            }
        }

        private Version GetDllVersion(string path)
        {
            try
            {
                var result = Version.Parse(FileVersionInfo.GetVersionInfo(path).FileVersion);

                Logger.Info("File {0} has version {1}", path, result);

                return result;
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error getting version number from {0}", ex, path);

                return new Version(1, 0);
            }
        }

        private List<Tuple<Assembly, string>> GetPluginAssemblies(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly)
                    .Where(EnablePlugin)
                    .Select(LoadAssembly)
                    .Where(a => a != null)
                    .ToList();
            }
            catch (DirectoryNotFoundException)
            {
                return new List<Tuple<Assembly, string>>();
            }
        }

        private bool EnablePlugin(string path)
        {
            var filename = Path.GetFileName(path);

            var exclude = new[]
            {
                "mbplus.dll",
                "mbintros.dll",
                "embytv.dll",
                "Messenger.dll",
                "Messages.dll",
                "MediaBrowser.Plugins.TvMazeProvider.dll",
                "MBBookshelf.dll",
                "MediaBrowser.Channels.Adult.YouJizz.dll",
                "MediaBrowser.Channels.Vine-co.dll",
                "MediaBrowser.Plugins.Vimeo.dll",
                "MediaBrowser.Channels.Vevo.dll",
                "MediaBrowser.Plugins.Twitch.dll",
                "MediaBrowser.Channels.SvtPlay.dll",
                "MediaBrowser.Plugins.SoundCloud.dll",
                "MediaBrowser.Plugins.SnesBox.dll",
                "MediaBrowser.Plugins.RottenTomatoes.dll",
                "MediaBrowser.Plugins.Revision3.dll",
                "MediaBrowser.Plugins.NesBox.dll",
                "MBChapters.dll",
                "MediaBrowser.Channels.LeagueOfLegends.dll",
                "MediaBrowser.Plugins.ADEProvider.dll",
                "MediaBrowser.Channels.BallStreams.dll",
                "MediaBrowser.Channels.Adult.Beeg.dll",
                "ChannelDownloader.dll",
                "Hamstercat.Emby.EmbyBands.dll",
                "EmbyTV.dll",
                "MediaBrowser.Channels.HitboxTV.dll",
                "MediaBrowser.Channels.HockeyStreams.dll",
                "MediaBrowser.Plugins.ITV.dll",
                "MediaBrowser.Plugins.Lastfm.dll",
                "ServerRestart.dll",
                "MediaBrowser.Plugins.NotifyMyAndroidNotifications.dll"
            };

            return !exclude.Contains(filename ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the system status.
        /// </summary>
        /// <returns>SystemInfo.</returns>
        public async Task<SystemInfo> GetSystemInfo(CancellationToken cancellationToken)
        {
            var localAddress = await GetLocalApiUrl(cancellationToken).ConfigureAwait(false);

            return new SystemInfo
            {
                HasPendingRestart = HasPendingRestart,
                IsShuttingDown = IsShuttingDown,
                Version = ApplicationVersion.ToString(),
                WebSocketPortNumber = HttpPort,
                CompletedInstallations = InstallationManager.CompletedInstallations.ToArray(),
                Id = SystemId,
                ProgramDataPath = ApplicationPaths.ProgramDataPath,
                LogPath = ApplicationPaths.LogDirectoryPath,
                ItemsByNamePath = ApplicationPaths.InternalMetadataPath,
                InternalMetadataPath = ApplicationPaths.InternalMetadataPath,
                CachePath = ApplicationPaths.CachePath,
                HttpServerPortNumber = HttpPort,
                SupportsHttps = SupportsHttps,
                HttpsPortNumber = HttpsPort,
                OperatingSystem = EnvironmentInfo.OperatingSystem.ToString(),
                OperatingSystemDisplayName = OperatingSystemDisplayName,
                CanSelfRestart = CanSelfRestart,
                CanSelfUpdate = CanSelfUpdate,
                CanLaunchWebBrowser = CanLaunchWebBrowser,
                WanAddress = ConnectManager.WanApiAddress,
                HasUpdateAvailable = HasUpdateAvailable,
                SupportsAutoRunAtStartup = SupportsAutoRunAtStartup,
                TranscodingTempPath = ApplicationPaths.TranscodingTempPath,
                ServerName = FriendlyName,
                LocalAddress = localAddress,
                SupportsLibraryMonitor = true,
                EncoderLocationType = MediaEncoder.EncoderLocationType,
                SystemArchitecture = EnvironmentInfo.SystemArchitecture,
                SystemUpdateLevel = SystemUpdateLevel,
                PackageName = StartupOptions.GetOption("-package")
            };
        }

        public WakeOnLanInfo[] GetWakeOnLanInfo()
        {
            return NetworkManager.GetMacAddresses()
                .Select(i => new WakeOnLanInfo
                {
                    MacAddress = i
                })
                .ToArray();
        }

        public async Task<PublicSystemInfo> GetPublicSystemInfo(CancellationToken cancellationToken)
        {
            var localAddress = await GetLocalApiUrl(cancellationToken).ConfigureAwait(false);

            return new PublicSystemInfo
            {
                Version = ApplicationVersion.ToString(),
                Id = SystemId,
                OperatingSystem = EnvironmentInfo.OperatingSystem.ToString(),
                WanAddress = ConnectManager.WanApiAddress,
                ServerName = FriendlyName,
                LocalAddress = localAddress
            };
        }

        public bool EnableHttps
        {
            get
            {
                return SupportsHttps && ServerConfigurationManager.Configuration.EnableHttps;
            }
        }

        public bool SupportsHttps
        {
            get { return Certificate != null || ServerConfigurationManager.Configuration.IsBehindProxy; }
        }

        public async Task<string> GetLocalApiUrl(CancellationToken cancellationToken)
        {
            try
            {
                // Return the first matched address, if found, or the first known local address
                var addresses = await GetLocalIpAddressesInternal(false, 1, cancellationToken).ConfigureAwait(false);

                foreach (var address in addresses)
                {
                    return GetLocalApiUrl(address);
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error getting local Ip address information", ex);
            }

            return null;
        }

        public string GetLocalApiUrl(IpAddressInfo ipAddress)
        {
            if (ipAddress.AddressFamily == IpAddressFamily.InterNetworkV6)
            {
                return GetLocalApiUrl("[" + ipAddress.Address + "]");
            }

            return GetLocalApiUrl(ipAddress.Address);
        }

        public string GetLocalApiUrl(string host)
        {
            return string.Format("http://{0}:{1}",
                host,
                HttpPort.ToString(CultureInfo.InvariantCulture));
        }

        public Task<List<IpAddressInfo>> GetLocalIpAddresses(CancellationToken cancellationToken)
        {
            return GetLocalIpAddressesInternal(true, 0, cancellationToken);
        }

        private async Task<List<IpAddressInfo>> GetLocalIpAddressesInternal(bool allowLoopback, int limit, CancellationToken cancellationToken)
        {
            var addresses = ServerConfigurationManager
                .Configuration
                .LocalNetworkAddresses
                .Select(NormalizeConfiguredLocalAddress)
                .Where(i => i != null)
                .ToList();

            if (addresses.Count == 0)
            {
                addresses.AddRange(NetworkManager.GetLocalIpAddresses());
            }

            var resultList = new List<IpAddressInfo>();

            foreach (var address in addresses)
            {
                if (!allowLoopback)
                {
                    if (address.Equals(IpAddressInfo.Loopback) || address.Equals(IpAddressInfo.IPv6Loopback))
                    {
                        continue;
                    }
                }

                var valid = await IsIpAddressValidAsync(address, cancellationToken).ConfigureAwait(false);
                if (valid)
                {
                    resultList.Add(address);

                    if (limit > 0 && resultList.Count >= limit)
                    {
                        return resultList;
                    }
                }
            }

            return resultList;
        }

        private IpAddressInfo NormalizeConfiguredLocalAddress(string address)
        {
            var index = address.Trim('/').IndexOf('/');

            if (index != -1)
            {
                address = address.Substring(index + 1);
            }

            IpAddressInfo result;
            if (NetworkManager.TryParseIpAddress(address.Trim('/'), out result))
            {
                return result;
            }
            return null;
        }

        private readonly ConcurrentDictionary<string, bool> _validAddressResults = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private async Task<bool> IsIpAddressValidAsync(IpAddressInfo address, CancellationToken cancellationToken)
        {
            if (address.Equals(IpAddressInfo.Loopback) ||
                address.Equals(IpAddressInfo.IPv6Loopback))
            {
                return true;
            }

            var apiUrl = GetLocalApiUrl(address);
            apiUrl += "/system/ping";

            bool cachedResult;
            if (_validAddressResults.TryGetValue(apiUrl, out cachedResult))
            {
                return cachedResult;
            }

            var logPing = false;

#if DEBUG
            logPing = true;
#endif

            try
            {
                using (var response = await HttpClient.SendAsync(new HttpRequestOptions
                {
                    Url = apiUrl,
                    LogErrorResponseBody = false,
                    LogErrors = logPing,
                    LogRequest = logPing,
                    TimeoutMs = 30000,
                    BufferContent = false,

                    CancellationToken = cancellationToken

                }, "POST").ConfigureAwait(false))
                {
                    using (var reader = new StreamReader(response.Content))
                    {
                        var result = reader.ReadToEnd();
                        var valid = string.Equals(Name, result, StringComparison.OrdinalIgnoreCase);

                        _validAddressResults.AddOrUpdate(apiUrl, valid, (k, v) => valid);
                        Logger.Debug("Ping test result to {0}. Success: {1}", apiUrl, valid);
                        return valid;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("Ping test result to {0}. Success: {1}", apiUrl, "Cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Debug("Ping test result to {0}. Success: {1} {2}", apiUrl, false, ex.Message);

                _validAddressResults.AddOrUpdate(apiUrl, false, (k, v) => false);
                return false;
            }
        }

        public string FriendlyName
        {
            get
            {
                return string.IsNullOrEmpty(ServerConfigurationManager.Configuration.ServerName)
                    ? Environment.MachineName
                    : ServerConfigurationManager.Configuration.ServerName;
            }
        }

        public int HttpPort { get; private set; }

        public int HttpsPort { get; private set; }

        /// <summary>
        /// Shuts down.
        /// </summary>
        public async Task Shutdown()
        {
            if (IsShuttingDown)
            {
                return;
            }

            IsShuttingDown = true;

            try
            {
                await SessionManager.SendServerShutdownNotification(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error sending server shutdown notification", ex);
            }

            ShutdownInternal();
        }

        protected abstract void ShutdownInternal();

        /// <summary>
        /// Registers the server with administrator access.
        /// </summary>
        private void RegisterServerWithAdministratorAccess()
        {
            Logger.Info("Requesting administrative access to authorize http server");

            try
            {
                AuthorizeServer();
            }
            catch (NotImplementedException)
            {

            }
            catch (Exception ex)
            {
                Logger.ErrorException("Error authorizing server", ex);
            }
        }

        protected virtual void AuthorizeServer()
        {
            throw new NotImplementedException();
        }

        public event EventHandler HasUpdateAvailableChanged;

        private bool _hasUpdateAvailable;
        public bool HasUpdateAvailable
        {
            get { return _hasUpdateAvailable; }
            set
            {
                var fireEvent = value && !_hasUpdateAvailable;

                _hasUpdateAvailable = value;

                if (fireEvent)
                {
                    EventHelper.FireEventIfNotNull(HasUpdateAvailableChanged, this, EventArgs.Empty, Logger);
                }
            }
        }

        /// <summary>
        /// Removes the plugin.
        /// </summary>
        /// <param name="plugin">The plugin.</param>
        public void RemovePlugin(IPlugin plugin)
        {
            var list = Plugins.ToList();
            list.Remove(plugin);
            Plugins = list.ToArray();
        }

        /// <summary>
        /// Checks for update.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        /// <returns>Task{CheckForUpdateResult}.</returns>
        public async Task<CheckForUpdateResult> CheckForApplicationUpdate(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var updateLevel = SystemUpdateLevel;
            var cacheLength = updateLevel == PackageVersionClass.Release ?
                TimeSpan.FromHours(12) :
                TimeSpan.FromMinutes(5);

            try
            {
                var result = await new GithubUpdater(HttpClient, JsonSerializer).CheckForUpdateResult("MediaBrowser",
                    "Emby.Releases",
                    ApplicationVersion,
                    updateLevel,
                    ReleaseAssetFilename,
                    "MBServer",
                    UpdateTargetFileName,
                    cacheLength,
                    cancellationToken).ConfigureAwait(false);

                HasUpdateAvailable = result.IsUpdateAvailable;

                return result;
            }
            catch (HttpException ex)
            {
                // users are overreacting to this occasionally failing
                if (ex.StatusCode.HasValue && ex.StatusCode.Value == HttpStatusCode.Forbidden)
                {
                    HasUpdateAvailable = false;
                    return new CheckForUpdateResult
                    {
                        IsUpdateAvailable = false
                    };
                }

                throw;
            }
        }

        protected virtual string UpdateTargetFileName
        {
            get { return "Mbserver.zip"; }
        }

        /// <summary>
        /// Updates the application.
        /// </summary>
        /// <param name="package">The package that contains the update</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        public async Task UpdateApplication(PackageVersionInfo package, CancellationToken cancellationToken, IProgress<double> progress)
        {
            await InstallationManager.InstallPackage(package, false, progress, cancellationToken).ConfigureAwait(false);

            HasUpdateAvailable = false;

            OnApplicationUpdated(package);
        }

        /// <summary>
        /// Configures the automatic run at startup.
        /// </summary>
        /// <param name="autorun">if set to <c>true</c> [autorun].</param>
        protected void ConfigureAutoRunAtStartup(bool autorun)
        {
            if (SupportsAutoRunAtStartup)
            {
                ConfigureAutoRunInternal(autorun);
            }
        }

        protected virtual void ConfigureAutoRunInternal(bool autorun)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This returns localhost in the case of no external dns, and the hostname if the 
        /// dns is prefixed with a valid Uri prefix.
        /// </summary>
        /// <param name="externalDns">The external dns prefix to get the hostname of.</param>
        /// <returns>The hostname in <paramref name="externalDns"/></returns>
        private static string GetHostnameFromExternalDns(string externalDns)
        {
            if (string.IsNullOrEmpty(externalDns))
            {
                return "localhost";
            }

            try
            {
                return new Uri(externalDns).Host;
            }
            catch
            {
                return externalDns;
            }
        }

        public virtual void LaunchUrl(string url)
        {
            if (!CanLaunchWebBrowser)
            {
                throw new NotSupportedException();
            }

            var process = ProcessFactory.Create(new ProcessOptions
            {
                FileName = url,
                //EnableRaisingEvents = true,
                UseShellExecute = true,
                ErrorDialog = false
            });

            process.Exited += ProcessExited;

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error launching url: {0}", url);
                Logger.ErrorException("Error launching url: {0}", ex, url);

                throw;
            }
        }

        private static void ProcessExited(object sender, EventArgs e)
        {
            ((IProcess)sender).Dispose();
        }

        public virtual void EnableLoopback(string appName)
        {
        }

        /// <summary>
        /// Called when [application updated].
        /// </summary>
        /// <param name="package">The package.</param>
        protected void OnApplicationUpdated(PackageVersionInfo package)
        {
            Logger.Info("Application has been updated to version {0}", package.versionStr);

            EventHelper.FireEventIfNotNull(ApplicationUpdated, this, new GenericEventArgs<PackageVersionInfo>
            {
                Argument = package

            }, Logger);

            NotifyPendingRestart();
        }

        private bool _disposed;
        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                Dispose(true);
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool dispose)
        {
            if (dispose)
            {
                var type = GetType();

                LogManager.AddConsoleOutput();
                Logger.Info("Disposing " + type.Name);

                var parts = DisposableParts.Distinct().Where(i => i.GetType() != type).ToList();
                DisposableParts.Clear();

                foreach (var part in parts)
                {
                    Logger.Info("Disposing " + part.GetType().Name);

                    try
                    {
                        part.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorException("Error disposing {0}", ex, part.GetType().Name);
                    }
                }
            }
        }

        private Dictionary<string, string> _values;
        public string GetValue(string name)
        {
            if (_values == null)
            {
                _values = LoadValues();
            }

            string value;

            if (_values.TryGetValue(name, out value))
            {
                return value;
            }

            return null;
        }

        private Dictionary<string, string> LoadValues()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var stream = typeof(ApplicationHost).Assembly.GetManifestResourceStream(typeof(ApplicationHost).Namespace + ".values.txt"))
            {
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        var index = line.IndexOf('=');
                        if (index != -1)
                        {
                            values[line.Substring(0, index)] = line.Substring(index + 1);
                        }
                    }
                }
            }

            return values;
        }
    }

    internal class CertificateInfo
    {
        public string Path { get; set; }
        public string Password { get; set; }
    }
}
