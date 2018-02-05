﻿using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Session
{
    public interface ISessionController
    {
        /// <summary>
        /// Gets a value indicating whether this instance is session active.
        /// </summary>
        /// <value><c>true</c> if this instance is session active; otherwise, <c>false</c>.</value>
        bool IsSessionActive { get; }

        /// <summary>
        /// Gets a value indicating whether [supports media remote control].
        /// </summary>
        /// <value><c>true</c> if [supports media remote control]; otherwise, <c>false</c>.</value>
        bool SupportsMediaControl { get; }
        
        /// <summary>
        /// Sends the play command.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task SendPlayCommand(PlayRequest command, CancellationToken cancellationToken);

        /// <summary>
        /// Sends the playstate command.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task SendPlaystateCommand(PlaystateRequest command, CancellationToken cancellationToken);

        /// <summary>
        /// Sends the generic command.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task SendGeneralCommand(GeneralCommand command, CancellationToken cancellationToken);

        /// <summary>
        /// Sends the playback start notification.
        /// </summary>
        /// <param name="sessionInfo">The session information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task SendPlaybackStartNotification(SessionInfoDto sessionInfo, CancellationToken cancellationToken);

        /// <summary>
        /// Sends the playback start notification.
        /// </summary>
        /// <param name="sessionInfo">The session information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task SendPlaybackStoppedNotification(SessionInfoDto sessionInfo, CancellationToken cancellationToken);

        /// <summary>
        /// Sends the message.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name">The name.</param>
        /// <param name="data">The data.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task SendMessage<T>(string name, T data, CancellationToken cancellationToken);

        /// <summary>
        /// Called when [activity].
        /// </summary>
        void OnActivity();
    }
}
