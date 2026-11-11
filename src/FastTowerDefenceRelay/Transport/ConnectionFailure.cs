using System.Net.Sockets;

namespace FastTowerDefenceRelay.Transport;

/// <summary>Failures caused by a peer that left, reset or stalled its connection.</summary>
internal static class ConnectionFailure
{
    public static bool Is(Exception exception) =>
        exception is IOException or SocketException or TimeoutException or ObjectDisposedException;
}
