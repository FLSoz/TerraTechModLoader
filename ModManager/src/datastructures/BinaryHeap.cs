using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModManager.Datastructures
{
    public class BinaryHeap<T> where T : struct, IComparable<T>
    {
        private bool minHeap = false;
        public int Count {
            get => this.array.Count - 1;
        }
        private int capacity = 10;
        private List<T> array;

        #region Constructors
        public BinaryHeap(bool minHeap)
        {
            this.minHeap = minHeap;
            this.array = new List<T>(this.capacity + 1);
            this.array.Add(new T());
        }
        public BinaryHeap(int capacity, bool minHeap = false)
        {
            this.capacity = capacity;
            this.minHeap = minHeap;
            this.array = new List<T>(this.capacity + 1);
            this.array.Add(new T());
        }
        public BinaryHeap(IEnumerable<T> items, bool minHeap = false)
        {
            this.capacity = items.Count();
            this.minHeap = minHeap;

            int i = items.Count() / 2;

            this.array = new List<T>(this.capacity + 1);
            this.array.Add(new T());
            this.array.AddRange(items);

            while (i > 0)
            {
                this.PropagateDown(i);
                i--;
            }
        }
        #endregion

        private void PropagateUp(int index)
        {
            int halfInd;
            while ((halfInd = index / 2) > 0)
            {
                if (this.array[index].CompareTo(this.array[halfInd]) < 0 ^ !this.minHeap)
                {
                    T tmp = this.array[halfInd];
                    this.array[halfInd] = this.array[index];
                    this.array[index] = tmp;
                }
                index /= 2;
            }
        }

        private int OpChild(int index)
        {
            if ((index * 2) + 1 > this.Count)
            {
                return index * 2;
            }
            else if (this.array[index * 2].CompareTo(this.array[(index * 2) + 1]) < 0 ^ !this.minHeap)
            {
                return index * 2;
            }
            else
            {
                return (index * 2) + 1;
            }
        }

        private void PropagateDown(int index)
        {
            while (index * 2 <= this.Count)
            {
                int child = this.OpChild(index);
                if (this.array[index].CompareTo(this.array[child]) > 0 ^ !this.minHeap)
                {
                    T tmp = this.array[child];
                    this.array[child] = this.array[index];
                    this.array[index] = tmp;
                }
                index = child;
            }
        }

        public void Add(T item)
        {
            this.array.Add(item);
            this.PropagateUp(this.Count);
        }

        public T Peek()
        {
            return this.array[1];
        }

        public T Remove()
        {
            T retval = this.array[1];
            this.array[1] = this.array[this.Count];
            this.array.RemoveAt(this.Count);
            this.PropagateDown(1);
            return retval;
        }

        public bool Contains(T item)
        {
            return this.array.Contains(item);
        }
    }
}
