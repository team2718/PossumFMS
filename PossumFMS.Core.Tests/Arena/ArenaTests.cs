using PossumFMS.Core.Arena;
using Xunit;

namespace PossumFMS.Core.Tests.Arena;

public sealed class ArenaTests
{
    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsIdle()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.Equal(MatchPhase.Idle, arena.Phase);
        Assert.Equal(PossumFMS.Core.Arena.MatchType.Test, arena.MatchType);
        Assert.Equal(1, arena.MatchNumber);
        Assert.Equal(0, arena.MatchRepeat);
        Assert.Equal(TimeSpan.Zero, arena.TimeRemaining);
        Assert.False(arena.IsMatchRunning);
        Assert.False(arena.IsMatchInProgress);
        Assert.False(arena.WasAborted);
        Assert.False(arena.ArenaEstop);
        Assert.False(arena.FreePracticeEnabled);
        Assert.Equal(string.Empty, arena.GameData);
    }

    [Fact]
    public void SetFreePracticeEnabled_FromIdle_UpdatesState()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.SetFreePracticeEnabled(true);

        Assert.True(arena.FreePracticeEnabled);
    }

    [Fact]
    public void SetFreePracticeEnabled_WhenNotIdle_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();

        Assert.Throws<InvalidOperationException>(() => arena.SetFreePracticeEnabled(true));
    }

    // ── StartPreMatch ──────────────────────────────────────────────────────────

    [Fact]
    public void StartPreMatch_FromIdle_TransitionsToPreMatch()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.StartPreMatch();

        Assert.Equal(MatchPhase.PreMatch, arena.Phase);
        Assert.Equal(TimeSpan.Zero, arena.TimeRemaining);
        Assert.False(arena.IsMatchInProgress);
        Assert.False(arena.IsMatchRunning);
    }

    [Fact]
    public void StartPreMatch_ClearsWasAborted()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        arena.AbortMatch();
        Assert.True(arena.WasAborted);

        // Clear back to idle then pre-match
        arena.ClearMatch();
        arena.StartPreMatch();

        Assert.False(arena.WasAborted);
    }

    [Fact]
    public void StartPreMatch_WhileMatchInProgress_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch(); // now in Auto

        Assert.Throws<InvalidOperationException>(() => arena.StartPreMatch());
    }

    [Fact]
    public void StartPreMatch_WhenFreePracticeEnabled_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.SetFreePracticeEnabled(true);

        Assert.Throws<InvalidOperationException>(() => arena.StartPreMatch());
    }

    // ── StartMatch ─────────────────────────────────────────────────────────────

    [Fact]
    public void StartMatch_FromPreMatch_TransitionsToAuto()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();

        arena.StartMatch();

        Assert.Equal(MatchPhase.Auto, arena.Phase);
        Assert.True(arena.IsMatchRunning);
        Assert.True(arena.IsMatchInProgress);
    }

    [Fact]
    public void StartMatch_TimeRemaining_IsApproximately20Seconds()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        var remaining = arena.TimeRemaining;

        // Should be very close to 20 s immediately after start
        Assert.InRange(remaining.TotalSeconds, 19.9, 20.0);
    }

    [Fact]
    public void StartMatch_NotFromPreMatch_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.Throws<InvalidOperationException>(() => arena.StartMatch());
    }

    [Fact]
    public void StartMatch_FromIdle_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.Throws<InvalidOperationException>(() => arena.StartMatch());
    }

    // ── AbortMatch ─────────────────────────────────────────────────────────────

    [Fact]
    public void AbortMatch_WhileAuto_SetsWasAbortedAndGoesToPostMatch()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        arena.AbortMatch();

        Assert.Equal(MatchPhase.PostMatch, arena.Phase);
        Assert.True(arena.WasAborted);
        Assert.False(arena.IsMatchInProgress);
        Assert.False(arena.IsMatchRunning);
    }

    [Fact]
    public void AbortMatch_WhenNotInProgress_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.Throws<InvalidOperationException>(() => arena.AbortMatch());
    }

    [Fact]
    public void AbortMatch_WhileIdle_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.Throws<InvalidOperationException>(() => arena.AbortMatch());
    }

    // ── ClearMatch ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClearMatch_FromPostMatch_TransitionsToIdle()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        arena.AbortMatch();

        arena.ClearMatch();

        Assert.Equal(MatchPhase.Idle, arena.Phase);
        Assert.False(arena.WasAborted);
    }

    [Fact]
    public void ClearMatch_WhileMatchInProgress_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        Assert.Throws<InvalidOperationException>(() => arena.ClearMatch());
    }

    // ── Tick / phase transitions ───────────────────────────────────────────────

    [Fact]
    public void Tick_WhenAutoHasTimeRemaining_StaysInAuto()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        // Immediately after StartMatch, Auto has ~20 s remaining, so Tick() should
        // leave the phase unchanged (the transition fires only when TimeRemaining == 0).
        Assert.Equal(MatchPhase.Auto, arena.Phase);
        arena.Tick();
        Assert.Equal(MatchPhase.Auto, arena.Phase);
    }

    [Fact]
    public void Tick_WhenIdle_IsNoOp()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.Tick(); // Should not throw

        Assert.Equal(MatchPhase.Idle, arena.Phase);
    }

    [Fact]
    public void Tick_WhenPreMatch_IsNoOp()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();

        arena.Tick();

        Assert.Equal(MatchPhase.PreMatch, arena.Phase);
    }

    // ── TimeRemaining ──────────────────────────────────────────────────────────

    [Fact]
    public void TimeRemaining_WhenIdle_IsZero()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.Equal(TimeSpan.Zero, arena.TimeRemaining);
    }

    [Fact]
    public void TimeRemaining_WhenPostMatch_IsZero()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        arena.AbortMatch();

        Assert.Equal(TimeSpan.Zero, arena.TimeRemaining);
    }

    [Fact]
    public void TimeRemaining_NeverGoesNegative()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        Assert.True(arena.TimeRemaining >= TimeSpan.Zero);
    }

    // ── IsMatchRunning / IsMatchInProgress ────────────────────────────────────

    [Fact]
    public void IsMatchRunning_FalseOutsideAutoAndTeleop()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        Assert.False(arena.IsMatchRunning); // Idle

        arena.StartPreMatch();
        Assert.False(arena.IsMatchRunning); // PreMatch

        arena.StartMatch();
        arena.AbortMatch();
        Assert.False(arena.IsMatchRunning); // PostMatch
    }

    [Fact]
    public void IsMatchRunning_TrueInAuto()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        Assert.True(arena.IsMatchRunning);
    }

    [Fact]
    public void IsMatchInProgress_TrueInAuto()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        Assert.True(arena.IsMatchInProgress);
    }

    // ── Arena E-Stop ───────────────────────────────────────────────────────────

    [Fact]
    public void TriggerArenaEstop_SetsArenaEstop()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.TriggerArenaEstop();

        Assert.True(arena.ArenaEstop);
    }

    [Fact]
    public void TriggerArenaEstop_WhileMatchInProgress_AbortsMatch()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();

        arena.TriggerArenaEstop();

        Assert.Equal(MatchPhase.PostMatch, arena.Phase);
        Assert.True(arena.WasAborted);
        Assert.True(arena.ArenaEstop);
    }

    [Fact]
    public void TriggerArenaEstop_WhenIdle_DoesNotAbort()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.TriggerArenaEstop(); // Should not throw

        Assert.Equal(MatchPhase.Idle, arena.Phase);
        Assert.True(arena.ArenaEstop);
    }

    [Fact]
    public void ResetArenaEstop_ClearsFlag()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.TriggerArenaEstop();

        arena.ResetArenaEstop();

        Assert.False(arena.ArenaEstop);
    }

    [Fact]
    public void ResetArenaEstop_WhileMatchRunning_Throws()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto — IsMatchInProgress is true

        // ResetArenaEstop guards against being called while the match is in progress,
        // regardless of whether the ArenaEstop flag is set.
        Assert.Throws<InvalidOperationException>(() => arena.ResetArenaEstop());
    }

    // ── GameData ───────────────────────────────────────────────────────────────

    [Fact]
    public void SetGameData_UpdatesGameDataProperty()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.SetGameData("R");

        Assert.Equal("R", arena.GameData);
    }

    [Fact]
    public void SetGameData_FiresGameDataChangedEvent()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        string? received = null;
        arena.GameDataChanged += v => received = v;

        arena.SetGameData("B");

        Assert.Equal("B", received);
    }

    [Fact]
    public void SetGameData_EmptyString_ClearsData()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.SetGameData("R");

        arena.SetGameData(string.Empty);

        Assert.Equal(string.Empty, arena.GameData);
    }

    // ── PhaseChanged event ─────────────────────────────────────────────────────

    [Fact]
    public void PhaseChanged_FiredOnStartPreMatch()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        MatchPhase? firedWith = null;
        arena.PhaseChanged += p => firedWith = p;

        arena.StartPreMatch();

        Assert.Equal(MatchPhase.PreMatch, firedWith);
    }

    [Fact]
    public void PhaseChanged_FiredOnStartMatch()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        MatchPhase? firedWith = null;
        arena.PhaseChanged += p => firedWith = p;

        arena.StartMatch();

        Assert.Equal(MatchPhase.Auto, firedWith);
    }

    [Fact]
    public void PhaseChanged_FiredOnAbortMatch()
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        arena.StartPreMatch();
        arena.StartMatch();
        MatchPhase? firedWith = null;
        arena.PhaseChanged += p => firedWith = p;

        arena.AbortMatch();

        Assert.Equal(MatchPhase.PostMatch, firedWith);
    }

    // ── MatchType / MatchNumber ────────────────────────────────────────────────

    [Fact]
    public void MatchType_CanBeSet()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.MatchType = PossumFMS.Core.Arena.MatchType.Qualification;

        Assert.Equal(PossumFMS.Core.Arena.MatchType.Qualification, arena.MatchType);
    }

    [Fact]
    public void MatchNumber_CanBeSet()
    {
        var arena = new PossumFMS.Core.Arena.Arena();

        arena.MatchNumber = 42;

        Assert.Equal(42, arena.MatchNumber);
    }
}
