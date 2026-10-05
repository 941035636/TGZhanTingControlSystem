using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TG.Control.Server;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

var hash = PasswordHasher.Hash("correct horse battery staple");
Check("PBKDF2 hash verifies the original password", PasswordHasher.Verify("correct horse battery staple", hash));
Check("PBKDF2 hash rejects a wrong password", !PasswordHasher.Verify("wrong", hash));
Check("Malformed password hash is rejected", !PasswordHasher.Verify("password", "$pbkdf2-sha256$bad"));
Check("Installer PBKDF2 format is Server-compatible", PasswordHasher.Verify("deployment-test",
    "$pbkdf2-sha256$210000$AAECAwQFBgcICQoLDA0ODw==$v5DsLq6FKrX6JEosJSlPjFwz9iA+A+12MbvZ3TkcMKU="));

var store = new AdminSessionStore(Options.Create(new AdminOptions
{
    AllowLegacyPlaintextPassword = false,
    Username = "legacy",
    Password = "legacy-password",
    Accounts =
    [
        new AdminAccountOptions { Username = "editor", PasswordHash = PasswordHasher.Hash("editor-password"), Roles = [AdminRoles.Editor] },
        new AdminAccountOptions { Username = "operator", PasswordHash = PasswordHasher.Hash("operator-password"), Roles = [AdminRoles.Operator] }
    ]
}));
Check("Legacy plaintext login is disabled in production", store.Login("legacy", "legacy-password") is null);
var editorLogin = store.Login("editor", "editor-password");
Check("Configured hashed account can log in", editorLogin is not null);
Check("Login returns account roles", editorLogin!.Roles.SequenceEqual([AdminRoles.Editor]));

var request = new DefaultHttpContext().Request;
request.Headers.Authorization = $"Bearer {editorLogin.Token}";
Check("Editor session authorizes Editor role", store.TryAuthorize(request, AdminRoles.Editor, out var editor, out var authenticated) && authenticated && editor.Username == "editor");
Check("Editor session cannot authorize Publisher role", !store.TryAuthorize(request, AdminRoles.Publisher, out _, out authenticated) && authenticated);

var legacyStore = new AdminSessionStore(Options.Create(new AdminOptions
{
    AllowLegacyPlaintextPassword = true,
    Username = "development",
    Password = "development-only"
}));
Check("Explicit development compatibility enables legacy login", legacyStore.Login("development", "development-only")?.Roles.Contains(AdminRoles.Administrator) == true);

Console.WriteLine($"Deployment security regression passed: {passed}/10.");
