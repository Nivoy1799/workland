using System;
using System.Globalization;
using UnityEngine;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Dev tool (spec 011): <c>-screenshot path [-screenshot-after seconds] [-screenshot-every seconds]</c>
    /// saves screenshots from a running client, so agents can check the UI without seeing the screen.
    /// Does nothing without the flag and in batch mode.
    /// </summary>
    public sealed class DevScreenshot : MonoBehaviour
    {
        private string _path;
        private float _next;
        private float _every;
        private int _count;

        private void Start()
        {
            if (Application.isBatchMode) { enabled = false; return; }
            string[] args = Environment.GetCommandLineArgs();
            _path = Value(args, "-screenshot");
            if (string.IsNullOrEmpty(_path)) { enabled = false; return; }
            _next = Time.unscaledTime + Number(args, "-screenshot-after", 12f);
            _every = Number(args, "-screenshot-every", 0f);
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            string file = _every > 0f ? _path.Replace(".png", "-" + _count + ".png") : _path;
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log("[Dev] screenshot " + file);
            _count++;
            if (_every > 0f && _count < 20) _next = Time.unscaledTime + _every;
            else enabled = false;
        }

        private static string Value(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        private static float Number(string[] args, string flag, float fallback)
        {
            string v = Value(args, flag);
            return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f >= 0f ? f : fallback;
        }
    }
}
