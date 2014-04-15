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
using System.Net;
using System.Text;

namespace AgNet.Channels
{
    class AgNetReliableChannel : AgNetSequenceChannel
    {
        int lastReceivedSequence;
        Dictionary<int, OutgoingMessage> awaitConfirmationMap;
        Dictionary<int, IncomingMessage> incomingMap;
        Dictionary<int, IncomingMessage> confirmationMap;

        public int AwaitConfirmationCount
        {
            get
            {
                lock (awaitConfirmationMap)
                    return awaitConfirmationMap.Count;
            }
        }


        public AgNetReliableChannel() : base(0)
        {
            awaitConfirmationMap = new Dictionary<int, OutgoingMessage>();
            incomingMap = new Dictionary<int, IncomingMessage>();
            confirmationMap = new Dictionary<int, IncomingMessage>();
        }

        public IEnumerable<OutgoingMessage> GetMessagesForSending(int take)
        {
            if (take < 1)
                return new OutgoingMessage[0];

            lock (sendQueue)
            {
                int count = Math.Min(sendQueue.Count, take);
                OutgoingMessage[] result = new OutgoingMessage[count];
                for (int i = 0; i < count; i++)
                    result[i] = sendQueue.Dequeue();
                return result;
            }
        }

        public override IEnumerable<IncomingMessage> ProcessMessage(IncomingMessage msg)
        {
            lock (incomingMap)
            {
                if (msg.Sequence <= lastReceivedSequence) //duplicate
                    return new IncomingMessage[0]; ;

                if (incomingMap.ContainsKey(msg.Sequence)) //duplicate
                    return new IncomingMessage[0];;

                //add new message to the map
                incomingMap.Add(msg.Sequence, msg);
                int maxKey = incomingMap.Keys.Max();
                List<IncomingMessage> toDelivery = new List<IncomingMessage>();

                //check for break in message chain, and delivery all before break.
                for (int i = lastReceivedSequence + 1; i <= maxKey; i++)
                {
                    if (!incomingMap.ContainsKey(i))
                        break;

                    toDelivery.Add(incomingMap[i]);
                }

                if (toDelivery.Count > 0)
                {
                    IncomingMessage last = toDelivery.Last();
                    if (last.Type == PacketType.PartialMessage && last.Channel != 255) //if partial chain broken
                    {
                        //remove all partial messages from the tail
                        int i = toDelivery.Count - 1;
                        while (i >= 0)
                        {
                            IncomingMessage message = toDelivery[i];
                            toDelivery.RemoveAt(i--);
                            if (message.Type == PacketType.PartialMessage && message.Channel == 0)
                                break;
                        }
                    }

                    if (toDelivery.Count > 0)
                    {
                        toDelivery.ForEach(m => incomingMap.Remove(m.Sequence)); //remove from map all delivered
                        lastReceivedSequence = toDelivery[toDelivery.Count - 1].Sequence; //remember last delivered message sequence
                    }
                }

                return MergeMessages(toDelivery);
            }
        }

        IEnumerable<IncomingMessage> MergeMessages(List<IncomingMessage> list)
        {
            if (list.Count == 0)
                return new IncomingMessage[0];

            List<IncomingMessage> tempList = new List<IncomingMessage>();
            List<IncomingMessage> mergedList = new List<IncomingMessage>();
            for (int i = 0; i < list.Count; i++)
            {
                IncomingMessage msg = list[i];
                if (msg.Type != PacketType.PartialMessage)
                {
                    mergedList.Add(msg);
                    continue;
                }

                tempList.Add(msg);
                if (msg.Channel == byte.MaxValue)
                {
                    IncomingMessage result = new IncomingMessage(tempList);
                    mergedList.Add(result);
                    tempList.Clear();
                }
            }

            if (tempList.Count > 0)
                throw new AgNetException("MergeMessages(): list contains broken partial messages chain");

            return mergedList;
        }

        public IEnumerable<OutgoingMessage> GetMessagesForResend(int timeout)
        {
            lock (awaitConfirmationMap)
                return awaitConfirmationMap
                    .Where(m => m.Value.LastSentTime.Ticks > 0 && (DateTime.UtcNow - m.Value.LastSentTime).TotalMilliseconds > timeout)
                    .OrderBy(m => m.Value.Sequence)
                    .Select(m => m.Value)
                    .ToArray();
        }

        public void AddToAwaitConfirmation(OutgoingMessage msg)
        {
            lock (awaitConfirmationMap)
            {
                if (awaitConfirmationMap.ContainsKey(msg.Sequence))
                    throw new InvalidOperationException("Message already added to await list. May be you tryes to send one message few times ?");

                awaitConfirmationMap.Add(msg.Sequence, msg);
            }
        }

        public OutgoingMessage PopFromAwaitConfirmation(int sequence)
        {
            lock (awaitConfirmationMap)
            {
                if (!awaitConfirmationMap.ContainsKey(sequence))
                    return null;

                OutgoingMessage result = awaitConfirmationMap[sequence];
                awaitConfirmationMap.Remove(sequence);
                return result;
            }
        }

        public IEnumerable<OutgoingMessage> GetConfirmationMessage(int currentMTU)
        {
            lock (confirmationMap)
            {
                if (confirmationMap.Count == 0)
                    return new OutgoingMessage[0];

                List<OutgoingMessage> result = new List<OutgoingMessage>();
                int maxConfirmsPerMessage = (currentMTU - sizeof(int)) / sizeof(int);
                int needMessages = (int)Math.Ceiling(confirmationMap.Count / (float)maxConfirmsPerMessage);

                for(int i = 0; i < needMessages; i++)
                {
                    int count = Math.Min(confirmationMap.Count, maxConfirmsPerMessage);
                    OutgoingMessage deliveryConfirm = new OutgoingMessage(PacketType.ConfirmDelivery);
                    deliveryConfirm.Write(count);

                    for (int j = 0; j < count; j++)
                    {
                        IncomingMessage msg = confirmationMap.First().Value;
                        confirmationMap.Remove(msg.Sequence);

                        if (msg.DeliveryType != DeliveryType.Reliable)
                            throw new ArgumentException("Wrong msg delivery type for confirmation: " + msg.ToString());

                        deliveryConfirm.Write(msg.Sequence);
                    }

                    result.Add(deliveryConfirm);
                }


                return result;
            }
        }

        public static IEnumerable<OutgoingMessage> TrySplit(OutgoingMessage msg, int mtu)
        {
            if (msg.BodyLength <= mtu) //does not need split this message
                return new OutgoingMessage[1] { msg };
            if (msg.BodyLength > (byte.MaxValue + 1) * mtu)
                throw new AgNetException("Message too long. Maximum message size is " + (byte.MaxValue * mtu).ToString());

            List<OutgoingMessage> list = new List<OutgoingMessage>();
            byte[] data = msg.GetBody();
            int needMessages = (int)Math.Ceiling(msg.BodyLength / (double)mtu);
            for (byte i = 0; i < needMessages; i++)
            {
                OutgoingMessage newMsg = new OutgoingMessage(PacketType.PartialMessage);
                newMsg.Channel = i;
                if (i == needMessages - 1)
                    newMsg.Channel = byte.MaxValue;
                int offset = i * mtu;
                newMsg.Write(data, offset, Math.Min(mtu, data.Length - offset));
                list.Add(newMsg);
            }

            return list;
        }

        public void AddToSendConfirmation(IncomingMessage msg)
        {
            lock (confirmationMap)
                if (!confirmationMap.ContainsKey(msg.Sequence))
                    confirmationMap.Add(msg.Sequence, msg);
        }

        public override void CommitMessage(OutgoingMessage msg)
        {
            lock (sendQueue)
            {
                msg.Sequence = base.NewOutSequence;
                EnqueueOutgoingMessage(msg);
            }
        }

    }
}
