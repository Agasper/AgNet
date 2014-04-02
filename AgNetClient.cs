using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace AgNet
{
    public class AgNetClient : AgNetPeer
    {
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
            base.RemoteEndpoint = GetIPEndPointFromHostName(host, port);
            this.Session = new AgNetSession(this, base.RemoteEndpoint);
            base.InitSocket();
            base.StartThread();

            base.serverSocket.Connect(base.RemoteEndpoint);

            Session.SetState(SessionState.Connecting);
            OutgoingMessage connectMsg = new OutgoingMessage(PacketType.ConnectAck);
            Session.CommitMessage(connectMsg);
            base.SendMessage(this.RemoteEndpoint, connectMsg);
        }

        internal override void OnSessionStateChangedInternal(AgNetSession session)
        {
            base.OnSessionStateChangedInternal(session);

            if (session.State == SessionState.Closed)
                base.Dispose();
        }

        internal override void OnMessageInternal(IncomingMessage msg)
        {
            foreach(var message in Session.ReceiveMessage(msg))
                PushMessage(message);
        }

        internal override void OnTimerTickInternal()
        {
            Session.Ping();
            Session.Tick();

            base.OnTimerTickInternal();
        }

        public void SendMessage(OutgoingMessage msg)
        {
            if (this.Session.State != SessionState.Connected)
                throw new InvalidOperationException("You should connect before send data");

            this.Session.CommitMessage(msg);
            base.SendMessage(this.RemoteEndpoint, msg);
        }

        public void Close()
        {
            if (Session.State == SessionState.Closed || Session.State == SessionState.Closing)
                return;

            Session.SetState(SessionState.Closing);
            OutgoingMessage finAck = new OutgoingMessage(PacketType.FinAck);
            Session.CommitMessage(finAck);
            SendMessage(Session.ClientEndPoint, finAck);
        }

        public AgNetClient() : base()
        {
            this.Session = new AgNetSession(this, GetIPEndPointFromHostName("localhost", 0));
            this.queueMessages = new Queue<IncomingMessage>();
        }

    }
}
