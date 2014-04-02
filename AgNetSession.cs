using AgNet.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace AgNet
{
    public enum SessionState
    {
        Closed,
        Connecting,
        Connected,
        Closing,
    }

    public class AgNetSession
    {
        internal const int timeout = 5;
        internal const double resendTimeout = 0.5;
        internal const int resendLimit = 10;

        public SessionState State { get; private set; }
        public EndPoint ClientEndPoint { get; internal set; }
        public DateTime LastIncomingData { get; private set; }
        public int PingRoundtrip { get; private set; }

        Dictionary<int, AgNetSequenceChannel> sequenceChannels;
        AgNetReliableChannel reliableChannel;
        AgNetUnreliableChannel unreliableChannel;
        DateTime lastPingSent;
        DateTime lastPongReceived;
        AgNetPeer peer;

        internal void SetState(SessionState state)
        {
            SessionState prevState = this.State;
            this.State = state;

            if (state != SessionState.Connected)
                PingRoundtrip = 0;

            if (state != prevState)
                peer.OnSessionStateChangedInternal(this);
        }

        internal IAgNetChannel GetChannel(DeliveryType deliveryType, byte index)
        {
            if (deliveryType == DeliveryType.NonReliable)
                return unreliableChannel;
            else if (deliveryType == DeliveryType.Reliable)
                return reliableChannel;
            else if (deliveryType == DeliveryType.Sequenced)
            {
                lock (sequenceChannels)
                {
                    if (!sequenceChannels.ContainsKey(index))
                        sequenceChannels.Add(index, new AgNetSequenceChannel(index));

                    return sequenceChannels[index];
                }
            }

            throw new ArgumentException("Invalid delivery type: " + deliveryType.ToString());
        }

        internal void Tick()
        {
            if (this.State == SessionState.Closed)
                return;

            if (this.State != SessionState.Closed &&
                (DateTime.UtcNow - LastIncomingData).TotalSeconds > timeout)
            {
                SetState(SessionState.Closed);
                return;
            }

            int timeFromLastPong = (int)(DateTime.UtcNow - lastPongReceived).TotalMilliseconds;
            if (timeFromLastPong > AgNetPeer.tickTimeout)
                PingRoundtrip = -1;

            //Console.WriteLine("PING: " + PingRoundtrip);

            foreach (OutgoingMessage msg in reliableChannel.OutgoingList.OrderBy(m => m.Sequence).Take(10))
            {
                if (msg.LastSentTime.Ticks > 0 && (DateTime.UtcNow - msg.LastSentTime).TotalSeconds > resendTimeout)
                {
                    if (msg.SentTimes > resendLimit)
                    {
                        SetState(SessionState.Closed);
                        return;
                    }
                    Console.WriteLine(DateTime.UtcNow.Second + "." + DateTime.UtcNow.Millisecond + " > RESEND: " + peer.ToString() + ", " + msg.ToString() + ", delta: " + (DateTime.UtcNow - msg.LastSentTime).TotalSeconds.ToString());
                    peer.SendMessage(ClientEndPoint, msg);
                }
            }
        }

        void ConfirmDelivery(IncomingMessage msg)
        {
            if (msg.DeliveryType != DeliveryType.Reliable)
                throw new ArgumentException("Wrong msg delivery type for confirmation: " + msg.ToString());

            OutgoingMessage deliveryConfirm = new OutgoingMessage(PacketType.ConfirmDelivery);
            deliveryConfirm.Channel = msg.Channel;
            deliveryConfirm.Sequence = msg.Sequence;
            deliveryConfirm.DeliveryType = DeliveryType.NonReliable;
            peer.SendMessage(msg.RemoteEndPoint, deliveryConfirm);
        }

        void SendError()
        {
            OutgoingMessage connectionError = new OutgoingMessage(PacketType.ConnectionError);
            CommitMessage(connectionError);
            peer.SendMessage(ClientEndPoint, connectionError);
        }

        internal void Ping()
        {
            OutgoingMessage pingMessage = new OutgoingMessage(PacketType.Ping);
            pingMessage.DeliveryType = DeliveryType.Sequenced;
            pingMessage.Channel = 0;
            pingMessage.Write(DateTime.UtcNow.Ticks);
            CommitMessage(pingMessage);
            peer.SendMessage(ClientEndPoint, pingMessage);
            lastPingSent = DateTime.UtcNow;
        }

        void FinResp()
        {
            OutgoingMessage finRespMessage = new OutgoingMessage(PacketType.FinResp);
            CommitMessage(finRespMessage);
            peer.SendMessage(ClientEndPoint, finRespMessage);
        }


        void Pong(IncomingMessage msg)
        {
            OutgoingMessage pongMessage = new OutgoingMessage(PacketType.Pong);
            pongMessage.DeliveryType = DeliveryType.Sequenced;
            pongMessage.Channel = msg.Channel;
            pongMessage.Write(msg.ReadInt64());
            CommitMessage(pongMessage);
            peer.SendMessage(msg.RemoteEndPoint, pongMessage);
        }

        internal IEnumerable<IncomingMessage> ReceiveMessage(IncomingMessage msg)
        {
            LastIncomingData = DateTime.UtcNow;

            if (msg.DeliveryType == DeliveryType.Reliable && msg.Type != PacketType.ConfirmDelivery)
                ConfirmDelivery(msg);

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
                OutgoingMessage deliveryFor = reliableChannel.PopFromOutgoingList(msg.Sequence);

                Console.WriteLine("Confirmed " + peer.ToString() + " "  + msg.Sequence.ToString());

                if (deliveryFor == null)
                    return false;

                if (State == SessionState.Connecting && deliveryFor.Type == PacketType.ConnectAck)
                    SetState(SessionState.Connected);

                if (State == SessionState.Closing && deliveryFor.Type == PacketType.FinResp)
                    SetState(SessionState.Closed);

                return false;
            }

            if (msg.Type == PacketType.Ping)
            {
                Pong(msg);
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
                SetState(SessionState.Closed);
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
                SetState(SessionState.Closed);
                return false;
            }

            if (msg.Type == PacketType.ConnectAck && State == SessionState.Closed)
            {
                SetState(SessionState.Connected);
                return false;
            }


            if (msg.Type == PacketType.Ping)
            {
                return false;
            }

            if (State == SessionState.Connected && msg.Type == PacketType.UserData)
            {
                return true;
            }

            SendError();
            SetState(SessionState.Closed);
            return false;
        }

        public override string ToString()
        {
            return string.Format("AgNetSession[state={0}, clientEndPoint={1}]", this.State, this.ClientEndPoint);
        }

        internal void CommitMessage(OutgoingMessage msg)
        {
            msg.LastSentTime = DateTime.UtcNow;
            IAgNetChannel channel = GetChannel(msg.DeliveryType, msg.Channel);
            channel.CommitMessage(msg);
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
        }
    }
}
