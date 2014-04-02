using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace AgNet
{
    public class IncomingMessage : Message
    {
        public EndPoint RemoteEndPoint { get; protected set; }

        public override byte Channel
        {
            get { return channel; }
            set { throw new FieldAccessException("This field is read-only"); }
        }

        public override int Sequence
        {
            get { return sequence; }
            set { throw new FieldAccessException("This field is read-only"); }
        }

        public override DeliveryType DeliveryType
        {
            get { return deliveryType; }
            set { throw new FieldAccessException("This field is read-only"); }
        }

        BinaryReader reader;

        public IncomingMessage(byte[] buffer, EndPoint remoteEndPoint)
        {
            this.RemoteEndPoint = remoteEndPoint;

            if (buffer.Length < HEADER_SIZE)
                throw new AgNetException("Wrong packet header");

            using (MemoryStream instream = new MemoryStream(buffer))
            {
                using (BinaryReader reader = new BinaryReader(instream))
                {
                    this.sequence = reader.ReadInt32();
                    this.channel = reader.ReadByte();
                    int serviceData = reader.ReadByte();
                    int len = reader.ReadUInt16();

                    this.deliveryType = (DeliveryType)(serviceData & 0x3);
                    this.Type = (PacketType)((serviceData >> 2) & 0xF);
                  
                    if (buffer.Length - HEADER_SIZE < len)
                        throw new AgNetException("Invalid packet");

                    this.stream = new MemoryStream(reader.ReadBytes((int)len));
                    this.reader = new BinaryReader(this.stream);
                }
            }
        }

        public byte ReadByte()
        {
            return reader.ReadByte();
        }

        public long ReadInt64()
        {
            return reader.ReadInt64();
        }

        public override void Dispose()
        {
            if (reader != null)
            {
                reader.Close();
                reader = null;
            }

            base.Dispose();
        }

        public override string ToString()
        {
            return string.Format("IncomingMessage[type={0}, channel={1}, sequence={2}, deliveryType={3}, remoteEndPoint={4}]", base.Type, this.Channel, this.Sequence, this.DeliveryType, this.RemoteEndPoint);
        }
    }
}
