using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 032, pure: the register's graybox layout, shared by the art catalog (its model) and the scene builder (its slots, labels
    /// and change mat). Station-local metres with the base on the counter top: x along the counter (+x the cook's right), y up,
    /// -z towards the cook. A Charcoal body with a display leaning towards the cook; in front of it the change drawer with six
    /// compartments (coins $1 and $2, notes $5 to $50); the BACK and GIVE buttons either side of the drawer; the card terminal on the
    /// right; the change mat on the left, where the change for the guest lies.
    /// </summary>
    public static class RegisterArtRules
    {
        /// <summary>The whole register (its aim box): along the counter, deep, high (the display's top).</summary>
        public const float Width = 1.0f;
        public const float Depth = 0.5f;
        public const float Height = 0.24f;

        public static readonly Vector3 BodyCentre = new Vector3(0f, 0.05f, 0.05f);
        public static readonly Vector3 BodySize = new Vector3(0.42f, 0.10f, 0.28f);
        public static readonly Vector3 DisplayCentre = new Vector3(0f, 0.17f, 0.12f);
        public static readonly Vector3 DisplaySize = new Vector3(0.32f, 0.13f, 0.025f);
        /// <summary>The display leans back, its face towards the cook.</summary>
        public static readonly Quaternion DisplayLean = Quaternion.Euler(-25f, 0f, 0f);
        public static readonly Vector3 DrawerCentre = new Vector3(0f, 0.016f, -0.15f);
        public static readonly Vector3 DrawerSize = new Vector3(0.48f, 0.032f, 0.12f);
        public static readonly Vector3 MatCentre = new Vector3(-0.40f, 0.002f, -0.10f);
        public static readonly Vector3 MatSize = new Vector3(0.2f, 0.004f, 0.16f);
        public static readonly Vector3 ButtonSize = new Vector3(0.075f, 0.025f, 0.055f);
        public static readonly Vector3 BackCentre = new Vector3(-0.29f, 0.0125f, -0.15f);
        public static readonly Vector3 GiveCentre = new Vector3(0.29f, 0.0125f, -0.15f);
        public static readonly Vector3 TerminalCentre = new Vector3(0.43f, 0.03f, -0.05f);
        public static readonly Vector3 TerminalSize = new Vector3(0.12f, 0.06f, 0.13f);
        public const float TraySpacing = 0.075f;

        /// <summary>The middle of tray compartment <paramref name="slot"/> (0-5), on the drawer's top.</summary>
        public static Vector3 TrayCentre(int slot) => new Vector3(-0.1875f + slot * TraySpacing, DrawerCentre.y + DrawerSize.y * 0.5f + 0.004f, DrawerCentre.z);

        /// <summary>The size of the money shown in compartment <paramref name="slot"/>: a coin for $1 and $2, a folded note else.</summary>
        public static Vector3 TrayMoneySize(int value) => value < 5 ? new Vector3(0.03f, 0.008f, 0.03f) : new Vector3(0.062f, 0.012f, 0.085f);

        /// <summary>Where the display's text sits (just in front of its screen).</summary>
        public static Vector3 DisplayTextPosition => DisplayCentre + DisplayLean * new Vector3(0f, -0.006f, -0.016f);

        /// <summary>The screen in front of the display's face.</summary>
        public static Vector3 ScreenCentre => DisplayCentre + DisplayLean * new Vector3(0f, 0f, -0.0135f);
        public static readonly Vector3 ScreenSize = new Vector3(0.29f, 0.10f, 0.002f);
    }
}
