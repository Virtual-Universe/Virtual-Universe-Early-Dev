/* 13 May 2019
 *   
 * Copyright Nani Sundara 2018, 2019
 * 
 * Faster and smarter. 
 * 
 *    NSimpleChain<T>
 *    
 */

using System.Threading;

namespace HttpServer
{
    // A simple NOT THREAD SAFE chain without a count.
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
