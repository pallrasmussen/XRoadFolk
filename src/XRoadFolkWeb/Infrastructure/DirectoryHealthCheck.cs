using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;

namespace XRoadFolkWeb.Infrastructure;

/// <summary>
/// Health check that validates connectivity to a directory (Active Directory) using configured credentials.
/// Skips on non-Windows platforms.
/// </summary>
public sealed class DirectoryHealthCheck : IHealthCheck
{
    private readonly DirectoryLookupOptions _opts;
    private readonly ILogger<DirectoryHealthCheck> _log;
    public DirectoryHealthCheck(IOptions<DirectoryLookupOptions> opts, ILogger<DirectoryHealthCheck> log)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(log);
        _opts = opts.Value; _log = log; }

    /// <summary>
    /// Checks the health of the directory by validating credentials and directory accessibility.
    /// </summary>
    /// <param name="context">Health check context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health check result indicating whether the directory is healthy, unhealthy, or skipped.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.Domain))
        {
            return Task.FromResult(HealthCheckResult.Healthy("Directory domain not configured (skipping)."));
        }
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HealthCheckResult.Healthy("Directory check skipped (non-Windows)."));
        }
        try
        {
            bool ok = WinValidate();
            if (!ok)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Directory credential validation failed for domain {_opts.Domain}."));
            }
            return Task.FromResult(HealthCheckResult.Healthy($"Directory reachable ({_opts.Domain})."));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Directory health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("Directory connectivity failure", ex));
        }
    }

    [SupportedOSPlatform("windows")]
    private bool WinValidate()
    {
        using var ctx = string.IsNullOrWhiteSpace(_opts.Container)
            ? new PrincipalContext(ContextType.Domain, _opts.Domain)
            : new PrincipalContext(ContextType.Domain, _opts.Domain, _opts.Container);
        return ctx.ValidateCredentials(_opts.Username ?? Environment.UserName, _opts.Password ?? string.Empty, ContextOptions.Negotiate | ContextOptions.Signing | ContextOptions.Sealing);
    }
}
