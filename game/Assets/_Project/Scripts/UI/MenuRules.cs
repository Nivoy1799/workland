using UnityEngine;
using Worktown.Game.Settings;

namespace Worktown.Game.UI
{
    /// <summary>Spec 047, pure: the title screen's choices and the slow camera that shows the town behind it.</summary>
    public static class MenuRules
    {
        public const string GameTitle = "Worktown";
        public const string Subtitle = "A little food shop, cooked with friends";

        /// <summary>
        /// Where the menu camera looks: a point west of the shop (the kitchen spans about x 1 to 15 along z 12 to 15), so the open
        /// shop front fills the right side of the screen, beside the menu panel on the left.
        /// </summary>
        public static readonly Vector3 CameraTarget = new Vector3(2f, 1f, 13.3f);
        /// <summary>The camera's middle position: south-east of the shop, on the street side, above head height.</summary>
        public static readonly Vector3 CameraEye = new Vector3(12f, 6f, -4f);
        /// <summary>The camera sways this far either side (around the target), once every <see cref="SwaySeconds"/>.</summary>
        public const float SwayDegrees = 5f;
        public const float SwaySeconds = 60f;

        /// <summary>The host buttons: "Continue" only once a save exists (spec 053: a shop file that loads), "New shop" always.</summary>
        public static string[] HostChoices(bool hasSave) => hasSave ? new[] { "Continue", "New shop" } : new[] { "New shop" };

        /// <summary>Spec 053: the line under Continue: "Day 4 · $230" (the newest save's next day and its shop wallet).</summary>
        public static string ContinueLine(long day, long balance) =>
            "Day " + System.Math.Max(1L, day) + " \u00b7 " + Worktown.Game.Economy.EconomyRules.Money(System.Math.Max(0L, balance));

        /// <summary>Spec 053 review: the line under Continue when the only (or newest) save comes from a newer build.</summary>
        public const string NewerContinueLine = "Saved by a newer version of Worktown";

        /// <summary>
        /// Spec 053 review: the title offers Continue when a save loads, or when the newest one was made by a newer build (pressing it
        /// shows that card instead of hiding the shop).
        /// </summary>
        public static bool OffersContinue(bool anySaveLoads, Worktown.Game.Save.SaveError newestError) =>
            anySaveLoads || newestError == Worktown.Game.Save.SaveError.NewerVersion;

        /// <summary>Spec 053: the line under Continue when the newest save does not load (pressing it shows why).</summary>
        public const string BrokenContinueLine = "The newest save could not be loaded";

        /// <summary>Spec 053: the error card's title when Continue cannot load the newest save.</summary>
        public const string SaveErrorTitle = "Could not continue";

        /// <summary>Spec 053: the error card's text (the file is never deleted).</summary>
        public static string SaveErrorText(Worktown.Game.Save.SaveError error) =>
            error == Worktown.Game.Save.SaveError.NewerVersion
                ? "This shop was saved by a newer version of Worktown."
                : "This save could not be loaded. Your file is kept.";

        /// <summary>The menu camera at <paramref name="seconds"/> (unscaled time): south-east of the shop, swaying slowly, looking at
        /// <see cref="CameraTarget"/>.</summary>
        public static Pose CameraPose(double seconds)
        {
            double phase = seconds / SwaySeconds * 2d * System.Math.PI;
            float yaw = SwayDegrees * (float)System.Math.Sin(phase);
            Vector3 eye = CameraTarget + Quaternion.Euler(0f, yaw, 0f) * (CameraEye - CameraTarget);
            return new Pose(eye, Quaternion.LookRotation(CameraTarget - eye, Vector3.up));
        }

        /// <summary>
        /// The friends list with nobody hosting. 049 review: it names the bound Players key (the lobby, where a host invites), so it
        /// follows a rebind (KeyPrompts).
        /// </summary>
        public static string NoFriendsHosting(ControlBindings bindings) =>
            "None of your Steam friends has a shop open right now. When one does, they can invite you ("
            + KeyPrompts.KeyName(bindings, ControlAction.Players) + " in their shop), or use Join Game in Steam.";

        /// <summary>The corner line: "build 0.1.0 (a1b2c3d4)".</summary>
        public static string BuildLine(string version, string shortHash) => "build " + version + " (" + shortHash + ")";
    }
}
