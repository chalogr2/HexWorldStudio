using System;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Pipeline slot that decides which tiles are land and which are
    /// water. The slot is pluggable per ADR-0010 + ADR-0020: this
    /// stage routes to a concrete continent-shape algorithm picked by
    /// <see cref="WorldGenerationSettings.ContinentShapeAlgorithm"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stage itself holds no algorithm code — it is a small
    /// dispatcher that reads the algorithm choice from settings and
    /// hands the <see cref="GenerationContext"/> to
    /// <see cref="ContinentShapeAlgorithms.Apply"/>. Concrete
    /// algorithms live in <see cref="ContinentShapeAlgorithms"/> and
    /// are documented one by one on the <see cref="ContinentShapeAlgorithm"/>
    /// enum members.
    /// </para>
    /// <para>
    /// Every algorithm honors three invariants regardless of its
    /// internal logic: <c>LandRatio</c> is matched within one tile of
    /// the theoretical count (via percentile thresholding); the
    /// result is deterministic for the same seed; and the cylindrical
    /// horizontal wrap is preserved.
    /// </para>
    /// </remarks>
    internal sealed class ContinentShapeStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "ContinentShape";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            ContinentShapeAlgorithms.Apply(ctx.Settings.ContinentShapeAlgorithm, ctx);
        }
    }
}
