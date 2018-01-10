using System.Threading.Tasks;
using Kroeg.EntityStore.Models;

namespace Kroeg.Services
{
    public interface IAuthorizer
    {
        Task<bool> VerifyAccess(APEntity entity, string userId);
    }
}