using System;
using System.Collections.Generic;

namespace HexWorldStudio.Data
{
    /// <summary>
    /// One step of a river: the traversal of a single hex edge from one
    /// hex corner to an adjacent one (ADR-0025).
    /// </summary>
    /// <remarks>
    /// A step is either a <b>channel</b> — the river running along the
    /// seam between two tiles at the same <c>ElevationLevel</c> — or a
    /// <b>waterfall</b>, the single-edge crossing of a terrace
    /// boundary. <see cref="DropLevels"/> distinguishes them.
    /// </remarks>
    [Serializable]
    public readonly struct RiverStep
    {
        /// <summary>The upstream corner this step leaves.</summary>
        public HexCorner From { get; }

        /// <summary>The downstream corner this step arrives at.</summary>
        public HexCorner To { get; }

        /// <summary>
        /// The hex edge traversed, in canonical form. Always present in
        /// <see cref="WorldMap.RiverEdges"/>.
        /// </summary>
        public RiverEdge Edge { get; }

        /// <summary>
        /// The <c>ElevationLevel</c> of the terrace the river occupies
        /// entering this step — the higher of the two tiles flanking
        /// <see cref="Edge"/>.
        /// </summary>
        public int FromLevel { get; }

        /// <summary>
        /// The <c>ElevationLevel</c> of the terrace the river occupies
        /// leaving this step — the lower of the two tiles flanking
        /// <see cref="Edge"/>. Equal to <see cref="FromLevel"/> for a
        /// channel step.
        /// </summary>
        public int ToLevel { get; }

        /// <summary>
        /// Elevation levels descended by this step. <c>0</c> for a
        /// channel (both tiles of <see cref="Edge"/> sit at the same
        /// level); greater than zero for a waterfall, giving the height
        /// of the drop in terrace steps.
        /// </summary>
        public int DropLevels => FromLevel - ToLevel;

        /// <summary>
        /// <c>true</c> when this step crosses a terrace boundary rather
        /// than running along one. Renderers draw these as falls: the
        /// water surface descends from <see cref="FromLevel"/> to
        /// <see cref="ToLevel"/> across this single edge.
        /// </summary>
        public bool IsWaterfall => FromLevel != ToLevel;

        /// <summary>Constructs a river step.</summary>
        /// <param name="from">Upstream corner.</param>
        /// <param name="to">Downstream corner.</param>
        /// <param name="edge">The canonical hex edge traversed.</param>
        /// <param name="fromLevel">Terrace level entering the step.</param>
        /// <param name="toLevel">Terrace level leaving the step.</param>
        public RiverStep(HexCorner from, HexCorner to, RiverEdge edge, int fromLevel, int toLevel)
        {
            From = from;
            To = to;
            Edge = edge;
            FromLevel = fromLevel;
            ToLevel = toLevel;
        }

        /// <inheritdoc/>
        public override string ToString() =>
            IsWaterfall ? $"Fall({From} -> {To}, {FromLevel}->{ToLevel})" : $"Flow({From} -> {To} @{FromLevel})";
    }

    /// <summary>
    /// A single river, as an ordered sequence of
    /// <see cref="RiverStep"/>s running from its source corner to its
    /// mouth (ADR-0025).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paths are the <b>directed, ordered</b> view of the river
    /// network; <see cref="WorldMap.RiverEdges"/> is the unordered
    /// canonical set of the same edges. Use paths when direction
    /// matters — flow animation, "which way is downstream", tracing a
    /// river to the sea — and the edge set for adjacency queries.
    /// </para>
    /// <para>
    /// Consecutive steps share a corner
    /// (<c>Steps[i].To == Steps[i + 1].From</c>), which is what makes a
    /// path render as one continuous ribbon.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class RiverPath
    {
        private readonly RiverStep[] _steps;

        /// <summary>
        /// The ordered steps, source first. Never empty for a path
        /// stored on a <see cref="WorldMap"/>.
        /// </summary>
        public IReadOnlyList<RiverStep> Steps => _steps;

        /// <summary>The corner this river rises at.</summary>
        public HexCorner Source => _steps[0].From;

        /// <summary>
        /// The corner this river ends at — touching the sea or a lake
        /// when the river reached water, otherwise the furthest point
        /// its descent could reach.
        /// </summary>
        public HexCorner Mouth => _steps[_steps.Length - 1].To;

        /// <summary>Number of hex edges this river occupies.</summary>
        public int Length => _steps.Length;

        /// <summary>
        /// <c>true</c> when this river terminates at a corner touching
        /// water (ocean, coast, or lake) rather than stranding inland.
        /// </summary>
        public bool ReachesWater { get; }

        /// <summary>
        /// Strahler stream order: <c>1</c> for a headwater with no
        /// tributaries, rising by one wherever two streams of equal
        /// order meet. Renderers scale river width by this so trunk
        /// rivers read larger than headwaters.
        /// </summary>
        public int StrahlerOrder { get; internal set; }

        /// <summary>Constructs a river path from its ordered steps.</summary>
        /// <param name="steps">Ordered source-to-mouth steps. Must be non-empty.</param>
        /// <param name="reachesWater">Whether the path terminates at water.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="steps"/> is null or empty.</exception>
        public RiverPath(IReadOnlyList<RiverStep> steps, bool reachesWater)
        {
            if (steps == null || steps.Count == 0)
            {
                throw new ArgumentException("A river path needs at least one step.", nameof(steps));
            }

            _steps = new RiverStep[steps.Count];
            for (int i = 0; i < steps.Count; i++) _steps[i] = steps[i];

            ReachesWater = reachesWater;
            StrahlerOrder = 1;
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"RiverPath[{Source} -> {Mouth}, {Length} edges, order {StrahlerOrder}]";
    }
}
