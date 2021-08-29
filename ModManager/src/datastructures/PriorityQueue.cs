using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModManager.Datastructures
{
    internal class PriorityQueue<T> : IEnumerable<T>, IReadOnlyCollection<T>, ICollection, IEnumerable where T : struct, IComparable<T>
    {
        private int capacity = 10;
        private bool minQueue = false;
        private BinaryHeap<T> heap;

        #region Constructors
        public PriorityQueue()
        {
            this.heap = new BinaryHeap<T>(this.capacity, this.minQueue);
        }
        public PriorityQueue(bool minQueue)
        {
            this.minQueue = minQueue;
            this.heap = new BinaryHeap<T>(this.capacity, minQueue);
        }
        public PriorityQueue(IEnumerable<T> items, bool minQueue = false)
        {
            this.capacity = items.Count();
            this.minQueue = minQueue;
            this.heap = new BinaryHeap<T>(items, minQueue);
        }
        public PriorityQueue(int capacity, bool minQueue = false)
        {
            this.capacity = capacity;
            this.minQueue = minQueue;
            this.heap = new BinaryHeap<T>(capacity, minQueue);
        }
        #endregion

        public void Add(T item)
        {
            this.heap.Add(item);
        }

        public T Peek()
        {
            return this.heap.Peek();
        }

        public T Remove()
        {
            return this.heap.Remove();
        }

        public bool Contains(T item)
        {
            return this.heap.Contains(item);
        }

        public int Count => this.heap.Count;

        public object SyncRoot => this;

        public bool IsSynchronized => false;

        public void CopyTo(Array array, int index)
        {
            for (int i = 0; i < this.heap.Count; i++)
            {
                array.SetValue(this.heap.Remove(), index + i);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            while (this.heap.Count > 0)
            {
                yield return this.heap.Remove();
            }
            yield break;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
