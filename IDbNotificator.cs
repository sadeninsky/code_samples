using System;
using System.Threading;

namespace libcore.Notificator
{
    public interface IDbNotificator
    {
        bool IsRunning { get; }
        event EventHandler<NotificationCommandEventArgs> Notification;

        /// <exception cref="DbNotificatorException"></exception>
        void Listen(string channelName);

        WaitHandle Stop();

        void Send<T>(string channel) where T : INotificationCommand;
        INotificationCommand SendAndReceive<T>(string channel) where T : IDuplexNotificationCommand;

    }
}