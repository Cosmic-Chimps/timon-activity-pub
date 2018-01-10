using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Newtonsoft.Json;
using Npgsql;

namespace Kroeg.Server.Services.Notifiers
{
    public class LocalNotifier : INotifier
    {
        private readonly NpgsqlConnection _connection;

        public LocalNotifier(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += _onNotification;
        }

        private void _onNotification(object sender, NpgsqlNotificationEventArgs e)
        {
            if (e.Condition != "kroeg") return;

            var data = JsonConvert.DeserializeObject<_notifyObject>(e.AdditionalInformation);

            if (_actions.ContainsKey(data.Path))
                foreach (var action in _actions[data.Path])
                    action(data.Value);
        }

        private class _notifyObject {
            public string Path { get; set; }
            public string Value { get; set; }
        }

        private Dictionary<string, List<Action<string>>> _actions = new Dictionary<string, List<Action<string>>>();

        public async Task Notify(string path, string val)
        {
            var data = JsonConvert.SerializeObject(new _notifyObject { Path = path, Value = val});
            await _connection.ExecuteAsync("SELECT pg_notify('kroeg', @Data);", new { Data = data });

            if (_actions.ContainsKey(path))
                foreach (var item in _actions[path])
                    item(val);
        }

        public async Task Subscribe(string path, Action<string> toRun)
        {
            if (_connection.State == ConnectionState.Closed)
            {
                await _connection.OpenAsync();
            }

            await _connection.ExecuteAsync("LISTEN kroeg;");
            if (!_actions.ContainsKey(path)) _actions[path] = new List<Action<string>>();
            _actions[path].Add(toRun);
        }

        public async Task Unsubscribe(string path, Action<string> toRun)
        {
            if (!_actions.ContainsKey(path)) return;
            _actions[path].Remove(toRun);
            await Task.Yield();
        }
    }
}
