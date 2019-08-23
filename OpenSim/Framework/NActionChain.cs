/* 21 March 2019
 * 
 * Nani made this :) 
 * 
*/

using System;
using System.Threading;

namespace OpenSim.Framework
{
    public class NActionChain
    {
        private class ActionNode
        {
            public Action action = null;
            public ActionNode Next = null;
        }

        private readonly ManualResetEvent m_signal = new ManualResetEvent(false);

        private readonly object m_headLock = new object();
        private readonly object m_tailLock = new object();

        private ActionNode head = null;
        private ActionNode tail = null;

        private bool m_running = true;

        private Thread[] m_Thread = null;

        public NActionChain(int NumThreads,
                             bool background = true,
                             ThreadPriority priority = ThreadPriority.Normal)
        {
            // The chain always contains one empty node, the tail, with Next == null.
            // That way we never need to check if the chain has nodes when we Enqueue a value.
            // We can just put the new value into the existing tail node and add a new empty node.
            head = tail = new ActionNode();

            m_running = true;

            m_Thread = new Thread[NumThreads];
            for (int i = 0; i < m_Thread.Length; i++)
            {
                try
                {
                    m_Thread[i] = new Thread(DoActions);
                    m_Thread[i].Priority = priority;
                    m_Thread[i].IsBackground = background;
                    m_Thread[i].Start();
                }
                catch { }
            }
        }

        ~NActionChain()
        {
            Destroy();
        }

        public void Destroy()
        {
            try
            {
                m_running = false;
                m_signal.Set();
            }
            catch { }

            for (int i = 0; i < m_Thread.Length; i++)
            {
                try
                {
                    m_Thread[i].Abort();
                    m_Thread[i] = null;
                }
                catch
                { }
            }

            try
            {
                head = tail = null;
            }
            catch { }
        }

        public void Enqueue(Action action)
        {
            // Make a new tail which is always an empty node with Next == null.
            ActionNode newTail = new ActionNode();
            lock (m_tailLock)
            {
                tail.action = action;

                // Add a new empty tail and then point to it.
                tail.Next = newTail;
                // At this point Dequeue could already grab this (old) tail, before
                // we set the new tail. Which is no problem since the new empty tail 
                // can safely be assigned with newTail.

                // Why is it safe to use 2 separate locks? 
                // Because there is always a node in the chain AND 
                // comparing and assigning reference types is thread safe.
                // So as soon as a new tail is assigned it can be accessed 
                // via the head.
                tail = newTail;
            }
            // DoActions() will only go sleep when the chain is really empty.
            // So we can be a bit lazy with setting the signal.
            m_signal.Set();
        }

        private bool Dequeue(out Action action)
        {
            // Why is it safe to use 2 separate locks? 
            // Because there is always a node in the chain AND 
            // comparing and assigning reference types is thread safe.
            // So as soon as a new tail is assigned it can be accesed 
            // via the head.

            action = null;

            // Reference types are thread safe.
            if (head.Next == null) // Empty chain
            {
                // Right here there is a chance for Enqueue to sneak in a new tail.
                // and to set the signal. Which we then reset again here. Not good.
                // Added a check in DoActions to only sleep when the chain is empty.
                m_signal.Reset();
                return false;
            }

            ActionNode node = null;

            lock (m_headLock)
            {
                if (head.Next == null) // Empty chain
                    return false;

                // Get the first request in the chain.
                // Move to the next request in the chain.
                node = head;
                head = head.Next; // head could now be the empty tail.
            }

            // Now we have the action.
            action = node.action;

            // Do some clean up.
            node.action = null;
            node.Next = null;
            node = null;
            return true;
        }

        private void DoActions()
        {
            Culture.SetCurrentCulture();

            while (m_running)
            {
                try
                {
                    Action action;
                    if (Dequeue(out action) && action != null && m_running)
                        action();

                    // We will not go sleep unless the chain is really empty.
                    if (m_running && head.Next == null) // Reference types are thread safe.
                        m_signal.WaitOne();
                }
                catch { }
            }
        }
    }
}
