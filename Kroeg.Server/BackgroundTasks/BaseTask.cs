﻿using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using System.Data;
using Dapper;
using System.Transactions;
using System.Data.Common;

namespace Kroeg.Server.BackgroundTasks
{
    public abstract class BaseTask
    {
        protected EventQueueItem EventQueueItem;

        protected BaseTask(EventQueueItem item)
        {
            EventQueueItem = item;
        }

        public static async Task Go(DbConnection connection, EventQueueItem item, IServiceProvider provider)
        {
            var type = Type.GetType("Kroeg.Server.BackgroundTasks." + item.Action);

            var resolved = (BaseTask)ActivatorUtilities.CreateInstance(provider, type, item);

            try
            {
                await connection.OpenAsync();
                using (var trans = connection.BeginTransaction())
                {
                    await resolved.Go();

                    trans.Commit();
                }

                await connection.ExecuteAsync("DELETE from \"EventQueue\" where \"Id\" = @Id", new { Id = item.Id });
            }
            catch(Exception)
            {
                // failed
                item.AttemptCount++;
                item.NextAttempt = resolved.NextTry(item.AttemptCount);

                await connection.ExecuteAsync("UPDATE \"EventQueue\" set \"AttemptCount\"=@AttemptCount, \"NextAttempt\"=@NextAttempt where \"Id\" = @Id", new { Attemptcount = item.AttemptCount, NextAttempt = item.NextAttempt, Id = item.Id });
            }
        }

        public virtual DateTime NextTry(int fails)
        {
            return DateTime.Now.AddMinutes(fails * fails * 3);
        }

        public abstract Task Go();
    }

    public abstract class BaseTask<T, TR> : BaseTask where TR : BaseTask<T, TR>
    {
        protected T Data;

        public static async Task Make(T data, DbConnection connection, DateTime? nextAttempt = null)
        {
            var type = typeof(TR).FullName.Replace("Kroeg.Server.BackgroundTasks.", "");

            await connection.ExecuteAsync("insert into \"EventQueue\" (\"Action\", \"Added\", \"Data\", \"NextAttempt\") values (@Action, @Added, @Data, @NextAttempt)",
                new EventQueueItem
                {
                    Action = type,
                    Added = DateTime.Now,
                    Data = JsonConvert.SerializeObject(data),
                    NextAttempt = nextAttempt ?? DateTime.Now
                });
        }

        protected BaseTask(EventQueueItem item)
            : base(item)
        {
            Data = JsonConvert.DeserializeObject<T>(EventQueueItem.Data);
        }
    }
}
