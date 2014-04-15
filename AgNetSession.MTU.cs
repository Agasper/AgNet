using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AgNet
{
    public partial class AgNetSession
    {
        const int MtuExpandInterval = 500;

        internal bool MTUExpandEnabled { get; set; }

        int smallestFailedMtu;
        int mtuTries;
        int lastSentMtu;
        DateTime mtuSentTime;
        bool mtuFinalized;

        void ResetMtu()
        {
            PayloadMTU = minPayloadMtu;
            mtuSentTime = DateTime.UtcNow;
            mtuTries = 0;
            smallestFailedMtu = -1;
        }

        void TryMTUExpand()
        {
            if (mtuFinalized || !MTUExpandEnabled || State != SessionState.Connected)
                return;

            if ((DateTime.UtcNow - mtuSentTime).TotalMilliseconds < MtuExpandInterval)
                return;

            if (mtuTries <= 3)
            {
                int nextMtu = 0;
                if (smallestFailedMtu < 0)
                    nextMtu = (int)(PayloadMTU * 1.2);
                else
                {
                    nextMtu = (int)((PayloadMTU + smallestFailedMtu) / 2.0);
                    if (nextMtu == PayloadMTU)
                    {
                        System.Diagnostics.Debug.WriteLine("MTU finalized at " + PayloadMTU.ToString());
                        mtuFinalized = true;
                        return;
                    }
                }

                OutgoingMessage mtuMessage = new OutgoingMessage(PacketType.MTUExpandRequest);
                mtuMessage.DontFragment = true;
                mtuMessage.RemoteEP = ClientEndPoint;
                mtuMessage.Write(new byte[nextMtu]);
                System.Diagnostics.Debug.WriteLine("Expanding MTU up to " + nextMtu.ToString());
                if (!peer.SendMessageInternal(mtuMessage, DeliveryType.Unreliable))
                    MtuFail(nextMtu);
                else
                {
                    mtuSentTime = DateTime.UtcNow;
                    mtuTries++;
                    lastSentMtu = nextMtu;
                }
            }
            else
                MtuFail(lastSentMtu);
        }

        void MtuFail(int mtu)
        {
            mtuTries = 0;
            smallestFailedMtu = mtu;
            mtuSentTime = new DateTime(0);
            System.Diagnostics.Debug.WriteLine("MTU FAILED to expand to " + mtu.ToString());
        }

        void ReceivedMtuExpand(IncomingMessage msg)
        {
            OutgoingMessage mtuMessage = new OutgoingMessage(PacketType.MTUSuccess);
            mtuMessage.RemoteEP = ClientEndPoint;
            mtuMessage.Write((int)msg.BodyLength);
            peer.SendMessageInternal(mtuMessage, DeliveryType.Unreliable);
        }

        void ReceivedMtuResponse(IncomingMessage msg)
        {
            int successMtu = msg.ReadInt32();

            if (successMtu > PayloadMTU)
            {
                PayloadMTU = successMtu;
                mtuTries = 0;
                System.Diagnostics.Debug.WriteLine("MTU expanded to " + successMtu.ToString());
            }
        }
    }
}
