using NUnit.Framework;
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Host;
using TftCompanion.Poc.Tests.TestSupport;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class ProtocolHandshakeTests
{
    private const string AllowedOrigin = "overwolf-tool://tft-companion-poc";
    private const string PairingToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string BridgeInstanceId = "bridge-unit-test";

    private static HelloMessage ValidHello(ChannelKind channel = ChannelKind.Ingest) => new(
        GameId: ProtocolConstants.GameId,
        ProtocolVersion: ProtocolConstants.ProtocolVersion,
        SchemaVersion: ProtocolConstants.SchemaVersion,
        Channel: channel,
        Origin: AllowedOrigin,
        PairingProof: PairingToken,
        BridgeInstanceId: BridgeInstanceId);

    private static ProtocolHandshake CreateHandshake() => new(AllowedOrigin, PairingToken);

    [Test]
    public void valid_hello_requires_the_expected_origin_pairing_and_channel()
    {
        ProtocolHandshake handshake = CreateHandshake();

        bool accepted = handshake.TryValidateHello(
            ValidHello(),
            ChannelKind.Ingest,
            out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.True);
            Assert.That(failureCode, Is.EqualTo("NONE"));
        });
    }

    [Test]
    public void wrong_game_id_is_rejected_before_any_runtime_session_is_created()
    {
        ProtocolHandshake handshake = CreateHandshake();
        HelloMessage hello = ValidHello() with { GameId = 99999 };

        bool accepted = handshake.TryValidateHello(hello, ChannelKind.Ingest, out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("GAME_ID_REJECTED"));
        });
    }

    [Test]
    public void wrong_protocol_version_is_rejected_before_any_runtime_session_is_created()
    {
        ProtocolHandshake handshake = CreateHandshake();
        HelloMessage hello = ValidHello() with { ProtocolVersion = 99 };

        bool accepted = handshake.TryValidateHello(hello, ChannelKind.Ingest, out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("PROTOCOL_VERSION_REJECTED"));
        });
    }

    [Test]
    public void missing_origin_is_rejected_with_a_named_code()
    {
        ProtocolHandshake handshake = CreateHandshake();
        HelloMessage hello = ValidHello() with { Origin = null };

        bool accepted = handshake.TryValidateHello(hello, ChannelKind.Ingest, out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("ORIGIN_MISSING"));
        });
    }

    [Test]
    public void foreign_origin_is_rejected_with_a_named_code()
    {
        ProtocolHandshake handshake = CreateHandshake();
        HelloMessage hello = ValidHello() with { Origin = "https://evil.example.invalid" };

        bool accepted = handshake.TryValidateHello(hello, ChannelKind.Ingest, out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("ORIGIN_REJECTED"));
        });
    }

    [Test]
    public void loopback_origin_is_not_a_substitute_for_the_configured_overwolf_origin()
    {
        OriginPolicy policy = new(AllowedOrigin);
        OriginVerification result = policy.Verify("http://127.0.0.1:32173");

        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.ShouldPersistToDisk, Is.False);
        });
    }

    [Test]
    public void missing_or_wrong_pairing_proof_is_rejected()
    {
        ProtocolHandshake handshake = CreateHandshake();

        bool missingAccepted = handshake.TryValidateHello(
            ValidHello() with { PairingProof = null },
            ChannelKind.Ingest,
            out string missingCode);
        bool wrongAccepted = handshake.TryValidateHello(
            ValidHello() with { PairingProof = PairingTokenGenerator.Generate() },
            ChannelKind.Ingest,
            out string wrongCode);

        Assert.Multiple(() =>
        {
            Assert.That(missingAccepted, Is.False);
            Assert.That(missingCode, Is.EqualTo("PAIRING_REJECTED"));
            Assert.That(wrongAccepted, Is.False);
            Assert.That(wrongCode, Is.EqualTo("PAIRING_REJECTED"));
        });
    }

    [Test]
    public void hello_declared_for_the_wrong_route_is_rejected()
    {
        ProtocolHandshake handshake = CreateHandshake();

        bool accepted = handshake.TryValidateHello(
            ValidHello(ChannelKind.Render),
            ChannelKind.Ingest,
            out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("CHANNEL_MISMATCH"));
        });
    }

    [Test]
    public void oversized_text_frame_exceeding_maximum_is_rejected()
    {
        ProtocolHandshake handshake = CreateHandshake();

        bool accepted = handshake.TryValidateTextFrame(
            ProtocolConstants.MaximumTextFrameBytes + 1,
            out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("FRAME_OVERSIZED"));
        });
    }

    [Test]
    public void message_types_are_whitelisted_per_channel()
    {
        ProtocolHandshake handshake = CreateHandshake();

        bool renderAcceptedIngressMessage = handshake.TryValidateChannelMessage(
            ChannelKind.Render,
            messageType: "stateSnapshot",
            out string renderCode);
        bool ingestAcceptedRenderMessage = handshake.TryValidateChannelMessage(
            ChannelKind.Ingest,
            messageType: "receipt",
            out string ingestCode);

        Assert.Multiple(() =>
        {
            Assert.That(renderAcceptedIngressMessage, Is.False);
            Assert.That(renderCode, Is.EqualTo("CHANNEL_MISMATCH"));
            Assert.That(ingestAcceptedRenderMessage, Is.False);
            Assert.That(ingestCode, Is.EqualTo("CHANNEL_MISMATCH"));
        });
    }

    [Test]
    public void pairing_token_is_a_32_byte_base64url_value_and_compares_in_constant_time()
    {
        string generated = PairingTokenGenerator.Generate();

        Assert.Multiple(() =>
        {
            Assert.That(generated, Has.Length.EqualTo(43));
            Assert.That(PairingTokenValidator.IsValid(generated), Is.True);
            Assert.That(PairingTokenValidator.FixedTimeEquals(generated, generated), Is.True);
            Assert.That(PairingTokenValidator.FixedTimeEquals(generated, generated + "x"), Is.False);
        });
    }

    [Test]
    public void connection_rate_limiter_rejects_a_burst_and_recovers_in_a_new_window()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ConnectionRateLimiter limiter = new(
            maximumMessagesPerWindow: 2,
            window: TimeSpan.FromSeconds(1),
            clock: () => now);

        Assert.Multiple(() =>
        {
            Assert.That(limiter.TryAccept(), Is.True);
            Assert.That(limiter.TryAccept(), Is.True);
            Assert.That(limiter.TryAccept(), Is.False);
        });

        now = now.AddSeconds(1);
        Assert.That(limiter.TryAccept(), Is.True);
    }

    // I1: canonical base64url pairing token.
    // 42 'A' chars + final 'B' has the correct length (43) and charset,
    // but the last character's non-zero padding bits make it non-canonical.
    // It must be rejected by IsValid, by the Host config, and by the Hello
    // pairing proof.
    private const string NonCanonicalToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB";

    [Test]
    public void non_canonical_base64url_pairing_token_is_rejected_by_validator()
    {
        Assert.Multiple(() =>
        {
            // Canonical (43 A's) is accepted.
            Assert.That(PairingTokenValidator.IsValid(PairingToken), Is.True);
            // Non-canonical (42 A's + B) must be rejected.
            Assert.That(PairingTokenValidator.IsValid(NonCanonicalToken), Is.False);
        });
    }

    [Test]
    public void non_canonical_pairing_token_is_rejected_by_hello_pairing_proof()
    {
        ProtocolHandshake handshake = CreateHandshake();

        bool accepted = handshake.TryValidateHello(
            ValidHello() with { PairingProof = NonCanonicalToken },
            ChannelKind.Ingest,
            out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("PAIRING_REJECTED"));
        });
    }

    [Test]
    public void non_canonical_pairing_token_is_rejected_by_host_config()
    {
        PocHostOptions options = new(
            Port: 0,
            AllowedOrigin: AllowedOrigin,
            PairingToken: NonCanonicalToken);

        Assert.Throws<ArgumentException>(() =>
        {
            _ = new PocHostFactory(options, FakeStorageFileSystem.ValidRoot());
        });
    }
}
