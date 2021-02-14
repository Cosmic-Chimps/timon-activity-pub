using System.Collections.Generic;
using Kroeg.EntityStore.Models;

namespace Kroeg.Server.Tos.Models
{
  public class OAuthTokenModel
  {
    public string grant_type { get; set; }
    public string code { get; set; }
    public string redirect_uri { get; set; }
    public string client_id { get; set; }
  }
}
