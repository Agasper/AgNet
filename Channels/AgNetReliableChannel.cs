using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgNet.Channels
{
    class AgNetReliableChannel : AgNetSequenceChannel
    {
        int lastDeliveredSequence;
        List<OutgoingMessage> outgoingList;
        Dictionary<int, IncomingMessage> incomingList;

        public OutgoingMessage[] OutgoingList
        {
            get
            {
                lock (outgoingList)
                    return outgoingList.ToArray();
            }
        }

        public AgNetReliableChannel() : base(0)
        {
            outgoingList = new List<OutgoingMessage>();
            incomingList = new Dictionary<int, IncomingMessage>();
        }

        public override IEnumerable<IncomingMessage> ProcessMessage(IncomingMessage msg)
        {
            lock (incomingList)
            {
                if (msg.Sequence <= lastDeliveredSequence) //duplicate
                    return new IncomingMessage[0];

                if (incomingList.ContainsKey(msg.Sequence)) //duplicate
                    return new IncomingMessage[0];


                incomingList.Add(msg.Sequence, msg);
                int maxKey = incomingList.Keys.Max();
                List<IncomingMessage> toDelivery = new List<IncomingMessage>();

                for (int i = lastDeliveredSequence + 1; i <= maxKey; i++)
                {
                    if (!incomingList.ContainsKey(i))
                        break;

                    toDelivery.Add(incomingList[i]);
                    incomingList.Remove(i);
                    lastDeliveredSequence = i;
                }

                return toDelivery;
            }

            return new IncomingMessage[0];
        }

        void AddToOutgoingList(OutgoingMessage msg)
        {
            lock (outgoingList)
            {
                if (outgoingList.Contains(msg))
                    throw new InvalidOperationException("Message already added to await list. May be you tryed to send one message few times ?");

                outgoingList.Add(msg);
            }
        }

        public OutgoingMessage PopFromOutgoingList(int sequence)
        {
            lock (outgoingList)
            {
                OutgoingMessage result = outgoingList.FirstOrDefault(m => m.Sequence == sequence);
                if (result != null)
                    outgoingList.Remove(result);

                return result;
            }
        }

        public override void CommitMessage(OutgoingMessage msg)
        {
            msg.Sequence = base.NewOutSequence;
            msg.Channel = 0;
            AddToOutgoingList(msg);
        }

    }
}
