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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace AgNet
{
    public class AgNetClient : AgNetPeer
    {
        public EndPoint RemoteEndpoint { get; protected set; }
        public AgNetSession Session { get; private set; }
        Queue<IncomingMessage> queueMessages;

        public int MessageCount
        {
            get
            {
                lock (this.queueMessages)
                {
                    return this.queueMessages.Count;
                }
            }
        }

        protected void PushMessage(IncomingMessage msg)
        {
            if (OnMessageEvent == null)
            {
                lock (this.queueMessages)
                {
                    queueMessages.Enqueue(msg);
                }
            }
            else
                OnMessageEvent(Session, msg);
        }

        public IncomingMessage PopMessage()
        {
            lock (this.queueMessages)
            {
                if (this.queueMessages.Count == 0)
                    return null;

                return this.queueMessages.Dequeue();
            }
        }

        public void Connect(string host, int port)
        {
            if (Session != null && Session.State != SessionState.Closed)
                throw new InvalidOperationException("Close socket before connect");

            this.queueMessages = new Queue<IncomingMessage>();
            this.RemoteEndpoint = GetIPEndPointFromHostName(host, port);
            this.Session = new AgNetSession(this, this.RemoteEndpoint);
            base.InitSocket();
            base.StartThread();

            base.socket.Connect(this.RemoteEndpoint);

            Session.SetState(SessionState.Connecting);
            Session.ConnectAck(null);
        }

        internal override void OnSessionStateChangedInternal(AgNetSession session)
        {
            base.OnSessionStateChangedInternal(session);

            if (session.State == SessionState.Closed)
                Dispose();
        }

        internal override void OnMessageInternal(IncomingMessage msg)
        {
            foreach(var message in Session.ReceiveMessage(msg))
                PushMessage(message);
        }

        protected override void Service()
        {
            Session.MTUExpandEnabled = this.MTUExpandEnabled;
            Session.Service();
            base.Service();
        }

        public void SendMessage(OutgoingMessage msg, DeliveryType deliveryType)
        {
            SendMessage(msg, deliveryType, 0);
        }

        public void SendMessage(OutgoingMessage msg, DeliveryType deliveryType, byte channel)
        {
            if (this.Session.State != SessionState.Connected)
                throw new InvalidOperationException("You should connect before sending data");

            Session.CommitAndEnqueueForSending(msg, deliveryType, channel);
        }

        public void Close()
        {
            Session.Shutdown();
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public AgNetClient() : base()
        {
            this.Session = new AgNetSession(this, GetIPEndPointFromHostName("localhost", 0));
            this.queueMessages = new Queue<IncomingMessage>();
        }

    }
}
