using System;
using System.Collections.Generic;
using AutomationMonitor.Core.Models;

namespace AutomationMonitor.Core.Services
{
    /// <summary>
    /// Builds parent/child execution trees from run evidence only.
    ///
    /// <para>
    /// The rule this class exists to enforce: a link exists when a run's recorded parent identifier
    /// matches a retrieved run's identifier, and for no other reason. Similar workflow names,
    /// near-identical start times, a shared trigger or a shared correlation id are all deliberately
    /// ignored here - they are not evidence, and a guessed link that reaches an operator looking
    /// like a confirmed hierarchy is worse than no link at all.
    /// </para>
    /// <para>
    /// Nothing is recursive. Run rows are ordinary data and a faulty ingestion can produce a parent
    /// chain of any shape, including a cycle; walking that with recursion would hand the plugin host
    /// a stack overflow instead of an honest "this hierarchy is inconsistent" message. Cyclic
    /// members are lifted out as roots and reported, so the graph that remains is a forest by
    /// construction and every retrieved run appears exactly once.
    /// </para>
    /// </summary>
    public static class RunCorrelationResolver
    {
        private const int Settled = 1;

        /// <summary>
        /// Resolve one result set into trees. Order is stable: roots in the order the runs arrived,
        /// children by start time with undated children last.
        /// </summary>
        public static RunCorrelationResult Resolve(IEnumerable<WorkflowRun>? runs)
        {
            List<WorkflowRun> all = new List<WorkflowRun>();
            if (runs != null)
            {
                foreach (WorkflowRun run in runs)
                {
                    if (run != null)
                    {
                        all.Add(run);
                    }
                }
            }

            Dictionary<string, WorkflowRun> byRunId = IndexById(all, out HashSet<string> ambiguous);
            HashSet<string> cyclic = FindCycles(all, byRunId, ambiguous);
            Dictionary<string, List<WorkflowRun>> childrenOf = BuildChildren(all, byRunId, ambiguous, cyclic);
            return BuildForest(all, byRunId, ambiguous, cyclic, childrenOf);
        }

        /// <summary>
        /// Map run id to the run carrying it. Two different runs sharing one id make that id
        /// meaningless as a parent reference, so it is removed from the index and recorded as
        /// ambiguous instead of picking one arbitrarily.
        /// </summary>
        private static Dictionary<string, WorkflowRun> IndexById(List<WorkflowRun> all, out HashSet<string> ambiguous)
        {
            Dictionary<string, WorkflowRun> byRunId = new Dictionary<string, WorkflowRun>(StringComparer.Ordinal);
            ambiguous = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < all.Count; i++)
            {
                WorkflowRun run = all[i];
                WorkflowRun? existing;
                if (!byRunId.TryGetValue(run.RunId, out existing))
                {
                    byRunId[run.RunId] = run;
                }
                else if (!existing.Key.Equals(run.Key))
                {
                    ambiguous.Add(run.RunId);
                    byRunId.Remove(run.RunId);
                }
            }

            return byRunId;
        }

        /// <summary>
        /// Walk every run's parent chain with an explicit path set. Anything on a closed loop is
        /// marked cyclic; a node that merely leads into a cycle is not marked, because its own
        /// evidence is coherent - it will simply hang under a cyclic root.
        /// </summary>
        private static HashSet<string> FindCycles(List<WorkflowRun> all, Dictionary<string, WorkflowRun> byRunId, HashSet<string> ambiguous)
        {
            HashSet<string> cyclic = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (WorkflowRun run in all)
            {
                if (!state.ContainsKey(run.Key.Canonical))
                {
                    TraceParentChain(run, byRunId, ambiguous, state, cyclic);
                }
            }

            return cyclic;
        }

        /// <summary>
        /// Iterative walk over parent links with the current path recorded. A node met twice on the
        /// path closes a cycle, and everything from that node onward is marked cyclic. A parent that
        /// is not in the result set ends the walk without judgement: that is a coverage gap, not a
        /// contradiction.
        /// </summary>
        private static void TraceParentChain(
            WorkflowRun start,
            Dictionary<string, WorkflowRun> byRunId,
            HashSet<string> ambiguous,
            Dictionary<string, int> state,
            HashSet<string> cyclic)
        {
            List<WorkflowRun> path = new List<WorkflowRun>();
            Dictionary<string, int> position = new Dictionary<string, int>(StringComparer.Ordinal);
            WorkflowRun? cursor = start;

            while (cursor != null)
            {
                string key = cursor.Key.Canonical;

                int alreadySettled;
                if (state.TryGetValue(key, out alreadySettled))
                {
                    break;
                }

                int seenAt;
                if (position.TryGetValue(key, out seenAt))
                {
                    for (int i = seenAt; i < path.Count; i++)
                    {
                        cyclic.Add(path[i].Key.Canonical);
                    }

                    break;
                }

                position[key] = path.Count;
                path.Add(cursor);
                cursor = ResolveParent(cursor, byRunId, ambiguous);
            }

            for (int i = 0; i < path.Count; i++)
            {
                state[path[i].Key.Canonical] = Settled;
            }
        }

        /// <summary>The run a parent reference points at, or null when it resolves to nothing here.</summary>
        private static WorkflowRun? ResolveParent(WorkflowRun run, Dictionary<string, WorkflowRun> byRunId, HashSet<string> ambiguous)
        {
            string? parentId = run.ParentRunId;
            if (string.IsNullOrEmpty(parentId) || ambiguous.Contains(parentId!))
            {
                return null;
            }

            WorkflowRun? parent;
            if (!byRunId.TryGetValue(parentId!, out parent) || ReferenceEquals(parent, run))
            {
                return null;
            }

            return parent;
        }

        /// <summary>
        /// Children keyed on the parent's canonical key. Cyclic members are excluded as children
        /// because they are presented as roots instead, which is what makes the remaining graph a
        /// forest and the walk below terminate without a visited set.
        /// </summary>
        private static Dictionary<string, List<WorkflowRun>> BuildChildren(
            List<WorkflowRun> all,
            Dictionary<string, WorkflowRun> byRunId,
            HashSet<string> ambiguous,
            HashSet<string> cyclic)
        {
            Dictionary<string, List<WorkflowRun>> childrenOf = new Dictionary<string, List<WorkflowRun>>(StringComparer.Ordinal);
            foreach (WorkflowRun run in all)
            {
                if (cyclic.Contains(run.Key.Canonical))
                {
                    continue;
                }

                WorkflowRun? parent = ResolveParent(run, byRunId, ambiguous);
                if (parent == null)
                {
                    continue;
                }

                List<WorkflowRun>? siblings;
                if (!childrenOf.TryGetValue(parent.Key.Canonical, out siblings))
                {
                    siblings = new List<WorkflowRun>();
                    childrenOf[parent.Key.Canonical] = siblings;
                }

                siblings.Add(run);
            }

            foreach (List<WorkflowRun> siblings in childrenOf.Values)
            {
                siblings.Sort(CompareByStart);
            }

            return childrenOf;
        }

        private static int CompareByStart(WorkflowRun? left, WorkflowRun? right)
        {
            if (left == null || right == null)
            {
                return ReferenceEquals(left, right) ? 0 : (left == null ? 1 : -1);
            }

            if (left.StartedAtUtc.HasValue != right.StartedAtUtc.HasValue)
            {
                return left.StartedAtUtc.HasValue ? -1 : 1;
            }

            if (left.StartedAtUtc.HasValue)
            {
                int byStart = left.StartedAtUtc!.Value.CompareTo(right.StartedAtUtc!.Value);
                if (byStart != 0)
                {
                    return byStart;
                }
            }

            return string.Compare(left.Key.Canonical, right.Key.Canonical, StringComparison.Ordinal);
        }

        /// <summary>
        /// Assemble the forest. Roots are the runs that name no parent, the runs whose parent is not
        /// in this result set, and every cyclic member - so a broken or missing link can only ever
        /// make a row harder to place, never make it disappear. Depth and subtree totals are then
        /// computed with a breadth-first expansion and a reverse-order pass; no recursion.
        /// </summary>
        private static RunCorrelationResult BuildForest(
            List<WorkflowRun> all,
            Dictionary<string, WorkflowRun> byRunId,
            HashSet<string> ambiguous,
            HashSet<string> cyclic,
            Dictionary<string, List<WorkflowRun>> childrenOf)
        {
            List<RunTree> roots = new List<RunTree>();
            List<string> unresolvedParents = new List<string>();
            int linked = 0;

            for (int i = 0; i < all.Count; i++)
            {
                WorkflowRun run = all[i];
                bool isCyclic = cyclic.Contains(run.Key.Canonical);
                WorkflowRun? parent = ResolveParent(run, byRunId, ambiguous);

                if (isCyclic || parent == null)
                {
                    roots.Add(new RunTree(run, 0, parent != null, isCyclic));
                    if (!isCyclic && !string.IsNullOrEmpty(run.ParentRunId) && !ambiguous.Contains(run.ParentRunId!))
                    {
                        AddDistinct(unresolvedParents, run.ParentRunId!);
                    }
                }
                else
                {
                    linked++;
                }
            }

            Dictionary<string, WorkflowRun> byCanonical = new Dictionary<string, WorkflowRun>(StringComparer.Ordinal);
            for (int i = 0; i < all.Count; i++)
            {
                byCanonical[all[i].Key.Canonical] = all[i];
            }

            // Expand breadth-first from the roots: each run is placed once, and its depth is known
            // before its own children are queued, so a deep chain costs heap, not stack.
            List<RunTree> order = new List<RunTree>(roots);
            for (int index = 0; index < order.Count; index++)
            {
                RunTree node = order[index];
                List<WorkflowRun>? children;
                if (!childrenOf.TryGetValue(node.Run.Key.Canonical, out children) || children == null)
                {
                    continue;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    RunTree childNode = new RunTree(children[i], node.Depth + 1, true, false);
                    node.Children.Add(childNode);
                    order.Add(childNode);
                }
            }

            Accumulate(order);

            Dictionary<string, IReadOnlyList<WorkflowRun>> childrenByRunId =
                new Dictionary<string, IReadOnlyList<WorkflowRun>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<WorkflowRun>> entry in childrenOf)
            {
                WorkflowRun? parent;
                if (byCanonical.TryGetValue(entry.Key, out parent))
                {
                    childrenByRunId[parent!.RunId] = entry.Value;
                }
            }

            List<string> ambiguousIds = new List<string>(ambiguous);
            ambiguousIds.Sort(StringComparer.Ordinal);

            return new RunCorrelationResult(roots, childrenByRunId, unresolvedParents, ambiguousIds, CyclicRunIds(all, cyclic), linked);
        }

        private static List<string> CyclicRunIds(List<WorkflowRun> all, HashSet<string> cyclic)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                if (cyclic.Contains(all[i].Key.Canonical))
                {
                    ids.Add(all[i].RunId);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>
        /// Subtree totals, computed children-first by walking the breadth-first order backwards. A
        /// duration the source never stated makes the subtree total incomplete and is flagged as
        /// such: a running child contributes no guessed number, and the caller is told the total is
        /// not a total. These are run counts and run milliseconds - never action or transaction
        /// counts, which run rows do not carry.
        /// </summary>
        private static void Accumulate(List<RunTree> order)
        {
            for (int i = order.Count - 1; i >= 0; i--)
            {
                RunTree node = order[i];
                int descendants = 0;
                long? total = node.Run.DurationMs;
                bool gap = node.Run.DurationMs == null;

                for (int c = 0; c < node.Children.Count; c++)
                {
                    RunTree child = node.Children[c];
                    descendants += 1 + child.DescendantCount;
                    if (child.HasDurationGap || child.TotalDurationMs == null)
                    {
                        gap = true;
                    }

                    if (child.TotalDurationMs.HasValue)
                    {
                        total = (total ?? 0L) + child.TotalDurationMs.Value;
                    }
                }

                node.DescendantCount = descendants;
                node.TotalDurationMs = total;
                node.HasDurationGap = gap;
            }
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (!values.Contains(value))
            {
                values.Add(value);
            }
        }
    }
}



