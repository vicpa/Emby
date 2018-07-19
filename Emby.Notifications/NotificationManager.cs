﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Dto;

namespace Emby.Notifications
{
    public class NotificationManager : INotificationManager
    {
        private readonly ILogger _logger;
        private readonly IUserManager _userManager;
        private readonly IServerConfigurationManager _config;

        private INotificationService[] _services;
        private INotificationTypeFactory[] _typeFactories;

        public NotificationManager(ILogManager logManager, IUserManager userManager, IServerConfigurationManager config)
        {
            _userManager = userManager;
            _config = config;
            _logger = logManager.GetLogger(GetType().Name);
        }

        private NotificationOptions GetConfiguration()
        {
            return _config.GetConfiguration<NotificationOptions>("notifications");
        }

        public Task SendNotification(NotificationRequest request, CancellationToken cancellationToken)
        {
            return SendNotification(request, null, cancellationToken);
        }

        public Task SendNotification(NotificationRequest request, BaseItem relatedItem, CancellationToken cancellationToken)
        {
            var notificationType = request.NotificationType;

            var options = string.IsNullOrEmpty(notificationType) ?
                null :
                GetConfiguration().GetOptions(notificationType);

            var users = GetUserIds(request, options)
                .Select(i => _userManager.GetUserById(i))
                .Where(i => relatedItem == null || relatedItem.IsVisibleStandalone(i))
                .ToArray();

            var title = request.Name;
            var description = request.Description;

            var tasks = _services.Where(i => IsEnabled(i, notificationType))
                .Select(i => SendNotification(request, i, users, title, description, cancellationToken));

            return Task.WhenAll(tasks);
        }

        private Task SendNotification(NotificationRequest request,
            INotificationService service,
            IEnumerable<User> users,
            string title,
            string description,
            CancellationToken cancellationToken)
        {
            users = users.Where(i => IsEnabledForUser(service, i))
                .ToList();

            var tasks = users.Select(i => SendNotification(request, service, title, description, i, cancellationToken));

            return Task.WhenAll(tasks);
        }

        private IEnumerable<Guid> GetUserIds(NotificationRequest request, NotificationOption options)
        {
            if (request.SendToUserMode.HasValue)
            {
                switch (request.SendToUserMode.Value)
                {
                    case SendToUserType.Admins:
                        return _userManager.Users.Where(i => i.Policy.IsAdministrator)
                                .Select(i => i.Id);
                    case SendToUserType.All:
                        return _userManager.Users.Select(i => i.Id);
                    case SendToUserType.Custom:
                        return request.UserIds;
                    default:
                        throw new ArgumentException("Unrecognized SendToUserMode: " + request.SendToUserMode.Value);
                }
            }

            if (options != null && !string.IsNullOrEmpty(request.NotificationType))
            {
                var config = GetConfiguration();

                return _userManager.Users
                    .Where(i => config.IsEnabledToSendToUser(request.NotificationType, i.Id.ToString("N"), i.Policy))
                    .Select(i => i.Id);
            }

            return request.UserIds;
        }

        private async Task SendNotification(NotificationRequest request,
            INotificationService service,
            string title,
            string description,
            User user,
            CancellationToken cancellationToken)
        {
            var notification = new UserNotification
            {
                Date = request.Date,
                Description = description,
                Level = request.Level,
                Name = title,
                Url = request.Url,
                User = user
            };

            _logger.Debug("Sending notification via {0} to user {1}", service.Name, user.Name);

            try
            {
                await service.SendNotification(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error sending notification to {0}", ex, service.Name);
            }
        }

        private bool IsEnabledForUser(INotificationService service, User user)
        {
            try
            {
                return service.IsEnabledForUser(user);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error in IsEnabledForUser", ex);
                return false;
            }
        }

        private bool IsEnabled(INotificationService service, string notificationType)
        {
            if (string.IsNullOrEmpty(notificationType))
            {
                return true;
            }

            return GetConfiguration().IsServiceEnabled(service.Name, notificationType);
        }

        public void AddParts(IEnumerable<INotificationService> services, IEnumerable<INotificationTypeFactory> notificationTypeFactories)
        {
            _services = services.ToArray();
            _typeFactories = notificationTypeFactories.ToArray();
        }

        public List<NotificationTypeInfo> GetNotificationTypes()
        {
            var list = _typeFactories.Select(i =>
            {
                try
                {
                    return i.GetNotificationTypes().ToList();
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error in GetNotificationTypes", ex);
                    return new List<NotificationTypeInfo>();
                }

            }).SelectMany(i => i).ToList();

            var config = GetConfiguration();

            foreach (var i in list)
            {
                i.Enabled = config.IsEnabled(i.Type);
            }

            return list;
        }

        public IEnumerable<NameIdPair> GetNotificationServices()
        {
            return _services.Select(i => new NameIdPair
            {
                Name = i.Name,
                Id = i.Name.GetMD5().ToString("N")

            }).OrderBy(i => i.Name);
        }
    }
}
