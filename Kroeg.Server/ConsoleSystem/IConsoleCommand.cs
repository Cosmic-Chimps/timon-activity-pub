using System.Threading.Tasks;

namespace Kroeg.Server.ConsoleSystem
{
    public interface IConsoleCommand
    {
        Task Do(string[] args);
    }
}