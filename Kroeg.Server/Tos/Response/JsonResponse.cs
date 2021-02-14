using System.Collections.Generic;
using Kroeg.EntityStore.Models;

namespace Kroeg.Server.Tos.Response
{
  public class JsonResponse
  {
    public string access_token { get; set; }
    public string token_type { get; set; }
    public int expires_in { get; set; }
  }
}
