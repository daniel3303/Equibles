using System.Security.Cryptography;

namespace Equibles.Web.Authentication;

public class AuthSettings {
    public string Username { get; set; }
    public string Password { get; set; }
    public string SessionSecret { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public bool IsEnabled => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
}
