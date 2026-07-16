using NUnit.Framework;
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Host.Channels;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class MailboxIsolationTests
{
    [Test]
    public void ingress_mailbox_is_bounded_and_rejects_new_semantic_snapshots_when_full()
    {
        IngressMailbox mailbox = new(capacity: 128, sessionId: "session-test");

        for (int index = 0; index < 128; index++)
        {
            Assert.That(mailbox.TryWrite(CreateIngressEnvelope(index)), Is.True);
        }

        Assert.That(mailbox.TryWrite(CreateIngressEnvelope(129)), Is.False);
    }

    [Test]
    public void render_mailbox_hideall_is_not_blocked_by_an_ingress_burst()
    {
        IngressMailbox ingressMailbox = new(capacity: 128, sessionId: "session-test");
        RenderMailbox renderMailbox = new(capacity: 16);

        for (int index = 0; index < 128; index++)
        {
            ingressMailbox.TryWrite(CreateIngressEnvelope(index));
        }

        Assert.That(renderMailbox.TryWriteHideAll("runtime-1", 1, 1, "session-1", "lease-1", "command-1"), Is.True);
        Assert.That(renderMailbox.TryWriteShowMarker("runtime-1", 1, 2, "session-1", "lease-1", "command-2"), Is.True);
    }

    [Test]
    public void render_mailbox_is_bounded_and_rejects_commands_when_full()
    {
        RenderMailbox mailbox = new(capacity: 16);

        for (int index = 0; index < 16; index++)
        {
            Assert.That(
                mailbox.TryWriteHideAll("runtime-1", 1, index + 1, "session-1", $"lease-{index}", $"command-{index}"),
                Is.True);
        }

        Assert.That(mailbox.TryWriteHideAll("runtime-1", 1, 17, "session-1", "overflow", "command-overflow"), Is.False);
    }

    [Test]
    public void dual_channel_isolation_keeps_typed_ingress_out_of_the_render_queue()
    {
        IngressMailbox ingressMailbox = new(capacity: 128, sessionId: "session-test");
        RenderMailbox renderMailbox = new(capacity: 16);

        ingressMailbox.TryWrite(CreateIngressEnvelope(0));
        renderMailbox.TryWriteHideAll("runtime-1", 1, 1, "session-1", "lease-1", "command-1");

        Assert.Multiple(() =>
        {
            Assert.That(renderMailbox.TryRead(out RenderCommandEnvelope? renderCommand), Is.True);
            Assert.That(renderCommand!.CommandType, Is.EqualTo("hideAll"));
            Assert.That(renderCommand.RuntimeInstanceId, Is.EqualTo("runtime-1"));
            Assert.That(renderCommand.ConnectionEpoch, Is.EqualTo(1));
            Assert.That(renderCommand.CommandSequence, Is.EqualTo(1));
            Assert.That(renderCommand.SessionId, Is.EqualTo("session-1"));

            Assert.That(ingressMailbox.TryRead(out IngressEnvelope? ingressEnvelope), Is.True);
            Assert.That(ingressEnvelope!.Snapshot.Type, Is.EqualTo("stateSnapshot"));
            Assert.That(ingressEnvelope.Snapshot.SessionId, Is.EqualTo("session-test"));
        });
    }

    [Test]
    public void ingress_mailbox_scoped_to_a_session_rejects_envelopes_from_a_foreign_session()
    {
        // C2 regression: a session-scoped mailbox must reject envelopes from a
        // foreign session so that old session envelopes cannot occupy new
        // session capacity or be drained into the new runtime.
        IngressMailbox mailbox = new(capacity: 128, sessionId: "session-current");

        bool acceptedForeign = mailbox.TryWrite(new IngressEnvelope(
            "session-old",
            new IngressMessage(
                "stateSnapshot",
                "session-old",
                1L,
                MatchObserved: true,
                RoundObserved: true,
                IsAuthoritativeSnapshot: true)));

        Assert.That(acceptedForeign, Is.False,
            "A session-scoped mailbox must reject envelopes from a foreign session.");

        bool acceptedCurrent = mailbox.TryWrite(new IngressEnvelope(
            "session-current",
            new IngressMessage(
                "stateSnapshot",
                "session-current",
                1L,
                MatchObserved: true,
                RoundObserved: true,
                IsAuthoritativeSnapshot: true)));

        Assert.That(acceptedCurrent, Is.True,
            "A session-scoped mailbox must accept envelopes from its own session.");
    }

    private static IngressEnvelope CreateIngressEnvelope(int index) => new(
        "session-test",
        new IngressMessage(
            "stateSnapshot",
            "session-test",
            index + 1L,
            MatchObserved: true,
            RoundObserved: true,
            IsAuthoritativeSnapshot: true));
}
