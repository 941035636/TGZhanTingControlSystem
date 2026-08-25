using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TG.Control.Server;

public sealed class AdminSessionStore
{
    private readonly AdminOptions options;
    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.Ordinal);

    public AdminSessionStore(IOptions<AdminOptions> options) => this.options = options.Value;

    public LoginResult? Login(string username, string password)
    {
        if (!SecureEquals(username, options.Username) || !SecureEquals(password, options.Password))
        {
            return null;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddHours(Math.Max(1, options.SessionHours));
        sessions[token] = new Session(options.Username, expiresAtUtc);
        return new LoginResult(token, options.Username, expiresAtUtc);
    }

    public bool TryValidate(HttpRequest request, out string username)
    {
        username = string.Empty;
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var token = authorization[7..].Trim();
        if (!sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            sessions.TryRemove(token, out _);
            return false;
        }

        username = session.Username;
        return true;
    }

    public void Logout(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            sessions.TryRemove(authorization[7..].Trim(), out _);
        }
    }

    private static bool SecureEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record Session(string Username, DateTimeOffset ExpiresAtUtc);
}

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResult(string Token, string Username, DateTimeOffset ExpiresAtUtc);
