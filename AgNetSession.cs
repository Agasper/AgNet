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
using AgNet.Channels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace AgNet
{
    public enum SessionState
    {
        Closed,
        Connecting,
        Connected,
        Closing,
    }

    public partial class AgNetSession
    {
        internal const int connectionTimeout = 5000;
        internal const int pingInterval = 1000;
        internal const int confirmInterval = 50;
        internal const int resendTimeout = 500;
        internal const int resendLimit = 10;
        internal const int maxQueuedMessages = 128;
        internal const int maxSendPerTick = 128;

        //The MTU must not be confused with the minimum datagram size that all hosts must be prepared to accept, 
        //which has a value of 576 bytes for IPv4[2] and of 1280 bytes for IPv6.
        //http://en.wikipedia.org/wiki/Maximum_transmission_unit
        //20 bytes - TCP header
        //8 bytes - UDP header
        //20 bytes - safe area
        //576 - 20 - 8 - 20 = 528
        internal readonly int minPayloadMtu = 528 - Message.HEADER_SIZE;

        public int PayloadMTU { get; internal set; }

        public string DisconnectCause { get; private set; }
        public SessionState State { get; private set; }
        public EndPoint ClientEndPoint { get; internal set; }
        public DateTime LastIncomingData { get; private set; }
        public int PingRoundtrip { get; private set; }

        Dictionary<int, AgNetSequenceChannel> sequenceChannels;
        AgNetReliableChannel reliableChannel;
        AgNetUnreliableChannel unreliableChannel;
        DateTime lastPingSent;
        DateTime lastPongReceived;
        DateTime lastConfirm;
        AgNetPeer peer;
        int currentTickSent;


        internal void SetState(SessionState state)
        {
            SessionState prevState = this.State;
            this.State = state;           
            if (state != prevState)
                peer.OnSessionStateChangedInternal(this);
        }

        void Close(string reason)
        {
            PingRoundtrip = 0;
            ResetMtu();
            DisconnectCause = reason;
            SetState(SessionState.Closed);
        }

        internal void Shutdown()
        {
            if (State == SessionState.Closed || State == SessionState.Closing)
                return;

            SetState(SessionState.Closing);
            FinAck();
        }

        internal IAgNetChannel GetChannel(DeliveryType deliveryType, byte channel)
        {
            if (deliveryType == DeliveryType.Unreliable)
                return unreliableChannel;
            else if (deliveryType == DeliveryType.Reliable)
                return reliableChannel;
            else if (deliveryType == DeliveryType.Sequenced)
            {
                lock (sequenceChannels)
                {
                    if (!sequenceChannels.ContainsKey(channel))
                        sequenceChannels.Add(channel, new AgNetSequenceChannel(channel));

                    return sequenceChannels[channel];
                }
            }

            throw new ArgumentException("Invalid delivery type: " + deliveryType.ToString());
        }

        internal bool Service()
        {
            if (this.State == SessionState.Closed)
                return true;

            if (this.State != SessionState.Closed &&
                (DateTime.UtcNow - LastIncomingData).TotalMilliseconds > connectionTimeout)
            {
                Close("Connection timed out");
                return true;
            }

            Ping();
            TryMTUExpand();

            int timeFromLastPong = (int)(DateTime.UtcNow - lastPongReceived).TotalMilliseconds;
            if (timeFromLastPong > pingInterval)
                PingRoundtrip = -1;

            currentTickSent = 0;
            ConfirmAll();
            if (ResendAll())
                return true;
            SendAll();

            return false;
        }

        bool ResendAll()
        {
            int cnt = 0;
            foreach (OutgoingMessage msg in reliableChannel.GetMessagesForResend(resendTimeout))
            {
                if (++cnt > maxSendPerTick || currentTickSent > maxSendPerTick)
                    break;
                if (msg.SentTimes > resendLimit)
                    return true;
                Debug.WriteLine(string.Format("Resend message {0} to {1}", msg, msg.RemoteEP));
                if (!peer.SendMessageInternal(msg, DeliveryType.Reliable)) //resending is failed
                    return true;

                currentTickSent++;
            }

            return false;
        }

        void SendAll()
        {
            int canSend = Math.Max(0, maxQueuedMessages - reliableChannel.AwaitConfirmationCount);
            foreach (OutgoingMessage msg in reliableChannel.GetMessagesForSending(canSend))
            {
                peer.SendMessageInternal(msg, DeliveryType.Reliable);
                reliableChannel.AddToAwaitConfirmation(msg);
                if (++currentTickSent > maxSendPerTick)
                    return;
            }

            lock(sequenceChannels)
                foreach(KeyValuePair<int,AgNetSequenceChannel> pair in sequenceChannels)
                    foreach (OutgoingMessage msg in pair.Value.GetMessagesForSending())
                    {
                        peer.SendMessageInternal(msg, DeliveryType.Sequenced);
                        if (++currentTickSent > maxSendPerTick)
                            return;
                    }

            foreach (OutgoingMessage msg in unreliableChannel.GetMessagesForSending())
            {
                peer.SendMessageInternal(msg, DeliveryType.Unreliable);
                if (++currentTickSent > maxSendPerTick)
                    return;
            }
        }

        void ConfirmAll()
        {
            if ((DateTime.UtcNow - lastConfirm).TotalMilliseconds < confirmInterval)
                return;

            lastConfirm = DateTime.UtcNow;

            foreach (OutgoingMessage confirmMsg in reliableChannel.GetConfirmationMessage(PayloadMTU))
                CommitAndEnqueueForSending(confirmMsg, DeliveryType.Unreliable);
        }


        internal void Ping()
        {
            if (peer is AgNetServer && !(peer as AgNetServer).PingUsers)
                return;

            if ((DateTime.UtcNow - lastPingSent).TotalMilliseconds < pingInterval)
                return;

            OutgoingMessage pingMessage = new OutgoingMessage(PacketType.Ping);
            pingMessage.Channel = byte.MaxValue;
            pingMessage.Write(DateTime.UtcNow.Ticks);
            CommitAndEnqueueForSending(pingMessage, DeliveryType.Sequenced, byte.MaxValue);
            lastPingSent = DateTime.UtcNow;
        }

        internal void SendError(string text)
        {
            OutgoingMessage connectionError = new OutgoingMessage(PacketType.ConnectionError);
            connectionError.RemoteEP = ClientEndPoint;
            connectionError.Write(text);
            peer.SendMessageInternal(connectionError, DeliveryType.Unreliable);
        }

        internal void ConnectAck(byte[] handShakeData)
        {
            OutgoingMessage connAckMessage = new OutgoingMessage(PacketType.ConnectAck);
            if (handShakeData != null)
                connAckMessage.Write(handShakeData);
            CommitAndEnqueueForSending(connAckMessage, DeliveryType.Reliable);
        }

        internal void FinAck()
        {
            OutgoingMessage finRespMessage = new OutgoingMessage(PacketType.FinResp);
            CommitAndEnqueueForSending(finRespMessage, DeliveryType.Reliable);
        }

        internal void FinResp()
        {
            OutgoingMessage finRespMessage = new OutgoingMessage(PacketType.FinResp);
            CommitAndEnqueueForSending(finRespMessage, DeliveryType.Reliable);
        }


        void Pong(IncomingMessage msg)
        {
            OutgoingMessage pongMessage = new OutgoingMessage(PacketType.Pong);
            pongMessage.Write(msg.ReadInt64());
            CommitAndEnqueueForSending(pongMessage, DeliveryType.Sequenced, msg.Channel);
        }

        internal IEnumerable<IncomingMessage> ReceiveMessage(IncomingMessage msg)
        {
            LastIncomingData = DateTime.UtcNow;

            if (msg.DeliveryType == DeliveryType.Reliable && msg.Type != PacketType.ConfirmDelivery)
                reliableChannel.AddToSendConfirmation(msg);

            IAgNetChannel channel = GetChannel(msg.DeliveryType, msg.Channel);
            IEnumerable<IncomingMessage> messages = channel.ProcessMessage(msg);
            List<IncomingMessage> userMessages = new List<IncomingMessage>();

            foreach (var message in messages)
            {
                if (ProcessMessage(channel, message))
                    userMessages.Add(message);
            }

            return userMessages;
        }

        bool ProcessMessage(IAgNetChannel channel, IncomingMessage msg)
        {
            if (msg.Type == PacketType.ConfirmDelivery)
            {
                int confirmed = msg.ReadInt32();
                for (int i = 0; i < confirmed; i++)
                {
                    int sequence = msg.ReadInt32();
                    OutgoingMessage deliveryFor = reliableChannel.PopFromAwaitConfirmation(sequence);

                    if (deliveryFor == null)
                        continue;

                    deliveryFor.Status = AgNetSendStatus.Confirmed;

                    if (State == SessionState.Connecting && deliveryFor.Type == PacketType.ConnectAck)
                        SetState(SessionState.Connected);

                    if (State == SessionState.Closing && deliveryFor.Type == PacketType.FinResp)
                        Close("Shutdown method called");

                    deliveryFor.Dispose();
                }

                return false;
            }

            if (msg.Type == PacketType.Ping)
            {
                Pong(msg);
                return false;
            }

            if (msg.Type == PacketType.MTUExpandRequest)
            {
                ReceivedMtuExpand(msg);
                return false;
            }

            if (msg.Type == PacketType.MTUSuccess)
            {
                ReceivedMtuResponse(msg);
                return false;
            }

            if (msg.Type == PacketType.Pong)
            {
                lastPongReceived = DateTime.UtcNow;
                DateTime sent = new DateTime(msg.ReadInt64());
                TimeSpan ts = DateTime.UtcNow - sent;
                PingRoundtrip = (int)ts.TotalMilliseconds;
                return false;
            }
            
            if (msg.Type == PacketType.ConnectionError)
            {
                Close(msg.ReadString());
                return false;
            }

            if (msg.Type == PacketType.FinAck)
            {
                SetState(SessionState.Closing);
                FinResp();
                return false;
            }

            if (msg.Type == PacketType.FinResp)
            {
                Close("Connection closed by remote peer");
                return false;
            }

            if (msg.Type == PacketType.ConnectAck && State == SessionState.Closed)
            {
                var server = peer as AgNetServer;
                if (server != null)
                {
                    bool result = false;
                    if (server.MaximumSessions > 0 && server.SessionsCount >= server.MaximumSessions)
                        result = false;
                    else
                        server.OnNewSessionInternal(this, msg, out result);

                    if (result)
                        SetState(SessionState.Connected);
                    else
                        SendError("Server rejected connection");
                }
                return false;
            }


            if (msg.Type == PacketType.Ping)
            {
                return false;
            }

            if (msg.Type == PacketType.PartialMessage)
            {
                return false;
            }

            if (State == SessionState.Connected && msg.Type == PacketType.UserData)
            {
                return true;
            }

            Close("Unknown service packet");
            return false;
        }

        public override string ToString()
        {
            return string.Format("AgNetSession[state={0}, clientEndPoint={1}]", this.State, this.ClientEndPoint);
        }

        internal void CommitAndEnqueueForSending(OutgoingMessage msg, DeliveryType deliveryType)
        {
            CommitAndEnqueueForSending(msg, deliveryType, 0);
        }

        internal void CommitAndEnqueueForSending(OutgoingMessage msg, DeliveryType deliveryType, byte channelIndex)
        {
            if (deliveryType != AgNet.DeliveryType.Reliable && msg.BodyLength > PayloadMTU)
                throw new AgNetException(string.Format("You can't send unreliable messages more than {0} bytes", PayloadMTU));

            IAgNetChannel channel = GetChannel(deliveryType, channelIndex);
            lock (channel)
            {
                IEnumerable<OutgoingMessage> messages = null;
                if (deliveryType == DeliveryType.Reliable)
                    messages = AgNetReliableChannel.TrySplit(msg, PayloadMTU);
                else
                    messages = new OutgoingMessage[1] { msg };

                foreach (OutgoingMessage splittedMsg in messages)
                {
                    splittedMsg.RemoteEP = ClientEndPoint;
                    channel.CommitMessage(splittedMsg);
                    splittedMsg.Status = AgNetSendStatus.Queued;
                }
            }
        }

        public AgNetSession(AgNetPeer peer, EndPoint endPoint)
        {
            this.peer = peer;
            this.State = SessionState.Closed;
            this.ClientEndPoint = endPoint;
            this.LastIncomingData = DateTime.UtcNow;
            this.reliableChannel = new AgNetReliableChannel();
            this.unreliableChannel = new AgNetUnreliableChannel();
            this.sequenceChannels = new Dictionary<int, AgNetSequenceChannel>();
            ResetMtu();
        }
    }
}
