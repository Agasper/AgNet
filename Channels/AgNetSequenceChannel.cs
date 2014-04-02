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

        public AgNetSequenceChannel(byte index)
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
            msg.Sequence = NewOutSequence;
            msg.Channel = index;
        }
    }
}
