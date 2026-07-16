using TftCompanion.Poc.Core.Protocol;

namespace TftCompanion.Poc.Host;

/// <summary>
/// Host-only configuration. Production creation is fail-closed: the Bridge and
/// Host must share the same exact loopback endpoint, origin and pairing proof.
/// </summary>
public sealed record PocHostOptions(
    int Port,
    string AllowedOrigin,
    string PairingToken)
{
    public static bool TryCreateForProduction(
        IReadOnlyList<string> args,
        out PocHostOptions? options,
        out string failureCode)
    {
        options = null;
        int port = ProtocolConstants.DefaultPort;

        for (int index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], "--port", StringComparison.Ordinal))
            {
                failureCode = "ARGUMENT_REJECTED";
                return false;
            }

            if (index + 1 >= args.Count ||
                !int.TryParse(args[++index], out port) ||
                port != ProtocolConstants.DefaultPort)
            {
                failureCode = "PORT_REJECTED";
                return false;
            }
        }

        string? allowedOrigin = Environment.GetEnvironmentVariable("TFT_COMPANION_POC_ALLOWED_ORIGIN");
        string? pairingToken = Environment.GetEnvironmentVariable("TFT_COMPANION_POC_PAIRING_TOKEN");
        if (string.IsNullOrWhiteSpace(allowedOrigin))
        {
            failureCode = "ALLOWED_ORIGIN_MISSING";
            return false;
        }

        if (!PairingTokenValidator.IsValid(pairingToken ?? string.Empty))
        {
            failureCode = "PAIRING_TOKEN_INVALID";
            return false;
        }

        options = new PocHostOptions(port, allowedOrigin, pairingToken!);
        failureCode = "NONE";
        return true;
    }
}
