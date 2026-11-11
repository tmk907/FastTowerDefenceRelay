using System.Security.Cryptography;

namespace FastTowerDefenceRelay.Protocol;

/// <summary>A 256-bit player bearer token, compared in constant time.</summary>
public sealed class RelayToken : IEquatable<RelayToken>
{
    private readonly byte[] bytes;

    public RelayToken(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Wire.TokenSize)
        {
            throw new ArgumentException($"A relay token has {Wire.TokenSize} bytes", nameof(bytes));
        }
        this.bytes = bytes.ToArray();
    }

    /// <summary>The all-zero token that clients send to a public relay.</summary>
    public bool IsZero => this.bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0;

    /// <summary>Parse 64 hexadecimal characters.</summary>
    /// <exception cref="FormatException">Not exactly 64 hexadecimal characters.</exception>
    public static RelayToken FromHex(string hex)
    {
        if (hex.Length != Wire.TokenSize * 2)
        {
            throw new FormatException($"relay player tokens must contain exactly {Wire.TokenSize} bytes ({Wire.TokenSize * 2} hexadecimal characters)");
        }
        try
        {
            return new RelayToken(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            throw new FormatException("relay player tokens must be hexadecimal");
        }
    }

    public void CopyTo(Span<byte> destination) => this.bytes.CopyTo(destination);

    public bool Equals(RelayToken? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(this.bytes, other.bytes);

    public override bool Equals(object? obj) => this.Equals(obj as RelayToken);

    // Tokens are only compared, never used as keys: keep the secret out of hashes.
    public override int GetHashCode() => 0;
}
