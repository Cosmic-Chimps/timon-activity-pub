namespace Kroeg.Server.Models
{
    public class UserActorPermission
    {
        public int UserActorPermissionId { get; set; }

        public APUser User { get; set; }
        public string UserId { get; set; }

        public int ActorId { get; set; }
        public APTripleEntity Actor { get; set; }

        public bool IsAdmin { get; set; }
    }
}
