using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static ManualResetEvent _quitEvent = new ManualResetEvent(false);

    static async Task Main(string[] args)
    {
        List<IPAddress> activeDevices = new List<IPAddress>();
        int startPort = 30;

        Console.WriteLine("Scanning network for devices with port 34567 open...");

        var ips = GetAllLocalNetworkIPs();
        var portOpenTasks = new List<Task>();

        foreach (var ip in ips)
        {
            portOpenTasks.Add(CheckAndAddActiveDevice(ip, activeDevices));
        }

        await Task.WhenAll(portOpenTasks);

        foreach (var deviceIp in activeDevices)
        {
            int localPort = startPort++;
            if (startPort > 65535) break; // Avoid exceeding port range

            Console.WriteLine($"Forwarding port 34567 from {deviceIp} to 100.96.247.31:{localPort}");
            ForwardPort(deviceIp, 34567, localPort);
        }

        Console.WriteLine("Port forwarding setup complete. Press Ctrl+C to exit.");

        Console.CancelKeyPress += (sender, e) => {
            e.Cancel = true;
            _quitEvent.Set();
        };

        _quitEvent.WaitOne(); // Wait until the user presses Ctrl+C
    }

    static IEnumerable<IPAddress> GetAllLocalNetworkIPs()
    {
        List<IPAddress> ipAddresses = new List<IPAddress>();
        IPAddress startIp = IPAddress.Parse("192.168.25.1");
        IPAddress endIp = IPAddress.Parse("192.168.25.254");

        foreach (var ip in GetIpRange(startIp, endIp))
        {
            ipAddresses.Add(ip);
        }

        return ipAddresses;
    }

    static IEnumerable<IPAddress> GetIpRange(IPAddress startIp, IPAddress endIp)
    {
        uint startIpUint = IpToUint(startIp);
        uint endIpUint = IpToUint(endIp);

        for (uint ip = startIpUint; ip <= endIpUint; ip++)
        {
            yield return UintToIp(ip);
        }
    }

    static uint IpToUint(IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();
        Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    static IPAddress UintToIp(uint ipUint)
    {
        byte[] bytes = BitConverter.GetBytes(ipUint);
        Array.Reverse(bytes);
        return new IPAddress(bytes);
    }

    static async Task<bool> IsReachableAsync(IPAddress ip, int timeout = 5000)
    {
        try
        {
            Ping ping = new Ping();
            PingReply reply = await ping.SendPingAsync(ip, timeout);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    static async Task<bool> IsPortOpen(IPAddress ip, int port, int timeout = 500)
    {
        try
        {
            using (var client = new TcpClient())
            {
                var cancellationSource = new CancellationTokenSource(timeout);
                await client.ConnectAsync(ip, port);
                return client.Connected;
            }
        }
        catch
        {
            return false;
        }
    }

    static async Task CheckAndAddActiveDevice(IPAddress ip, List<IPAddress> activeDevices)
    {
        if (await IsReachableAsync(ip))
        {
            if (await IsPortOpen(ip, 34567))
            {
                lock (activeDevices)
                {
                    activeDevices.Add(ip);
                }
                Console.WriteLine($"Device found: {ip}");
            }
        }
    }

    static void ForwardPort(IPAddress remoteIp, int remotePort, int localPort)
    {
        TcpListener listener = new TcpListener(IPAddress.Parse("100.96.247.31"), localPort);
        listener.Start();

        listener.BeginAcceptTcpClient(async (ar) =>
        {
            TcpClient localClient = listener.EndAcceptTcpClient(ar);
            TcpClient remoteClient = new TcpClient();
            await remoteClient.ConnectAsync(remoteIp, remotePort);

            NetworkStream localStream = localClient.GetStream();
            NetworkStream remoteStream = remoteClient.GetStream();

            Task localToRemote = localStream.CopyToAsync(remoteStream);
            Task remoteToLocal = remoteStream.CopyToAsync(localStream);

            await Task.WhenAll(localToRemote, remoteToLocal);

            localClient.Close();
            remoteClient.Close();
        }, null);
    }
}
