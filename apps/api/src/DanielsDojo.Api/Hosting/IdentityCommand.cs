using DanielsDojo.Infrastructure.Identity;

namespace DanielsDojo.Api.Hosting;

/// <summary>
/// Explicit operator entry point for the one privileged identity action Phase 3 supports:
/// granting the first administrator.
/// </summary>
/// <remarks>
/// This is deliberately not an HTTP endpoint. There is no API route that can grant Admin, so a
/// compromised session or a frontend bug cannot escalate anyone. The command runs off the API's
/// own dependency-injection composition and always exits instead of serving traffic.
/// </remarks>
internal static partial class IdentityCommand
{
    /// <summary>First argument that selects this command.</summary>
    public const string CommandName = "identity";

    private const string GrantAdminVerb = "grant-admin";
    private const string UserIdOption = "--user-id";
    private const string ReasonOption = "--reason";
    private const string ConfirmOption = "--confirm";

    /// <summary>Whether the supplied arguments select the identity command.</summary>
    public static bool Matches(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    /// <summary>Runs the requested identity operation and returns a process exit code.</summary>
    public static async Task<int> ExecuteAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length < 2 || !string.Equals(args[1], GrantAdminVerb, StringComparison.Ordinal))
        {
            WriteUsage(args.Length < 2 ? "A verb is required." : $"Unknown verb '{args[1]}'.");
            return 1;
        }

        if (!TryReadOption(args, UserIdOption, out string? userIdText)
            || !Guid.TryParse(userIdText, out Guid userId)
            || userId == Guid.Empty)
        {
            WriteUsage(
                $"{UserIdOption} must be the internal Daniel's Dojo user ID (a GUID). " +
                "An email address is deliberately not accepted: email is not the identity key " +
                "and could resolve to the wrong person.");
            return 1;
        }

        if (!TryReadOption(args, ReasonOption, out string? reason) || string.IsNullOrWhiteSpace(reason))
        {
            WriteUsage($"{ReasonOption} is required and must be a non-empty operator justification.");
            return 1;
        }

        if (!args.Contains(ConfirmOption, StringComparer.Ordinal))
        {
            WriteUsage(
                $"{ConfirmOption} is required. Granting Admin is a privileged, audited change; " +
                "rerun with the flag once you are certain of the target user ID.");
            return 1;
        }

        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IdentityCommand).FullName!);

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

        try
        {
            AdminRoleGrantService grantService =
                scope.ServiceProvider.GetRequiredService<AdminRoleGrantService>();

            string operatorContext =
                $"{Environment.UserName}@{Environment.MachineName}";
            string correlationId = Guid.NewGuid().ToString("N");

            AdminGrantResult result = await grantService
                .GrantAsync(userId, reason, operatorContext, correlationId)
                .ConfigureAwait(false);

            switch (result.Failure)
            {
                case AdminGrantFailure.UserNotFound:
                    await Console.Error.WriteLineAsync(
                        "No local user exists with that ID. Have the customer sign in once so the " +
                        "account is provisioned, then read the ID from /api/v1/auth/session.")
                        .ConfigureAwait(false);
                    return 1;

                case AdminGrantFailure.AdminRoleMissing:
                    await Console.Error.WriteLineAsync(
                        "The seeded Admin role is missing. Apply the reference seed first: " +
                        "dotnet run --project apps/api/src/DanielsDojo.Api -- database seed --profile reference")
                        .ConfigureAwait(false);
                    return 1;

                default:
                    break;
            }

            // Reports the outcome without echoing the reason, email, or display name.
            if (result.RoleWasAdded)
            {
                LogAdminGranted(logger, userId, result.AuditLogId!.Value);
                Console.WriteLine($"Admin role added to user {userId}. Audit record {result.AuditLogId}.");
            }
            else
            {
                LogAdminAlreadyHeld(logger, userId, result.AuditLogId!.Value);
                Console.WriteLine($"User {userId} already held the Admin role. Audit record {result.AuditLogId}.");
            }

            Console.WriteLine(
                "Paired operator step: add the same external identity to the Entra " +
                "'DanielsDojo-Admins-MFA' group so administrator sign-in is MFA-enforced. " +
                "This command does not call Microsoft Graph.");

            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await Console.Error.WriteLineAsync($"Identity command failed: {exception.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static bool TryReadOption(string[] args, string option, out string? value)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        value = args[index + 1];
        return true;
    }

    private static void WriteUsage(string problem)
    {
        Console.Error.WriteLine(problem);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine(
            "  identity grant-admin --user-id <guid> --reason \"<why>\" --confirm");
    }

    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Warning,
        Message = "Admin role granted to user {UserId}. Audit record {AuditLogId}.")]
    private static partial void LogAdminGranted(ILogger logger, Guid userId, Guid auditLogId);

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Information,
        Message = "User {UserId} already held the Admin role. Audit record {AuditLogId}.")]
    private static partial void LogAdminAlreadyHeld(ILogger logger, Guid userId, Guid auditLogId);
}
