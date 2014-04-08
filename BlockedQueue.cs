using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace AgNet
{
    class BlockedQueue<T>
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

        public T BlockedDequeue()
        {
            lock (queue)
            {
                while (queue.Count == 0)
                    Monitor.Wait(queue);

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