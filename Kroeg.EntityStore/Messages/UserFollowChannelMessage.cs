namespace Kroeg.EntityStore.Messages
{
    public class UserFollowChannelMessage
    {
        public string FollowerId { get; set; }
        public string ActivityPubChannelId { get; set; }
    }
}
