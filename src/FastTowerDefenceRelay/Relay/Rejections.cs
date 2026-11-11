namespace FastTowerDefenceRelay.Relay;

/// <summary>RELAY_REJECT reasons; game clients show them to players.</summary>
internal static class Rejections
{
    public const string CapacityFull = "relay connection capacity is full";
    public const string RequestTimedOut = "relay directory request timed out";
    public const string MalformedRequest = "malformed relay directory request";
    public const string Unsupported = "unsupported relay directory request";
    public const string Unauthorized = "player token is not authorized";

    public const string MalformedListing = "malformed room listing request";

    public const string MalformedCreation = "malformed room creation request";
    public const string DirectoryFull = "relay room directory is full";

    public const string MalformedAttachment = "malformed host room attachment";
    public const string AttachNeedsMultiGuest = "multi-guest room attachment requires protocol minor 3";
    public const string InvalidAttachment = "invalid or full host room attachment";

    public const string MalformedJoin = "malformed room join request";
    public const string RoomUnavailable = "relay room is no longer available";
    public const string SameTokenAsHost = "host and guest must use different player tokens";
    public const string TokenAlreadyAdmitted = "player token is already admitted to this room";
    public const string HostGone = "relay room host has disconnected or has no free channels";
    public const string HostLeftDuringJoin = "relay room host disconnected before join";
}
