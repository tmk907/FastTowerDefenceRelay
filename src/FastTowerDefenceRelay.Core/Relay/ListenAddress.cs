using System.Net;

namespace FastTowerDefenceRelay.Relay;

public static class ListenAddress
{
    /// <summary>Resolve an IP address literal or a host name to the address to listen on.</summary>
    public static IPAddress Resolve(string listen) =>
        IPAddress.TryParse(listen, out var address)
            ? address
            : Dns.GetHostAddresses(listen).FirstOrDefault()
              ?? throw new ArgumentException($"could not resolve listen address {listen}");
}
