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
    public class AgNetServer : AgNetPeer
    {
        public delegate void DOnNewSession(AgNetSession session, IncomingMessage handshakeMessage, out bool result);

        public int MaximumSessions { get; set; }
        public int SessionsCount
        {
            get
            {
                lock (sessions)
                    return sessions.Count;
            }
        }
        public AgNetSession[] Sessions
        {
            get
            {
                lock (sessions)
                    return sessions.Values.ToArray();
            }
        }
        public bool PingUsers { get; set; }
        public EndPoint ListenEndpoint { get; private set; }
        public event DOnNewSession OnNewSession;

        Dictionary<EndPoint, AgNetSession> sessions;
        List<IncomingMessage> confirmList;
        DateTime lastPurgeTime;

        public AgNetServer(string listenHost, int listenPort)
            : this(listenPort)
		{
            if (!String.IsNullOrEmpty(listenHost))
                this.ListenEndpoint = GetIPEndPointFromHostName(listenHost, listenPort);
		}

        public AgNetServer(int listenPort) : base()
		{
            this.ListenEndpoint = GetIPEndPointFromHostName("localhost", listenPort);
            this.sessions = new Dictionary<EndPoint, AgNetSession>();
		}

        internal void OnNewSessionInternal(AgNetSession session, IncomingMessage handshakeMessage, out bool result)
        {
            if (OnNewSession == null)
            {
                result = false;
                return;
            }

            OnNewSession(session, handshakeMessage, out result);
        }

        AgNetSession GetSession(EndPoint fromEndPoint)
        {
            lock (this.sessions)
            {
                if (sessions.ContainsKey(fromEndPoint))
                    return sessions[fromEndPoint];

                return CreateNewSession(fromEndPoint);
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

        public void Close()
        {
            AgNetSession[] toClose;

            lock (sessions)
                toClose = sessions.Values.Where(s => s.State != SessionState.Closed && s.State != SessionState.Closing).ToArray();

            foreach (var session in toClose)
                session.Shutdown();

            int cnt = 0;
            do
            {
                Thread.Sleep(100);
                lock (sessions)
                    cnt = sessions.Where(s => s.Value.State != SessionState.Closed).Count();
            } while (cnt > 0);


            base.Dispose();
        }

        public void Listen()
        {
            base.InitSocket();
            base.socket.Bind(this.ListenEndpoint);
            base.StartThread();
            System.Diagnostics.Debug.WriteLine(string.Format("Server listen at {0}", this.ListenEndpoint));
        }

        protected override void Service()
        {
            lock (sessions)
            {
                List<EndPoint> toDelete = new List<EndPoint>();

                foreach (KeyValuePair<EndPoint, AgNetSession> pair in sessions)
                {
                    pair.Value.MTUExpandEnabled = base.MTUExpandEnabled;
                    if (pair.Value.Service())
                        toDelete.Add(pair.Key);
                }

                toDelete.ForEach(ep => sessions.Remove(ep));
            }

            base.Service();
        }

        internal override void OnMessageInternal(IncomingMessage msg)
        {
            AgNetSession session = GetSession(msg.RemoteEndPoint);
            IEnumerable<IncomingMessage> messages = session.ReceiveMessage(msg);

            if (OnMessageEvent != null)
            {
                foreach(var message in messages)
                    OnMessageEvent(session, message);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public void SendMessage(AgNetSession session, OutgoingMessage msg)
        {
            if (session.State != SessionState.Connected)
                throw new InvalidOperationException("This session was disconnected");

            session.CommitAndEnqueueForSending(msg);
        }
    }
}
