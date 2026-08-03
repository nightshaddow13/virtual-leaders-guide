using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using VirtualLeadersGuide.Identity.Contracts;

namespace VirtualLeadersGuide.Web.Identity;

// IUserStore<ApplicationUser> implemented as thin HTTP calls to Api's internal identity endpoints, rather
// than a local IdentityDbContext - see ADR-0022 for why. Only FindById/FindByName/FindByEmail, Create,
// Update, and Delete actually cross the wire; every other interface here (IUserPasswordStore,
// IUserEmailStore, IUserSecurityStampStore, IUserLockoutStore, IUserPhoneNumberStore) is pure get/set on
// the in-memory ApplicationUser instance a FindBy*/CreateAsync call already produced - that's the whole
// reason one CRUD-by-user endpoint set on Api can back all of them. Deliberately does NOT implement
// IUserTwoFactorStore - see ADR-0022's Consequences and issue #54.
public sealed class ApiUserStore(IHttpClientFactory httpClientFactory, IdentityErrorDescriber? describer = null) :
    IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserPhoneNumberStore<ApplicationUser>
{
    private readonly IdentityErrorDescriber _describer = describer ?? new IdentityErrorDescriber();

    public void Dispose()
    {
        // No HttpClient is held onto - one is requested from IHttpClientFactory per call - so there is
        // nothing here to dispose.
    }

    // ---- IUserStore: the only methods that actually reach Api ----

    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        await FindUserAsync(InternalIdentityRoutes.ForUserById(userId), cancellationToken);

    public async Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        await FindUserAsync(
            InternalIdentityRoutes.ForUserByNormalizedUserName(normalizedUserName), cancellationToken);

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, InternalIdentityRoutes.ForUsers())
        {
            Content = JsonContent.Create(ToDto(user))
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return IdentityResult.Failed(_describer.DuplicateUserName(user.UserName ?? user.Email ?? user.Id));
        }

        EnsureExpectedStatus(response, HttpStatusCode.Created);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put, InternalIdentityRoutes.ForUserById(user.Id))
        {
            Content = JsonContent.Create(ToDto(user))
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return IdentityResult.Failed(_describer.ConcurrencyFailure());
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);

        // Api generated a fresh ConcurrencyStamp when it wrote the row - reflect it back onto the caller's
        // instance, matching the stock EF Core Identity UserStore's behavior (its UpdateAsync mutates
        // user.ConcurrencyStamp in place before saving), so a subsequent update in the same request/circuit
        // uses the current stamp rather than immediately conflicting with itself.
        IdentityUserDto? updated = await response.Content.ReadFromJsonAsync<IdentityUserDto>(cancellationToken);
        if (updated is not null)
        {
            user.ConcurrencyStamp = updated.ConcurrencyStamp;
        }

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete, InternalIdentityRoutes.ForUserById(user.Id));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        EnsureExpectedStatus(response, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
        return IdentityResult.Success;
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(
        ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    // ---- IUserPasswordStore: in-memory only ----

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // ---- IUserEmailStore: FindByEmailAsync reaches Api; everything else is in-memory ----

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await FindUserAsync(InternalIdentityRoutes.ForUserByNormalizedEmail(normalizedEmail), cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(
        ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // ---- IUserSecurityStampStore: in-memory only ----

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    // ---- IUserLockoutStore: in-memory only ----

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(
        ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // ---- IUserPhoneNumberStore: in-memory only ----

    public Task SetPhoneNumberAsync(ApplicationUser user, string? phoneNumber, CancellationToken cancellationToken)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(
        ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // ---- HTTP plumbing ----

    private async Task<ApplicationUser?> FindUserAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureExpectedStatus(response, HttpStatusCode.OK);
        IdentityUserDto? dto = await response.Content.ReadFromJsonAsync<IdentityUserDto>(cancellationToken);
        return dto is null ? null : FromDto(dto);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient("Api");

        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Retries/circuit-breaking already happened inside the "Api" HttpClient's standard resilience
            // handler (see ServiceDefaults/Extensions.cs) - reaching here means those were exhausted, or
            // Api is genuinely unreachable. Wrapped rather than left as a bare transport exception, and
            // deliberately not caught anywhere upstream that would turn it into a null/"not found" result -
            // see IdentityStoreUnavailableException.
            throw new IdentityStoreUnavailableException("The identity store (Api) is unreachable.", ex);
        }
    }

    private static void EnsureExpectedStatus(HttpResponseMessage response, params ReadOnlySpan<HttpStatusCode> expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new IdentityStoreUnavailableException(
                $"The identity store (Api) returned an unexpected {(int)response.StatusCode} response.",
                new HttpRequestException(response.ReasonPhrase));
        }
    }

    private static IdentityUserDto ToDto(ApplicationUser user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        NormalizedUserName = user.NormalizedUserName,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        EmailConfirmed = user.EmailConfirmed,
        DisplayName = user.DisplayName,
        PasswordHash = user.PasswordHash,
        SecurityStamp = user.SecurityStamp,
        ConcurrencyStamp = user.ConcurrencyStamp
            ?? throw new InvalidOperationException("ConcurrencyStamp must always be set."),
        PhoneNumber = user.PhoneNumber,
        PhoneNumberConfirmed = user.PhoneNumberConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled,
        LockoutEnd = user.LockoutEnd,
        LockoutEnabled = user.LockoutEnabled,
        AccessFailedCount = user.AccessFailedCount
    };

    private static ApplicationUser FromDto(IdentityUserDto dto) => new()
    {
        Id = dto.Id,
        UserName = dto.UserName,
        NormalizedUserName = dto.NormalizedUserName,
        Email = dto.Email,
        NormalizedEmail = dto.NormalizedEmail,
        EmailConfirmed = dto.EmailConfirmed,
        DisplayName = dto.DisplayName,
        PasswordHash = dto.PasswordHash,
        SecurityStamp = dto.SecurityStamp,
        ConcurrencyStamp = dto.ConcurrencyStamp,
        PhoneNumber = dto.PhoneNumber,
        PhoneNumberConfirmed = dto.PhoneNumberConfirmed,
        TwoFactorEnabled = dto.TwoFactorEnabled,
        LockoutEnd = dto.LockoutEnd,
        LockoutEnabled = dto.LockoutEnabled,
        AccessFailedCount = dto.AccessFailedCount
    };
}
