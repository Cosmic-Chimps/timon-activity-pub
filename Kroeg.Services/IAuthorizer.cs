using System.Threading.Tasks;
using Kroeg.EntityStore.Models;

namespace Kroeg.Services
{
  public interface IAuthorizer
  {
    bool VerifyAccess(APEntity entity, string userId);
  }
}
