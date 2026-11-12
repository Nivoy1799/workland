using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b, client: the held piece's ghost in build mode (feel.md 4): a translucent copy of the piece's meshes (Art.PlacementGhost)
    /// where a right click would put it, Mint where it fits and Terracotta where it does not. Local and visual only.
    /// </summary>
    public sealed class BuildGhost
    {
        public const float Alpha = 0.45f;
        public static readonly Color FitColor = Palette.WithAlpha(Palette.Mint, Alpha);
        public static readonly Color NoFitColor = Palette.WithAlpha(Palette.Terracotta, Alpha);

        private readonly PlacementGhost _ghost;

        public BuildGhost(Material material)
        {
            _ghost = material != null ? new PlacementGhost(material, FitColor) : null;
        }

        public bool IsShown => _ghost != null && _ghost.IsShown;
        public Vector3 Position => _ghost != null ? _ghost.Position : Vector3.zero;

        /// <summary>Shows <paramref name="piece"/>'s copy at its pose (x, z in millimetres, quarter turns), tinted by <paramref name="fits"/>.</summary>
        public void Show(Transform piece, int xMm, int zMm, int turns, bool fits)
        {
            if (_ghost == null || piece == null) return;
            _ghost.SetColor(fits ? FitColor : NoFitColor);
            var pose = new Pose(new Vector3(xMm / 1000f, piece.position.y, zMm / 1000f), Quaternion.Euler(0f, PosePacking.Yaw(turns), 0f));
            _ghost.Show(piece, pose);
        }

        public void Hide() => _ghost?.Hide();

        public void Destroy() => _ghost?.Destroy();
    }
}
