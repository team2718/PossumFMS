using Microsoft.Extensions.Logging.Abstractions;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using Xunit;

namespace PossumFMS.Core.Tests.DriverStation;

/// <summary>
/// Tests for DriverStationManager.EncodeControlPacket — the method that writes
/// the 22-byte FMS→DS UDP control packet.  This is safety-critical: a wrong
/// control byte could leave a robot enabled when it should be disabled.
/// </summary>
public sealed class ControlPacketTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (DriverStationManager Mgr, PossumFMS.Core.Arena.Arena Arena, byte[] Buf, DriverStationConnection Ds)
        Setup(AllianceStation? station = null)
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var mgr   = new DriverStationManager(arena, NullLogger<DriverStationManager>.Instance);
        var s     = station ?? AllianceStations.Red1;
        var buf   = new byte[22];
        var ds    = mgr.Stations[s];
        return (mgr, arena, buf, ds);
    }

    // Convenience: decode the control byte for assertions.
    private static bool IsAuto(byte ctrl)    => (ctrl & 0x02) != 0;
    private static bool IsEnabled(byte ctrl) => (ctrl & 0x04) != 0;
    private static bool IsAstop(byte ctrl)   => (ctrl & 0x40) != 0;
    private static bool IsEstop(byte ctrl)   => (ctrl & 0x80) != 0;

    // ── Sequence number ────────────────────────────────────────────────────────

    [Fact]
    public void Buf0_1_EncodeSequenceNumberBigEndian()
    {
        var (mgr, _, buf, ds) = Setup();
        ds.TxSequence = 0x01FF;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x01, buf[0]); // high byte
        Assert.Equal(0xFF, buf[1]); // low byte
    }

    [Fact]
    public void Buf0_1_TxSequence255_EncodesAs0x00FF()
    {
        var (mgr, _, buf, ds) = Setup();
        ds.TxSequence = 255;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x00, buf[0]);
        Assert.Equal(0xFF, buf[1]);
    }

    [Fact]
    public void Buf0_1_TxSequence65535_MaxUint16_EncodesAs0xFFFF()
    {
        var (mgr, _, buf, ds) = Setup();
        ds.TxSequence = 65535;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0xFF, buf[0]);
        Assert.Equal(0xFF, buf[1]);
    }

    [Fact]
    public void Buf0_1_TxSequence65536_WrapsTo0x0000_ViaBytecast()
    {
        // In Cheesy Arena (Go) the packet count is a uint16 and wraps natively.
        // In PossumFMS, TxSequence is an int32; the (byte) casts on buf[0]/buf[1]
        // produce the same 16-bit rollover behaviour automatically.
        var (mgr, _, buf, ds) = Setup();
        ds.TxSequence = 65536; // 0x10000 — upper bits beyond 16 don't appear in buf

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x00, buf[0]);
        Assert.Equal(0x00, buf[1]);
    }

    [Fact]
    public void Buf2_IsAlwaysZero_ProtocolVersion()
    {
        var (mgr, _, buf, ds) = Setup();

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0, buf[2]);
    }

    // ── Control byte — e-stop ──────────────────────────────────────────────────

    [Fact]
    public void ControlByte_WhenDsEstop_EstopBitSet_EnabledBitClear()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto
        ds.Estop = true;

        mgr.EncodeControlPacket(buf, ds);

        Assert.True(IsEstop(buf[3]));
        Assert.False(IsEnabled(buf[3]));
    }

    [Fact]
    public void ControlByte_WhenArenaEstop_EstopBitSet_EnabledBitClear()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch();
        arena.TriggerArenaEstop(); // aborts match, sets ArenaEstop

        mgr.EncodeControlPacket(buf, ds);

        Assert.True(IsEstop(buf[3]));
        Assert.False(IsEnabled(buf[3]));
    }

    [Fact]
    public void ControlByte_WhenNoEstopMatchRunning_EnabledBitSet()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto, no estop

        mgr.EncodeControlPacket(buf, ds);

        Assert.False(IsEstop(buf[3]));
        Assert.True(IsEnabled(buf[3]));
    }

    // ── Control byte — a-stop ──────────────────────────────────────────────────

    [Fact]
    public void ControlByte_WhenAstop_AstopBitSet_EnabledBitClear()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch();
        ds.Astop = true;

        mgr.EncodeControlPacket(buf, ds);

        Assert.True(IsAstop(buf[3]));
        Assert.False(IsEnabled(buf[3]));
    }

    [Fact]
    public void ControlByte_WhenNoAstop_AstopBitClear()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch();

        mgr.EncodeControlPacket(buf, ds);

        Assert.False(IsAstop(buf[3]));
    }

    // ── Control byte — auto mode ───────────────────────────────────────────────

    [Fact]
    public void ControlByte_WhenAutoPhase_AutoBitSet()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto

        mgr.EncodeControlPacket(buf, ds);

        Assert.True(IsAuto(buf[3]));
    }

    [Fact]
    public void ControlByte_WhenNotAutoPhase_AutoBitClear()
    {
        var (mgr, _, buf, ds) = Setup();
        // Idle phase — no match running

        mgr.EncodeControlPacket(buf, ds);

        Assert.False(IsAuto(buf[3]));
    }

    // ── Control byte — bypassed ────────────────────────────────────────────────

    [Fact]
    public void ControlByte_WhenBypassed_EnabledBitClear()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch();
        ds.Bypassed = true;

        mgr.EncodeControlPacket(buf, ds);

        Assert.False(IsEnabled(buf[3]));
    }

    // ── Control byte — not running ─────────────────────────────────────────────

    [Fact]
    public void ControlByte_WhenIdle_AllBitsClear()
    {
        var (mgr, _, buf, ds) = Setup();

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0, buf[3]);
    }

    [Fact]
    public void ControlByte_WhenPreMatch_EnabledBitClear()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();

        mgr.EncodeControlPacket(buf, ds);

        Assert.False(IsEnabled(buf[3]));
    }

    // ── Exact combined control byte values (aligned to Cheesy Arena) ──────────
    //
    // Cheesy Arena Go test asserts exact byte values for specific flag combinations:
    //   Auto=true              → 0x02
    //   Auto=true, Enabled     → 0x06
    //   Enabled (teleop)       → 0x04
    //
    // Note on EStop+Enabled: In Cheesy Arena, Enabled and EStop bits are set
    // independently; their test produces 0x84 (EStop | Enabled) and 0xC4
    // (EStop | AStop | Enabled).  PossumFMS intentionally forces Enabled=false
    // when estop is active (defense-in-depth), so the same scenarios here
    // produce 0x80 and 0xC0 respectively.  Tests below document both behaviours.

    [Fact]
    public void ControlByte_AutoOnly_ExactValue0x02()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto — enabled because match is running

        // Strip the enabled bit off to simulate "auto but not enabled" — not
        // directly reachable through normal phase flow, but the bit encoding
        // must map to 0x02 when only the auto flag is set.  We verify this by
        // confirming auto=true gives bit 0x02 AND enabled also sets bit 0x04.
        // (Equivalent to Cheesy Arena: dsConn.Auto=true, dsConn.Enabled=false.)
        // We reach auto=true + enabled=false by bypassing the station.
        ds.Bypassed = true; // enabled logic: "!estop && !astop && IsMatchRunning && !Bypassed" → false

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x02, buf[3]); // auto bit only
    }

    [Fact]
    public void ControlByte_AutoAndEnabled_ExactValue0x06()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch(); // Auto phase, no stops

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x06, buf[3]); // 0x02 (auto) | 0x04 (enabled)
    }



    [Fact]
    public void ControlByte_EstopOnly_ExactValue0x80()
    {
        // Cheesy Arena (Go) test sequence: Auto=true → 0x02, Auto+Enabled → 0x06,
        // Auto=false → 0x04, then EStop=true → 0x84 (Enabled bit stays, both set
        // independently).  PossumFMS derives enabled as (!estop && ...), so estop
        // forces Enabled=false.  To compare apples-to-apples on the estop bit value
        // we use Idle phase (auto=false, match not running) — same as Go's Auto=false
        // step.  Expected: 0x80 (estop only); Go produces 0x84 (estop | enabled).
        var (mgr, _, buf, ds) = Setup(); // Idle phase — auto=false, match not running
        ds.Estop = true;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x80, buf[3]); // estop bit only; enabled suppressed (differs from Cheesy Arena 0x84)
    }

    [Fact]
    public void ControlByte_EstopAndAstop_ExactValue0xC0()
    {
        // Cheesy Arena (Go): EStop=true, AStop=true, Enabled=true → 0xC4.
        // PossumFMS: estop suppresses enabled → 0xC0.  Idle phase used so auto
        // bit (0x02) is also clear, matching Go's Auto=false precondition.
        var (mgr, _, buf, ds) = Setup(); // Idle phase
        ds.Estop = true;
        ds.Astop = true;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0xC0, buf[3]); // estop (0x80) | astop (0x40); enabled suppressed (differs from Cheesy Arena 0xC4)
    }

    // ── Alliance station index (buf[5]) ────────────────────────────────────────

    [Theory]
    [InlineData(AllianceColor.Red,  StationPosition.One,   0)]
    [InlineData(AllianceColor.Red,  StationPosition.Two,   1)]
    [InlineData(AllianceColor.Red,  StationPosition.Three, 2)]
    [InlineData(AllianceColor.Blue, StationPosition.One,   3)]
    [InlineData(AllianceColor.Blue, StationPosition.Two,   4)]
    [InlineData(AllianceColor.Blue, StationPosition.Three, 5)]
    public void Buf5_EncodesAllianceStationIndex(AllianceColor color, StationPosition pos, byte expectedIndex)
    {
        var station = new AllianceStation(color, pos);
        var (mgr, _, buf, ds) = Setup(station);

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(expectedIndex, buf[5]);
    }

    // ── Match type (buf[6]) ────────────────────────────────────────────────────

    [Theory]
    [InlineData(PossumFMS.Core.Arena.MatchType.None,          0)]
    [InlineData(PossumFMS.Core.Arena.MatchType.Practice,      1)]
    [InlineData(PossumFMS.Core.Arena.MatchType.Qualification, 2)]
    [InlineData(PossumFMS.Core.Arena.MatchType.Playoff,       3)]
    public void Buf6_EncodesMatchType(PossumFMS.Core.Arena.MatchType matchType, byte expected)
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.MatchType = matchType;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(expected, buf[6]);
    }

    // ── Match number (buf[7-8]) ────────────────────────────────────────────────

    [Fact]
    public void Buf7_8_EncodesMatchNumberBigEndian()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.MatchNumber = 0x01F4; // 500

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x01, buf[7]); // high byte
        Assert.Equal(0xF4, buf[8]); // low byte
    }

    [Fact]
    public void Buf7_8_MatchNumberDefault1()
    {
        var (mgr, arena, buf, ds) = Setup();
        // Default MatchNumber is 1

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0x00, buf[7]);
        Assert.Equal(0x01, buf[8]);
    }

    // ── Match repeat (buf[9]) ──────────────────────────────────────────────────

    [Fact]
    public void Buf9_EncodesMatchRepeat()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.MatchRepeat = 3;

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(3, buf[9]);
    }

    // ── Seconds remaining (buf[20-21]) ─────────────────────────────────────────

    [Fact]
    public void Buf20_21_WhenIdle_AreZero()
    {
        var (mgr, _, buf, ds) = Setup();

        mgr.EncodeControlPacket(buf, ds);

        Assert.Equal(0, buf[20]);
        Assert.Equal(0, buf[21]);
    }

    [Fact]
    public void Buf20_21_WhenAutoStarted_EncodeApprox20Seconds()
    {
        var (mgr, arena, buf, ds) = Setup();
        arena.StartPreMatch();
        arena.StartMatch();

        mgr.EncodeControlPacket(buf, ds);

        int secsRemaining = (buf[20] << 8) | buf[21];
        Assert.InRange(secsRemaining, 19, 20);
    }

    // ── Buffer is cleared before encoding ─────────────────────────────────────

    [Fact]
    public void Encode_ClearsBufferBeforeWriting()
    {
        var (mgr, _, buf, ds) = Setup();
        Array.Fill(buf, (byte)0xFF); // Fill with garbage

        mgr.EncodeControlPacket(buf, ds);

        // Buf[2] and buf[4] should always be zero regardless
        Assert.Equal(0, buf[2]);
        Assert.Equal(0, buf[4]);
    }
}
