namespace Kroeg.Server.Tos.Request
{
  public class LoginRequest
  {
    public string Username { get; set; }
    public string Password { get; set; }
    public string Redirect { get; set; }
  }
}
