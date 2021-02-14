namespace Kroeg.Server.Tos.Request
{
    public class RegisterRequest
    {
      public string Username { get; set; }
      public string Password { get; set; }
      public string VerifyPassword { get; set; }
      public string Email { get; set; }
      public string Redirect { get; set; }
    }
}
