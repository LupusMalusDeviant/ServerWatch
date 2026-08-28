using Whiskers.Services.ImageUpdate;

namespace Whiskers.Tests;

/// <summary>
/// Measuring what an image update would change before recreating anything (GAP-6, 2026-08-28).
///
/// <para>Most of the honest answer to "would this break something?" is computable. An image declares its
/// entrypoint, its user, the ports it exposes, the volumes it expects, whether it has a healthcheck. If the
/// running container depends on one of those and the new image states it differently, that is a fact readable
/// before anything is touched — not a guess about release notes.</para>
///
/// <para>The other half of these tests is about what the answer must NOT claim. A configuration diff cannot
/// see a schema migration or a changed config format, and those break just as hard. So "low risk" here can
/// only ever mean "nothing detectable changed" — and the assessment has to say so itself, or it becomes one
/// more place where the absence of a signal reads as good news.</para>
/// </summary>
public class UpdateRiskAssessorTests
{
    private static ImageContract Contract(
        string? user = null, string[]? entrypoint = null, string[]? cmd = null,
        string[]? ports = null, string[]? volumes = null, bool health = false,
        string? os = "linux", string? workdir = "/app")
        => new(entrypoint ?? ["/entrypoint.sh"], cmd ?? ["serve"], user,
            new HashSet<string>(ports ?? ["8080/tcp"]), new HashSet<string>(volumes ?? []),
            workdir, health, os);

    // ---- the changes that actually break containers ----------------------------------------------------

    [Fact]
    public void A_changed_user_is_flagged_high_because_the_old_data_keeps_its_owner()
    {
        // The classic silent breakage: same volumes, suddenly the wrong owner, crash loop right after update.
        var risk = UpdateRiskAssessor.Assess(
            Contract(user: "root"), Contract(user: "1000"), "1.2", "1.3", cvesClosed: 0);

        var f = Assert.Single(risk.Findings, x => x.What.Contains("different user"));
        Assert.Equal(RiskLevel.High, f.Level);
        Assert.Contains("unable to read or write", f.WhyItMatters);
        Assert.Equal(RiskLevel.High, risk.Level);
    }

    [Fact]
    public void A_port_that_disappears_is_flagged_high()
    {
        // Whatever points at it — reverse proxy, sibling container — stops arriving, and the container looks
        // perfectly healthy while it happens.
        var risk = UpdateRiskAssessor.Assess(
            Contract(ports: ["8080/tcp"]), Contract(ports: ["9000/tcp"]), "1.2", "1.3", 0);

        Assert.Contains(risk.Findings, f => f.Level == RiskLevel.High && f.What.Contains("8080"));
        Assert.Contains(risk.Findings, f => f.Level == RiskLevel.Low && f.What.Contains("9000"));
    }

    [Fact]
    public void A_declared_volume_that_vanishes_is_flagged_high()
    {
        var risk = UpdateRiskAssessor.Assess(
            Contract(volumes: ["/data"]), Contract(volumes: []), "1.2", "1.3", 0);

        var f = Assert.Single(risk.Findings, x => x.What.Contains("/data"));
        Assert.Equal(RiskLevel.High, f.Level);
        Assert.Contains("writes into nothing", f.WhyItMatters);
    }

    [Fact]
    public void A_removed_healthcheck_is_flagged_and_says_what_it_costs()
    {
        // A monitoring tool should be the last thing to shrug at this: without it the container reports
        // itself as running whatever state it is in.
        var risk = UpdateRiskAssessor.Assess(
            Contract(health: true), Contract(health: false), "1.2", "1.3", 0);

        var f = Assert.Single(risk.Findings, x => x.What.Contains("Healthcheck removed"));
        Assert.Equal(RiskLevel.Medium, f.Level);
        Assert.Contains("Nothing will notice", f.WhyItMatters);
    }

    [Fact]
    public void A_major_version_jump_is_flagged_high()
    {
        var risk = UpdateRiskAssessor.Assess(Contract(), Contract(), "v1.10.0", "v2.0.0", 0);

        Assert.Contains(risk.Findings, f => f.Level == RiskLevel.High && f.What.Contains("Major version jump"));
    }

    [Fact]
    public void A_patch_bump_with_nothing_else_changed_is_no_risk_at_all()
    {
        // The counter-direction. If every update came back "medium", the number would be worth nothing and
        // the whole assessment would be ignored inside a week.
        var risk = UpdateRiskAssessor.Assess(Contract(), Contract(), "1.10.0", "1.10.1", cvesClosed: 41);

        Assert.Empty(risk.Findings);
        Assert.Equal(RiskLevel.None, risk.Level);
        Assert.Equal(41, risk.CvesClosed);
    }

    [Fact]
    public void Latest_is_flagged_for_what_it_hides_rather_than_for_what_it_changes()
    {
        // ":latest" is not risky because of a diff — it is risky because the diff cannot tell you how far it
        // jumped.
        var risk = UpdateRiskAssessor.Assess(Contract(), Contract(), "latest", "latest", 0);

        var f = Assert.Single(risk.Findings);
        Assert.Contains("no version to compare", f.WhyItMatters);
        Assert.Contains(risk.BlindSpots, b => b.Contains("how far this update actually jumps",
            StringComparison.OrdinalIgnoreCase));
    }

    // ---- and what the verdict must never claim ---------------------------------------------------------

    [Fact]
    public void Even_a_clean_verdict_states_what_it_could_not_look_at()
    {
        // THE assertion of this file. A configuration diff cannot see a schema migration, a changed config
        // format or a newly required environment variable, and every one of those breaks just as hard. If
        // "no findings" were allowed to read as "safe", this feature would become exactly the kind of quiet
        // false all-clear the rest of this project exists to remove.
        var risk = UpdateRiskAssessor.Assess(Contract(), Contract(), "1.0.0", "1.0.1", 0);

        Assert.Equal(RiskLevel.None, risk.Level);
        Assert.NotEmpty(risk.BlindSpots);
        Assert.Contains(risk.BlindSpots, b => b.Contains("migration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(risk.BlindSpots, b => b.Contains("Environment variable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_level_is_the_worst_finding_not_an_average()
    {
        // One high finding among five harmless ones is still a high-risk update. An average would bury it.
        var risk = UpdateRiskAssessor.Assess(
            Contract(ports: ["8080/tcp"], health: false),
            Contract(ports: ["8080/tcp", "9000/tcp"], health: true, user: "1000"),
            "1.0", "1.1", 0);

        Assert.Equal(RiskLevel.High, risk.Level);
        Assert.Contains(risk.Findings, f => f.Level == RiskLevel.Low);
    }

    [Fact]
    public void The_benefit_is_reported_next_to_the_risk()
    {
        // "Closes 41 CVEs and changes the entrypoint" is a decision. Either half alone is not.
        var risk = UpdateRiskAssessor.Assess(
            Contract(entrypoint: ["/old.sh"]), Contract(entrypoint: ["/new.sh"]), "1.0", "1.1", cvesClosed: 41);

        Assert.Equal(41, risk.CvesClosed);
        Assert.Equal(RiskLevel.High, risk.Level);
    }

    [Fact]
    public void An_unversioned_tag_declines_rather_than_inventing_a_number()
    {
        // "bookworm" or "stable" have no major version. Guessing one would produce confident nonsense.
        var risk = UpdateRiskAssessor.Assess(Contract(), Contract(), "bookworm", "trixie", 0);

        Assert.DoesNotContain(risk.Findings, f => f.What.Contains("Major version"));
    }
}
