using Microsoft.Extensions.Logging.Abstractions;
using PossumFMS.Core.Arena;
using PossumFMS.Core.DriverStation;
using Xunit;

namespace PossumFMS.Core.Tests.DriverStation;

/// <summary>
/// Tests for <see cref="DriverStationManager.ParseStatusPacket"/>.
///
/// Packet layout (DS → FMS UDP):
///   [0-1]  Sequence number (ignored by FMS)
///   [2]    Reserved
///   [3]    Status flags: 0x08=RioLinked, 0x10=RadioLinked, 0x20=RobotLinked
///   [4-5]  Team number, big-endian
///   [6]    Battery voltage integer part (volts)
///   [7]    Battery voltage fractional part (value / 256 V)
///   [8+]   Variable-length tags: [length] [type] [data * (length-1)]
///            Tag type 1, length 6: [lost_hi] [lost_lo] [?] [?] [trip_ms]
/// </summary>
public sealed class ParseStatusPacketTests
{
    private static DriverStationManager CreateManager(int teamNumber = 1234, AllianceStation? station = null)
    {
        var arena = new PossumFMS.Core.Arena.Arena();
        var mgr = new DriverStationManager(arena, NullLogger<DriverStationManager>.Instance);
        mgr.AssignTeam(station ?? AllianceStations.Red1, teamNumber);
        return mgr;
    }

    /// <summary>
    /// Builds a minimal 8-byte status packet for the given team with all other fields zero.
    /// </summary>
    private static byte[] MinimalPacket(int teamNumber)
    {
        var pkt = new byte[8];
        pkt[4] = (byte)(teamNumber >> 8);
        pkt[5] = (byte)(teamNumber & 0xFF);
        return pkt;
    }

    // ── Team number routing ────────────────────────────────────────────────────

    [Fact]
    public void UnknownTeamNumber_DoesNotThrow_AndDoesNotUpdateAnyStation()
    {
        var mgr = CreateManager(teamNumber: 1234);
        var ds = mgr[AllianceStations.Red1];

        // Packet for a completely different team
        var pkt = MinimalPacket(9999);
        mgr.ParseStatusPacket(pkt);

        Assert.False(ds.DsLinked);
    }

    [Fact]
    public void KnownTeamNumber_SetsDsLinkedTrue()
    {
        var mgr = CreateManager(teamNumber: 1234);

        mgr.ParseStatusPacket(MinimalPacket(1234));

        Assert.True(mgr[AllianceStations.Red1].DsLinked);
    }

    [Fact]
    public void KnownTeamNumber_UpdatesLastPacketTime()
    {
        var mgr = CreateManager(teamNumber: 1234);
        var before = DateTime.UtcNow;

        mgr.ParseStatusPacket(MinimalPacket(1234));

        Assert.True(mgr[AllianceStations.Red1].LastPacketTime >= before);
    }

    [Fact]
    public void TeamNumberBigEndian_LargeTeamNumber()
    {
        // Team 9999 = 0x270F → byte[4]=0x27, byte[5]=0x0F
        var mgr = new PossumFMS.Core.Arena.Arena()
            is var arena
            ? new DriverStationManager(arena, NullLogger<DriverStationManager>.Instance)
            : throw new InvalidOperationException();
        mgr.AssignTeam(AllianceStations.Blue3, 9999);

        var pkt = MinimalPacket(9999);
        Assert.Equal(0x27, pkt[4]);
        Assert.Equal(0x0F, pkt[5]);

        mgr.ParseStatusPacket(pkt);

        Assert.True(mgr[AllianceStations.Blue3].DsLinked);
    }

    // ── Status flags ───────────────────────────────────────────────────────────

    [Fact]
    public void NoFlags_AllLinkFieldsFalse()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x00;

        mgr.ParseStatusPacket(pkt);

        var ds = mgr[AllianceStations.Red1];
        Assert.False(ds.RioLinked);
        Assert.False(ds.RadioLinked);
        Assert.False(ds.RobotLinked);
    }

    [Fact]
    public void RioLinkedFlag_0x08_SetsRioLinked()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x08;

        mgr.ParseStatusPacket(pkt);

        var ds = mgr[AllianceStations.Red1];
        Assert.True(ds.RioLinked);
        Assert.False(ds.RadioLinked);
        Assert.False(ds.RobotLinked);
    }

    [Fact]
    public void RadioLinkedFlag_0x10_SetsRadioLinked()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x10;

        mgr.ParseStatusPacket(pkt);

        var ds = mgr[AllianceStations.Red1];
        Assert.False(ds.RioLinked);
        Assert.True(ds.RadioLinked);
        Assert.False(ds.RobotLinked);
    }

    [Fact]
    public void RobotLinkedFlag_0x20_SetsRobotLinked()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x20;

        mgr.ParseStatusPacket(pkt);

        var ds = mgr[AllianceStations.Red1];
        Assert.False(ds.RioLinked);
        Assert.False(ds.RadioLinked);
        Assert.True(ds.RobotLinked);
    }

    [Fact]
    public void AllThreeFlags_0x38_SetsAllLinked()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x38;

        mgr.ParseStatusPacket(pkt);

        var ds = mgr[AllianceStations.Red1];
        Assert.True(ds.RioLinked);
        Assert.True(ds.RadioLinked);
        Assert.True(ds.RobotLinked);
    }

    // ── Battery voltage ────────────────────────────────────────────────────────

    [Fact]
    public void BatteryVoltage_WhenRobotLinked_DecodesCorrectly()
    {
        // 12V integer + 128/256 = 12.5V
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x20; // RobotLinked
        pkt[6] = 12;   // integer volts
        pkt[7] = 128;  // fractional: 128/256 = 0.5V

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(12.5, mgr[AllianceStations.Red1].BatteryVoltage, precision: 3);
    }

    [Fact]
    public void BatteryVoltage_WhenRobotLinked_ZeroFraction()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x20; // RobotLinked
        pkt[6] = 13;
        pkt[7] = 0;

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(13.0, mgr[AllianceStations.Red1].BatteryVoltage, precision: 3);
    }

    [Fact]
    public void BatteryVoltage_WhenNotRobotLinked_StaysZero()
    {
        var mgr = CreateManager();
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x00; // no flags
        pkt[6] = 12;
        pkt[7] = 128;

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(0.0, mgr[AllianceStations.Red1].BatteryVoltage);
    }

    [Fact]
    public void BatteryVoltage_WhenRobotLinked_UpdatesLastRobotLinkedTime()
    {
        var mgr = CreateManager();
        var before = DateTime.UtcNow;
        var pkt = MinimalPacket(1234);
        pkt[3] = 0x20;

        mgr.ParseStatusPacket(pkt);

        Assert.True(mgr[AllianceStations.Red1].LastRobotLinkedTime >= before);
    }

    // ── Tag parsing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a type-1 tag (length=6) with the given missed-packet count and trip time.
    /// Tag layout following the length byte: [type=1] [lost_hi] [lost_lo] [?] [?] [trip_ms]
    /// </summary>
    private static byte[] PacketWithTag1(int teamNumber, int missedPackets, byte tripTimeMs)
    {
        // Parser reads: length byte at [8] increments i to 9, then checks (9 + 6 <= length).
        // So packet must be at least 15 bytes.  Data indices relative to i=9:
        //   [i+0]=type=1, [i+1]=lost_hi, [i+2]=lost_lo, [i+3]=?, [i+4]=?, [i+5]=trip_ms
        //   → pkt[9]=type, pkt[10]=lost_hi, pkt[11]=lost_lo, pkt[14]=trip_ms
        var pkt = new byte[15];
        pkt[4] = (byte)(teamNumber >> 8);
        pkt[5] = (byte)(teamNumber & 0xFF);
        pkt[8]  = 6;                            // length
        pkt[9]  = 1;                            // type
        pkt[10] = (byte)(missedPackets >> 8);   // lost_hi  (i+1)
        pkt[11] = (byte)(missedPackets & 0xFF); // lost_lo  (i+2)
        // pkt[12], pkt[13] = reserved zeros    (i+3, i+4)
        pkt[14] = tripTimeMs;                   // trip_ms  (i+5)
        return pkt;
    }

    [Fact]
    public void Tag1_SetsMissedPacketCount()
    {
        var mgr = CreateManager();
        var pkt = PacketWithTag1(1234, missedPackets: 500, tripTimeMs: 0);

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(500, mgr[AllianceStations.Red1].MissedPacketCount);
    }

    [Fact]
    public void Tag1_MissedPacketsBigEndian_LargeValue()
    {
        // 0x0200 = 512
        var mgr = CreateManager();
        var pkt = PacketWithTag1(1234, missedPackets: 512, tripTimeMs: 0);

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(512, mgr[AllianceStations.Red1].MissedPacketCount);
    }

    [Fact]
    public void Tag1_SetsDsRobotTripTimeMs()
    {
        var mgr = CreateManager();
        var pkt = PacketWithTag1(1234, missedPackets: 0, tripTimeMs: 42);

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(42, mgr[AllianceStations.Red1].DsRobotTripTimeMs);
    }

    [Fact]
    public void UnknownTagType_DoesNotSetMissedPacketCount()
    {
        var mgr = CreateManager();
        // Packet must be exactly 15 bytes so the bounds check (i=9, i+length=15 <= 15)
        // passes and the parser actually reads the tag type byte. With 14 bytes the
        // loop would break on the bounds check before ever inspecting the type, which
        // would not test the unknown-type discrimination logic.
        var pkt = new byte[15];
        pkt[4] = 1234 >> 8;
        pkt[5] = 1234 & 0xFF;
        pkt[8]  = 6;    // length byte at [8]: i becomes 9, check 9+6=15 <= 15 → enters body
        pkt[9]  = 2;    // type 2 is unknown — parser should skip without setting MissedPacketCount
        pkt[10] = 0x01; // would be lost_hi if incorrectly parsed as type 1
        pkt[11] = 0xF4; // 500 decimal

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(0, mgr[AllianceStations.Red1].MissedPacketCount);
    }

    [Fact]
    public void MalformedTag_OverrunsBuffer_DoesNotThrow()
    {
        var mgr = CreateManager();
        // Report length=100 but only provide 10 bytes total — loop should break cleanly
        var pkt = new byte[10];
        pkt[4] = 1234 >> 8;
        pkt[5] = 1234 & 0xFF;
        pkt[8] = 100; // claimed length exceeds remaining bytes

        var ex = Record.Exception(() => mgr.ParseStatusPacket(pkt));
        Assert.Null(ex);
    }

    [Fact]
    public void ZeroLengthTag_DoesNotThrow_AndLoopContinues()
    {
        var mgr = CreateManager();
        // Two zero-length tags followed by a valid type-1 tag.
        // After consuming length at [8] and [9], the real tag starts at [10].
        // length=pkt[10]=6, i becomes 11.  Check: 11+6=17 <= 17 → OK.
        // Data: pkt[11]=type, pkt[12]=lost_hi, pkt[13]=lost_lo, pkt[14,15]=reserved, pkt[16]=trip_ms
        var pkt = new byte[17];
        pkt[4] = 1234 >> 8;
        pkt[5] = 1234 & 0xFF;
        pkt[8]  = 0; // zero-length — skipped by continue
        pkt[9]  = 0; // zero-length — skipped again
        pkt[10] = 6; // valid tag length
        pkt[11] = 1; // type
        pkt[12] = 0x00; // lost_hi
        pkt[13] = 0x07; // lost_lo → 7 missed
        // pkt[14], pkt[15] = reserved zeros
        pkt[16] = 10; // trip_ms (i+5 = 11+5 = 16)

        mgr.ParseStatusPacket(pkt);

        Assert.Equal(7, mgr[AllianceStations.Red1].MissedPacketCount);
        Assert.Equal(10, mgr[AllianceStations.Red1].DsRobotTripTimeMs);
    }
}
