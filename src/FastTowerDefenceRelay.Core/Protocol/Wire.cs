using System.Globalization;

namespace FastTowerDefenceRelay.Protocol;

/// <summary>Fixed sizes and limits of the FVD wire protocol, see docs/wire-protocol.md.</summary>
internal static class Wire
{
    /// <summary>ASCII "FVDN" at the start of every frame.</summary>
    public const uint FrameMagic = 0x4656444e;

    public const int FrameHeaderSize = 32;

    public const int MaxPayloadSize = 1024 * 1024;

    public const int TokenSize = 32;

    public const int MaxRoomNameSize = 64;

    public const int MaxParticipantNameSize = 128;

    public const int MaxRejectionSize = 4096;

    /// <summary>Most rooms one room list may carry, and so most rooms a relay may hold.</summary>
    public const int MaxDirectoryRooms = 4096;

    /// <summary>Oldest minor version with relay admission messages.</summary>
    public const ushort RelayMinor = 1;

    /// <summary>Oldest minor version with the room directory.</summary>
    public const ushort DirectoryMinor = 2;

    /// <summary>Oldest minor version with additional host seats.</summary>
    public const ushort MultiGuestMinor = 3;

    /// <summary>Slot of the first guest seat; the host itself is player 1.</summary>
    public const ulong FirstGuestSlot = 2;

    /// <summary>Most participants in one match, so the highest slot number.</summary>
    public const ulong MaxPlayers = 8;
}

/// <summary>Message types of the FVD protocol; the relay only handles the directory ones.</summary>
internal enum MessageType : ushort
{
    ClientHello = 1,
    ServerAccept = 2,
    ServerReject = 3,
    ParticipantSnapshot = 4,
    StartMatch = 5,
    CommandIntent = 6,
    CommandResult = 7,
    WorldSnapshot = 8,
    WorldDelta = 9,
    ParticipantDisconnected = 10,
    Ping = 11,
    Pong = 12,
    SnapshotRequest = 13,
    RelayConnect = 14,
    RelayReady = 15,
    RelayReject = 16,
    MatchReady = 17,
    RelayCreateRoom = 18,
    RelayRoomCreated = 19,
    RelayListRooms = 20,
    RelayRoomList = 21,
    RelayJoinRoom = 22,
    RelayAttachRoom = 23,
    RelayRoomAttached = 24,
    AuthorityRehostOffer = 25,
    AuthorityRehostProof = 26,
}

/// <summary>A protocol version as carried in every frame header.</summary>
public readonly record struct ProtocolVersion(ushort Major, ushort Minor)
{
    /// <summary>Parse MAJOR.MINOR, such as 22.4.</summary>
    /// <exception cref="FormatException">Not two dot-separated 16-bit numbers.</exception>
    public static ProtocolVersion Parse(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 2
            || !ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            throw new FormatException($"invalid protocol version {value}, expected MAJOR.MINOR");
        }
        return new ProtocolVersion(major, minor);
    }

    public override string ToString() => $"{this.Major}.{this.Minor}";
}

/// <summary>Bytes received from a peer that violate the wire protocol.</summary>
internal sealed class ProtocolException(string message) : Exception(message);
