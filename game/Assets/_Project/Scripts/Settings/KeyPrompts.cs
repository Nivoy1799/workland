using System.Text;
using UnityEngine.InputSystem;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048 (review): the words for a bound key in the game's prompts, the F1 controls, the lobby hint and the tutorial, so they
    /// follow a rebind. Pure: the bindings are passed in (the views pass GameSettings.CurrentBindings). A text can hold
    /// <c>{Action}</c> tokens (<c>{Use}</c>, <c>{ToggleView}</c>, ...) and <c>{Move}</c> (the four walk keys); <see cref="Fill"/>
    /// puts the key names in.
    /// </summary>
    public static class KeyPrompts
    {
        /// <summary>What a prompt says for an action without a key.</summary>
        public const string NoKey = "(no key)";
        public const string MoveToken = "{Move}";

        /// <summary>The action's key as a prompt names it: "E", "Left Shift", <see cref="NoKey"/>.</summary>
        public static string KeyName(ControlBindings bindings, ControlAction action)
        {
            Key key = (bindings ?? ControlBindings.Defaults()).KeyOf(action);
            return key == UnityEngine.InputSystem.Key.None ? NoKey : SettingsRules.KeyText(key);
        }

        /// <summary>The four walk keys: "WASD" when each is one letter, else "W / A / S / D" (forward, left, back, right).</summary>
        public static string MoveKeys(ControlBindings bindings)
        {
            string[] keys =
            {
                KeyName(bindings, ControlAction.MoveForward), KeyName(bindings, ControlAction.MoveLeft),
                KeyName(bindings, ControlAction.MoveBack), KeyName(bindings, ControlAction.MoveRight)
            };
            bool letters = true;
            foreach (string k in keys) letters &= k.Length == 1;
            return letters ? string.Concat(keys) : string.Join(" / ", keys);
        }

        /// <summary>The text with every <c>{Action}</c> token and <see cref="MoveToken"/> replaced by the bound key names.</summary>
        public static string Fill(string text, ControlBindings bindings)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? string.Empty;
            var sb = new StringBuilder(text);
            sb.Replace(MoveToken, MoveKeys(bindings));
            foreach (ControlAction action in ControlBindings.Actions)
                sb.Replace("{" + action + "}", KeyName(bindings, action));
            return sb.ToString();
        }
    }
}
