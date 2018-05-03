﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Users;
using MediaBrowser.Controller.Authentication;

namespace MediaBrowser.Controller.Library
{
    /// <summary>
    /// Interface IUserManager
    /// </summary>
    public interface IUserManager
    {
        /// <summary>
        /// Gets the users.
        /// </summary>
        /// <value>The users.</value>
        IEnumerable<User> Users { get; }

        /// <summary>
        /// Occurs when [user updated].
        /// </summary>
        event EventHandler<GenericEventArgs<User>> UserUpdated;

        /// <summary>
        /// Occurs when [user deleted].
        /// </summary>
        event EventHandler<GenericEventArgs<User>> UserDeleted;

        event EventHandler<GenericEventArgs<User>> UserCreated;
        event EventHandler<GenericEventArgs<User>> UserPolicyUpdated;
        event EventHandler<GenericEventArgs<User>> UserConfigurationUpdated;
        event EventHandler<GenericEventArgs<User>> UserPasswordChanged;
        event EventHandler<GenericEventArgs<User>> UserLockedOut;

        /// <summary>
        /// Gets a User by Id
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>User.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        User GetUserById(Guid id);

        /// <summary>
        /// Gets the user by identifier.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>User.</returns>
        User GetUserById(string id);

        /// <summary>
        /// Gets the name of the user by.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>User.</returns>
        User GetUserByName(string name);

        /// <summary>
        /// Refreshes metadata for each user
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        Task RefreshUsersMetadata(CancellationToken cancellationToken);

        /// <summary>
        /// Renames the user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="newName">The new name.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">user</exception>
        /// <exception cref="System.ArgumentException"></exception>
        Task RenameUser(User user, string newName);

        /// <summary>
        /// Updates the user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <exception cref="System.ArgumentNullException">user</exception>
        /// <exception cref="System.ArgumentException"></exception>
        void UpdateUser(User user);

        /// <summary>
        /// Creates the user.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>User.</returns>
        /// <exception cref="System.ArgumentNullException">name</exception>
        /// <exception cref="System.ArgumentException"></exception>
        Task<User> CreateUser(string name);

        /// <summary>
        /// Deletes the user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.ArgumentNullException">user</exception>
        /// <exception cref="System.ArgumentException"></exception>
        Task DeleteUser(User user);

        /// <summary>
        /// Resets the password.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Task.</returns>
        Task ResetPassword(User user);

        /// <summary>
        /// Gets the offline user dto.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>UserDto.</returns>
        UserDto GetOfflineUserDto(User user);

        /// <summary>
        /// Resets the easy password.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Task.</returns>
        void ResetEasyPassword(User user);
        
        /// <summary>
        /// Changes the password.
        /// </summary>
        Task ChangePassword(User user, string newPassword);

        /// <summary>
        /// Changes the easy password.
        /// </summary>
        void ChangeEasyPassword(User user, string newPassword, string newPasswordSha1);
        
        /// <summary>
        /// Gets the user dto.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="remoteEndPoint">The remote end point.</param>
        /// <returns>UserDto.</returns>
        UserDto GetUserDto(User user, string remoteEndPoint = null);

        /// <summary>
        /// Authenticates the user.
        /// </summary>
        Task<User> AuthenticateUser(string username, string password, string passwordSha1, string passwordMd5, string remoteEndPoint, bool isUserSession);

        /// <summary>
        /// Starts the forgot password process.
        /// </summary>
        /// <param name="enteredUsername">The entered username.</param>
        /// <param name="isInNetwork">if set to <c>true</c> [is in network].</param>
        /// <returns>ForgotPasswordResult.</returns>
        Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork);

        /// <summary>
        /// Redeems the password reset pin.
        /// </summary>
        /// <param name="pin">The pin.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        Task<PinRedeemResult> RedeemPasswordResetPin(string pin);

        /// <summary>
        /// Gets the user policy.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>UserPolicy.</returns>
        UserPolicy GetUserPolicy(User user);

        /// <summary>
        /// Gets the user configuration.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>UserConfiguration.</returns>
        UserConfiguration GetUserConfiguration(User user);

        /// <summary>
        /// Updates the configuration.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="newConfiguration">The new configuration.</param>
        /// <returns>Task.</returns>
        void UpdateConfiguration(string userId, UserConfiguration newConfiguration);

        void UpdateConfiguration(User user, UserConfiguration newConfiguration);

        /// <summary>
        /// Updates the user policy.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="userPolicy">The user policy.</param>
        void UpdateUserPolicy(string userId, UserPolicy userPolicy);

        /// <summary>
        /// Makes the valid username.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>System.String.</returns>
        string MakeValidUsername(string username);

        void AddParts(IEnumerable<IAuthenticationProvider> authenticationProviders);

        NameIdPair[] GetAuthenticationProviders();
    }
}
