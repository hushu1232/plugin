using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.SecondScreen.Recovery;

public sealed class ManualSessionRecoveryCodec
{
    private const string FixturePackVersion = "fixture-v1";
    private const string LossStreakFixtureScenarioId = "loss-streak-review-v1";
    private const string RerollFixtureScenarioId = "reroll-review-v1";
    private static readonly JsonWriterOptions CanonicalWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    public byte[] Encode(ManualSessionRecoveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte[] canonicalPayload = EncodeCanonicalPayload(payload);
        string digest = ComputeDigest(canonicalPayload);
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer, CanonicalWriterOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("payload");
            WriteCanonicalPayload(writer, payload);
            writer.WriteString("canonicalPayloadDigest", digest);
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public bool TryDecode(
        ReadOnlySpan<byte> utf8,
        out ManualSessionRecoveryPayload? payload,
        out string failureCode)
    {
        payload = null;
        failureCode = "EMPTY_DOCUMENT";

        if (utf8.IsEmpty)
        {
            return false;
        }

        if (utf8.Length > ManualSessionRecoveryContract.MaximumSnapshotBytes)
        {
            failureCode = "DOCUMENT_TOO_LARGE";
            return false;
        }

        try
        {
            Utf8JsonReader reader = new(
                utf8,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });

            if (!reader.Read())
            {
                failureCode = "ROOT_NOT_OBJECT";
                return false;
            }

            return TryReadDocument(ref reader, out payload, out failureCode);
        }
        catch (JsonException)
        {
            payload = null;
            failureCode = "MALFORMED_JSON";
            return false;
        }
        catch (FormatException)
        {
            payload = null;
            failureCode = "MALFORMED_VALUE";
            return false;
        }
        catch (ArgumentException)
        {
            payload = null;
            failureCode = "MALFORMED_VALUE";
            return false;
        }
        catch (InvalidOperationException)
        {
            payload = null;
            failureCode = "MALFORMED_VALUE";
            return false;
        }
    }

    private static bool TryReadDocument(
        ref Utf8JsonReader reader,
        out ManualSessionRecoveryPayload? payload,
        out string failureCode)
    {
        payload = null;
        failureCode = "ROOT_NOT_OBJECT";

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        bool hasPayload = false;
        bool hasDigest = false;
        bool reachedRootEnd = false;
        ManualSessionRecoveryPayload? parsedPayload = null;
        string? providedDigest = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                reachedRootEnd = true;
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                failureCode = "ROOT_PROPERTY_EXPECTED";
                return false;
            }

            string propertyName = reader.GetString() ?? string.Empty;

            switch (propertyName)
            {
                case "payload":
                    if (hasPayload)
                    {
                        failureCode = "DUPLICATE_ROOT_PROPERTY";
                        return false;
                    }

                    if (!reader.Read())
                    {
                        failureCode = "ROOT_VALUE_MISSING";
                        return false;
                    }

                    if (!TryReadPayload(ref reader, out parsedPayload, out failureCode))
                    {
                        payload = null;
                        return false;
                    }

                    hasPayload = true;
                    break;

                case "canonicalPayloadDigest":
                    if (hasDigest)
                    {
                        failureCode = "DUPLICATE_ROOT_PROPERTY";
                        return false;
                    }

                    if (!reader.Read())
                    {
                        failureCode = "ROOT_VALUE_MISSING";
                        return false;
                    }

                    if (!TryReadString(ref reader, out string digest))
                    {
                        failureCode = "ROOT_PROPERTY_TYPE";
                        return false;
                    }

                    providedDigest = digest;
                    hasDigest = true;
                    break;

                default:
                    failureCode = "UNKNOWN_ROOT_PROPERTY";
                    return false;
            }
        }

        if (!reachedRootEnd)
        {
            failureCode = "ROOT_END_MISSING";
            return false;
        }

        if (reader.Read())
        {
            failureCode = "TRAILING_CONTENT";
            return false;
        }

        if (!hasPayload || !hasDigest || parsedPayload is null || providedDigest is null)
        {
            failureCode = "MISSING_ROOT_PROPERTY";
            return false;
        }

        if (!TryValidatePayload(parsedPayload, out failureCode))
        {
            return false;
        }

        if (!IsUppercaseSha256(providedDigest))
        {
            failureCode = "DIGEST_FORMAT_INVALID";
            return false;
        }

        string expectedDigest = ComputeDigest(EncodeCanonicalPayload(parsedPayload));

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(providedDigest),
                Encoding.ASCII.GetBytes(expectedDigest)))
        {
            failureCode = "DIGEST_MISMATCH";
            return false;
        }

        payload = parsedPayload;
        failureCode = "NONE";
        return true;
    }

    private static bool TryReadPayload(
        ref Utf8JsonReader reader,
        out ManualSessionRecoveryPayload? payload,
        out string failureCode)
    {
        payload = null;
        failureCode = "PAYLOAD_NOT_OBJECT";

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        bool hasSchemaVersion = false;
        bool hasSnapshotGeneration = false;
        bool hasManualRunId = false;
        bool hasHighestRevision = false;
        bool hasSessionPhase = false;
        bool hasFixtureScenarioId = false;
        bool hasTopic = false;
        bool hasIntent = false;
        bool hasHealthBand = false;
        bool hasGoldBand = false;
        bool hasCopiesBand = false;
        bool hasUnitCostBand = false;
        bool hasProvenance = false;
        bool hasFixturePackVersion = false;
        bool hasCreatedAt = false;
        bool hasExpiresAt = false;
        bool reachedPayloadEnd = false;

        string schemaVersion = string.Empty;
        long snapshotGeneration = default;
        Guid manualRunId = default;
        long highestRevision = default;
        ManualSessionPhase sessionPhase = default;
        string fixtureScenarioId = string.Empty;
        ManualTopic topic = default;
        ManualIntent intent = default;
        ManualRiskBand healthBand = default;
        ManualRiskBand goldBand = default;
        ManualCopiesBand copiesBand = default;
        ManualUnitCostBand unitCostBand = default;
        LocalFactProvenance provenance = default;
        string fixturePackVersion = string.Empty;
        DateTimeOffset createdAt = default;
        DateTimeOffset expiresAt = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                reachedPayloadEnd = true;
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                failureCode = "PAYLOAD_PROPERTY_EXPECTED";
                return false;
            }

            string propertyName = reader.GetString() ?? string.Empty;

            if (!reader.Read())
            {
                failureCode = "PAYLOAD_VALUE_MISSING";
                return false;
            }

            switch (propertyName)
            {
                case "schemaVersion":
                    if (hasSchemaVersion)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadString(ref reader, out schemaVersion))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasSchemaVersion = true;
                    break;

                case "snapshotGeneration":
                    if (hasSnapshotGeneration)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadInt64(ref reader, out snapshotGeneration))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasSnapshotGeneration = true;
                    break;

                case "manualRunId":
                    if (hasManualRunId)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadGuid(ref reader, out manualRunId))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasManualRunId = true;
                    break;

                case "highestRevision":
                    if (hasHighestRevision)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadInt64(ref reader, out highestRevision))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasHighestRevision = true;
                    break;

                case "sessionPhase":
                    if (hasSessionPhase)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out sessionPhase))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasSessionPhase = true;
                    break;

                case "fixtureScenarioId":
                    if (hasFixtureScenarioId)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadString(ref reader, out fixtureScenarioId))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasFixtureScenarioId = true;
                    break;

                case "topic":
                    if (hasTopic)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out topic))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasTopic = true;
                    break;

                case "intent":
                    if (hasIntent)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out intent))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasIntent = true;
                    break;

                case "healthBand":
                    if (hasHealthBand)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out healthBand))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasHealthBand = true;
                    break;

                case "goldBand":
                    if (hasGoldBand)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out goldBand))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasGoldBand = true;
                    break;

                case "copiesBand":
                    if (hasCopiesBand)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out copiesBand))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasCopiesBand = true;
                    break;

                case "unitCostBand":
                    if (hasUnitCostBand)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out unitCostBand))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasUnitCostBand = true;
                    break;

                case "provenance":
                    if (hasProvenance)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadEnum(ref reader, out provenance))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasProvenance = true;
                    break;

                case "fixturePackVersion":
                    if (hasFixturePackVersion)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadString(ref reader, out fixturePackVersion))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasFixturePackVersion = true;
                    break;

                case "createdAt":
                    if (hasCreatedAt)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadDateTimeOffset(ref reader, out createdAt))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasCreatedAt = true;
                    break;

                case "expiresAt":
                    if (hasExpiresAt)
                    {
                        failureCode = "DUPLICATE_PAYLOAD_PROPERTY";
                        return false;
                    }

                    if (!TryReadDateTimeOffset(ref reader, out expiresAt))
                    {
                        failureCode = "PAYLOAD_FIELD_TYPE";
                        return false;
                    }

                    hasExpiresAt = true;
                    break;

                default:
                    failureCode = "UNKNOWN_PAYLOAD_PROPERTY";
                    return false;
            }
        }

        if (!reachedPayloadEnd)
        {
            failureCode = "PAYLOAD_END_MISSING";
            return false;
        }

        if (!hasSchemaVersion ||
            !hasSnapshotGeneration ||
            !hasManualRunId ||
            !hasHighestRevision ||
            !hasSessionPhase ||
            !hasFixtureScenarioId ||
            !hasTopic ||
            !hasIntent ||
            !hasHealthBand ||
            !hasGoldBand ||
            !hasCopiesBand ||
            !hasUnitCostBand ||
            !hasProvenance ||
            !hasFixturePackVersion ||
            !hasCreatedAt ||
            !hasExpiresAt)
        {
            failureCode = "MISSING_PAYLOAD_PROPERTY";
            return false;
        }

        payload = new ManualSessionRecoveryPayload(
            schemaVersion,
            snapshotGeneration,
            manualRunId,
            highestRevision,
            sessionPhase,
            fixtureScenarioId,
            topic,
            intent,
            healthBand,
            goldBand,
            copiesBand,
            unitCostBand,
            provenance,
            fixturePackVersion,
            createdAt,
            expiresAt);
        failureCode = "NONE";
        return true;
    }

    private static bool TryValidatePayload(ManualSessionRecoveryPayload payload, out string failureCode)
    {
        if (!string.Equals(payload.SchemaVersion, ManualSessionRecoveryContract.SchemaVersion, StringComparison.Ordinal))
        {
            failureCode = "SCHEMA_VERSION_INVALID";
            return false;
        }

        if (payload.SnapshotGeneration <= 0)
        {
            failureCode = "SNAPSHOT_GENERATION_INVALID";
            return false;
        }

        if (!string.Equals(payload.FixturePackVersion, FixturePackVersion, StringComparison.Ordinal))
        {
            failureCode = "FIXTURE_PACK_VERSION_INVALID";
            return false;
        }

        if (payload.Provenance != LocalFactProvenance.UserEntered)
        {
            failureCode = "PROVENANCE_INVALID";
            return false;
        }

        switch (payload.SessionPhase)
        {
            case ManualSessionPhase.NoSession:
                return TryValidateNoSession(payload, out failureCode);

            case ManualSessionPhase.EditingCheckpoint:
                return TryValidateEditingCheckpoint(payload, out failureCode);

            case ManualSessionPhase.CurrentAdvice:
                return TryValidateCurrentAdvice(payload, out failureCode);

            case ManualSessionPhase.Cleared:
            case ManualSessionPhase.Expired:
                return TryValidateTerminalSnapshot(payload, out failureCode);

            default:
                failureCode = "SESSION_PHASE_INVALID";
                return false;
        }
    }

    private static bool TryValidateNoSession(ManualSessionRecoveryPayload payload, out string failureCode)
    {
        if (payload.ManualRunId != Guid.Empty || payload.HighestRevision != 0)
        {
            failureCode = "NO_SESSION_RUN_OR_REVISION_INVALID";
            return false;
        }

        if (payload.Intent != ManualIntent.Review ||
            payload.HealthBand != ManualRiskBand.Unknown ||
            payload.GoldBand != ManualRiskBand.Unknown ||
            payload.CopiesBand != ManualCopiesBand.Unknown ||
            payload.UnitCostBand != ManualUnitCostBand.Unknown)
        {
            failureCode = "NO_SESSION_CHECKPOINT_INVALID";
            return false;
        }

        if (!string.Equals(payload.FixtureScenarioId, string.Empty, StringComparison.Ordinal) ||
            payload.Topic != ManualTopic.LossStreakReview)
        {
            failureCode = "NO_SESSION_TOPIC_OR_SCENARIO_INVALID";
            return false;
        }

        if (payload.ExpiresAt != payload.CreatedAt)
        {
            failureCode = "NO_SESSION_TIMING_INVALID";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    private static bool TryValidateEditingCheckpoint(ManualSessionRecoveryPayload payload, out string failureCode)
    {
        if (payload.ManualRunId == Guid.Empty || payload.HighestRevision != 0)
        {
            failureCode = "EDITING_RUN_OR_REVISION_INVALID";
            return false;
        }

        if (!HasExpectedScenarioForTopic(payload.Topic, payload.FixtureScenarioId))
        {
            failureCode = "TOPIC_SCENARIO_MISMATCH";
            return false;
        }

        if (payload.Intent != ManualIntent.Review ||
            payload.HealthBand != ManualRiskBand.Unknown ||
            payload.GoldBand != ManualRiskBand.Unknown ||
            payload.CopiesBand != ManualCopiesBand.Unknown ||
            payload.UnitCostBand != ManualUnitCostBand.Unknown)
        {
            failureCode = "EDITING_CHECKPOINT_INVALID";
            return false;
        }

        if (payload.ExpiresAt != payload.CreatedAt)
        {
            failureCode = "EDITING_TIMING_INVALID";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    private static bool TryValidateCurrentAdvice(ManualSessionRecoveryPayload payload, out string failureCode)
    {
        if (!HasActiveRunAndRevision(payload, out failureCode) ||
            !HasExpectedScenarioForTopic(payload.Topic, payload.FixtureScenarioId))
        {
            failureCode = failureCode == "NONE" ? "TOPIC_SCENARIO_MISMATCH" : failureCode;
            return false;
        }

        if (payload.ExpiresAt <= payload.CreatedAt)
        {
            failureCode = "CURRENT_TIMING_INVALID";
            return false;
        }

        bool hasRequiredBands = payload.Topic switch
        {
            ManualTopic.LossStreakReview =>
                payload.HealthBand != ManualRiskBand.Unknown &&
                payload.GoldBand != ManualRiskBand.Unknown,
            ManualTopic.RerollReview =>
                payload.GoldBand != ManualRiskBand.Unknown &&
                payload.CopiesBand != ManualCopiesBand.Unknown &&
                payload.UnitCostBand != ManualUnitCostBand.Unknown,
            _ => false
        };

        if (!hasRequiredBands)
        {
            failureCode = "CURRENT_REQUIRED_BANDS_MISSING";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    private static bool TryValidateTerminalSnapshot(ManualSessionRecoveryPayload payload, out string failureCode)
    {
        if (!HasActiveRunAndRevision(payload, out failureCode) ||
            !HasExpectedScenarioForTopic(payload.Topic, payload.FixtureScenarioId))
        {
            failureCode = failureCode == "NONE" ? "TOPIC_SCENARIO_MISMATCH" : failureCode;
            return false;
        }

        if (payload.ExpiresAt < payload.CreatedAt)
        {
            failureCode = "TERMINAL_TIMING_INVALID";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    private static bool HasActiveRunAndRevision(ManualSessionRecoveryPayload payload, out string failureCode)
    {
        if (payload.ManualRunId == Guid.Empty)
        {
            failureCode = "ACTIVE_RUN_ID_INVALID";
            return false;
        }

        if (payload.HighestRevision <= 0)
        {
            failureCode = "ACTIVE_REVISION_INVALID";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    private static bool HasExpectedScenarioForTopic(ManualTopic topic, string fixtureScenarioId)
    {
        string? expectedScenarioId = topic switch
        {
            ManualTopic.LossStreakReview => LossStreakFixtureScenarioId,
            ManualTopic.RerollReview => RerollFixtureScenarioId,
            _ => null
        };

        return expectedScenarioId is not null &&
            string.Equals(fixtureScenarioId, expectedScenarioId, StringComparison.Ordinal);
    }

    private static bool TryReadString(ref Utf8JsonReader reader, out string value)
    {
        value = string.Empty;

        if (reader.TokenType != JsonTokenType.String)
        {
            return false;
        }

        string? text = reader.GetString();

        if (text is null)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadInt64(ref Utf8JsonReader reader, out long value)
    {
        value = default;
        return reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out value);
    }

    private static bool TryReadGuid(ref Utf8JsonReader reader, out Guid value)
    {
        value = default;
        return TryReadString(ref reader, out string text) && Guid.TryParse(text, out value);
    }

    private static bool TryReadDateTimeOffset(ref Utf8JsonReader reader, out DateTimeOffset value)
    {
        value = default;

        if (!TryReadString(ref reader, out string text) ||
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed))
        {
            return false;
        }

        value = parsed.ToUniversalTime();
        return true;
    }

    private static bool TryReadEnum<TEnum>(ref Utf8JsonReader reader, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (!TryReadString(ref reader, out string text) ||
            !Enum.TryParse(text, ignoreCase: false, out value) ||
            !Enum.IsDefined(value) ||
            !string.Equals(Enum.GetName(typeof(TEnum), value), text, StringComparison.Ordinal))
        {
            value = default;
            return false;
        }

        return true;
    }

    private static byte[] EncodeCanonicalPayload(ManualSessionRecoveryPayload payload)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer, CanonicalWriterOptions))
        {
            WriteCanonicalPayload(writer, payload);
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonicalPayload(Utf8JsonWriter writer, ManualSessionRecoveryPayload payload)
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", payload.SchemaVersion);
        writer.WriteNumber("snapshotGeneration", payload.SnapshotGeneration);
        writer.WriteString("manualRunId", payload.ManualRunId.ToString("N"));
        writer.WriteNumber("highestRevision", payload.HighestRevision);
        WriteEnum(writer, "sessionPhase", payload.SessionPhase);
        writer.WriteString("fixtureScenarioId", payload.FixtureScenarioId);
        WriteEnum(writer, "topic", payload.Topic);
        WriteEnum(writer, "intent", payload.Intent);
        WriteEnum(writer, "healthBand", payload.HealthBand);
        WriteEnum(writer, "goldBand", payload.GoldBand);
        WriteEnum(writer, "copiesBand", payload.CopiesBand);
        WriteEnum(writer, "unitCostBand", payload.UnitCostBand);
        WriteEnum(writer, "provenance", payload.Provenance);
        writer.WriteString("fixturePackVersion", payload.FixturePackVersion);
        writer.WriteString("createdAt", payload.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("expiresAt", payload.ExpiresAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteEnum<TEnum>(Utf8JsonWriter writer, string propertyName, TEnum value)
        where TEnum : struct, Enum
    {
        writer.WriteString(propertyName, value.ToString());
    }

    private static string ComputeDigest(byte[] canonicalPayload)
    {
        return Convert.ToHexString(SHA256.HashData(canonicalPayload));
    }

    private static bool IsUppercaseSha256(string digest)
    {
        if (digest.Length != 64)
        {
            return false;
        }

        foreach (char character in digest)
        {
            bool isDigit = character is >= '0' and <= '9';
            bool isUppercaseHex = character is >= 'A' and <= 'F';

            if (!isDigit && !isUppercaseHex)
            {
                return false;
            }
        }

        return true;
    }
}
