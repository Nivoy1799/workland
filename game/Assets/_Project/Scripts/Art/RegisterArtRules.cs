using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 032, pure: the register's graybox layout, shared by the art catalog (its model) and the scene builder (its slots, labels
    /// and change mat). Station-local metres with the base on the counter top: x along the counter (+x the cook's right), y up,
    /// -z towards the cook. A Charcoal body with a display leaning towards the cook; in front of it the change drawer with eight
    /// compartments (spec 037: coins $0.10, $0.20, $0.50, $1 and $2, notes $5, $10 and $20); the BACK and GIVE buttons either side of
    /// the drawer; the card terminal on the right; the change mat on the left, where the change for the guest lies.
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
        /// <summary>Spec 037: eight compartments (was six at 0.075 m), so the drawer keeps its width and the buttons their place.</summary>
        public const float TraySpacing = 0.058f;
        public const int TrayCompartments = 8;

        /// <summary>The middle of tray compartment <paramref name="slot"/> (0-7), on the drawer's top.</summary>
        public static Vector3 TrayCentre(int slot) =>
            new Vector3((slot - (TrayCompartments - 1) * 0.5f) * TraySpacing, DrawerCentre.y + DrawerSize.y * 0.5f + 0.004f, DrawerCentre.z);

        /// <summary>
        /// The size of the money shown in a compartment for <paramref name="cents"/>: a coin below $5 (by its size, RegisterView.CoinSize),
        /// a folded note else (spec 037: 0.05 m wide, so eight fit the drawer).
        /// </summary>
        public static Vector3 TrayMoneySize(int cents)
        {
            if (!Worktown.Game.Shop.RegisterView.IsCoin(cents)) return new Vector3(0.05f, 0.012f, 0.085f);
            float d = Worktown.Game.Shop.RegisterView.CoinSize(cents);
            return new Vector3(d, 0.008f, d);
        }

        /// <summary>Where the display's text sits (just in front of its screen).</summary>
        public static Vector3 DisplayTextPosition => DisplayCentre + DisplayLean * new Vector3(0f, -0.006f, -0.016f);

        /// <summary>The screen in front of the display's face.</summary>
        public static Vector3 ScreenCentre => DisplayCentre + DisplayLean * new Vector3(0f, 0f, -0.0135f);
        public static readonly Vector3 ScreenSize = new Vector3(0.29f, 0.10f, 0.002f);
    }
}
