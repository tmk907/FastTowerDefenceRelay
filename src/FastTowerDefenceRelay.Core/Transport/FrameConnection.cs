using System.Net;
using System.Net.Sockets;
using FastTowerDefenceRelay.Protocol;

namespace FastTowerDefenceRelay.Transport;

/// <summary>
/// One TCP connection carrying FVD frames.
/// </summary>
/// <remarks>
/// One task may receive while others send: sends are serialized so frames never interleave.
/// </remarks>
internal sealed class FrameConnection : IDisposable
{
    private readonly Socket socket;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly TimeSpan sendTimeout;
    private int closed;

    public FrameConnection(Socket socket, TimeSpan sendTimeout)
    {
        // Gameplay frames must not wait for another small packet or a delayed ACK.
        socket.NoDelay = true;
        this.socket = socket;
        this.stream = new NetworkStream(socket, ownsSocket: true);
        this.sendTimeout = sendTimeout;
        this.RemoteEndPoint = socket.RemoteEndPoint;
    }

    public EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Receive the next complete frame.
    /// </summary>
    /// <returns>The frame, or null after the peer closed the connection between frames.</returns>
    /// <exception cref="ProtocolException">An invalid header or a connection closed inside a frame.</exception>
    public async Task<ReceivedFrame?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var header = new byte[Wire.FrameHeaderSize];
        var received = await this.stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (received == 0)
        {
            return null;
        }
        if (received < header.Length)
        {
            throw new ProtocolException("Peer closed the connection inside a frame header");
        }

        var parsed = FrameHeader.Parse(header);
        var bytes = new byte[Wire.FrameHeaderSize + parsed.PayloadSize];
        header.CopyTo(bytes, 0);
        var payload = bytes.AsMemory(Wire.FrameHeaderSize);
        if (await this.stream.ReadAtLeastAsync(payload, payload.Length, throwOnEndOfStream: false, cancellationToken) < payload.Length)
        {
            throw new ProtocolException("Peer closed the connection inside a frame payload");
        }
        return new ReceivedFrame(parsed, bytes);
    }

    /// <summary>Send one complete encoded frame.</summary>
    /// <exception cref="TimeoutException">The peer did not accept the frame within the send timeout.</exception>
    public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        await this.sendLock.WaitAsync(cancellationToken);
        try
        {
            // A silent or backpressured peer must not block the relay forever.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(this.sendTimeout);
            try
            {
                await this.stream.WriteAsync(frame, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out sending a frame ({this.sendTimeout.TotalSeconds:0}s)");
            }
        }
        finally
        {
            this.sendLock.Release();
        }
    }

    /// <summary>
    /// Nonblocking check for a peer that has already closed the connection.
    /// </summary>
    /// <remarks>
    /// Must not be called while a receive is pending. A disconnect not yet
    /// observed by TCP cannot be detected here.
    /// </remarks>
    public bool PeerConnected()
    {
        if (Volatile.Read(ref this.closed) != 0)
        {
            return false;
        }
        try
        {
            // Readable without pending bytes means the peer sent FIN or the socket failed.
            return !(this.socket.Poll(0, SelectMode.SelectRead) && this.socket.Available == 0);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Close after the peer has read everything sent so far.
    /// </summary>
    /// <remarks>
    /// Closing a socket with unread received bytes resets the connection,
    /// which can destroy a rejection still in flight. So this half-closes,
    /// discards whatever the peer still sends and closes once the peer does,
    /// or after the timeout.
    /// </remarks>
    public async Task DrainAndCloseAsync(TimeSpan timeout)
    {
        try
        {
            this.socket.Shutdown(SocketShutdown.Send);
            using var deadline = new CancellationTokenSource(timeout);
            var discard = new byte[4096];
            while (await this.stream.ReadAsync(discard, deadline.Token) > 0)
            {
            }
        }
        catch (Exception exception) when (ConnectionFailure.Is(exception) || exception is OperationCanceledException)
        {
            // The peer reset the connection or kept it open too long; close regardless.
        }
        this.Close();
    }

    /// <summary>Close the connection; later calls have no effect.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref this.closed, 1) != 0)
        {
            return;
        }
        try
        {
            this.socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // The peer may already have reset the connection.
        }
        this.stream.Dispose();
    }

    // The send lock stays undisposed: concurrent senders may still be waiting
    // on it, and without a wait handle it holds no unmanaged resources.
    public void Dispose() => this.Close();
}
