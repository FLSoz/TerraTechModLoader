using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using LogManager;


namespace ModManager.Datastructures
{
    internal class DependencyGraph<T>
    {
        private HashSet<Node> leaves = new HashSet<Node>();
        private HashSet<Node> roots = new HashSet<Node>();
        private List<Node> nodes = new List<Node>();
        private List<HashSet<int>> children = new List<HashSet<int>>();
        private List<HashSet<int>> parents = new List<HashSet<int>>();
        private Dictionary<T, int> valIdMap = new Dictionary<T, int>();

        private static NLog.Logger logger = NLog.LogManager.GetLogger($"ModManager.DependencyGraph-{typeof(T).Name}");
        internal static void ConfigureLogger()
        {
            LogManager.LogConfig config = new LogManager.LogConfig
            {
                layout = "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${logger:shortName=true} | ${message}  ${exception}",
                keepOldFiles = false
            };
            TTLogManager.RegisterLogger(logger, config);
        }

        public DependencyGraph() { }
        public DependencyGraph(IEnumerable<Node> items)
        {
            foreach (Node item in items)
            {
                this.AddNode(item);
            }
        }

        public void AddNode(Node node)
        {
            node.id = this.nodes.Count;
            
            this.nodes.Add(node);
            this.children.Add(new HashSet<int>());
            this.parents.Add(new HashSet<int>());

            this.valIdMap.Add(node.value, node.id);
            this.leaves.Add(node);
            this.roots.Add(node);

            logger.Debug("Added node to dependency graph: {$node}", node);
        }

        public void AddEdge(int source, int destination)
        {
            if (source <= this.nodes.Count && destination <= this.nodes.Count)
            {
                this.leaves.Remove(this.nodes[source]);

                this.roots.Remove(this.nodes[destination]);
                if (this.parents[source].Count == 0)
                {
                    this.roots.Add(this.nodes[source]);
                }

                this.children[source].Add(destination);
                this.parents[destination].Add(source);

                logger.Debug("Added edge from {sourceId} to {destinationId}", source, destination);
            }
            else
            {
                logger.Error("Trying to add edge between {sourceId} and {destinationId}, but there are only {size} nodes", source, destination, this.nodes.Count);
            }
        }

        public void AddEdge(T source, T destination)
        {
            if (this.valIdMap.TryGetValue(source, out int sourceId) && this.valIdMap.TryGetValue(destination, out int destinationId))
            {
                logger.Debug("Added edge from {source} ({sourceId}) => {destination} ({destinationId})", source, sourceId, destination, destinationId);
                this.AddEdge(sourceId, destinationId);
            }
            else
            {
                logger.Error("Failed to add edge from {source} => {destination}", source, destination);
            }
        }

        public Node GetNodeByValue(T value)
        {
            return this.nodes[this.valIdMap[value]];
        }

        private bool isCyclicUtil(int i, bool[] visited, bool[] recStack)
        {
            // Mark the current node as visited and
            // part of recursion stack
            if (recStack[i])
            {
                return true;
            }

            if (visited[i])
            {
                return false;
            }

            visited[i] = true;

            recStack[i] = true;
            HashSet<int> children = this.children[i];

            foreach (int c in children)
            {
                if (isCyclicUtil(c, visited, recStack))
                {
                    return true;
                }
            }

            recStack[i] = false;

            return false;
        }

        private bool DetectCyclesDFS()
        {
            bool[] visited = new bool[this.nodes.Count];
            bool[] recStack = new bool[this.nodes.Count];

            for (int i = 0; i < this.nodes.Count; i++)
            {
                if (isCyclicUtil(i, visited, recStack))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasCycles()
        {
            return this.DetectCyclesDFS();
        }

        public List<List<int>> FindCycles()
        {
            List<List<int>> cycles = new List<List<int>>();

            return cycles;
        }

        public void PrintCycles(LogLevel logLevel, List<List<int>> cycles)
        {
            foreach (List<int> cycle in cycles)
            {
                logger.Log(logLevel, "Cycle found with elements: {@cycle}", cycle);
            }
        }

        public void ResolveCycles(List<List<int>> cycles)
        {
            logger.Warn("Resolving cycles via Social Agony alg");

            // Roots are still guaranteed to be roots.
            // Going from roots downwards, we assign ranking
            // Importance is assigned via BFS, and starts with 1 for most important.
            // Afterwards, we assign from leaves upwards for any remainders, and link them up
            // We run the social agony alg on all nodes calculated by above method
            // After, only un-ranked nodes are those of isolated cycles. May be multiple cycles - but no node in the connected segment is not a cycle
            // Here, we try to check for intersecting dependencies.

            int[] importance = new int[this.nodes.Count];
            int[] tiersRoot = new int[this.nodes.Count];


            int[] tiersLeaves = new int[this.nodes.Count];
        }

        public IEnumerable<T> OrderedQueue()
        {
            // We assume that we know there are no cycles, and that leaves form the leaves
            // Update order priority - how urgently we want to run this node based on the priority of its dependent nodes
            // Currently, priority is only present if the following nodes have a higher priority than the original node
            Queue<Node> frontier = new Queue<Node>(10);
            bool[] visited = new bool[this.nodes.Count];
            foreach (Node root in this.roots)
            {
                if (!this.leaves.Contains(root))
                {
                    frontier.Enqueue(root);
                }
            }
            while (frontier.Count > 0)
            {
                Node curr = frontier.Dequeue();

                HashSet<int> parents = this.parents[curr.id];
                List<Node> parentNodes = parents.Select(i => this.nodes[i]).ToList();
                
                bool allParents = true;
                int maxPriority = 0;
                foreach (int parentInd in parents)
                {
                    if (!visited[parentInd])
                    {
                        allParents = false;
                        break;
                    }
                    Node parentNode = this.nodes[parentInd];
                    maxPriority = Math.Max(maxPriority, parentNode.orderPriority + Math.Max(0, curr.order - parentNode.order));
                }
                if (allParents)
                {
                    curr.orderPriority = maxPriority;
                    visited[curr.id] = true;

                    HashSet<int> children = this.children[curr.id];
                    foreach (int childInd in children)
                    {
                        Node child = this.nodes[childInd];
                        if (!frontier.Contains(child))
                        {
                            frontier.Enqueue(child);
                        }
                    }
                }
                else
                {
                    frontier.Enqueue(curr);
                }
            }

            // Order Priority is set - going to be used in comparison operators by priority queue when load order is tied
            // Otherwise, load order determines the order mods are processed. If order and order priority are the same, then random (alphabetical) order is used
            PriorityQueue<Node> queue = new PriorityQueue<Node>(this.leaves, true);
            for (int i = 0; i < this.nodes.Count; i++)
            {
                visited[i] = false;
            }
            while (queue.Count > 0)
            {
                Node curr = queue.Remove();
                visited[curr.id] = true;

                // add parents if not yet processed
                foreach (int parentId in this.parents[curr.id])
                {
                    Node parent = this.nodes[parentId];
                    bool allChildren = true;
                    foreach (int child in this.children[parentId])
                    {
                        if (!visited[child])
                        {
                            allChildren = false;
                            break;
                        }
                    }

                    if (allChildren && !queue.Contains(parent) && !visited[parentId])
                    {
                        queue.Add(parent);
                    }
                }
                yield return curr.value;
            }
            yield break;
        }

        [Serializable]
        internal struct Node : IComparable<Node>, IEquatable<Node>
        {
            public int id;
            public int orderPriority;
            public int order;
            public T value;

            public Node(T value, int order)
            {
                this.id = -1;
                this.orderPriority = 0;
                this.value = value;
                this.order = order;
            }

            public override string ToString()
            {
                return $"[id: {id}, value: {value}, order: {order}, orderPriority: {orderPriority}]";
            }

            public int CompareTo(Node other)
            {
                if (other.id == -1 || this.order > other.order)
                {
                    return 1;
                }
                else if (this.order < other.order)
                {
                    return -1;
                }
                else
                {
                    int comparison = -this.orderPriority.CompareTo(other.orderPriority);
                    if (comparison == 0)
                    {
                        return this.value.ToString().CompareTo(other.value.ToString());
                    }
                    return comparison;
                }
            }

            public bool Equals(Node other)
            {
                return this.id == other.id && this.value != null && this.value.Equals(other.value) && this.order == other.order;
            }

            // Define the is greater than operator.
            public static bool operator > (Node operand1, Node operand2)
            {
                return operand1.CompareTo(operand2) > 0;
            }

            // Define the is less than operator.
            public static bool operator < (Node operand1, Node operand2)
            {
                return operand1.CompareTo(operand2) < 0;
            }

            // Define the is greater than or equal to operator.
            public static bool operator >= (Node operand1, Node operand2)
            {
                return operand1.CompareTo(operand2) >= 0;
            }

            // Define the is less than or equal to operator.
            public static bool operator <= (Node operand1, Node operand2)
            {
                return operand1.CompareTo(operand2) <= 0;
            }
        }
    }
}
