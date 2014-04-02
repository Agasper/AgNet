using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace AgNet
{
    public class AgNetServer : AgNetPeer
    {
        public bool PingUsers { get; set; }

        Dictionary<EndPoint, AgNetSession> sessions;

        public AgNetServer(string listenHost, int listenPort)
            : this(listenPort)
		{
            if (!String.IsNullOrEmpty(listenHost))
                base.ListenEndpoint = GetIPEndPointFromHostName(listenHost, listenPort);
		}

        public AgNetServer(int listenPort) : base()
		{
            this.ListenEndpoint = GetIPEndPointFromHostName("localhost", listenPort);
            this.sessions = new Dictionary<EndPoint, AgNetSession>();
		}

        AgNetSession GetSession(EndPoint fromEndPoint)
        {
            lock (sessions)
            {
                if (sessions.ContainsKey(fromEndPoint))
                    return sessions[fromEndPoint];
                return null;
            }
        }

        void RemoveSession(AgNetSession session)
        {
            lock(this.sessions)
            {
                this.sessions.Remove(session.ClientEndPoint);
            }
        }

        AgNetSession CreateNewSession(EndPoint endPoint)
        {
            lock (this.sessions)
            {
                AgNetSession session = new AgNetSession(this, endPoint);
                this.sessions.Add(endPoint, session);
                return session;
            }
        }

        internal override void OnSessionStateChangedInternal(AgNetSession session)
        {
            base.OnSessionStateChangedInternal(session);

            if (session.State == SessionState.Closed)
                RemoveSession(session);
        }

        public void Close()
        {
            AgNetSession[] toClose;

            lock (sessions)
            {
                toClose = sessions.Values.Where(s => s.State != SessionState.Closed && s.State != SessionState.Closing).ToArray();
            }

            foreach (var session in toClose)
                CloseSession(session);

            int cnt = 0;
            do
            {
                Thread.Sleep(100);
                lock (sessions)
                    cnt = sessions.Where(s => s.Value.State != SessionState.Closed).Count();
            } while (cnt > 0);


            base.Dispose();
        }

        void PurgeSessions()
        {
            lock (sessions)
            {
                KeyValuePair<EndPoint, AgNetSession>[] toPurge = sessions.Where(s => s.Value.State == SessionState.Closed).ToArray();

                foreach (KeyValuePair<EndPoint, AgNetSession> pair in toPurge)
                {
                    sessions.Remove(pair.Key);
                }
            }
        }


        internal override void OnTimerTickInternal()
        {
            lock (sessions)
            {
                foreach (var session in sessions.Values.ToArray())
                {
                    if (PingUsers)
                        session.Ping();
                    session.Tick();
                }
            }
            base.OnTimerTickInternal();
        }

        public void CloseSession(AgNetSession session)
        {
            session.SetState(SessionState.Closing);
            OutgoingMessage finAck = new OutgoingMessage(PacketType.FinAck);
            session.CommitMessage(finAck);
            SendMessage(session.ClientEndPoint, finAck);
        }

        public void Listen()
        {
            base.InitSocket();
            base.serverSocket.Bind(base.ListenEndpoint);
            base.StartThread();
            System.Diagnostics.Debug.WriteLineIf(base.Debug, string.Format("UDP Server listen at {0}", base.ListenEndpoint));
        }

        internal override void OnMessageInternal(IncomingMessage msg)
        {
            AgNetSession session = GetSession(msg.RemoteEndPoint);

            if (session == null)
                session = CreateNewSession(msg.RemoteEndPoint);

            IEnumerable<IncomingMessage> messages = session.ReceiveMessage(msg);

            if (OnMessageEvent != null)
            {
                foreach(var message in messages)
                    OnMessageEvent(session, message);
            }
        }

        public void SendMessage(AgNetSession session, OutgoingMessage msg)
        {
            if (session.State != SessionState.Connected)
                throw new InvalidOperationException("This session was disconnected");

            session.CommitMessage(msg);
            base.SendMessage(session.ClientEndPoint, msg);
        }
    }
}
