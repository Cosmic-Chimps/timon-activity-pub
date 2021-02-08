using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Newtonsoft.Json;
using Npgsql;

namespace Kroeg.EntityStore.Notifier
{
  public class LocalNotifier : INotifier
  {
    private readonly NpgsqlConnection _connection;

    public LocalNotifier(NpgsqlConnection connection)
    {
      _connection = connection;
      _connection.Notification += OnNotification;
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
      if (e.Channel != "kroeg") return;

      var data = JsonConvert.DeserializeObject<NotifyObject>(e.Payload);

      if (_actions.ContainsKey(data.Path))
        foreach (var action in _actions[data.Path])
          action(data.Value);
    }

    private class NotifyObject
    {
      public string Path { get; set; }
      public string Value { get; set; }
    }

    private readonly Dictionary<string, List<Action<string>>> _actions = new();

    public async Task Notify(string path, string val)
    {
      var data = JsonConvert.SerializeObject(new NotifyObject { Path = path, Value = val });
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
