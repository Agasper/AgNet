using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgNet.Channels
{
    interface IAgNetChannel
    {
        IEnumerable<IncomingMessage> ProcessMessage(IncomingMessage msg);
        void CommitMessage(OutgoingMessage msg);
    }
}
