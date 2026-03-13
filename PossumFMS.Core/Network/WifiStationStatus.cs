namespace PossumFMS.Core.Network;

/// <summary>Per-station WiFi status read from the VH-113 access point's /status API.</summary>
public sealed class WifiStationStatus
{
    public int    TeamId            { get; set; }
    public bool   RadioLinked       { get; set; }
    public double BandwidthUsedMbps { get; set; }
    public double RxRateMbps        { get; set; }
    public double TxRateMbps        { get; set; }
    public int    SignalNoiseRatio  { get; set; }

    /// <summary>0=unknown, 1=caution, 2=warning, 3=good, 4=excellent</summary>
    public int ConnectionQuality   { get; set; }
}
