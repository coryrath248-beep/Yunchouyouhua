using System;
using System.Collections.Generic;

namespace FactoryTwin
{
    /// <summary>
    /// Per-resource mutual exclusion with a FIFO queue of waiters. When the current
    /// owner releases the resource, the oldest waiter is granted it immediately.
    /// This is the building block for multi-workpiece concurrency.
    /// </summary>
    public sealed class ResourceLock
    {
        private sealed class ResourceRequest
        {
            public readonly string Owner;
            public readonly Action<string> OnAcquired;

            public ResourceRequest(string owner, Action<string> onAcquired)
            {
                Owner = owner;
                OnAcquired = onAcquired;
            }
        }

        private readonly Queue<ResourceRequest> waiters = new Queue<ResourceRequest>();

        public string Owner { get; private set; }
        public bool IsFree => string.IsNullOrEmpty(Owner);
        public int WaitingCount => waiters.Count;

        public bool TryAcquire(string owner)
        {
            if (string.IsNullOrEmpty(owner) || !IsFree)
            {
                return false;
            }

            Owner = owner;
            return true;
        }

        /// <summary>
        /// Acquires immediately when the resource is free; otherwise enqueues the
        /// waiter and reports false. The callback fires as soon as the resource
        /// becomes available.
        /// </summary>
        public bool TryAcquireOrEnqueue(string owner, Action<string> onAcquired)
        {
            if (string.IsNullOrEmpty(owner))
            {
                return false;
            }

            if (IsFree)
            {
                Owner = owner;
                onAcquired?.Invoke(owner);
                return true;
            }

            waiters.Enqueue(new ResourceRequest(owner, onAcquired));
            return false;
        }

        public bool Release(string owner)
        {
            if (!string.Equals(Owner, owner, StringComparison.Ordinal))
            {
                return false;
            }

            Owner = null;
            if (waiters.Count > 0)
            {
                ResourceRequest next = waiters.Dequeue();
                Owner = next.Owner;
                next.OnAcquired?.Invoke(Owner);
            }

            return true;
        }
    }

    /// <summary>
    /// Named resource locks shared by the production flow. Keys are stable resource
    /// identifiers such as "robot_01", "machine_m05" or "conveyor_line2_AB".
    /// </summary>
    public sealed class ResourcePool
    {
        private readonly Dictionary<string, ResourceLock> locks =
            new Dictionary<string, ResourceLock>(StringComparer.Ordinal);

        public ResourceLock Get(string key)
        {
            if (!locks.TryGetValue(key, out ResourceLock value))
            {
                value = new ResourceLock();
                locks.Add(key, value);
            }

            return value;
        }

        public bool TryAcquire(string key, string owner)
        {
            return Get(key).TryAcquire(owner);
        }

        public bool TryAcquireOrEnqueue(string key, string owner, Action<string> onAcquired)
        {
            return Get(key).TryAcquireOrEnqueue(owner, onAcquired);
        }

        public bool Release(string key, string owner)
        {
            return Get(key).Release(owner);
        }
    }
}
