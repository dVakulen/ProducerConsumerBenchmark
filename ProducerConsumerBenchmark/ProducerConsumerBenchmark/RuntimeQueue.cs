using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Orleans.Runtime
{
    internal class RuntimeQueue<T> : IDisposable
    {
        private List<T> list;
        private readonly object lockable;
        public bool IsAddingCompleted { get; private set; }

        public RuntimeQueue()
        {
         
                lockable = new object();
                list = new List<T>();
                IsAddingCompleted = false;
        }

        public void Add(T item)
        {
         
                if (IsAddingCompleted)
                    throw new InvalidOperationException("IsAddingCompleted.");
                bool lockTaken = false;
                try
                {
                    Monitor.Enter(lockable, ref lockTaken);
                    if (IsAddingCompleted)
                        throw new InvalidOperationException("IsAddingCompleted.");
                    list.Add(item);
                    Monitor.PulseAll(lockable);
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(lockable);
                }
        }

        public bool TryTake(out T item)
        {
         
            bool lockTaken = false;
            try
            {
                Monitor.Enter(lockable, ref lockTaken);
                {
                    if (list.Count > 0)
                    {
                        item = list[0];
                        list.RemoveAt(0);
                        return true;
                    }
                    else
                    {
                        item = default(T);
                        return false;
                    }
                }
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(lockable);
            }
        }

        public T Take()
        {
           
            while (true)
            {
                bool lockTaken = false;
                try
                {
                    Monitor.Enter(lockable, ref lockTaken);
                    {
                        if (list.Count > 0)
                        {
                            T item = list[0];
                            list.RemoveAt(0);
                            return item;
                        }
                        else if (IsAddingCompleted)
                        {
                            throw new InvalidOperationException("IsAddingCompleted and the queue is empty.");
                        }
                        else
                        {
                            Monitor.Wait(lockable);
                            continue; // loop and try again.
                        }
                    }
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(lockable);
                }
            }
        }

        public T First()
        {
           
            while (true)
            {
                bool lockTaken = false;
                try
                {
                    Monitor.Enter(lockable, ref lockTaken);
                    {
                        if (list.Count > 0) return list[0];

                        if (IsAddingCompleted) throw new InvalidOperationException("IsAddingCompleted and the queue is empty.");

                        Monitor.Wait(lockable);
                        continue; // loop and try again.
                    }
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(lockable);
                }
            }
        }

        public void CompleteAdding()
        {
          
                lock (lockable)
                {
                    IsAddingCompleted = true;
                }
        }

        public int Count
        {
            get
            {
              
                lock (lockable)
                {
                    return list.Count;
                }
            }
        }

        public void Dispose()
        {
                lock (lockable)
                {
                    IsAddingCompleted = true;
                    list = null;
                }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

                lock (lockable)
                {
                    IsAddingCompleted = true;
                    list = null;
                }
        }
    }
}