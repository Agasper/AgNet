using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace AgNet
{
    class NetworkQueue<T>
    {
        readonly Queue<T> queue = new Queue<T>();

        public void Enqueue(T item)
        {
            lock (queue)
            {
                queue.Enqueue(item);
                if (queue.Count == 1)
                    Monitor.PulseAll(queue);
            }
        }

        public T BlockedDequeue(int timeout = -1)
        {
            lock (queue)
            {
                while (queue.Count == 0)
                    Monitor.Wait(queue, timeout);

                return queue.Dequeue();
            }
        }

        public T Dequeue()
        {
            lock (queue)
            {
                return queue.Dequeue();
            }
        }

        public int Count
        {
            get
            {
                return queue.Count;
            }
        }
    }
}