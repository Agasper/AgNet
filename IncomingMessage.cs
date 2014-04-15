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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace AgNet
{
    public class IncomingMessage : Message
    {
        public EndPoint RemoteEndPoint { get; protected set; }

        public byte Channel
        {
            get { return channel; }
            set { channel = value; }
        }

        public override int Sequence
        {
            get { return sequence; }
            set { throw new FieldAccessException("This field is read-only"); }
        }

        public DeliveryType DeliveryType { get; private set; }

        BinaryReader reader;

        internal IncomingMessage(IEnumerable<IncomingMessage> toMerge)
        {
            MemoryStream mergedStream = new MemoryStream();
            foreach (IncomingMessage msg in toMerge)
            {
                Debug.Assert(msg.Type == PacketType.PartialMessage, "Can't merge message typed non partial");
                byte[] buffer = msg.stream.ToArray();
                mergedStream.Write(buffer, 0, buffer.Length);
            }

            this.Type = PacketType.UserData;
            this.channel = 0;
            this.sequence = 0;
            this.DeliveryType = AgNet.DeliveryType.Reliable;
            this.stream = mergedStream;
            this.stream.Position = 0;
            this.reader = new BinaryReader(this.stream);
        }

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

                    this.DeliveryType = (DeliveryType)(serviceData & 0x3);
                    this.Type = (PacketType)((serviceData >> 2) & 0xF);
                  
                    if (buffer.Length - HEADER_SIZE < len)
                        throw new AgNetException("Invalid packet");

                    this.stream = new MemoryStream(reader.ReadBytes((int)len));
                    this.reader = new BinaryReader(this.stream);
                }
            }
        }

        public byte[] ReadBytes(int count)
        {
            return reader.ReadBytes(count);
        }

        public byte ReadByte()
        {
            return reader.ReadByte();
        }

        public short ReadInt16()
        {
            return reader.ReadInt16();
        }

        public ushort ReadUInt16()
        {
            return reader.ReadUInt16();
        }

        public int ReadInt32()
        {
            return reader.ReadInt32();
        }

        public uint ReadUInt32()
        {
            return reader.ReadUInt32();
        }

        public long ReadInt64()
        {
            return reader.ReadInt64();
        }

        public ulong ReadUInt64()
        {
            return reader.ReadUInt64();
        }

        public string ReadString()
        {
            return reader.ReadString();
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
            return string.Format("IncomingMessage[type={0}, channel={1}, sequence={2}, deliveryType={3}, remoteEndPoint={4}, len={5}]", base.Type, this.Channel, this.Sequence, this.DeliveryType, this.RemoteEndPoint, this.BodyLength);
        }
    }
}
