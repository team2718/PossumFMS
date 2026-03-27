namespace PossumFMS.Core.Database;

public sealed record TeamRecord
{
    public int TeamNumber { get; init; }
    public string Nickname { get; init; } = "";
    public string Name { get; init; } = "";
    public string City { get; init; } = "";
    public string StateProv { get; init; } = "";
    public string Country { get; init; } = "";
    public string SchoolName { get; init; } = "";
    public int RookieYear { get; init; }

    /// <summary>
    /// Raw base64-encoded PNG image (40x40) from The Blue Alliance avatar media.
    /// Null if the team has not uploaded an avatar.
    /// Display using: data:image/png;base64,{AvatarBase64}
    /// </summary>
    public string? AvatarBase64 { get; init; }
}
