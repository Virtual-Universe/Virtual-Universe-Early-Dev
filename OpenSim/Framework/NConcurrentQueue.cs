/* 28 April 2019
 *   
 * Copyright Nani Sundara 2018, 2019
 * 
 * Faster and smarter. 
 * 
 *    NBlockingQueue<T>
 *    NConcurrentQueue<T>
 *    NCountlessConcurrentQueue<T>
 *    NCountlessQueue<T>
 *    NSingleReaderCountlessQueue<T>
 *    NSingleReaderConcurrentQueue<T>
 *    NSimpleChain<T>
 *    
 */

using System.Threading;

namespace OpenSim.Framework
{
    // Keep it simple. A chain, a signal and 2 locks.
    public class NConcurrentQueue<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private readonly ManualResetEvent m_signal = new ManualResetEvent(false);

        private readonly object m_headLock = new object();
        private readonly object m_tailLock = new object();

        private CNode head = null;
        private CNode tail = null;

        private int m_count = 0;

        private bool m_active = true;
        private bool m_running = true;

        public NConcurrentQueue()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Enqueue one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();

            m_active = m_running = true;
        }

        ~NConcurrentQueue()
        {
            try
            {
                Destroy();
            }
            catch { }
        }

        public void Destroy()
        {
            try
            {
                m_active = m_running = false;
                m_signal.Set();

                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            lock (m_tailLock)
            {
                tail.Value = Value;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new tail 
                // can safely be assigned with newTail.

                Interlocked.Increment(ref m_count);

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
            // TryDequeue() will only go sleep when the chain is really empty.
            // So we can be a bit lazy with setting the signal.
            m_signal.Set();
        }

        public int Count
        {
            get
            {
                return m_count;
            }
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                }
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead;

                lock (m_headLock)
                {
                    lock (m_tailLock)
                    {
                        oldHead = head;

                        // The chain always contains one empty node, the tail, with Next == null.
                        // That way we never need to check if the chain has nodes when we Sumbmit one.
                        // We can just put the new value into the existing empty tail node and
                        // add a new empty node.
                        head = tail = new CNode();
                        m_count = 0;
                    }
                }

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            }
            catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Why is it safe to use 2 separate locks? 
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accessed 
            // via the head.

            Value = default(T);

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
            {
                // Right here there is a chance for Enqueue to sneak in a new tail.
                // and to set the signal. Which we then reset again here. Not good.
                // Added a check in TryDequeue to only sleep when the chain is empty.
                m_signal.Reset();
                return false;
            }

            CNode cnode = null;
            lock (m_headLock)
            {
                if (head.Next == null) // Empty chain
                {
                    // m_count = 0; trust the add and subtract.
                    return false;
                }
                // Get the first node in the chain.
                cnode = head;
                // Move to the next node in the chain.
                head = cnode.Next; // This can be the empty tail now.

                Interlocked.Decrement(ref m_count);
            }

            // Finally we have the value.
            Value = cnode.Value;
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }

        public bool TryDequeue(out T Value)
        {
            while (m_active && m_running)
            {
                if (Dequeue(out Value))
                    return true;

                // We will not go sleep unless the chain is really empty.
                // Reference types are thread safe.
                if (m_active && m_running && head.Next == null) 
                    m_signal.WaitOne();
            }
            Value = default(T);
            return false;
        }
                
        public bool TryDequeue(out T Value, int millisecondsTimeOut)
        {
            if (Dequeue(out Value))
                return true;

            if (m_active && m_running)
            {
                // We will not go sleep unless the chain is really empty.
                if (head.Next == null) // reference types are thread safe.
                    m_signal.WaitOne(millisecondsTimeOut);

                if (m_active && m_running && Dequeue(out Value))
                    return true;
            }
            Value = default(T);
            return false;
        }

        public void CancelWait()
        {
            try
            {
                m_active = false;
                m_signal.Set();
                Thread.Sleep(50);
                m_signal.Set();
                Thread.Sleep(50);
                m_active = m_running;
            }
            catch { }
        }
    }

    // A version of NConcurrentQueue without a counter.
    public class NCountlessConcurrentQueue<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private readonly ManualResetEvent m_signal = new ManualResetEvent(false);

        private readonly object m_headLock = new object();
        private readonly object m_tailLock = new object();

        private CNode head = null;
        private CNode tail = null;

        private bool m_active = true;
        private bool m_running = true;

        public NCountlessConcurrentQueue()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Enqueue one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();

            m_active = m_running = true;
        }

        ~NCountlessConcurrentQueue()
        {
            try
            {
                Destroy();
            }
            catch { }
        }

        public void Destroy()
        {
            try
            {
                m_active = m_running = false;
                m_signal.Set();

                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            lock (m_tailLock)
            {
                tail.Value = Value;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new tail 
                // can safely be assigned with newTail.

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
            // TryDequeue() will only go sleep when the chain is really empty.
            // So we can be a bit lazy with setting the signal.
            m_signal.Set();
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                }
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead;

                lock (m_headLock)
                {
                    lock (m_tailLock)
                    {
                        oldHead = head;

                        // The chain always contains one empty node, the tail, with Next == null.
                        // That way we never need to check if the chain has nodes when we Sumbmit one.
                        // We can just put the new value into the existing empty tail node and
                        // add a new empty node.
                        head = tail = new CNode();
                    }
                }

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            }
            catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Why is it safe to use 2 separate locks? 
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accessed 
            // via the head.

            Value = default(T);

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
            {
                // Right here there is a chance for Enqueue to sneak in a new tail.
                // and to set the signal. Which we then reset again here. Not good.
                // Added a check in TryDequeue to only sleep when the chain is empty.
                m_signal.Reset();
                return false;
            }

            CNode cnode = null;
            lock (m_headLock)
            {
                if (head.Next == null) // Empty chain
                {
                    // m_count = 0; trust the add and subtract.
                    return false;
                }
                // Get the first node in the chain.
                cnode = head;
                // Move to the next node in the chain.
                head = cnode.Next; // This can be the empty tail now.
            }
            // Finally we have the value.
            Value = cnode.Value;
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }

        public bool TryDequeue(out T Value)
        {
            while (m_active && m_running)
            {
                if (Dequeue(out Value))
                    return true;

                // We will not go sleep unless the chain is really empty.
                // Reference types are thread safe.
                if (m_active && m_running && head.Next == null)
                    m_signal.WaitOne();
            }
            Value = default(T);
            return false;
        }

        public bool TryDequeue(out T Value, int millisecondsTimeOut)
        {
            if (Dequeue(out Value))
                return true;

            if (m_active && m_running)
            {
                // We will not go sleep unless the chain is really empty.
                if (head.Next == null) // reference types are thread safe.
                    m_signal.WaitOne(millisecondsTimeOut);

                if (m_active && m_running && Dequeue(out Value))
                    return true;
            }
            Value = default(T);
            return false;
        }

        public void CancelWait()
        {
            try
            {
                m_active = false;
                m_signal.Set();
                Thread.Sleep(50);
                m_signal.Set();
                Thread.Sleep(50);
                m_active = m_running;
            }
            catch { }
        }
    }

    // A simple version of NConcurrentQueue without a signal.
    public class NBlockingQueue<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private readonly object m_headLock = new object();
        private readonly object m_tailLock = new object();

        private CNode head = null;
        private CNode tail = null;

        private int m_count = 0;

        public NBlockingQueue()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Sumbmit one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();
        }

        ~NBlockingQueue()
        {
            try
            {
                Destroy();
            }
            catch { }
        }

        public void Destroy()
        {
            try
            {
                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            lock (m_tailLock)
            {
                tail.Value = Value;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new tail 
                // can safely be assigned with newTail.

                Interlocked.Increment(ref m_count);

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
        }

        public int Count
        {
            get
            {
                return m_count;
            }
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                }
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead;

                lock (m_headLock)
                {
                    lock (m_tailLock)
                    {
                        oldHead = head;

                        // The chain always contains one empty node, the tail, with Next == null.
                        // That way we never need to check if the chain has nodes when we Sumbmit one.
                        // We can just put the new value into the existing empty tail node and
                        // add a new empty node.
                        head = tail = new CNode();
                        m_count = 0;
                    }
                }

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            } catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Why is it safe to use 2 separate locks? 
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accessed 
            // via the head.

            Value = default(T);

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
                return false;

            CNode cnode = null;
            lock (m_headLock)
            {
                if (head.Next == null) // Empty chain
                {
                    // m_count = 0; trust the add and subtract.
                    return false;
                }
                // Get the first node in the chain.
                cnode = head;
                // Move to the next node in the chain.
                head = cnode.Next; // This can be the empty tail now.

                Interlocked.Decrement(ref m_count);
            }

            // Finally we have the value.
            Value = cnode.Value;
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }
    }

    // A simple version of NBlockingQueue without a count.
    public class NCountlessQueue<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private readonly object m_headLock = new object();
        private readonly object m_tailLock = new object();

        private CNode head = null;
        private CNode tail = null;

        public NCountlessQueue()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Sumbmit one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();
        }

        ~NCountlessQueue()
        {
            try
            {
                Destroy();
            } catch { }
        }

        public void Destroy()
        {
            try
            {
                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            lock (m_tailLock)
            {
                tail.Value = Value;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new tail 
                // can safely be assigned with newTail.

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                } 
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead;

                lock (m_headLock)
                {
                    lock (m_tailLock)
                    {
                        oldHead = head;

                        // The chain always contains one empty node, the tail, with Next == null.
                        // That way we never need to check if the chain has nodes when we Sumbmit one.
                        // We can just put the new value into the existing empty tail node and
                        // add a new empty node.
                        head = tail = new CNode();
                    }
                }

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            } catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Why is it safe to use 2 separate locks? 
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accessed 
            // via the head.

            Value = default(T);

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
                return false;

            CNode cnode = null;
            lock (m_headLock)
            {
                if (head.Next == null) // Empty chain
                    return false;

                // Get the first node in the chain.
                cnode = head;
                // Move to the next node in the chain.
                head = cnode.Next; // This can be the empty tail now.
            }
            // Finally we have the value.
            Value = cnode.Value;
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }
    }

    // A version of NCountlessQueue that can be used when only one 
    // thread reads (Dequeues) but multiple can write (Enqueue).
    public class NSingleReaderCountlessQueue<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private readonly object m_headLock = new object();
        private readonly object m_tailLock = new object();

        private CNode head = null;
        private CNode tail = null;

        public NSingleReaderCountlessQueue()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Sumbmit one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();
        }

        ~NSingleReaderCountlessQueue()
        {
            try
            {
                Destroy();
            }
            catch { }
        }

        public void Destroy()
        {
            try
            {
                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            lock (m_tailLock)
            {
                tail.Value = Value;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new tail 
                // can safely be assigned with newTail.

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                }
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead;

                lock (m_tailLock)
                {
                    oldHead = head;

                    // The chain always contains one empty node, the tail, with Next == null.
                    // That way we never need to check if the chain has nodes when we Sumbmit one.
                    // We can just put the new value into the existing empty tail node and
                    // add a new empty node.
                    head = tail = new CNode();
                }

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            }
            catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accessed 
            // via the head.

            Value = default(T);

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
                return false;

            // Get the first node in the chain.
            CNode cnode = head;
            // Move to the next node in the chain.
            head = cnode.Next; // This can be the empty tail now.

            // Finally we have the value.
            Value = cnode.Value;
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }
    }

    // A version of NConcurrentQueue that can be used when only one 
    // thread reads (Dequeues) but multiple can write (Enqueue).
    public class NSingleReaderConcurrentQueue<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private readonly ManualResetEvent m_signal = new ManualResetEvent(false);

        private readonly object m_tailLock = new object();

        private CNode head = null;
        private CNode tail = null;

        private int m_count = 0;

        private bool m_active = true;
        private bool m_running = true;

        public NSingleReaderConcurrentQueue()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Enqueue one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();

            m_active = m_running = true;
        }

        ~NSingleReaderConcurrentQueue()
        {
            try
            {
                Destroy();
            }
            catch { }
        }

        public void Destroy()
        {
            try
            {
                m_active = m_running = false;
                m_signal.Set();

                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            lock (m_tailLock)
            {
                tail.Value = Value;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new tail 
                // can safely be assigned with newTail.

                Interlocked.Increment(ref m_count);

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
            // TryDequeue() will only go sleep when the chain is really empty.
            // So we can be a bit lazy with setting the signal.
            m_signal.Set();
        }

        public int Count
        {
            get
            {
                return m_count;
            }
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                }
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead;

                lock (m_tailLock)
                {
                    oldHead = head;

                    // The chain always contains one empty node, the tail, with Next == null.
                    // That way we never need to check if the chain has nodes when we Sumbmit one.
                    // We can just put the new value into the existing empty tail node and
                    // add a new empty node.
                    head = tail = new CNode();
                    m_count = 0;
                }

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            }
            catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Why is it safe to use 2 separate locks? 
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accessed 
            // via the head.

            Value = default(T);

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
            {
                // Right here there is a chance for Enqueue to sneak in a new tail.
                // and to set the signal. Which we then reset again here. Not good.
                // Added a check in TryDequeue to only sleep when the chain is empty.
                m_signal.Reset();
                return false;
            }

            // Get the first node in the chain.
            CNode cnode = head;
            // Move to the next node in the chain.
            head = cnode.Next; // This can be the empty tail now.

            Interlocked.Decrement(ref m_count);
            
            // Finally we have the value.
            Value = cnode.Value;
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }

        public bool TryDequeue(out T Value)
        {
            while (m_active && m_running)
            {
                if (Dequeue(out Value))
                    return true;

                // We will not go sleep unless the chain is really empty.
                // Reference types are thread safe.
                if (m_active && m_running && head.Next == null)
                    m_signal.WaitOne();
            }
            Value = default(T);
            return false;
        }

        public bool TryDequeue(out T Value, int millisecondsTimeOut)
        {
            if (Dequeue(out Value))
                return true;

            if (m_active && m_running)
            {
                // We will not go sleep unless the chain is really empty.
                if (head.Next == null) // reference types are thread safe.
                    m_signal.WaitOne(millisecondsTimeOut);

                if (m_active && m_running && Dequeue(out Value))
                    return true;
            }
            Value = default(T);
            return false;
        }

        public void CancelWait()
        {
            try
            {
                m_active = false;
                m_signal.Set();
                Thread.Sleep(50);
                m_signal.Set();
                Thread.Sleep(50);
                m_active = m_running;
            }
            catch { }
        }
    }

    // A simple NOT THREAD SAFE chainwithout a count.
    public class NSimpleChain<T>
    {
        private class CNode
        {
            public T Value = default(T);
            public CNode Next = null;
        }

        private CNode head = null;
        private CNode tail = null;

        public NSimpleChain()
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Sumbmit one.
            // We can just put the new value into the existing empty tail node and
            // add a new empty node.
            head = tail = new CNode();
        }

        ~NSimpleChain()
        {
            try
            {
                Destroy();
            }
            catch { }
        }

        public void Destroy()
        {
            try
            {
                while (head != null)
                {
                    CNode node = head;
                    node.Value = default(T);
                    head = node.Next;
                    node = null;
                }
                tail = null;
            }
            catch { }
        }

        public void Enqueue(T Value)
        {
            tail.Value = Value;

            // Make a new tail which is always an empty node with Next == null.
            CNode newTail = new CNode();
            // Add a new empty tail and then point to it.
            tail.Next = newTail;
            tail = newTail;
        }

        public bool isEmpty
        {
            get
            {
                try
                {
                    return head.Next == null;
                } 
                catch
                {
                    return true;
                }
            }
        }

        public void Clear()
        {
            try
            {
                CNode oldHead = head;

                // The chain always contains one empty node, the tail, with Next == null.
                // That way we never need to check if the chain has nodes when we Sumbmit one.
                // We can just put the new value into the existing empty tail node and
                // add a new empty node.
                head = tail = new CNode();

                while (oldHead != null)
                {
                    CNode node = oldHead;
                    node.Value = default(T);
                    oldHead = node.Next;
                    node = null;
                }
            }
            catch { }
        }

        public bool Dequeue(out T Value)
        {
            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
            {
                Value = default(T);
                return false;
            }

            // Get the first node in the chain.
            CNode cnode = head;
            // Move to the next node in the chain.
            head = cnode.Next; // This can be the empty tail now.

            // Finally we have the value.
            Value = cnode.Value;
  
            // Some clean up.
            cnode.Value = default(T);
            cnode.Next = null;
            cnode = null;
            return true;
        }
    }
}
