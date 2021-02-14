using System.Collections.Generic;
using Kroeg.EntityStore.Models;

namespace Kroeg.Server.Tos.Models
{
  public class OAuthActorModel
  {
    public List<APEntity> Actors { get; set; }
    public APEntity Actor { get; set; }
    public string ActorID { get; set; }
    public string ResponseType { get; set; }
    public string RedirectUri { get; set; }
    public string State { get; set; }
    public int Expiry { get; set; }
    public string Deny { get; set; }
  }
}
