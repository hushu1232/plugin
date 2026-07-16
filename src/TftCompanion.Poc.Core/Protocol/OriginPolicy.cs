namespace TftCompanion.Poc.Core.Protocol;

public sealed record OriginVerification(
    bool Accepted,
    string FailureCode,
    bool ShouldPersistToDisk);

public sealed class OriginPolicy
{
    private readonly string allowedOrigin;

    public OriginPolicy(string allowedOrigin)
    {
        if (string.IsNullOrWhiteSpace(allowedOrigin))
        {
            throw new ArgumentException("A non-empty exact Overwolf origin is required.", nameof(allowedOrigin));
        }

        this.allowedOrigin = allowedOrigin;
    }

    public OriginVerification Verify(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return new OriginVerification(false, "ORIGIN_MISSING", false);
        }

        if (origin.Equals(allowedOrigin, StringComparison.Ordinal))
        {
            // Origin is never persisted to disk.
            return new OriginVerification(true, "NONE", false);
        }

        return new OriginVerification(false, "ORIGIN_REJECTED", false);
    }
}
