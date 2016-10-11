﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Security.Permissions;

namespace DTOGenerator
{
    public class SimpleLockThreadPool : IThreadPool
    {
        public SimpleLockThreadPool() :
        this(Environment.ProcessorCount, true)
        { }

        public SimpleLockThreadPool(int concurrencyLevel) :
            this(concurrencyLevel, true)
        { }

        public SimpleLockThreadPool(bool flowExecutionContext) :
            this(Environment.ProcessorCount, flowExecutionContext)
        { }

        public SimpleLockThreadPool(int concurrencyLevel, bool flowExecutionContext)
        {
            if (concurrencyLevel <= 0)
                throw new ArgumentOutOfRangeException("concurrencyLevel");

            m_concurrencyLevel = concurrencyLevel;
            m_flowExecutionContext = flowExecutionContext;

            // If suppressing flow, we need to demand permissions.
            if (!flowExecutionContext)
                new SecurityPermission(SecurityPermissionFlag.Infrastructure).Demand();
        }

        // Each work item consists of a closure: work + (optional) state obj + context.
        struct WorkItem
        {
            internal WaitCallback m_work;
            internal object m_obj;
            internal ExecutionContext m_executionContext;

            internal WorkItem(WaitCallback work, object obj)
            {
                m_work = work;
                m_obj = obj;
                m_executionContext = null;
            }

            internal void Invoke()
            {
                // Run normally (delegate invoke) or under context, as appropriate.
                if (m_executionContext == null)
                    m_work(m_obj);
                else
                    ExecutionContext.Run(m_executionContext, ContextInvoke, null);
            }

            private void ContextInvoke(object obj)
            {
                m_work(m_obj);
            }
        }

        private readonly int m_concurrencyLevel;
        private readonly bool m_flowExecutionContext;
        private readonly Queue<WorkItem> m_queue = new Queue<WorkItem>();
        private Thread[] m_threads;
        private int m_threadsWaiting;
        private bool m_shutdown;

        // Methods to queue work.

        public void QueueUserWorkItem(WaitCallback work)
        {
            QueueUserWorkItem(work, null);
        }

        public void QueueUserWorkItem(WaitCallback work, object obj)
        {
            WorkItem wi = new WorkItem(work, obj);

            // If execution context flowing is on, capture the caller's context.
            if (m_flowExecutionContext)
                wi.m_executionContext = ExecutionContext.Capture();

            // Make sure the pool is started (threads created, etc).
            EnsureStarted();

            // Now insert the work item into the queue, possibly waking a thread.
            lock (m_queue)
            {
                m_queue.Enqueue(wi);
                if (m_threadsWaiting > 0)
                    Monitor.Pulse(m_queue);
            }
        }


        // Ensures that threads have begun executing.
        private void EnsureStarted()
        {
            if (m_threads == null)
            {
                lock (m_queue)
                {
                    if (m_threads == null)
                    {
                        m_threads = new Thread[m_concurrencyLevel];
                        for (int i = 0; i < m_threads.Length; i++)
                        {
                            m_threads[i] = new Thread(DispatchLoop);
                            m_threads[i].Start();
                        }
                    }
                }
            }
        }

        // Each thread runs the dispatch loop.
        private void DispatchLoop()
        {
            while (true)
            {
                WorkItem wi = default(WorkItem);
                lock (m_queue)
                {
                    // If shutdown was requested, exit the thread.
                    if (m_shutdown)
                        return;

                    // Find a new work item to execute.
                    while (m_queue.Count == 0)
                    {
                        m_threadsWaiting++;
                        try { Monitor.Wait(m_queue); }
                        finally { m_threadsWaiting--; }

                        // If we were signaled due to shutdown, exit the thread.
                        if (m_shutdown)
                            return;
                    }

                    // We found a work item! Grab it ...
                    wi = m_queue.Dequeue();
                }

                // ...and Invoke it. Note: exceptions will go unhandled (and crash).
                wi.Invoke();
            }
        }

        // Disposing will signal shutdown, and then wait for all threads to finish.
        public void Dispose()
        {
            m_shutdown = true;
            lock (m_queue)
            {
                Monitor.PulseAll(m_queue);
            }

            if (m_threads != null)
                for (int i = 0; i < m_threads.Length; i++)
                    m_threads[i].Join();
        }
    }
}