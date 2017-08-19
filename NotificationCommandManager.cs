using System;
using System.Collections.Generic;
using company.core.Utils;

namespace libcore.Notificator
{
    public class NotificationCommandManager : INotificationCommandManager
    {
        private readonly Dictionary<string, Type> _commands = new Dictionary<string, Type>();
        private readonly NotificationCommandSerializator _commandSerializator = new NotificationCommandSerializator();

        public NotificationCommandManager(IIoCResolver resolver)
        {
            Resolver = resolver;
        }

        private IIoCResolver Resolver { get; }

        public void Register(Type commandType)
        {
            if (!typeof(INotificationCommand).IsAssignableFrom(commandType))
                // ReSharper disable once ExceptionNotDocumented
                throw new ArgumentException($"Invalid type: must implement {nameof(INotificationCommand)}", nameof(commandType));
            var attribute = commandType.GetAttribute<NotificationCommandAttribute>();
            if (attribute == null)
                // ReSharper disable once ExceptionNotDocumented
                throw new ArgumentException($"Invalid type: must have attribute {nameof(NotificationCommandAttribute)}", nameof(commandType));
            if (string.IsNullOrEmpty(attribute.Code))
                // ReSharper disable once ExceptionNotDocumented
                throw new ArgumentException($"Invalid attribute: empty {nameof(NotificationCommandAttribute.Code)}", nameof(commandType));
            if (_commands.ContainsKey(attribute.Code))
                // ReSharper disable once ExceptionNotDocumented
            {
                throw new ArgumentException($"Invalid type: type with same {nameof(NotificationCommandAttribute.Code)} is already registered",
                    nameof(commandType));
            }
            _commands.Add(attribute.Code, commandType);
        }

        public T Create<T>() where T : INotificationCommand
        {
            return Resolver.Resolve<T>();
        }

        /// <exception cref="NotificationCommandManagerException"></exception>
        public INotificationCommand Deserialize(string message, string channel, int pid)
        {
            var obj = _commandSerializator.Parse(message);
            if (obj == null || !obj.ContainsKey(nameof(INotificationCommand.Code)))
                throw new NotificationCommandManagerException($"Invalid command: {message}");

            var code = obj[nameof(INotificationCommand.Code)] as string;

            if (code == null || !_commands.ContainsKey(code))
                throw new NotificationCommandManagerException($"Invalid command: {message}");

            var type = _commands[code];
            var notificationCommand = _commandSerializator.Parse(message, type);
            if (notificationCommand == null)
                throw new NotificationCommandManagerException($"Invalid command: {message}");

            notificationCommand.Channel = channel;
            notificationCommand.Pid = pid;

            return notificationCommand;
        }

        public string Serialize(INotificationCommand cmd)
        {
            return _commandSerializator.Serialize(cmd);
        }
    }
}