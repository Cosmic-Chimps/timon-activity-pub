using System.Threading.Tasks;
using Kroeg.Server.Models;

namespace Kroeg.Server.Services
{
    public interface IAuthorizer
    {
        Task<bool> VerifyAccess(APEntity entity, string userId);
    }
}