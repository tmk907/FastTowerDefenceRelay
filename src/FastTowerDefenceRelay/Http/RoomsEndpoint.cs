using System.Text;
using System.Text.Json.Serialization;
using FastTowerDefenceRelay.Relay;

namespace FastTowerDefenceRelay.Http;

/// <summary>The public room listing, see docs/http-api.md.</summary>
internal static class RoomsEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/rooms", GetRoomsAsync);

    private static async Task<IResult> GetRoomsAsync(HttpContext context, RoomDirectory directory,
                                                    CancellationToken cancellationToken)
    {
        var rooms = await directory.StatusAsync(cancellationToken);
        var listing = new RoomListing(
            new ProtocolInfo(directory.Protocol.Major, directory.Protocol.Minor),
            rooms.Select(room => new RoomInfo(
                    room.Summary.RoomId,
                    Encoding.UTF8.GetString(room.Summary.RoomName),
                    Encoding.UTF8.GetString(room.Summary.HostName),
                    room.PlayerCount,
                    room.PlayerCapacity))
                .ToList());

        context.Response.Headers.CacheControl = "no-store";
        // The listing is public, so web pages on any origin may read it.
        context.Response.Headers.AccessControlAllowOrigin = "*";
        return TypedResults.Json(listing, RelayJsonContext.Default.RoomListing);
    }
}

internal sealed record RoomListing(ProtocolInfo Protocol, IReadOnlyList<RoomInfo> Rooms);

internal sealed record ProtocolInfo(int Major, int Minor);

internal sealed record RoomInfo(ulong Id, string Name, string Host, int Players, int Capacity);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RoomListing))]
internal sealed partial class RelayJsonContext : JsonSerializerContext;
