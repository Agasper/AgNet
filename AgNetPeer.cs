using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace AgNet
{
    
    public abstract class AgNetPeer : IDisposable
    {
        public delegate void DOnMessage(AgNetSession session, IncomingMessage message);
        public delegate void DOnSessionStateChanged(AgNetSession session);

        internal const int tickTimeout = 500;

        public DOnMessage OnMessageEvent;
        public DOnSessionStateChanged OnSessionStateChangedEvent;

        public bool Debug { get; set; }
        public EndPoint ListenEndpoint { get; protected set; }
        public EndPoint RemoteEndpoint { get; protected set; }
        public double DropChanceTest { get; set; }

        protected const int maxBufferSize = 1408;
        protected Socket serverSocket;
        protected Thread readThread;
        protected Timer timer;
        protected bool disposed;

        public AgNetPeer()
        {
        }

        public AgNetPeer(bool debug) : this()
        {
            this.Debug = debug;
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            if (this.serverSocket != null)
            {
                serverSocket.Close();
                serverSocket = null;
            }

            if (this.timer != null)
            {
                this.timer.Dispose();
                this.timer = null;
            }

            if (this.readThread != null)
            {
                this.readThread.Abort();
                this.readThread = null;
            }
        }

        protected void StartThread()
        {
            this.readThread = new Thread(new ThreadStart(Receive));
            this.readThread.Start();
            this.timer = new Timer(new TimerCallback((o) => OnTimerTickInternal()), null, tickTimeout, System.Threading.Timeout.Infinite);
        }

        protected void InitSocket()
        {
            this.serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            this.serverSocket.ReceiveBufferSize = maxBufferSize;
            this.serverSocket.SendBufferSize = maxBufferSize;

            try
            {
                const uint IOC_IN = 0x80000000;
                const uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                this.serverSocket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            }
            catch
            {
                // ignore; SIO_UDP_CONNRESET not supported on this platform
            }
        }

        protected void Receive()
        {

            while (!disposed)
            {
                try
                {
                    EndPoint refEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buffer = new byte[maxBufferSize];
                    int bytesRead = this.serverSocket.ReceiveFrom(buffer, ref refEndPoint);

                    if (DropChanceTest > 0)
                    {
                        Random rnd = new Random();
                        if (rnd.NextDouble() < DropChanceTest)
                            continue;
                    }

                    IncomingMessage message = new IncomingMessage(buffer, refEndPoint);
                    System.Diagnostics.Debug.WriteLineIf(this.Debug, string.Format("Received {0}", message));
                    
                    OnMessageInternal(message);

                    //Console.WriteLine("Rec " + this.ToString() + " " + message.Sequence);
                }
                catch (AgNetException ex)
                {

                }
                catch (SocketException ex)
                {
                    if (this.disposed)
                        return;

                    System.Diagnostics.Debug.WriteLineIf(this.Debug, string.Format("Socket exception at AgNetServer.Receive(): " + ex.Message + ". " + ex.StackTrace));
                }
                catch (ThreadAbortException ex)
                {
                    return;
                }
            }
        }

        internal abstract void OnMessageInternal(IncomingMessage msg);
        internal virtual void OnSessionStateChangedInternal(AgNetSession session)
        {
            if (OnSessionStateChangedEvent != null)
                OnSessionStateChangedEvent(session);
        }
        internal virtual void OnTimerTickInternal()
        {
            this.timer = new Timer(new TimerCallback((o) => OnTimerTickInternal()), null, tickTimeout, System.Threading.Timeout.Infinite);
        }


        protected static IPEndPoint GetIPEndPointFromHostName(string hostName, int port)
        {
            IPAddress[] addresses = System.Net.Dns.GetHostAddresses(hostName)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
            if (addresses.Length == 0)
            {
                throw new ArgumentException(
                    "Unable to retrieve address from specified host name.",
                    "hostName"
                );
            }

            return new IPEndPoint(addresses[0], port);
        }

        internal virtual void SendMessage(EndPoint endPoint, OutgoingMessage msg)
        {
            if (disposed)
                return;

            msg.LastSentTime = DateTime.UtcNow;
            msg.SentTimes++;

            byte[] sendBuffer = msg.ToByteArray();

            SocketAsyncEventArgs e = new SocketAsyncEventArgs();
            e.SetBuffer(sendBuffer, 0, sendBuffer.Length);
            e.RemoteEndPoint = endPoint;
            serverSocket.SendToAsync(e);
        }
		
    }
}
