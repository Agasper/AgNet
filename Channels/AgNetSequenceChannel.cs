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
using System.Text;

namespace AgNet.Channels
{
    class AgNetSequenceChannel : AgNetUnreliableChannel
    {
        protected int outSequence;
        protected int inSequence;

        byte index;

        public AgNetSequenceChannel(byte index) : base()
        {
            this.outSequence = 0;
            this.inSequence = 0;
            this.index = index;
        }

        public override IEnumerable<IncomingMessage> ProcessMessage(IncomingMessage msg)
        {
            if (msg.Sequence > inSequence)
            {
                inSequence = msg.Sequence;
                return new IncomingMessage[1] { msg };
            }

            return new IncomingMessage[0];
        }

        public int NewOutSequence
        {
            get
            {
                return ++outSequence;
            }
        }

        public override void CommitMessage(OutgoingMessage msg)
        {
            lock (sendQueue)
            {
                msg.Sequence = NewOutSequence;
                msg.Channel = index;
                EnqueueOutgoingMessage(msg);
            }
        }
    }
}
