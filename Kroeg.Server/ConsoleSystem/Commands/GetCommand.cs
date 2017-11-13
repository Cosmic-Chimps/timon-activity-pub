using System;
using System.Threading.Tasks;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.ConsoleSystem.Commands
{
    public class GetCommand : IConsoleCommand
    {
        private readonly IEntityStore _entityStore;

        public GetCommand(IEntityStore entityStore)
        {
            _entityStore = entityStore;
        }

        public async Task Do(string[] args)
        {
            foreach (var item in args)
            {
                Console.WriteLine($"--- {item} ---");
                var data = await _entityStore.GetEntity(item, true);
                if (data == null)
                {
                    Console.WriteLine("not found");
                    continue;
                }

                Console.WriteLine($"--- IsOwner: {data.IsOwner}, LastUpdate: {data.Updated}, Type: {data.Type}");
                Console.WriteLine(data.Data.Serialize(true).ToString());
                Console.WriteLine("--- ---");
            }
        }
    }
}