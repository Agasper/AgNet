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
        ConnectionError = 7,
        PartialMessage = 8,
        MTUExpandRequest = 9,
        MTUSuccess = 10
    }

    public enum DeliveryType : byte //MAX = 3 (2 bit)
    {
        Unreliable = 0,
        Sequenced = 1,
        Reliable = 2
    }

    public abstract class Message : IDisposable
    {
        internal const int HEADER_SIZE = 7;


        public virtual int Sequence
        {
            get { return sequence; }
            set { sequence = value; }
        }

        internal PacketType Type { get; set; }

        protected MemoryStream stream;
        protected byte channel;
        protected int sequence;

        bool disposed;

        public virtual byte[] GetBody()
        {
            return stream.ToArray();
        }

        public virtual long BodyLength
        {
            get
            {
                return stream.Length;
            }
        }

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
