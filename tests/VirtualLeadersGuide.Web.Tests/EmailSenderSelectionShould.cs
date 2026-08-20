using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VirtualLeadersGuide.Web.Identity;

namespace VirtualLeadersGuide.Web.Tests;

/// <remarks>
/// Exercises <c>EmailSenderRegistration.AddConfiguredEmailSender</c>'s fail-closed provider selection
/// (P2.1-4, #62; ADR-0032) - the two-layer guard (<c>Email:FileSinkAllowed</c> plus <c>IsProduction()</c>)
/// that stops a real deployed environment from silently dropping password-reset emails into a file no one
/// reads. <c>Program.cs</c> reads <c>Email:Provider</c>/<c>Email:FileSinkAllowed</c>/
/// <c>ASPNETCORE_ENVIRONMENT</c> as part of top-level statement execution, so - like
/// <c>ConnectionStrings:blobs</c>, see <see cref="DashboardShould"/>'s remarks - these have to be set as
/// environment variables before the <see cref="WebApplicationFactory{TEntryPoint}"/> itself is constructed,
/// not merely before <c>.Services</c> is first touched. <see cref="BuildFactory"/> centralizes that ordering.
/// </remarks>
public class EmailSenderSelectionShould : IAsyncLifetime
{
    private readonly string _dataProtectionKeysDirectory =
        Path.Combine(Path.GetTempPath(), "vlg-web-tests-keys-" + Guid.NewGuid());

    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__blobs",
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__blobs", null);
        Environment.SetEnvironmentVariable("Email__Provider", null);
        Environment.SetEnvironmentVariable("Email__FileSinkAllowed", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);

        if (Directory.Exists(_dataProtectionKeysDirectory))
        {
            Directory.Delete(_dataProtectionKeysDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public void ResolveAcsEmailSender_WhenProviderIsUnset_ForAddConfiguredEmailSender()
    {
        using WebApplicationFactory<Program> factory = BuildFactory(provider: null, fileSinkAllowed: null);
        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender<ApplicationUser>>();

        Assert.IsType<AcsEmailSender>(sender);
    }

    [Fact]
    public void ResolveFileSinkEmailSender_WhenProviderIsFileSinkAndAllowed_ForAddConfiguredEmailSender()
    {
        using WebApplicationFactory<Program> factory = BuildFactory(provider: "FileSink", fileSinkAllowed: true);
        using IServiceScope scope = factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender<ApplicationUser>>();

        Assert.IsType<FileSinkEmailSender>(sender);
    }

    [Fact]
    public void ThrowOnBuild_WhenProviderIsFileSinkButNotAllowed_ForAddConfiguredEmailSender()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BuildFactory(provider: "FileSink", fileSinkAllowed: null));

        Assert.Contains("Email:FileSinkAllowed", exception.Message);
    }

    [Fact]
    public void ThrowOnBuild_WhenProviderIsUnrecognized_ForAddConfiguredEmailSender()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BuildFactory(provider: "Sendgrid", fileSinkAllowed: null));

        Assert.Contains("Email:Provider", exception.Message);
    }

    [Fact]
    public void ThrowOnBuild_WhenProviderIsFileSinkAndAllowedButEnvironmentIsProduction_ForAddConfiguredEmailSender()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BuildFactory(provider: "FileSink", fileSinkAllowed: true, production: true));

        Assert.Contains("Production", exception.Message);
    }

    /// <remarks>
    /// Forces the host to build immediately, inside this call, by touching <c>.Services</c> while the
    /// environment variables set here are still in scope (see this class's own remarks). A build failure
    /// surfaces here, possibly wrapped by reflection/host-building machinery -
    /// <see cref="UnwrapInvalidOperationException"/> recovers the real <see cref="InvalidOperationException"/>
    /// <c>EmailSenderRegistration</c> throws, rather than callers asserting on whatever wrapper type happens
    /// to carry it. <paramref name="production"/> is applied via the raw <c>ASPNETCORE_ENVIRONMENT</c>
    /// variable rather than <c>IWebHostBuilder.UseEnvironment</c> - the same reasoning as
    /// <c>ConnectionStrings:blobs</c> above: <c>WebApplication.CreateBuilder</c> reads it from the process
    /// environment on its very first line, before <c>WithWebHostBuilder</c>'s own hooks would apply.
    /// </remarks>
    private WebApplicationFactory<Program> BuildFactory(string? provider, bool? fileSinkAllowed, bool production = false)
    {
        Environment.SetEnvironmentVariable("Email__Provider", provider);
        Environment.SetEnvironmentVariable(
            "Email__FileSinkAllowed", fileSinkAllowed is null ? null : fileSinkAllowed.Value ? "true" : "false");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", production ? "Production" : null);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
                services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_dataProtectionKeysDirectory)));
        });

        try
        {
            _ = factory.Services;
        }
        catch (Exception ex)
        {
            factory.Dispose();
            throw UnwrapInvalidOperationException(ex);
        }

        return factory;
    }

    private static Exception UnwrapInvalidOperationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException)
            {
                return current;
            }
        }

        return exception;
    }
}
