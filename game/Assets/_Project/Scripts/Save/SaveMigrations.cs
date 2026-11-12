using System;
using System.Collections.Generic;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: the ordered steps that bring an older save up to this build's format: step (n, f) turns a version n save into a version
    /// n + 1 one. They run in order, each once, up to <see cref="Latest"/>. Spec 037 (inbox question 17, D39): this build's steps are
    /// <see cref="Current"/>: <see cref="V1ToV2"/>, money from the old scale's whole dollars to cents; spec 062a: <see cref="V2ToV3"/>, the
    /// kitchen's layout (the default one). Immutable once built.
    /// </summary>
    public sealed class SaveMigrations
    {
        /// <summary>The first format version (a build without steps writes it).</summary>
        public const int FirstVersion = 1;

        private readonly List<(int From, Func<ShopSave, ShopSave> Step)> _steps = new List<(int, Func<ShopSave, ShopSave>)>();

        /// <summary>No steps (format version 1), for tests of the mechanism.</summary>
        public static readonly SaveMigrations None = new SaveMigrations(Array.Empty<(int, Func<ShopSave, ShopSave>)>());

        /// <summary>Spec 037: this build's steps (spec 062a: format version 3).</summary>
        public static readonly SaveMigrations Current = new SaveMigrations(new (int, Func<ShopSave, ShopSave>)[] { (1, V1ToV2), (2, V2ToV3) });

        /// <summary>
        /// Spec 062a, v2 to v3: a version 2 save has no layout part; it gets the default layout (Resources/Layouts/default.json), the
        /// kitchen every v2 game played in.
        /// </summary>
        public static ShopSave V2ToV3(ShopSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.layout = Worktown.Game.Layout.DefaultLayout.Part() ?? new ShopSave.LayoutPart();
            return save;
        }

        /// <summary>
        /// Spec 037: a version 1 save's money was the old scale's whole dollars, 115 of which made one dollar of spec 052's climb (053: the
        /// game's dish price $460 over the climb's $4). Spec 037 prices the game in street-food cents (the climb's dollar is 100 cents, the
        /// food corner's scale is 1), so one old dollar is 100 / 115 cents.
        /// </summary>
        public const long OldDollarsPerDollar = 115;
        public const long CentsPerDollar = 100;

        /// <summary>
        /// Spec 037, v1 to v2: the balance, the totals and the best service from the old whole dollars to cents (x 100 / 115, rounded
        /// down), so a saved food corner keeps its progress to the rent goal ($34,500 of old dollars is the new $300.00 goal exactly). A v1
        /// save has no menu prices: the board starts at its references.
        /// </summary>
        public static ShopSave V1ToV2(ShopSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.money ??= new ShopSave.MoneyPart();
            save.stats ??= new ShopSave.StatsPart();
            save.money.balance = ToCents(save.money.balance);
            save.money.earnedTotal = ToCents(save.money.earnedTotal);
            save.money.spentTotal = ToCents(save.money.spentTotal);
            save.stats.bestServiceEarned = ToCents(save.stats.bestServiceEarned);
            save.menu = new ShopSave.MenuPart[0];
            return save;
        }

        /// <summary><paramref name="oldDollars"/> x 100 / 115, rounded towards 0, without overflow (0 or less stays as it is: clamped later).</summary>
        public static long ToCents(long oldDollars)
        {
            if (oldDollars <= 0) return oldDollars;
            long whole = oldDollars / OldDollarsPerDollar, rest = oldDollars % OldDollarsPerDollar;
            return whole * CentsPerDollar + rest * CentsPerDollar / OldDollarsPerDollar;
        }

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
