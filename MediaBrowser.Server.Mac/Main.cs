﻿using MediaBrowser.Model.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using MonoMac.AppKit;
using MonoMac.Foundation;
using MonoMac.ObjCRuntime;
using Emby.Server.Core;
using Emby.Server.Core.Cryptography;
using Emby.Server.Implementations;
using Emby.Server.Implementations.Logging;
using Emby.Server.Mac.Native;
using Emby.Server.Implementations.IO;
using Mono.Unix.Native;
using MediaBrowser.Model.System;
using MediaBrowser.Model.IO;
using Emby.Drawing;
using Emby.Drawing.Skia;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Model.Drawing;
using Emby.Server.Implementations.EnvironmentInfo;
using Emby.Server.Implementations.Networking;

namespace MediaBrowser.Server.Mac
{
	class MainClass
	{
		internal static MacAppHost AppHost;

		private static ILogger _logger;
		private static IFileSystem _fileSystem;

		static void Main (string[] args)
		{
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());

            var applicationPath = Assembly.GetEntryAssembly().Location;

			var options = new StartupOptions(Environment.GetCommandLineArgs());

			// Allow this to be specified on the command line.
			var customProgramDataPath = options.GetOption("-programdata");

			var appFolderPath = Path.GetDirectoryName(applicationPath);

			var appPaths = CreateApplicationPaths(appFolderPath, customProgramDataPath);

			var logManager = new SimpleLogManager(appPaths.LogDirectoryPath, "server");
			logManager.ReloadLogger(LogSeverity.Info);
			logManager.AddConsoleOutput();

			var logger = _logger = logManager.GetLogger("Main");

			ApplicationHost.LogEnvironmentInfo(logger, appPaths, true);

			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

			StartApplication(appPaths, logManager, options);
			NSApplication.Init ();
			NSApplication.Main (args);
		}

		private static ServerApplicationPaths CreateApplicationPaths(string appFolderPath, string programDataPath)
		{
			if (string.IsNullOrEmpty(programDataPath))
			{
				// TODO: Use CommonApplicationData? Will we always have write access?
				programDataPath = Path.Combine(Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "mediabrowser-server");

				if (!Directory.Exists (programDataPath)) {
					programDataPath = Path.Combine(Environment.GetFolderPath (Environment.SpecialFolder.ApplicationData), "emby-server");
				}
			}

			// Within the mac bundle, go uo two levels then down into Resources folder
			var resourcesPath = Path.Combine(Path.GetDirectoryName(appFolderPath), "Resources");

			return new ServerApplicationPaths(programDataPath, appFolderPath, resourcesPath);
		}

		/// <summary>
		/// Runs the application.
		/// </summary>
		/// <param name="appPaths">The app paths.</param>
		/// <param name="logManager">The log manager.</param>
		/// <param name="options">The options.</param>
		private static void StartApplication(ServerApplicationPaths appPaths, 
			ILogManager logManager, 
			StartupOptions options)
		{
			// Allow all https requests
			ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(delegate { return true; });

			var environmentInfo = GetEnvironmentInfo();

			var fileSystem = new MonoFileSystem(logManager.GetLogger("FileSystem"), environmentInfo, appPaths.TempDirectory);

			_fileSystem = fileSystem;

			var imageEncoder = GetImageEncoder(appPaths, fileSystem, logManager);

			AppHost = new MacAppHost(appPaths,
									 logManager,
									 options,
									 fileSystem,
									 new PowerManagement(),
									 "Emby.Server.Mac.pkg",
									 environmentInfo,
									 imageEncoder,
									 new SystemEvents(logManager.GetLogger("SystemEvents")),
			                         new NetworkManager(logManager.GetLogger("NetworkManager")));

			if (options.ContainsOption("-v")) {
				Console.WriteLine (AppHost.ApplicationVersion.ToString());
				return;
			}

			Console.WriteLine ("appHost.Init");

			Task.Run (() => StartServer(CancellationToken.None));
        }

	    private static IImageEncoder GetImageEncoder(ServerApplicationPaths appPaths, IFileSystem fileSystem, ILogManager logManager)
	    {
	        try
	        {
                return new SkiaEncoder(logManager.GetLogger("Skia"), appPaths, () => AppHost.HttpClient, fileSystem);
            }
            catch (Exception ex)
	        {
	            return new NullImageEncoder();
	        }
	    }

        private static EnvironmentInfo GetEnvironmentInfo()
        {
            var info = new EnvironmentInfo()
            {
                OperatingSystem = MediaBrowser.Model.System.OperatingSystem.OSX
            };

            var uname = GetUnixName();

            var sysName = uname.sysname ?? string.Empty;

            if (string.Equals(sysName, "Darwin", StringComparison.OrdinalIgnoreCase))
            {
                //info.OperatingSystem = Startup.Common.OperatingSystem.Osx;
            }
            else if (string.Equals(sysName, "Linux", StringComparison.OrdinalIgnoreCase))
            {
                //info.OperatingSystem = Startup.Common.OperatingSystem.Linux;
            }
            else if (string.Equals(sysName, "BSD", StringComparison.OrdinalIgnoreCase))
            {
                //info.OperatingSystem = Startup.Common.OperatingSystem.Bsd;
                //info.IsBsd = true;
            }

            var archX86 = new Regex("(i|I)[3-6]86");

            if (archX86.IsMatch(uname.machine))
            {
                info.SystemArchitecture = Architecture.X86;
            }
            else if (string.Equals(uname.machine, "x86_64", StringComparison.OrdinalIgnoreCase))
            {
                info.SystemArchitecture = Architecture.X64;
            }
            else if (uname.machine.StartsWith("arm", StringComparison.OrdinalIgnoreCase))
            {
                info.SystemArchitecture = Architecture.Arm;
            }
            else if (System.Environment.Is64BitOperatingSystem)
            {
                info.SystemArchitecture = Architecture.X64;
            }
            else
            {
                info.SystemArchitecture = Architecture.X86;
            }

            return info;
        }

        private static Uname _unixName;

        private static Uname GetUnixName()
        {
            if (_unixName == null)
            {
                var uname = new Uname();
                try
                {
                    Utsname utsname;
                    var callResult = Syscall.uname(out utsname);
                    if (callResult == 0)
                    {
                        uname.sysname = utsname.sysname ?? string.Empty;
                        uname.machine = utsname.machine ?? string.Empty;
                    }

                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error getting unix name", ex);
                }
                _unixName = uname;
            }
            return _unixName;
        }

        public class Uname
        {
            public string sysname = string.Empty;
            public string machine = string.Empty;
        }

        private static async void StartServer(CancellationToken cancellationToken) 
		{
			var initProgress = new Progress<double>();

			await AppHost.Init (initProgress).ConfigureAwait (false);

			await AppHost.RunStartupTasks ().ConfigureAwait (false);

			if (MenuBarIcon.Instance != null) 
			{
				MenuBarIcon.Instance.Localize ();
			}
		}

		public static void Shutdown()
		{
			ShutdownApp();
		}

		private static void ShutdownApp()
		{
			_logger.Info ("Calling ApplicationHost.Dispose");
			AppHost.Dispose ();

			_logger.Info("AppController.Terminate");
			MenuBarIcon.Instance.Terminate ();
		}

        public static void Restart()
        {
            _logger.Info("Disposing app host");
            AppHost.Dispose();

            _logger.Info("Starting new instance");

            var args = Environment.GetCommandLineArgs()
				.Skip(1)
                .Select(NormalizeCommandLineArgument);

            var commandLineArgsString = string.Join(" ", args.ToArray());
			var module = Environment.GetCommandLineArgs().First();

			_logger.Info ("Executable: {0}", module);
			_logger.Info ("Arguments: {0}", commandLineArgsString);

            Process.Start(module, commandLineArgsString);

            _logger.Info("AppController.Terminate");
            MenuBarIcon.Instance.Terminate();
        }

        private static string NormalizeCommandLineArgument(string arg)
        {
            if (arg.IndexOf(" ", StringComparison.OrdinalIgnoreCase) == -1)
            {
                return arg;
            }

            return "\"" + arg + "\"";
        }

		/// <summary>
		/// Handles the UnhandledException event of the CurrentDomain control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="UnhandledExceptionEventArgs"/> instance containing the event data.</param>
		static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			var exception = (Exception)e.ExceptionObject;

			var consoleLogger = new ConsoleLogger();

			new UnhandledExceptionWriter(AppHost.ServerConfigurationManager.ApplicationPaths, _logger, AppHost.LogManager, _fileSystem, consoleLogger).Log(exception);

			if (!Debugger.IsAttached)
			{
				Environment.Exit(System.Runtime.InteropServices.Marshal.GetHRForException(exception));
			}
		}
	}

	class NoCheckCertificatePolicy : ICertificatePolicy
	{
		public bool CheckValidationResult (ServicePoint srvPoint, 
		                                   System.Security.Cryptography.X509Certificates.X509Certificate certificate, 
		                                   WebRequest request, 
		                                   int certificateProblem)
		{
			return true;
		}
    }
}

