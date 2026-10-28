using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FactoryTwin
{
    /// <summary>
    /// Minimal MQTT 3.1.1 subscriber for a localhost desktop build.
    /// It deliberately supports only the packets required by this visualizer.
    /// </summary>
    public sealed class MqttTcpClient : IDisposable
    {
        private readonly object sendGate = new object();
        private Thread worker;
        private TcpClient socket;
        private NetworkStream stream;
        private string host;
        private int port;
        private string clientId;
        private string[] topics;
        private volatile bool stopRequested;
        private volatile bool connected;
        private string lastError = string.Empty;
        private ushort packetId = 1;

        public event Action<string, string> MessageReceived;

        public bool IsConnected => connected;
        public string LastError => lastError;

        public void Start(string brokerHost, int brokerPort, string mqttClientId, IEnumerable<string> subscriptions)
        {
            if (worker != null)
            {
                throw new InvalidOperationException("MQTT client has already started.");
            }

            host = brokerHost;
            port = brokerPort;
            clientId = mqttClientId;
            topics = new List<string>(subscriptions).ToArray();
            stopRequested = false;
            worker = new Thread(ConnectionLoop)
            {
                IsBackground = true,
                Name = "FactoryTwin MQTT"
            };
            worker.Start();
        }

        private void ConnectionLoop()
        {
            while (!stopRequested)
            {
                try
                {
                    ConnectOnce();
                    ReadLoop();
                }
                catch (Exception exception)
                {
                    if (!stopRequested)
                    {
                        lastError = exception.Message;
                    }
                }
                finally
                {
                    CleanupSocket();
                }

                if (!stopRequested)
                {
                    Thread.Sleep(2000);
                }
            }
        }

        private void ConnectOnce()
        {
            socket = new TcpClient { NoDelay = true };
            socket.Connect(host, port);
            stream = socket.GetStream();

            using (var body = new MemoryStream())
            {
                WriteMqttString(body, "MQTT");
                body.WriteByte(4); // MQTT 3.1.1
                body.WriteByte(2); // Clean session
                WriteUInt16(body, 30);
                WriteMqttString(body, clientId);
                SendPacket(0x10, body.ToArray());
            }

            int header;
            byte[] payload;
            ReadPacketWithDeadline(out header, out payload, 5000);
            if ((header >> 4) != 2 || payload.Length != 2 || payload[1] != 0)
            {
                throw new IOException("MQTT broker rejected the connection.");
            }

            Subscribe(topics);
            connected = true;
            lastError = string.Empty;
        }

        private void Subscribe(IEnumerable<string> subscriptions)
        {
            using (var body = new MemoryStream())
            {
                WriteUInt16(body, packetId++);
                foreach (string topic in subscriptions)
                {
                    WriteMqttString(body, topic);
                    body.WriteByte(0); // QoS 0
                }
                SendPacket(0x82, body.ToArray());
            }
        }

        private void ReadLoop()
        {
            DateTime nextPing = DateTime.UtcNow.AddSeconds(10);
            while (!stopRequested && socket != null)
            {
                if (stream.DataAvailable)
                {
                    int header;
                    byte[] payload;
                    ReadPacket(out header, out payload);
                    if ((header >> 4) == 3)
                    {
                        HandlePublish(header, payload);
                    }
                    continue;
                }

                if (socket.Client.Poll(0, SelectMode.SelectRead) && socket.Client.Available == 0)
                {
                    throw new IOException("MQTT connection closed by broker.");
                }

                if (DateTime.UtcNow >= nextPing)
                {
                    SendPacket(0xC0, Array.Empty<byte>());
                    nextPing = DateTime.UtcNow.AddSeconds(10);
                }
                Thread.Sleep(20);
            }
        }

        private void HandlePublish(int header, byte[] payload)
        {
            if (payload.Length < 2)
            {
                return;
            }

            int topicLength = (payload[0] << 8) | payload[1];
            int offset = 2;
            if (topicLength <= 0 || offset + topicLength > payload.Length)
            {
                return;
            }

            string topic = Encoding.UTF8.GetString(payload, offset, topicLength);
            offset += topicLength;
            int qos = (header >> 1) & 0x03;
            if (qos > 0)
            {
                offset += 2;
            }
            if (offset > payload.Length)
            {
                return;
            }

            string message = Encoding.UTF8.GetString(payload, offset, payload.Length - offset);
            MessageReceived?.Invoke(topic, message);
        }

        private void ReadPacketWithDeadline(out int header, out byte[] payload, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (!stream.DataAvailable)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("Timed out waiting for MQTT broker response.");
                }
                Thread.Sleep(10);
            }
            ReadPacket(out header, out payload);
        }

        private void ReadPacket(out int header, out byte[] payload)
        {
            header = stream.ReadByte();
            if (header < 0)
            {
                throw new EndOfStreamException();
            }

            int remaining = 0;
            int multiplier = 1;
            int encoded;
            do
            {
                encoded = stream.ReadByte();
                if (encoded < 0 || multiplier > 128 * 128 * 128)
                {
                    throw new IOException("Invalid MQTT remaining length.");
                }
                remaining += (encoded & 127) * multiplier;
                multiplier *= 128;
            } while ((encoded & 128) != 0);

            if (remaining > 1024 * 1024)
            {
                throw new IOException("MQTT packet is too large for the visualizer.");
            }

            payload = new byte[remaining];
            int offset = 0;
            while (offset < remaining)
            {
                int read = stream.Read(payload, offset, remaining - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
        }

        private void SendPacket(byte header, byte[] body)
        {
            lock (sendGate)
            {
                if (stream == null)
                {
                    return;
                }

                stream.WriteByte(header);
                int remaining = body.Length;
                do
                {
                    int encoded = remaining % 128;
                    remaining /= 128;
                    if (remaining > 0)
                    {
                        encoded |= 128;
                    }
                    stream.WriteByte((byte)encoded);
                } while (remaining > 0);

                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
        }

        private static void WriteMqttString(Stream output, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteUInt16(output, (ushort)bytes.Length);
            output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUInt16(Stream output, ushort value)
        {
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)(value & 0xFF));
        }

        private void CleanupSocket()
        {
            connected = false;
            lock (sendGate)
            {
                stream?.Dispose();
                socket?.Close();
                stream = null;
                socket = null;
            }
        }

        public void Dispose()
        {
            stopRequested = true;
            if (connected)
            {
                try
                {
                    SendPacket(0xE0, Array.Empty<byte>());
                }
                catch
                {
                    // The socket may already be closing.
                }
            }
            CleanupSocket();
            if (worker != null && worker.IsAlive)
            {
                worker.Join(1000);
            }
            worker = null;
        }
    }
}
