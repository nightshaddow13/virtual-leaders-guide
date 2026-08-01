// Creates one local-Identity account by POSTing directly to Api's internal identity endpoint
// (InternalIdentityEndpoints.cs), with a real ASP.NET Core Identity password hash so the account can
// actually sign in afterward. Deliberately a separate, unshipped tool rather than app code - see ADR-0023
// and the P2-2 (#11) plan's Scope section: bootstrapping "how does a fresh environment get its first
// Admin" is P2-4's job (#13), not something this tool or P2-2 invents a second answer for. This exists
// purely to unblock local development/testing before P2-3/P2-4/P2-12 land.
//
// Prerequisite: the AppHost must be running (dotnet run --project src/VirtualLeadersGuide.AppHost, or your
// IDE's debug session) so Api is reachable.
//
// Usage:
//   dotnet run --project tools/VirtualLeadersGuide.Tools.SeedUser -- --api-key <key> [--email <email>] [--api-url <url>]
// Example:
//   dotnet run --project tools/VirtualLeadersGuide.Tools.SeedUser -- --api-key local-dev-key --email xgoss@live.com
//
// The password is always prompted for interactively (masked) - never pass it as a command-line argument,
// or it ends up in shell history.

using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Identity.Contracts;

Dictionary<string, string> options = ParseOptions(args);

if (options.ContainsKey("help"))
{
    PrintUsage();
    return 0;
}

if (!options.TryGetValue("api-key", out string? apiKey))
{
    Console.Error.WriteLine("Missing required --api-key.");
    Console.Error.WriteLine(
        "Find it with: dotnet user-secrets list --project src/VirtualLeadersGuide.AppHost " +
        "(look for Parameters:internal-api-key).");
    Console.Error.WriteLine();
    PrintUsage();
    return 1;
}

string apiBaseUrl = options.GetValueOrDefault("api-url", "http://localhost:5058");

string? email = options.GetValueOrDefault("email");
if (string.IsNullOrWhiteSpace(email))
{
    Console.Write("Email: ");
    email = Console.ReadLine();
}

if (string.IsNullOrWhiteSpace(email))
{
    Console.Error.WriteLine("Email cannot be empty.");
    return 1;
}

Console.Write($"Password for {email}: ");
string password = ReadMaskedLine();
Console.WriteLine();

if (string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("Password cannot be empty.");
    return 1;
}

string normalizedEmail = email.ToUpperInvariant();
var user = new IdentityUserDto
{
    Id = Guid.NewGuid().ToString(),
    UserName = email,
    NormalizedUserName = normalizedEmail,
    Email = email,
    NormalizedEmail = normalizedEmail,
    EmailConfirmed = true,
    PasswordHash = new PasswordHasher<object>().HashPassword(new object(), password),
    SecurityStamp = Guid.NewGuid().ToString(),
    ConcurrencyStamp = Guid.NewGuid().ToString(),
    LockoutEnabled = true
};

using var client = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
client.DefaultRequestHeaders.Add("X-Internal-Key", apiKey);

HttpResponseMessage response = await client.PostAsJsonAsync(InternalIdentityRoutes.ForUsers(), user);

if (response.IsSuccessStatusCode)
{
    Console.WriteLine($"Created {email} (id {user.Id}). Sign in at /Account/Login.");
    return 0;
}

Console.Error.WriteLine($"Failed: {(int)response.StatusCode} {response.StatusCode}");
Console.Error.WriteLine(await response.Content.ReadAsStringAsync());
return 1;

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] is "-h" or "--help")
        {
            result["help"] = "true";
            continue;
        }

        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        string key = args[i][2..];
        string value = i + 1 < args.Length ? args[++i] : "";
        result[key] = value;
    }

    return result;
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project tools/VirtualLeadersGuide.Tools.SeedUser --");
    Console.WriteLine("           --api-key <internal-api-key> [--email <email>] [--api-url <url>]");
    Console.WriteLine();
    Console.WriteLine("  --api-key   Required. Matches Parameters:internal-api-key in AppHost's user-secrets.");
    Console.WriteLine("  --email     Optional. Prompted for if omitted.");
    Console.WriteLine("  --api-url   Optional. Defaults to http://localhost:5058 (Api's local launch profile port).");
    Console.WriteLine();
    Console.WriteLine("The password is always prompted for interactively, masked.");
}

static string ReadMaskedLine()
{
    var chars = new List<char>();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (chars.Count > 0)
            {
                chars.RemoveAt(chars.Count - 1);
                Console.Write("\b \b");
            }
            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            chars.Add(key.KeyChar);
            Console.Write('*');
        }
    }

    return new string([.. chars]);
}
