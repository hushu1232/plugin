using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NUnit.Framework;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class ManualSessionRecoveryCodecTests
{
    [Test]
    public void encode_writes_a_deterministic_canonical_payload()
    {
        ManualSessionRecoveryCodec codec = new();

        byte[] first = codec.Encode(ValidCurrent);
        byte[] second = codec.Encode(ValidCurrent);
        string document = Encoding.UTF8.GetString(first);
        string digest = ExtractDigest(document);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(
                document,
                Does.StartWith(
                    "{\"payload\":{\"schemaVersion\":\"manual-session-v1\",\"snapshotGeneration\":11,\"manualRunId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"highestRevision\":4,\"sessionPhase\":\"CurrentAdvice\",\"fixtureScenarioId\":\"loss-streak-review-v1\",\"topic\":\"LossStreakReview\",\"intent\":\"PreserveLossStreak\",\"healthBand\":\"Medium\",\"goldBand\":\"High\",\"copiesBand\":\"Unknown\",\"unitCostBand\":\"Unknown\",\"provenance\":\"UserEntered\",\"fixturePackVersion\":\"fixture-v1\",\"createdAt\":\"2026-07-16T10:00:00.0000000+00:00\",\"expiresAt\":\"2026-07-16T10:02:00.0000000+00:00\"},\"canonicalPayloadDigest\":\""));
            Assert.That(document, Does.EndWith("\"}"));
            Assert.That(digest, Does.Match("^[0-9A-F]{64}$"));
        });
    }

    [Test]
    public void encode_does_not_include_rendered_text()
    {
        ManualSessionRecoveryCodec codec = new();
        string document = Encoding.UTF8.GetString(codec.Encode(ValidCurrent));

        Assert.That(document, Does.Not.Contain("RenderedText"));
    }

    [Test]
    public void try_decode_returns_the_valid_payload_and_none_failure_code()
    {
        ManualSessionRecoveryCodec codec = new();

        bool success = codec.TryDecode(codec.Encode(ValidCurrent), out ManualSessionRecoveryPayload? payload, out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(payload, Is.EqualTo(ValidCurrent));
            Assert.That(failureCode, Is.EqualTo("NONE"));
        });
    }

    [Test]
    public void try_decode_rejects_a_digest_mismatch_without_broken_json()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewriteCanonicalPayloadDigest: static originalDigest =>
            {
                string mismatchedDigest = originalDigest[0] == '0'
                    ? $"1{originalDigest[1..]}"
                    : $"0{originalDigest[1..]}";

                return mismatchedDigest;
            });

        AssertRejected(codec, mutated);
    }

    [Test]
    public void try_decode_rejects_an_unknown_root_property()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            appendRootProperties: static (writer, _) => writer.WriteBoolean("unknownRoot", true));

        AssertRejected(codec, mutated);
    }

    [Test]
    public void try_decode_rejects_an_unknown_payload_property()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            appendPayloadProperties: static (writer, _) => writer.WriteBoolean("unknownPayload", true));

        AssertRejected(codec, mutated);
    }

    [Test]
    public void try_decode_rejects_a_duplicate_root_property()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            appendRootProperties: static (writer, root) =>
            {
                writer.WritePropertyName("canonicalPayloadDigest");
                root.GetProperty("canonicalPayloadDigest").WriteTo(writer);
            });

        AssertRejected(codec, mutated);
    }

    [Test]
    public void try_decode_rejects_a_duplicate_payload_property()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            appendPayloadProperties: static (writer, payload) =>
            {
                writer.WritePropertyName("topic");
                payload.GetProperty("topic").WriteTo(writer);
            });

        AssertRejected(codec, mutated);
    }

    [Test]
    public void try_decode_rejects_a_nonpositive_snapshot_generation()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewritePayloadProperty: static (writer, property) =>
            {
                if (!property.NameEquals("snapshotGeneration"))
                {
                    return false;
                }

                writer.WriteNumber(property.Name, 0);
                return true;
            },
            recomputeCanonicalPayloadDigest: true);

        AssertRejected(codec, mutated, "SNAPSHOT_GENERATION_INVALID");
    }

    [Test]
    public void try_decode_rejects_an_undefined_enum_value()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewritePayloadProperty: static (writer, property) =>
            {
                if (!property.NameEquals("sessionPhase"))
                {
                    return false;
                }

                writer.WriteString(property.Name, "999");
                return true;
            },
            recomputeCanonicalPayloadDigest: true);

        AssertRejected(codec, mutated, "PAYLOAD_FIELD_TYPE");
    }

    [Test]
    public void try_decode_rejects_current_advice_with_an_empty_run_id()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewritePayloadProperty: static (writer, property) =>
            {
                if (!property.NameEquals("manualRunId"))
                {
                    return false;
                }

                writer.WriteString(property.Name, Guid.Empty.ToString("N"));
                return true;
            },
            recomputeCanonicalPayloadDigest: true);

        AssertRejected(codec, mutated, "ACTIVE_RUN_ID_INVALID");
    }

    [Test]
    public void try_decode_rejects_current_advice_without_a_revision()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewritePayloadProperty: static (writer, property) =>
            {
                if (!property.NameEquals("highestRevision"))
                {
                    return false;
                }

                writer.WriteNumber(property.Name, 0);
                return true;
            },
            recomputeCanonicalPayloadDigest: true);

        AssertRejected(codec, mutated, "ACTIVE_REVISION_INVALID");
    }

    [Test]
    public void try_decode_rejects_a_fixture_scenario_and_topic_mismatch()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewritePayloadProperty: static (writer, property) =>
            {
                if (!property.NameEquals("fixtureScenarioId"))
                {
                    return false;
                }

                writer.WriteString(property.Name, "reroll-review-v1");
                return true;
            },
            recomputeCanonicalPayloadDigest: true);

        AssertRejected(codec, mutated, "TOPIC_SCENARIO_MISMATCH");
    }

    [Test]
    public void try_decode_rejects_current_advice_that_does_not_expire_after_creation()
    {
        ManualSessionRecoveryCodec codec = new();
        byte[] mutated = RewriteValidDocument(
            codec,
            rewritePayloadProperty: static (writer, property) =>
            {
                if (!property.NameEquals("expiresAt"))
                {
                    return false;
                }

                writer.WriteString(property.Name, "2026-07-16T10:00:00.0000000+00:00");
                return true;
            },
            recomputeCanonicalPayloadDigest: true);

        AssertRejected(codec, mutated, "CURRENT_TIMING_INVALID");
    }

    [Test]
    public void try_decode_rejects_malformed_json()
    {
        ManualSessionRecoveryCodec codec = new();

        AssertRejected(codec, Encoding.UTF8.GetBytes("{\"payload\":"));
    }

    [Test]
    public void try_decode_rejects_invalid_utf8()
    {
        ManualSessionRecoveryCodec codec = new();

        AssertRejected(codec, new byte[] { 0x7B, 0xFF, 0x7D });
    }

    [Test]
    public void try_decode_rejects_a_document_larger_than_the_snapshot_limit_before_parsing()
    {
        ManualSessionRecoveryCodec codec = new();

        AssertRejected(codec, new byte[ManualSessionRecoveryContract.MaximumSnapshotBytes + 1]);
    }

    private static ManualSessionRecoveryPayload ValidCurrent { get; } = new(
        SchemaVersion: "manual-session-v1",
        SnapshotGeneration: 11,
        ManualRunId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        HighestRevision: 4,
        SessionPhase: ManualSessionPhase.CurrentAdvice,
        FixtureScenarioId: "loss-streak-review-v1",
        Topic: ManualTopic.LossStreakReview,
        Intent: ManualIntent.PreserveLossStreak,
        HealthBand: ManualRiskBand.Medium,
        GoldBand: ManualRiskBand.High,
        CopiesBand: ManualCopiesBand.Unknown,
        UnitCostBand: ManualUnitCostBand.Unknown,
        Provenance: LocalFactProvenance.UserEntered,
        FixturePackVersion: "fixture-v1",
        CreatedAt: new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero),
        ExpiresAt: new DateTimeOffset(2026, 7, 16, 10, 2, 0, TimeSpan.Zero));

    private static readonly JsonWriterOptions CanonicalJsonWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    private static void AssertRejected(
        ManualSessionRecoveryCodec codec,
        byte[] utf8,
        string? expectedFailureCode = null)
    {
        bool success = codec.TryDecode(utf8, out ManualSessionRecoveryPayload? payload, out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(payload, Is.Null);
            Assert.That(failureCode, Is.Not.Empty);
            Assert.That(failureCode, Is.Not.EqualTo("NONE"));
            Assert.That(failureCode, Does.Match("^[A-Z][A-Z0-9_]*$"));

            if (expectedFailureCode is not null)
            {
                Assert.That(failureCode, Is.EqualTo(expectedFailureCode));
            }
        });
    }

    private static byte[] RewriteValidDocument(
        ManualSessionRecoveryCodec codec,
        JsonPropertyRewrite? rewritePayloadProperty = null,
        Action<Utf8JsonWriter, JsonElement>? appendPayloadProperties = null,
        Func<string, string>? rewriteCanonicalPayloadDigest = null,
        Action<Utf8JsonWriter, JsonElement>? appendRootProperties = null,
        bool recomputeCanonicalPayloadDigest = false)
    {
        using JsonDocument document = JsonDocument.Parse(codec.Encode(ValidCurrent));
        JsonElement root = document.RootElement;
        byte[] payloadUtf8 = EncodePayload(
            root.GetProperty("payload"),
            rewritePayloadProperty,
            appendPayloadProperties);
        string digest = root.GetProperty("canonicalPayloadDigest").GetString() ?? string.Empty;

        if (recomputeCanonicalPayloadDigest)
        {
            digest = Convert.ToHexString(SHA256.HashData(payloadUtf8));
        }

        if (rewriteCanonicalPayloadDigest is not null)
        {
            digest = rewriteCanonicalPayloadDigest(digest);
        }

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer, CanonicalJsonWriterOptions))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("payload");
            writer.WriteRawValue(payloadUtf8, skipInputValidation: true);
            writer.WriteString("canonicalPayloadDigest", digest);
            appendRootProperties?.Invoke(writer, root);
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodePayload(
        JsonElement payload,
        JsonPropertyRewrite? rewritePayloadProperty,
        Action<Utf8JsonWriter, JsonElement>? appendPayloadProperties)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer, CanonicalJsonWriterOptions))
        {
            writer.WriteStartObject();

            foreach (JsonProperty property in payload.EnumerateObject())
            {
                if (rewritePayloadProperty is not null && rewritePayloadProperty(writer, property))
                {
                    continue;
                }

                WriteProperty(writer, property);
            }

            appendPayloadProperties?.Invoke(writer, payload);
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteProperty(Utf8JsonWriter writer, JsonProperty property)
    {
        writer.WritePropertyName(property.Name);
        property.Value.WriteTo(writer);
    }

    private static string ExtractDigest(string document)
    {
        using JsonDocument parsedDocument = JsonDocument.Parse(document);
        string? digest = parsedDocument.RootElement
            .GetProperty("canonicalPayloadDigest")
            .GetString();

        Assert.That(digest, Is.Not.Null);

        return digest!;
    }

    private delegate bool JsonPropertyRewrite(Utf8JsonWriter writer, JsonProperty property);
}
