using System.IO;
using NUnit.Framework;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Presentation;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class SecondScreenPresentationSkillTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 7, 16, 10, 2, 0, TimeSpan.Zero);

    [Test]
    public void current_projection_is_the_only_advice_source_and_is_passed_through_without_reason_code()
    {
        ManualPanelProjection projection = Projection(
            isCurrent: true,
            phase: LocalAdvicePhase.Current,
            renderedText: "fixture-rendered-advice",
            reasonCode: "fixture.hidden-reason",
            expiresAt: Expiry);
        SecondScreenPresentation presentation = new SecondScreenPresentationSkill().Present(new SecondScreenSessionState(
            projection,
            ManualSessionPhase.CurrentAdvice,
            ManualRecoveryStatus.RecoveryAvailable,
            ManualSessionNotice.Submitted));

        Assert.Multiple(() =>
        {
            Assert.That(presentation.IsCurrentAdviceVisible, Is.True);
            Assert.That(presentation.AdviceText, Is.EqualTo("fixture-rendered-advice"));
            Assert.That(presentation.AdviceText, Is.Not.EqualTo("fixture.hidden-reason"));
            Assert.That(presentation.ExpiresAt, Is.EqualTo(Expiry));
            Assert.That(presentation.Precision, Is.EqualTo(LocalPrecisionState.Educational));
            Assert.That(presentation.SourceLabel, Is.EqualTo("Manual / FixtureOnly"));
            Assert.That(presentation.FixturePackVersion, Is.EqualTo("fixture-v1"));
            Assert.That(typeof(SecondScreenPresentation).GetProperty("ReasonCode"), Is.Null);
        });
    }

    [Test]
    public void current_projection_passes_whitespace_rendered_text_through_without_reinterpreting_harness_authority()
    {
        ManualPanelProjection projection = Projection(
            isCurrent: true,
            phase: LocalAdvicePhase.Current,
            renderedText: "  ",
            reasonCode: "fixture.hidden-reason",
            expiresAt: Expiry);

        SecondScreenPresentation presentation = new SecondScreenPresentationSkill().Present(new SecondScreenSessionState(
            projection,
            ManualSessionPhase.CurrentAdvice,
            ManualRecoveryStatus.RecoveryAvailable,
            ManualSessionNotice.Submitted));

        Assert.Multiple(() =>
        {
            Assert.That(presentation.IsCurrentAdviceVisible, Is.True);
            Assert.That(presentation.AdviceText, Is.EqualTo("  "));
        });
    }

    [TestCase(ManualSessionPhase.NoSession, ManualSessionNotice.None, ManualRecoveryStatus.RecoveryAvailable,
        "Start a manual fixture session to begin.")]
    [TestCase(ManualSessionPhase.EditingCheckpoint, ManualSessionNotice.IncompleteCheckpoint, ManualRecoveryStatus.RecoveryAvailable,
        "Complete the bounded checkpoint fields before submitting.")]
    [TestCase(ManualSessionPhase.Cleared, ManualSessionNotice.Cleared, ManualRecoveryStatus.RecoveryAvailable,
        "The current advice was cleared.")]
    [TestCase(ManualSessionPhase.Expired, ManualSessionNotice.Expired, ManualRecoveryStatus.RecoveryAvailable,
        "The current advice expired and is no longer shown.")]
    [TestCase(ManualSessionPhase.EditingCheckpoint, ManualSessionNotice.RecoveryEnabled, ManualRecoveryStatus.RecoveryAvailable,
        "D: recovery is available.")]
    [TestCase(ManualSessionPhase.EditingCheckpoint, ManualSessionNotice.RecoveryUnavailable, ManualRecoveryStatus.MemoryOnlyDegraded,
        "This session is in memory only and will not be restored after closing.")]
    [TestCase(ManualSessionPhase.EditingCheckpoint, ManualSessionNotice.RecoveryRejected, ManualRecoveryStatus.RecoveryRejected,
        "The saved recovery snapshot was rejected; no prior advice was restored.")]
    public void noncurrent_state_maps_to_one_allowed_status_and_never_exposes_advice(
        ManualSessionPhase phase,
        ManualSessionNotice notice,
        ManualRecoveryStatus recoveryStatus,
        string expectedStatus)
    {
        ManualPanelProjection projection = Projection(
            isCurrent: false,
            phase: phase == ManualSessionPhase.Expired ? LocalAdvicePhase.Expired : LocalAdvicePhase.Cleared,
            renderedText: "must-not-be-exposed",
            reasonCode: "fixture.hidden-reason",
            expiresAt: Expiry);
        SecondScreenPresentation presentation = new SecondScreenPresentationSkill().Present(new SecondScreenSessionState(
            projection,
            phase,
            recoveryStatus,
            notice));

        Assert.Multiple(() =>
        {
            Assert.That(presentation.IsCurrentAdviceVisible, Is.False);
            Assert.That(presentation.AdviceText, Is.Null);
            Assert.That(presentation.StatusText, Is.EqualTo(expectedStatus));
            Assert.That(presentation.ExpiresAt, Is.EqualTo(Expiry));
        });
    }

    [Test]
    public void presentation_source_does_not_render_semantic_advice_or_add_unapproved_player_text()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TftCompanion.SecondScreen",
            "Presentation",
            "SecondScreenPresentationSkill.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("EmbeddedFixtureExpressionSkill"));
            Assert.That(source, Does.Not.Contain("TryRender("));
            Assert.That(source, Does.Not.Contain("SemanticAdvice"));
            Assert.That(source, Does.Contain("Start a manual fixture session to begin."));
            Assert.That(source, Does.Contain("Complete the bounded checkpoint fields before submitting."));
            Assert.That(source, Does.Contain("The current advice was cleared."));
            Assert.That(source, Does.Contain("The current advice expired and is no longer shown."));
            Assert.That(source, Does.Contain("D: recovery is available."));
            Assert.That(source, Does.Contain("This session is in memory only and will not be restored after closing."));
            Assert.That(source, Does.Contain("The saved recovery snapshot was rejected; no prior advice was restored."));
            Assert.That(source, Does.Not.Contain("ReasonCode"));
        });
    }

    private static ManualPanelProjection Projection(
        bool isCurrent,
        LocalAdvicePhase phase,
        string? renderedText,
        string reasonCode,
        DateTimeOffset? expiresAt) => new(
            ManualRunId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ManualRevision: 1,
            Phase: phase,
            Precision: LocalPrecisionState.Educational,
            SourceLabel: "Manual / FixtureOnly",
            FixturePackVersion: "fixture-v1",
            MessageKey: "manual.loss-streak.review",
            RenderedText: renderedText,
            ReasonCode: reasonCode,
            ExpiresAt: expiresAt,
            IsCurrent: isCurrent);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TftCompanion.Poc.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the TFT Companion repository root.");
    }
}
