using System;
using System.Collections.Generic;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: the ordered steps that bring an older save up to this build's format: step (n, f) turns a version n save into a version
    /// n + 1 one. They run in order, each once, up to <see cref="Latest"/>. Empty in v1 (<see cref="None"/>); should inbox question 17
    /// move money to cents, its spec adds the step "v1 to v2: money x 100". Immutable once built.
    /// </summary>
    public sealed class SaveMigrations
    {
        /// <summary>The first format version (a build without steps writes it).</summary>
        public const int FirstVersion = 1;

        private readonly List<(int From, Func<ShopSave, ShopSave> Step)> _steps = new List<(int, Func<ShopSave, ShopSave>)>();

        /// <summary>This build's steps: none (format version 1).</summary>
        public static readonly SaveMigrations None = new SaveMigrations(Array.Empty<(int, Func<ShopSave, ShopSave>)>());

        public SaveMigrations(IEnumerable<(int From, Func<ShopSave, ShopSave> Step)> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            int expected = FirstVersion;
            foreach ((int From, Func<ShopSave, ShopSave> Step) s in steps)
            {
                if (s.Step == null) throw new ArgumentException("a step needs its function", nameof(steps));
                if (s.From != expected) throw new ArgumentException("steps run in order from version " + FirstVersion + ", one each", nameof(steps));
                _steps.Add(s);
                expected++;
            }
            Latest = expected;
        }

        /// <summary>The format version this build writes and reads up to.</summary>
        public int Latest { get; }

        /// <summary>Runs every step from <paramref name="save"/>'s version up to <see cref="Latest"/>, in order, each once.</summary>
        public ShopSave Run(ShopSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            foreach ((int From, Func<ShopSave, ShopSave> Step) s in _steps)
            {
                if (save.formatVersion != s.From) continue;
                save = s.Step(save) ?? throw new InvalidOperationException("a migration step returned nothing");
                save.formatVersion = s.From + 1;
            }
            return save;
        }
    }
}
