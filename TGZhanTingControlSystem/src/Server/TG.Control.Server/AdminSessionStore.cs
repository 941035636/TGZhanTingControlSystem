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
        var account = options.Accounts.FirstOrDefault(item => SecureEquals(username, item.Username));
        IReadOnlyList<string> roles;
        string authenticatedUsername;
        if (account is not null && PasswordHasher.Verify(password, account.PasswordHash))
        {
            authenticatedUsername = account.Username;
            roles = NormalizeRoles(account.Roles);
        }
        else if (options.AllowLegacyPlaintextPassword && SecureEquals(username, options.Username) &&
                 SecureEquals(password, options.Password))
        {
            authenticatedUsername = options.Username;
            roles = [AdminRoles.Administrator];
        }
        else return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddHours(Math.Max(1, options.SessionHours));
        var principal = new AdminPrincipal(authenticatedUsername, roles);
        sessions[token] = new Session(principal, expiresAtUtc);
        return new LoginResult(token, authenticatedUsername, roles, expiresAtUtc);
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

        username = session.Principal.Username;
        return true;
    }

    public bool TryGetPrincipal(HttpRequest request, out AdminPrincipal principal)
    {
        principal = AdminPrincipal.Empty;
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var token = authorization[7..].Trim();
        if (!sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            sessions.TryRemove(token, out _);
            return false;
        }
        principal = session.Principal;
        return true;
    }

    public bool TryAuthorize(HttpRequest request, string role, out AdminPrincipal principal, out bool authenticated)
    {
        authenticated = TryGetPrincipal(request, out principal);
        return authenticated && principal.IsInRole(role);
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

    private static IReadOnlyList<string> NormalizeRoles(IReadOnlyList<string>? roles)
    {
        var normalized = (roles ?? []).Where(AdminRoles.IsKnown).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return normalized.Length == 0 ? [AdminRoles.Viewer] : normalized;
    }

    private sealed record Session(AdminPrincipal Principal, DateTimeOffset ExpiresAtUtc);
}

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResult(string Token, string Username, IReadOnlyList<string> Roles, DateTimeOffset ExpiresAtUtc);
public sealed record AdminPrincipal(string Username, IReadOnlyList<string> Roles)
{
    public static AdminPrincipal Empty { get; } = new(string.Empty, []);
    public bool IsInRole(string role) => Roles.Contains(AdminRoles.Administrator, StringComparer.OrdinalIgnoreCase) ||
                                         Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public static class AdminRoles
{
    public const string Administrator = "Administrator";
    public const string Publisher = "Publisher";
    public const string Editor = "Editor";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static bool IsKnown(string role) => new[] { Administrator, Publisher, Editor, Operator, Viewer }
        .Contains(role, StringComparer.OrdinalIgnoreCase);
}
