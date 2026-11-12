using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: what <see cref="LayoutRules.Validate"/> found: the <see cref="Problems"/> in a stable order (by kind, which follows the
    /// rules, then by piece id, then by the other piece's id; problems equal in all three keep the order they were found in). Valid when there are none. Immutable; two reports are equal when their problems are.
    /// </summary>
    public sealed class LayoutReport : IEquatable<LayoutReport>
    {
        public LayoutReport(IEnumerable<LayoutProblem>? problems)
        {
            // Spec 062b (062a Part B review): sorted as the summary says, stably (LINQ's OrderBy keeps the rules' order on ties).
            Problems = (problems ?? Array.Empty<LayoutProblem>()).Where(p => p != null)
                .OrderBy(p => (int)p.Kind).ThenBy(p => p.PieceId).ThenBy(p => p.OtherId).ToList().AsReadOnly();
        }

        public IReadOnlyList<LayoutProblem> Problems { get; }
        public bool IsValid => Problems.Count == 0;

        /// <summary>How many problems are of kind <paramref name="kind"/>.</summary>
        public int Count(LayoutProblemKind kind) => Problems.Count(p => p.Kind == kind);

        /// <summary>One line per problem ("Aisle 7/8: Too close to the griddle: aisles need 0.9 m"), for the log.</summary>
        public IReadOnlyList<string> Lines() =>
            Problems.Select(p => p.Kind + " " + p.PieceId + "/" + p.OtherId + ": " + p.Text).ToList().AsReadOnly();

        public bool Equals(LayoutReport? other) => other is not null && Problems.SequenceEqual(other.Problems);
        public override bool Equals(object? obj) => obj is LayoutReport other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                foreach (LayoutProblem p in Problems) h = h * 31 + p.GetHashCode();
                return h;
            }
        }
    }
}
