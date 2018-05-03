﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks
{
    /// <summary>
    /// Class ReloadLoggerFileTask
    /// </summary>
    public class ReloadLoggerFileTask : IScheduledTask, IConfigurableScheduledTask
    {
        /// <summary>
        /// Gets or sets the log manager.
        /// </summary>
        /// <value>The log manager.</value>
        private ILogManager LogManager { get; set; }
        /// <summary>
        /// Gets or sets the configuration manager.
        /// </summary>
        /// <value>The configuration manager.</value>
        private IConfigurationManager ConfigurationManager { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReloadLoggerFileTask" /> class.
        /// </summary>
        /// <param name="logManager">The logManager.</param>
        /// <param name="configurationManager">The configuration manager.</param>
        public ReloadLoggerFileTask(ILogManager logManager, IConfigurationManager configurationManager)
        {
            LogManager = logManager;
            ConfigurationManager = configurationManager;
        }

        /// <summary>
        /// Gets the default triggers.
        /// </summary>
        /// <returns>IEnumerable{BaseTaskTrigger}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var trigger = new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(0).Ticks }; //12am

            return new[] { trigger };
        }

        /// <summary>
        /// Executes the internal.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        /// <returns>Task.</returns>
        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress.Report(0);

            return LogManager.ReloadLogger(ConfigurationManager.CommonConfiguration.EnableDebugLevelLogging
                                        ? LogSeverity.Debug
                                        : LogSeverity.Info, cancellationToken);
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return "Rotate log file"; }
        }

        public string Key { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        /// <value>The description.</value>
        public string Description
        {
            get { return "Moves logging to a new file to help reduce log file sizes."; }
        }

        /// <summary>
        /// Gets the category.
        /// </summary>
        /// <value>The category.</value>
        public string Category
        {
            get { return "Application"; }
        }

        public bool IsHidden
        {
            get { return false; }
        }

        public bool IsEnabled
        {
            get { return true; }
        }

        public bool IsLogged
        {
            get { return true; }
        }
    }
}
