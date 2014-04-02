using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgNet
{
    internal enum PacketType : byte //MAX = 15 (4 bit)
    {
        UserData = 0,
        ConnectAck = 1,
        FinAck = 2,
        FinResp = 3,
        ConfirmDelivery = 4,
        Ping = 5,
        Pong = 6,
        ConnectionError = 7
    }

    public enum DeliveryType : byte //MAX = 3 (2 bit)
    {
        NonReliable = 0,
        Sequenced = 1,
        Reliable = 2
    }

    public abstract class Message : IDisposable
    {
        protected const int HEADER_SIZE = 7;

        public virtual byte Channel
        {
            get { return channel; }
            set { channel = value; }
        }

        public virtual int Sequence
        {
            get { return sequence; }
            set { sequence = value; }
        }

        public virtual DeliveryType DeliveryType
        {
            get { return deliveryType; }
            set { deliveryType = value; }
        }

        internal PacketType Type { get; set; }

        protected MemoryStream stream;
        protected byte channel;
        protected int sequence;
        protected DeliveryType deliveryType;

        bool disposed;

        public virtual void Dispose()
        {
            if (disposed)
                throw new ObjectDisposedException(this.ToString());
            disposed = true;

            if (stream != null)
            {
                stream.Dispose();
                stream = null;
            }
        }

        public Message()
        {
        }
    }
}
