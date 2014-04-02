using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgNet
{
    public class OutgoingMessage : Message
    {
        internal DateTime LastSentTime { get; set; }
        internal int SentTimes { get; set; }

        BinaryWriter writer;

        public byte[] GetBody()
        {
            return stream.ToArray();
        }

        public byte[] ToByteArray()
        {
            using (MemoryStream data = new MemoryStream())
            {
                using (BinaryWriter dataWriter = new BinaryWriter(data))
                {
                    byte[] body = this.stream.ToArray();

                    dataWriter.Write((int)this.Sequence);
                    dataWriter.Write((byte)this.Channel);
                    int serviceData = (int)this.DeliveryType | ((int)this.Type << 2);
                    dataWriter.Write((byte)serviceData);
                    dataWriter.Write((ushort)body.Length);
                    dataWriter.Write(body, 0, body.Length);

                    return data.ToArray();
                }
            }
        }

        #region Writes

        public void Write(byte data)
        {
            writer.Write(data);
        }

        public void Write(long data)
        {
            writer.Write(data);
        }

        #endregion

        public OutgoingMessage(byte channel, DeliveryType deliveryType) : this()
        {
            this.Channel = channel;
            this.DeliveryType = deliveryType;
        }

        public OutgoingMessage() 
        {
            base.stream = new MemoryStream();
            this.writer = new BinaryWriter(base.stream);
            this.Type = PacketType.UserData;
            this.DeliveryType = AgNet.DeliveryType.NonReliable;
        }

        internal OutgoingMessage(PacketType type) : this()
        {
            base.Type = type;
            this.Channel = 254;
            this.DeliveryType = AgNet.DeliveryType.Reliable;
        }

        public override void Dispose()
        {
            if (writer != null)
            {
                writer.Close();
                writer = null;
            }

            base.Dispose();
        }

        public override string ToString()
        {
            return string.Format("IncomingMessage[type={0}, channel={1}, sequence={2}, deliveryType={3}]", base.Type, this.Channel, this.Sequence, this.DeliveryType);
        }
    }
}
