using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 033, pure: the drink dispenser's graybox layout, shared by the art catalog (its model), the scene builder (its tools and
    /// labels) and the views (the stream, the cup under a tap, the puddle). Station-local metres with the base on the counter top: x
    /// along the counter (+x the cook's right), y up, -z towards the cook. A Steel body at the back with a coloured panel per drink;
    /// three taps on its front (a lever in the drink's colour over a spout); a drip tray under them where the cup is held; the cup
    /// stack on the left and the lid stack on the right, both standing on the counter.
    /// </summary>
    public static class DrinkArtRules
    {
        /// <summary>The whole dispenser (its aim box): along the counter, deep, high above the counter top.</summary>
        public const float Width = 0.84f;
        public const float Depth = 0.42f;
        public const float Height = 0.56f;

        /// <summary>A takeaway cup's real height (m; 400 ml, about 12 cm); in the game x1.5 (D29, small items).</summary>
        public const float CupReal = 0.12f;
        /// <summary>The cup in the game (graybox: an open, tapered paper cup of <see cref="CupSides"/> wall slabs, x1.5).</summary>
        public const float CupHeight = CupReal * 1.5f;
        public const float CupBottomRadius = 0.049f;
        public const float CupTopRadius = 0.066f;
        public const float CupWall = 0.003f;
        public const int CupSides = 12;

        /// <summary>The liquid's height in the cup (share of its height) for a drawn fill (its bottom is a little above the base).</summary>
        public static float CupLiquidHeight(float fill01) => 0.05f + 0.9f * Mathf.Clamp01(fill01);

        /// <summary>The cup's inner diameter (m) at <paramref name="heightShare"/> of its height (it widens to the top).</summary>
        public static float CupInnerDiameter(float heightShare) =>
            2f * (CupBottomRadius + (CupTopRadius - CupBottomRadius) * Mathf.Clamp01(heightShare));

        /// <summary>The fill line's height on the cup (share of its height): where a right cup's liquid stands.</summary>
        public static float FillLineHeight => CupLiquidHeight(DrinkRules.FillLine);

        public static readonly Vector3 BodyCentre = new Vector3(0f, 0.30f, 0.09f);
        public static readonly Vector3 BodySize = new Vector3(0.62f, 0.52f, 0.24f);
        public static readonly Vector3 CapCentre = new Vector3(0f, 0.545f, 0.07f);
        public static readonly Vector3 CapSize = new Vector3(0.66f, 0.03f, 0.30f);
        public static readonly Vector3 TrayCentre = new Vector3(0f, 0.012f, -0.10f);
        public static readonly Vector3 TraySize = new Vector3(0.54f, 0.024f, 0.18f);
        /// <summary>The drip tray's grid top (where a cup stands under a spout).</summary>
        public const float TrayTop = 0.024f;
        public static readonly Vector3 PanelSize = new Vector3(0.16f, 0.14f, 0.01f);
        public static readonly Vector3 LeverSize = new Vector3(0.05f, 0.09f, 0.035f);
        public static readonly Vector3 SpoutSize = new Vector3(0.035f, 0.05f, 0.04f);
        public const float TapSpacing = 0.19f;
        public const float SpoutBottom = 0.30f;
        public const float SpoutZ = -0.11f;

        /// <summary>The cup stack (left) and the lid stack (right), standing on the counter.</summary>
        public static readonly Vector3 CupStackCentre = new Vector3(-0.35f, 0f, -0.10f);
        public static readonly Vector3 LidStackCentre = new Vector3(0.35f, 0f, -0.10f);
        public const int StackedCups = 4;
        /// <summary>How much higher each nested cup in the stack stands (m).</summary>
        public const float CupNest = 0.025f;
        public const int StackedLids = 6;
        public const float LidThickness = 0.008f;

        /// <summary>The middle of tap <paramref name="tap"/> along the counter.</summary>
        public static float TapX(int tap) => (tap - (DrinkRules.TapCount - 1) * 0.5f) * TapSpacing;

        /// <summary>The spout's tip of tap <paramref name="tap"/> (where the stream starts).</summary>
        public static Vector3 SpoutTip(int tap) => new Vector3(TapX(tap), SpoutBottom, SpoutZ);

        /// <summary>The spout's box centre of tap <paramref name="tap"/>.</summary>
        public static Vector3 SpoutCentre(int tap) => SpoutTip(tap) + new Vector3(0f, SpoutSize.y * 0.5f, 0f);

        /// <summary>The lever above tap <paramref name="tap"/>'s spout (in the drink's colour).</summary>
        public static Vector3 LeverCentre(int tap) => new Vector3(TapX(tap), SpoutBottom + SpoutSize.y + LeverSize.y * 0.5f + 0.01f, SpoutZ - 0.005f);

        /// <summary>The coloured panel of tap <paramref name="tap"/> on the body's front.</summary>
        public static Vector3 PanelCentre(int tap) =>
            new Vector3(TapX(tap), 0.43f, BodyCentre.z - BodySize.z * 0.5f - PanelSize.z * 0.5f);

        /// <summary>Where a cup stands under tap <paramref name="tap"/> (its bottom on the drip tray).</summary>
        public static Vector3 CupUnderTap(int tap) => new Vector3(TapX(tap), TrayTop, SpoutZ);

        /// <summary>The aim anchor of tool <paramref name="tool"/>: the spout of a tap, the top of the cup stack, the lid stack.</summary>
        public static Vector3 ToolAnchor(int tool, float cupHeight)
        {
            if (DrinkRules.IsTap(tool)) return SpoutCentre(tool);
            if (tool == DrinkRules.CupTool) return CupStackCentre + new Vector3(0f, cupHeight * 0.6f + CupNest * (StackedCups - 1), 0f);
            return LidStackCentre + new Vector3(0f, LidThickness * StackedLids, 0f);
        }

        /// <summary>The drink colour of tap <paramref name="tap"/> (or of a look's drink byte - 1).</summary>
        public static Color DrinkColour(int tap)
        {
            switch (tap)
            {
                case 0: return Palette.FoodLemonade;
                case 1: return Palette.FoodIcedTea;
                case 2: return Palette.FoodSoda;
                default: return Palette.Sky;
            }
        }

        /// <summary>The stream's width (m) and how far below the spout a puddle lies on the tray.</summary>
        public const float StreamWidth = 0.012f;
        /// <summary>The puddle of a spill: a flat disc on the counter in front of the tray (m: diameter).</summary>
        public const float PuddleSize = 0.26f;
        public static readonly Vector3 PuddleCentre = new Vector3(0.05f, 0.002f, -0.24f);

        /// <summary>
        /// The share (0..1) of the way the held cup has moved from the hand to under the tap, <paramref name="elapsed"/> s after a press
        /// (or back after the release): an ease-out over <paramref name="seconds"/> (docs/feel.md 2: nothing teleports).
        /// </summary>
        public static float UnderTapShare(float elapsed, float seconds)
        {
            if (!(seconds > 0f)) return 1f;
            float t = Mathf.Clamp01(elapsed / seconds);
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>The seconds the held cup takes to move under the tap and back (the pick-up arc's time, feel.md 2).</summary>
        public const float UnderTapSeconds = 0.14f;

        /// <summary>
        /// 042 follow-up: how fast (cup heights per second) a drawn level may move towards the level to show. Four times a tap's pour
        /// (200 ml/s into a 400 ml cup is 0.5 per second), so a pour is drawn as it runs, and a jump to the server's bucket (another
        /// player's cup a round trip late, a refused pour) glides in about 0.1 to 0.25 s instead of snapping.
        /// </summary>
        public const float FillEaseSpeed = 2f;

        /// <summary>042 follow-up: the drawn level <paramref name="dt"/> s later, moved from <paramref name="shown"/> towards <paramref name="target"/>.</summary>
        public static float EaseFill(float shown, float target, float dt)
        {
            if (float.IsNaN(shown) || float.IsNaN(target)) return target;
            if (!(dt > 0f)) return shown;
            return Mathf.MoveTowards(shown, target, FillEaseSpeed * dt);
        }
    }
}
