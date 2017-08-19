using System;

namespace libcore.Notificator
{
    public interface INotificationCommandManager
    {
        void Register(Type commandType);
        T Create<T>() where T : INotificationCommand;

        /// <exception cref="NotificationCommandManagerException"></exception>
        INotificationCommand Deserialize(string message, string channel, int pid);

        string Serialize(INotificationCommand cmd);
    }
}