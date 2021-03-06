using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroeg.EntityStore.Models;
using Microsoft.Extensions.Logging;
using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;
using System.Data.Common;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Kroeg.EntityStore.Notifier;

namespace Kroeg.EntityStore
{
  public class BackgroundTaskQueuer
  {
    private CancellationTokenSource _cancellationTokenSource;
    private readonly NpgsqlConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundTaskQueuer> _logger;
    private readonly INotifier _notifier;

    public const string BackgroundTaskPath = "backgroundtask:new";

    public BackgroundTaskQueuer(NpgsqlConnection connection, IServiceProvider serviceProvider, ILogger<BackgroundTaskQueuer> logger, INotifier notifier)
    {
      _connection = connection;
      _serviceProvider = serviceProvider;
      _logger = logger;
      _notifier = notifier;

      _connection.Open();
      Console.WriteLine("Working background tasks...");
      Do();
    }

    private async Task<int> GetSleepTime(DateTime? after = null)
    {
      var av = after ?? DateTime.MinValue;
      var nextAction = await _connection.QuerySingleOrDefaultAsync<EventQueueItem>("SELECT * FROM \"EventQueue\" WHERE \"NextAttempt\" > @time order by \"NextAttempt\" limit 1", new { time = after });
      if (nextAction == null) return -1;
      var retval = (int)(nextAction.NextAttempt - DateTime.Now).TotalMilliseconds;
      return Math.Max(retval, 0);
    }

    public async void Do()
    {
      await Task.Delay(2000);

      DateTime after = DateTime.MinValue;
      while (true)
      {
        // set up the wait
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var sleepTime = await GetSleepTime(after);
        if (sleepTime != 0)
        {
          try
          {
            if (sleepTime > 0)
              _cancellationTokenSource.CancelAfter(sleepTime);

            await _notifier.Subscribe(BackgroundTaskPath, (a) => _cancellationTokenSource?.Cancel());
            await _connection.WaitAsync(_cancellationTokenSource.Token);
          }
          catch (OperationCanceledException) { }
        }
        _connection.Close(); // workaround for npgsql issue??
        await _connection.OpenAsync();
        var transaction = _connection.BeginTransaction();
        var nextAction = await _connection.QuerySingleOrDefaultAsync<EventQueueItem>("SELECT * FROM \"EventQueue\" WHERE \"NextAttempt\" < @time order by \"NextAttempt\" limit 1 for update skip locked", new { time = DateTime.Now.AddMinutes(1) });
        _logger.LogDebug($"Next action: {nextAction?.Action ?? "nothing"}");

        if (nextAction == null)
        {
          transaction.Rollback();
          transaction.Dispose();
          continue;
        }

        await BaseTask.Go(_connection, nextAction, _serviceProvider, transaction);
      }
    }
  }
}
