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
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace AgNet
{
    public class OutgoingMessage : Message
    {
        public AgNetSendStatus Status { get; internal set; }

        internal byte Channel
        {
            get { return channel; }
            set { channel = value; }
        }

        internal bool DontFragment { get; set; }
        internal EndPoint RemoteEP { get; set; }
        internal DateTime LastSentTime { get; set; }
        internal int SentTimes { get; set; }

        BinaryWriter writer;

        public byte[] ToByteArray(DeliveryType deliveryType)
        {
            using (MemoryStream data = new MemoryStream())
            {
                using (BinaryWriter dataWriter = new BinaryWriter(data))
                {
                    byte[] body = this.stream.ToArray();

                    dataWriter.Write((int)this.Sequence);
                    dataWriter.Write((byte)this.Channel);
                    int serviceData = (int)deliveryType | ((int)this.Type << 2);
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

        public void Write(ulong data)
        {
            writer.Write(data);
        }

        public void Write(int data)
        {
            writer.Write(data);
        }

        public void Write(uint data)
        {
            writer.Write(data);
        }

        public void Write(short data)
        {
            writer.Write(data);
        }

        public void Write(ushort data)
        {
            writer.Write(data);
        }

        public void Write(string data)
        {
            writer.Write(data);
        }

        public void Write(byte[] data)
        {
            writer.Write(data);
        }

        public void Write(byte[] data, int index, int count)
        {
            writer.Write(data, index, count);
        }

        #endregion

        public OutgoingMessage(byte channel) : this()
        {
            this.Channel = channel;
        }

        public OutgoingMessage() 
        {
            base.stream = new MemoryStream();
            this.writer = new BinaryWriter(base.stream);
            this.Type = PacketType.UserData;
        }

        internal OutgoingMessage(PacketType type) : this()
        {
            base.Type = type;
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
            return string.Format("OutgoingMessage[type={0}, len={1}]", base.Type, this.BodyLength);
        }
    }
}
