using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using company.core;
using company.core.Data.Log;
using libcore;
using libcore.Notificator;
using utils;

namespace company.data.dapper.Notificator
{
    public class PgNotificator : IDbNotificator
    {
        private const int Interval = 2000;
        private readonly INotificationCommandManager _commandManager;
        private readonly ICompanyConnection _connection;
        private readonly ICompanyConnectionCloneMaker _connectionCloneMaker;
        private readonly IIoCResolver _resolver;
        private readonly Thread _worker;
        private string _channelName;

        private bool _exit;


        public PgNotificator(IIoCResolver resolver, ICompanyConnection connection, ICompanyConnectionCloneMaker connectionCloneMaker,
            INotificationCommandManager commandManager)
        {
            _resolver = resolver;
            _connectionCloneMaker = connectionCloneMaker;
            _commandManager = commandManager;
            _connection = connectionCloneMaker.CloneConnection(connection.DbConnection);
            _worker = new Thread(GettingNotifyTask) {Name = "Notificator"};
        }

        /// <exception cref="DbNotificatorException"></exception>
        public void Listen(string channelName)
        {
            Debug.Assert(!string.IsNullOrEmpty(channelName));
            if (!string.IsNullOrEmpty(_channelName))
                throw new DbNotificatorException($"Уже открыт канал {_channelName}");

            _channelName = channelName;
            _connection.UseNotification(channelName);

            _worker.Start();
        }

        public WaitHandle Stop()
        {
            _exit = true;
            _worker.Join();
            return new AutoResetEvent(true);
        }

        public bool IsRunning => _worker.IsAlive;
        public event EventHandler<NotificationCommandEventArgs> Notification;

        public void Send<T>(string channel) where T : INotificationCommand
        {
            var cmd = _commandManager.Create<T>();
            var task = DoSend(cmd, channel);
            Task.WaitAll(task);
        }

        public INotificationCommand SendAndReceive<T>(string channel) where T : IDuplexNotificationCommand
        {
            INotificationCommand answer = null;
            var answerChannel = $"ch{_connection.ProcessId}{DateTime.Now.Ticks}";

            var answerTask = Task.Factory.StartNew(() =>
            {
                using (var connection = _connectionCloneMaker.CloneConnection(_connection.DbConnection))
                {
                    connection.UseNotification(answerChannel);
                    connection.Notification += (sender, e) => { answer = _commandManager.Deserialize(e.Message, e.Channel, e.PID); };
                    var dbCommand = connection.DbConnection.CreateCommand();
                    dbCommand.CommandText = ";";
                    while (true)
                        try
                        {
                            dbCommand.ExecuteNonQuery();
                        }
                        // ReSharper disable once CatchAllClause
                        // ReSharper disable once EmptyGeneralCatchClause
                        catch
                        {
                        }
                        finally
                        {
                            Thread.Sleep(100);
                        }
                }
            });

            var cmd = _commandManager.Create<T>();
            cmd.AnswerChannel = answerChannel;
            var sendTask = DoSend(cmd, channel);

            Task.WaitAll(new[] {answerTask, sendTask}, (int) (Interval * 1.5));
            //Task.WaitAll(answerTask, sendTask);

            return answer;
        }

        private void GettingNotifyTask()
        {
            _connection.Notification += OnNotification;
            var dbCommand = _connection.DbConnection.CreateCommand();
            dbCommand.CommandText = ";";
            while (!_exit)
                try
                {
                    dbCommand.ExecuteNonQuery();
                }
                // ReSharper disable once CatchAllClause
                // ReSharper disable once EmptyGeneralCatchClause
                catch
                {
                }
                finally
                {
                    Thread.Sleep(Interval);
                }
        }

        private void OnNotification(object sender, CompanyNotificationEventArgs e)
        {
            var loggerManager = _resolver.Resolve<ILoggerManager>();
            var logger = loggerManager?.Create();
            logger?.SetContext(Environment.NewLine.MakeKeyValueStrings(
                "Message".KeyValueString(e.Message),
                "Channel".KeyValueString(e.Channel),
                "PID".KeyValueString(e.PID)
            ));

            try
            {
                var notificationCommand = _commandManager.Deserialize(e.Message, e.Channel, e.PID);
                OnNotification(new NotificationCommandEventArgs(notificationCommand));
                Task.Factory.StartNew(() => logger?.Info("Notification", "Принято сообщение", "Command".KeyValueString(notificationCommand.GetType())));
            }
            catch (NotificationCommandManagerException ex)
            {
                Task.Factory.StartNew(() => logger?.Warning("Notification", ex.Message));
            }
        }

        protected virtual void OnNotification(NotificationCommandEventArgs e)
        {
            Notification?.Invoke(this, e);
        }

        private Task DoSend(INotificationCommand cmd, string channel)
        {
            return Task.Factory.StartNew(() =>
            {
                using (var connection = _connectionCloneMaker.CloneConnection(_connection.DbConnection))
                {
                    using (var dbCommand = connection.DbConnection.CreateCommand())
                    {
                        dbCommand.CommandText = $"NOTIFY {channel}, '{_commandManager.Serialize(cmd)}'";
                        dbCommand.ExecuteNonQuery();
                    }
                }
            });
        }
    }
}