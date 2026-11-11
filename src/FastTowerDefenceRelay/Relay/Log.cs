using System.Net;
using FastTowerDefenceRelay.Protocol;

namespace FastTowerDefenceRelay.Relay;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Relay listening on {EndPoint} with {Access} access, protocol {Protocol}")]
    public static partial void Listening(ILogger logger, EndPoint endPoint, string access, ProtocolVersion protocol);

    [LoggerMessage(Level = LogLevel.Information, Message = "room_created room={RoomId}")]
    public static partial void RoomCreated(ILogger logger, ulong roomId);

    [LoggerMessage(Level = LogLevel.Information, Message = "host_slot_reserved room={RoomId} slot={Slot}")]
    public static partial void HostSlotReserved(ILogger logger, ulong roomId, ulong slot);

    [LoggerMessage(Level = LogLevel.Information, Message = "pair_connected room={RoomId} slot={Slot}")]
    public static partial void PairConnected(ILogger logger, ulong roomId, ulong slot);

    [LoggerMessage(Level = LogLevel.Information, Message = "forwarding_started room={RoomId} slot={Slot}")]
    public static partial void ForwardingStarted(ILogger logger, ulong roomId, ulong slot);

    [LoggerMessage(Level = LogLevel.Information, Message = "forwarding_ended room={RoomId} slot={Slot}")]
    public static partial void ForwardingEnded(ILogger logger, ulong roomId, ulong slot);

    [LoggerMessage(Level = LogLevel.Warning, Message = "forwarding_error room={RoomId} slot={Slot}")]
    public static partial void ForwardingError(ILogger logger, ulong roomId, ulong slot, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected {RemoteEndPoint}: {Reason}")]
    public static partial void Rejected(ILogger logger, EndPoint? remoteEndPoint, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relay connection {RemoteEndPoint} ended with an error")]
    public static partial void ConnectionError(ILogger logger, EndPoint? remoteEndPoint, Exception exception);
}
