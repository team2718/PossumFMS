using System.Net;
using System.Net.Sockets;

namespace PossumFMS.Core.DriverStation;

internal interface IDriverStationUdpTransport : IDisposable
{
    void Bind();
    int Receive(Span<byte> buffer, out IPEndPoint remoteEndpoint);
    void Send(ReadOnlySpan<byte> packet, IPEndPoint destination);
}

internal sealed class SocketDriverStationUdpTransport : IDriverStationUdpTransport
{
    private const int FmsUdpListenPort = 1160;
    private Socket? _socket;

    public void Bind()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, FmsUdpListenPort));
            socket.Blocking = false;
            _socket = socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public int Receive(Span<byte> buffer, out IPEndPoint remoteEndpoint)
    {
        var socket = _socket ?? throw new InvalidOperationException("Driver Station UDP transport is not bound.");
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int bytes = socket.ReceiveFrom(buffer, SocketFlags.None, ref remote);
        remoteEndpoint = (IPEndPoint)remote;
        return bytes;
    }

    public void Send(ReadOnlySpan<byte> packet, IPEndPoint destination)
    {
        var socket = _socket ?? throw new InvalidOperationException("Driver Station UDP transport is not bound.");
        socket.SendTo(packet, SocketFlags.None, destination);
    }

    public void Dispose()
    {
        _socket?.Dispose();
        _socket = null;
    }
}