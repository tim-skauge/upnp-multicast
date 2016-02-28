using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace UPnPMulticast
{
    class Program
    {
        static void Main(string[] args)
        {
            if (File.Exists("upnp-multicast.txt"))
                File.Delete("upnp-multicast.txt");

            if (File.Exists("devices.txt"))
                File.Delete("devices.txt");

            if (Directory.Exists("xml"))
                Directory.Delete("xml", recursive: true);

            Directory.CreateDirectory("xml");

            //var localEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var localEndPoint = new IPEndPoint(Dns.GetHostEntry(Dns.GetHostName()).AddressList.First(ip => ip.AddressFamily == AddressFamily.InterNetwork), 0);
            var multicastEndPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(localEndPoint);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(multicastEndPoint.Address, IPAddress.Any));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            Console.WriteLine("UDP-Socket setup done...\r\n");

            const string searchString = "M-SEARCH * HTTP/1.1\r\nHOST:239.255.255.250:1900\r\nMAN:\"ssdp:discover\"\r\nST:ssdp:all\r\nMX:3\r\n\r\n";

            socket.SendTo(Encoding.UTF8.GetBytes(searchString), SocketFlags.None, multicastEndPoint);

            Console.WriteLine("M-Search sent...\r\n");

            var receiveBuffer = new byte[64000];
            var locations = new List<string>();

            while (true)
            {
                if (socket.Available <= 0)
                    continue;

                var receivedBytes = socket.Receive(receiveBuffer, SocketFlags.None);
                if (receivedBytes <= 0)
                    continue;

                var recieved = Encoding.UTF8.GetString(receiveBuffer, 0, receivedBytes);
                File.AppendAllText("upnp-multicast.txt", recieved);

                var location = recieved
                    .Split(Environment.NewLine.ToCharArray())
                    .Where(x => x.StartsWith("Location: ", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Substring("Location: ".Length))
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(location) || locations.Contains(location))
                    continue;

                locations.Add(location);

                try
                {
                    var document = XDocument.Load(location);

                    var ns = document.Root.GetDefaultNamespace();

                    var builder = new StringBuilder();

                    var friendlyName = document.Descendants(ns + "friendlyName").FirstOrDefault()?.Value;
                    if (!string.IsNullOrWhiteSpace(friendlyName))
                        builder.AppendLine("Friendly name: " + friendlyName);

                    var manufacturer = document.Descendants(ns + "manufacturer").FirstOrDefault()?.Value;
                    if (!string.IsNullOrWhiteSpace(manufacturer))
                        builder.AppendLine("Manufacturer: " + manufacturer);

                    var modelType = document.Descendants(ns + "modelType").FirstOrDefault()?.Value;
                    if (!string.IsNullOrWhiteSpace(modelType))
                        builder.AppendLine("Model type: " + modelType);

                    var modelName = document.Descendants(ns + "modelName").FirstOrDefault()?.Value;
                    if (!string.IsNullOrWhiteSpace(modelName))
                        builder.AppendLine("Model name: " + modelName);

                    var modelDescription = document.Descendants(ns + "modelDescription").FirstOrDefault()?.Value;
                    if (!string.IsNullOrWhiteSpace(modelDescription))
                        builder.AppendLine("Model description: " + modelDescription);

                    var ipaddress = new Uri(location, UriKind.Absolute).GetLeftPart(UriPartial.Authority).Remove(0, "http://".Length);
                    builder.AppendLine("IP Address: " + ipaddress);

                    builder.AppendLine("More at: " + location);
                    builder.AppendLine();

                    File.AppendAllText("devices.txt", builder.ToString());
                    Console.WriteLine(builder.ToString());

                    document.Save("xml\\" + ipaddress.Replace(":", "-") + ".xml");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to read data from " + location);
                    Console.WriteLine(e.Message);
                }
            }
        }
    }
}
