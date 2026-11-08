using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027: takes the shots and frame sequences of one tour into <c>dir/NN-name.png</c> and writes
    /// <c>dir/sheet.png</c> (plus <c>sheet.txt</c>, number to name) at the end. The screen is read on the main thread at
    /// the end of a frame; encoding, writing and downsampling run on worker threads so the frames keep their timing.
    /// </summary>
    public sealed class TourCapture
    {
        private readonly string _dir;
        private readonly Vector2Int _cell;
        private readonly List<string> _names = new List<string>();
        private readonly List<Task<Color32[]>> _cells = new List<Task<Color32[]>>();

        public TourCapture(string dir, Vector2Int cell)
        {
            _dir = dir;
            _cell = cell;
            Directory.CreateDirectory(_dir);
        }

        public int Count => _names.Count;

        /// <summary>One screenshot named <paramref name="name"/> (taken at the end of this frame).</summary>
        public IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            Capture(name);
        }

        /// <summary><paramref name="count"/> frames, one every <paramref name="interval"/> seconds (unscaled time).</summary>
        public IEnumerator Frames(string name, int count, float interval)
        {
            float next = Time.unscaledTime;
            for (int i = 0; i < count; i++)
            {
                while (Time.unscaledTime < next) yield return null;
                yield return new WaitForEndOfFrame();
                Capture(name + "-" + (i + 1).ToString("00"));
                next += interval;
            }
        }

        private void Capture(string name)
        {
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
            {
                Debug.LogWarning("[Tour] screen capture failed for " + name);
                return;
            }
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width, height = texture.height;
            Object.Destroy(texture);
            int number = _names.Count + 1;
            _names.Add(name);
            string path = Path.Combine(_dir, number.ToString("00") + "-" + name + ".png");
            Vector2Int cell = _cell;
            _cells.Add(Task.Run(() =>
            {
                ContactSheet.MakeOpaque(pixels);
                File.WriteAllBytes(path, ContactSheet.EncodePng(pixels, width, height));
                return ContactSheet.Downsample(pixels, width, height, cell.x, cell.y);
            }));
        }

        /// <summary>Waits for every frame, then writes the sheet. Returns its path, or null without frames.</summary>
        public IEnumerator WriteSheet(int columns, System.Action<string> done)
        {
            foreach (Task<Color32[]> task in _cells)
                while (!task.IsCompleted) yield return null;
            if (_cells.Count == 0)
            {
                done?.Invoke(null);
                yield break;
            }
            var cells = new List<Color32[]>(_cells.Count);
            foreach (Task<Color32[]> task in _cells)
            {
                if (task.IsFaulted) Debug.LogWarning("[Tour] a frame could not be written: " + task.Exception?.GetBaseException().Message);
                cells.Add(task.IsFaulted ? null : task.Result);
            }
            SheetLayout layout = ContactSheet.Layout(cells.Count, columns, _cell);
            string path = Path.Combine(_dir, "sheet.png");
            Task write = Task.Run(() =>
                File.WriteAllBytes(path, ContactSheet.EncodePng(ContactSheet.Compose(layout, cells), layout.Width, layout.Height)));
            while (!write.IsCompleted) yield return null;
            if (write.IsFaulted)
            {
                Debug.LogError("[Tour] sheet not written: " + write.Exception?.GetBaseException().Message);
                done?.Invoke(null);
                yield break;
            }
            var index = new StringBuilder();
            for (int i = 0; i < _names.Count; i++) index.Append((i + 1).ToString("00")).Append(' ').Append(_names[i]).Append('\n');
            File.WriteAllText(Path.Combine(_dir, "sheet.txt"), index.ToString());
            done?.Invoke(path);
        }
    }
}
