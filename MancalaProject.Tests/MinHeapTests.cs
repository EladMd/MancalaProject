using MancalaProject;

namespace MancalaProject.Tests
{
    /// <summary>
    /// Unit tests for the hand-written <see cref="MinHeap{TItem,TPriority}"/> —
    /// the priority queue that serves as the search frontier. Verifies the
    /// min-heap ordering, peeking, counting, the empty-heap guards and the
    /// negated-priority trick the beam search uses to obtain max-heap behaviour.
    /// </summary>
    [TestClass]
    public class MinHeapTests
    {
        [TestMethod]
        public void Pop_ReturnsItemsInAscendingPriorityOrder()
        {
            var heap = new MinHeap<string, int>();
            heap.Push("priority-3", 3);
            heap.Push("priority-1", 1);
            heap.Push("priority-2", 2);

            Assert.AreEqual("priority-1", heap.Pop());
            Assert.AreEqual("priority-2", heap.Pop());
            Assert.AreEqual("priority-3", heap.Pop());
        }

        [TestMethod]
        public void Peek_ReturnsSmallestPriorityWithoutRemovingIt()
        {
            var heap = new MinHeap<string, int>();
            heap.Push("big", 50);
            heap.Push("small", 5);

            Assert.AreEqual("small", heap.Peek());
            Assert.AreEqual(2, heap.Count);   // nothing removed
        }

        [TestMethod]
        public void Count_TracksPushesAndPops()
        {
            var heap = new MinHeap<int, int>();
            Assert.AreEqual(0, heap.Count);

            heap.Push(1, 1);
            heap.Push(2, 2);
            Assert.AreEqual(2, heap.Count);

            heap.Pop();
            Assert.AreEqual(1, heap.Count);
        }

        [TestMethod]
        public void Pop_OnEmptyHeap_ThrowsInvalidOperationException()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new MinHeap<int, int>().Pop());
        }

        [TestMethod]
        public void Peek_OnEmptyHeap_ThrowsInvalidOperationException()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new MinHeap<int, int>().Peek());
        }

        [TestMethod]
        public void NegatedPriority_ProducesMaxHeapBehaviour()
        {
            // The beam search pushes nodes with priority = -heuristic so the
            // highest-scoring state is popped first. This mirrors that use.
            var heap = new MinHeap<string, int>();
            heap.Push("score-5", -5);
            heap.Push("score-20", -20);
            heap.Push("score-12", -12);

            Assert.AreEqual("score-20", heap.Pop());   // highest score first
            Assert.AreEqual("score-12", heap.Pop());
            Assert.AreEqual("score-5", heap.Pop());
        }

        [TestMethod]
        public void Pop_OverManyItems_DrainsTheHeapFullyOrdered()
        {
            var heap = new MinHeap<int, int>();

            // (i * 7) % 50 is a permutation of 0..49, so the items are pushed
            // in a scrambled order and must still come out sorted.
            for (int i = 0; i < 50; i++)
            {
                int value = (i * 7) % 50;
                heap.Push(value, value);
            }

            int previous = -1;
            for (int i = 0; i < 50; i++)
            {
                int popped = heap.Pop();
                Assert.IsTrue(popped > previous, "items must come out in ascending order");
                previous = popped;
            }
            Assert.AreEqual(0, heap.Count);
        }
    }
}
