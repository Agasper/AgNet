/* Copyright (c) 2014 Alexander Melkozerov

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom
the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
USE OR OTHER DEALINGS IN THE SOFTWARE.

*/
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
        const int defaultBufferSize = 131071;

        public delegate void DOnMessage(AgNetSession session, IncomingMessage message);
        public delegate void DOnSessionStateChanged(AgNetSession session);

        public SynchronizationContext Context { get; set; }
        public bool MTUExpandEnabled { get; set; }
        public DOnMessage OnMessageEvent;
        public DOnSessionStateChanged OnSessionStateChangedEvent;


        public int BufferUsage
        {
            get
            {
                lock (socket)
                    return 100 - (int)Math.Round(100 * (socket.Available / (double)socket.ReceiveBufferSize));
            }
        }

        public int ReceiveBufferSize
        {
            get
            {
                return socket.ReceiveBufferSize;
            }
            set
            {
                this.socket.ReceiveBufferSize = value;
            }
        }

        public int SendBufferSize
        {
            get
            {
                return socket.SendBufferSize;
            }
            set
            {
                this.socket.SendBufferSize = value;
            }
        }

        public double DropChanceTest { get; set; }

        protected Socket socket;
        protected Thread peerThread;
        protected bool disposed;

        public AgNetPeer()
        {
            MTUExpandEnabled = true;
        }

        public virtual void Dispose()
        {
            disposed = true;

            if (this.peerThread != null)
            {
                this.peerThread.Join();
                this.peerThread = null;
            }

            if (this.socket != null)
            {
                socket.Close();
                socket = null;
            }
        }

        protected void StartThread()
        {
            this.peerThread = new Thread(new ThreadStart(PeerThreadTick));
            this.peerThread.Start();
        }

        protected void InitSocket()
        {
            if (this.socket != null)
                throw new InvalidOperationException("You should shutdown peer before reinit");

            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            this.socket.ReceiveBufferSize = defaultBufferSize;
            this.socket.SendBufferSize = defaultBufferSize;

            try
            {
                const uint IOC_IN = 0x80000000;
                const uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                this.socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            }
            catch
            {
                // ignore; SIO_UDP_CONNRESET not supported on this platform
            }
        }

        void PeerThreadTick()
        {
            while (!disposed)
            {
                try
                {
                    Service();
                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("PeerThreadTick(): " + ex.Message + "\n" + ex.StackTrace);
                }
            }
        }

        protected virtual void Service()
        {
            if (this.socket.Poll(1000, SelectMode.SelectRead))
            {
                EndPoint refEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = new byte[ushort.MaxValue]; // max MTU value
                try
                {
                    this.socket.ReceiveFrom(buffer, ref refEndPoint);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionReset)
                        return;
                }

                if (DropChanceTest > 0)
                {
                    Random rnd = new Random();
                    if (rnd.NextDouble() < DropChanceTest)
                        return;
                }

                try
                {
                    IncomingMessage message = new IncomingMessage(buffer, refEndPoint);
                    OnMessageInternal(message);
                }
                catch { }
                
            }
        }

        internal abstract void OnMessageInternal(IncomingMessage msg);
        internal virtual void OnSessionStateChangedInternal(AgNetSession session)
        {
            if (OnSessionStateChangedEvent != null)
            {
                if (Context == null)
                    OnSessionStateChangedEvent(session);
                else
                    Context.Post(o => OnSessionStateChangedEvent(session), null);
            }
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

        internal bool SendMessageInternal(OutgoingMessage msg, DeliveryType deliveryType)
        {
            if (disposed)
                return false;

            byte[] sendBuffer = msg.ToByteArray(deliveryType);

            try
            {
                lock (socket)
                {
                    socket.DontFragment = msg.DontFragment;
                    socket.SendTo(sendBuffer, msg.RemoteEP);
                    // TO DO: Check sent size
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionReset ||
                    ex.SocketErrorCode == SocketError.MessageSize)
                    return false;
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    throw new AgNetException("Socket send buffer is full. Try to expand it.");
                }
            }

            msg.LastSentTime = DateTime.UtcNow;
            msg.SentTimes++;
            msg.Status = AgNetSendStatus.Sent;

            if (deliveryType != DeliveryType.Reliable)
                msg.Dispose();

            return true;
        }
		
    }
}
