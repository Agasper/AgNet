using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgNet.Channels
{
    class AgNetUnreliableChannel : IAgNetChannel
    {
        public virtual IEnumerable<IncomingMessage> ProcessMessage(IncomingMessage msg)
        {
            return new IncomingMessage[1] { msg };
        }

        public virtual void CommitMessage(OutgoingMessage msg)
        {
            msg.Sequence = 0;
            msg.Channel = 0;
        }
    }
}
