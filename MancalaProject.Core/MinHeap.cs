using System;
using System.Collections.Generic;

namespace MancalaProject
{
    /// <summary>
    /// A generic binary min-heap implemented as a complete binary tree mapped
    /// onto a dynamic array. Items with the smallest associated priority are
    /// popped first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// To use this collection as a max-heap (popping the largest priority
    /// first), push items with their priority negated:
    /// <code>heap.Push(item, -score);</code>
    /// </para>
    /// <para>
    /// This heap serves as the agent's frontier in <see cref="BeamSearch"/>:
    /// every state discovered during search is held here, ordered by
    /// heuristic value, so locating the next-best expansion target costs
    /// O(log n) instead of O(n) over a flat list.
    /// </para>
    /// <para>
    /// Complete-binary-tree-on-array layout. For an item stored at index
    /// <c>i</c>:
    /// <list type="bullet">
    /// <item>parent  index = (i − 1) / 2</item>
    /// <item>left    child = 2·i + 1</item>
    /// <item>right   child = 2·i + 2</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <typeparam name="TItem">The type of items stored in the heap.</typeparam>
    /// <typeparam name="TPriority">
    /// The priority type used to order items. Must implement
    /// <see cref="IComparable{T}"/> so the heap can compare priorities.
    /// </typeparam>
    internal sealed class MinHeap<TItem, TPriority>
        where TPriority : IComparable<TPriority>
    {
        // The backing store. Using a List<(item, priority)> keeps the array
        // resize logic standard while pairing every item with its priority
        // in a single cache-friendly slot.
        private readonly List<(TItem Item, TPriority Priority)> _data;

        /// <summary>Creates an empty heap with default backing-array capacity.</summary>
        public MinHeap()
        {
            _data = new List<(TItem, TPriority)>();
        }

        /// <summary>Creates an empty heap with the given initial backing-array capacity.</summary>
        /// <param name="capacity">Initial allocation hint. The heap grows automatically beyond this.</param>
        public MinHeap(int capacity)
        {
            _data = new List<(TItem, TPriority)>(capacity);
        }

        /// <summary>The number of items currently in the heap.</summary>
        public int Count => _data.Count;

        /// <summary>
        /// Adds <paramref name="item"/> with the given <paramref name="priority"/>
        /// to the heap. Runs in O(log n) due to the bubble-up restoration of
        /// the heap invariant.
        /// </summary>
        public void Push(TItem item, TPriority priority)
        {
            _data.Add((item, priority));
            BubbleUp(_data.Count - 1);
        }

        /// <summary>
        /// Removes and returns the item with the smallest priority.
        /// Runs in O(log n).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the heap is empty.</exception>
        public TItem Pop()
        {
            ThrowIfEmpty();
            TItem result = _data[0].Item;

            // Replace the root with the last leaf, then sift it back down.
            int lastIndex = _data.Count - 1;
            _data[0] = _data[lastIndex];
            _data.RemoveAt(lastIndex);

            if (_data.Count > 0)
                BubbleDown(0);

            return result;
        }

        /// <summary>
        /// Returns the item with the smallest priority without removing it.
        /// Runs in O(1).
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the heap is empty.</exception>
        public TItem Peek()
        {
            ThrowIfEmpty();
            return _data[0].Item;
        }

        // ============================================================
        //  Internal heap-invariant maintenance
        //
        //  Both BubbleUp and BubbleDown are implemented as tail recursion
        //  rather than while-loops with break, so the control flow stays
        //  flat (no break/continue statements anywhere). Stack depth is
        //  O(log n), well within safe limits for the agent's budget.
        // ============================================================

        // Move the item at `index` up the tree while it is smaller than its
        // parent. Restores the heap property after a Push.
        private void BubbleUp(int index)
        {
            if (index > 0)
            {
                int parent = (index - 1) / 2;
                if (IsLowerPriority(index, parent))
                {
                    Swap(index, parent);
                    BubbleUp(parent);
                }
            }
        }

        // Move the item at `index` down the tree while a child has a smaller
        // priority than it. Restores the heap property after a Pop.
        private void BubbleDown(int index)
        {
            int smallest = SmallestOfNodeAndChildren(index);
            if (smallest != index)
            {
                Swap(index, smallest);
                BubbleDown(smallest);
            }
        }

        // Returns whichever of {index, leftChild, rightChild} currently holds
        // the smallest priority. Out-of-range children are skipped.
        private int SmallestOfNodeAndChildren(int index)
        {
            int left     = 2 * index + 1;
            int right    = 2 * index + 2;
            int smallest = index;
            if (left  < _data.Count && IsLowerPriority(left,  smallest)) smallest = left;
            if (right < _data.Count && IsLowerPriority(right, smallest)) smallest = right;
            return smallest;
        }

        private bool IsLowerPriority(int a, int b) =>
            _data[a].Priority.CompareTo(_data[b].Priority) < 0;

        private void Swap(int a, int b)
        {
            (TItem Item, TPriority Priority) tmp = _data[a];
            _data[a] = _data[b];
            _data[b] = tmp;
        }

        private void ThrowIfEmpty()
        {
            if (_data.Count == 0)
                throw new InvalidOperationException("Heap is empty.");
        }
    }
}
