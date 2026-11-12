using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: the furniture types a layout may hold (one <see cref="PieceDef"/> per type id), validated on construction (throws
    /// <see cref="ArgumentException"/>: ids of lower-case letters, digits and underscores, unique; sizes above 0; lengths only on
    /// counters). <see cref="CreateDefault"/> has one row per type in today's kitchen and the rows reserved for later specs.
    /// </summary>
    public sealed class PieceCatalog
    {
        // ---- the default rows' type ids ----
        public const string ShelfDry = "shelf_dry";
        public const string Fridge = "fridge";
        public const string Stove = "stove";
        public const string Fryer = "fryer";
        public const string Griddle = "griddle";
        public const string Assembly = "assembly";
        public const string PlateShelf = "plate_shelf";
        public const string Sink = "sink";
        public const string Worktop = "worktop";
        public const string FrontCounter = "front_counter";
        public const string ServingCounter = "serving_counter";
        public const string RegisterModule = "register_module";
        public const string DispenserModule = "dispenser_module";
        public const string SpitModule = "spit_module";
        public const string DishReturnModule = "dish_return_module";
        public const string Trash = "trash";
        public const string Terminal = "terminal";
        public const string MenuBoard = "menu_board";
        public const string Bin = "bin";
        /// <summary>Reserved for spec 057 (not in the default layout).</summary>
        public const string GoalBoard = "goal_board";
        /// <summary>Reserved for spec 062b: the shop's open or closed sign (a station: a press opens or closes the shop).</summary>
        public const string DoorSign = "door_sign";
        /// <summary>Spec 062b: the pallet in the yard by the back door where stored pieces wait (fixed, not required).</summary>
        public const string StoragePallet = "storage_pallet";
        /// <summary>Reserved for spec 059.</summary>
        public const string OfficeTerminal = "office_terminal";

        /// <summary>The ids of the rows reserved for later specs (never in the default layout).</summary>
        public static readonly IReadOnlyList<string> ReservedTypes = new[] { GoalBoard, DoorSign, OfficeTerminal };

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);
        private readonly Dictionary<string, PieceDef> _rows = new Dictionary<string, PieceDef>(StringComparer.Ordinal);

        public PieceCatalog(IEnumerable<PieceDef> rows)
        {
            if (rows == null) throw new ArgumentException("rows are required", nameof(rows));
            var list = new List<PieceDef>();
            foreach (PieceDef row in rows)
            {
                if (row == null) throw new ArgumentException("a row is null", nameof(rows));
                if (row.TypeId == null || !IdPattern.IsMatch(row.TypeId)) throw new ArgumentException("bad type id: " + row.TypeId, nameof(rows));
                if (row.WidthMm <= 0 || row.DepthMm <= 0) throw new ArgumentException(row.TypeId + ": sizes must be above 0", nameof(rows));
                if (row.HasLength && (!row.IsCounter || row.MinLengthMm <= 0 || row.MinLengthMm > row.MaxLengthMm
                        || row.WidthMm < row.MinLengthMm || row.WidthMm > row.MaxLengthMm))
                    throw new ArgumentException(row.TypeId + ": a length needs a counter and 0 < min <= width <= max", nameof(rows));
                if (!row.HasLength && row.MinLengthMm != 0) throw new ArgumentException(row.TypeId + ": a minimum length without a maximum", nameof(rows));
                if (_rows.ContainsKey(row.TypeId)) throw new ArgumentException("duplicate type id: " + row.TypeId, nameof(rows));
                _rows.Add(row.TypeId, row);
                list.Add(row);
            }
            Rows = list.OrderBy(r => r.TypeId, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        /// <summary>Every row in type id order.</summary>
        public IReadOnlyList<PieceDef> Rows { get; }

        public bool TryGet(string typeId, out PieceDef def)
        {
            def = null!;
            if (typeId == null || !_rows.TryGetValue(typeId, out PieceDef? found)) return false;
            def = found;
            return true;
        }

        public bool Contains(string typeId) => typeId != null && _rows.ContainsKey(typeId);

        /// <summary>
        /// Spec 062a: today's kitchen. Sizes are the prefabs' footprints (PieceFootprintTests): the station bodies, a counter-line module's
        /// counter body (its appliance's width plus 20 mm either side, the margin spec 039's counter tops keep from it), the worktop and the
        /// front counter of variable length (a front counter piece shorter than 100 mm has no top, like spec 039's narrow gaps).
        /// </summary>
        public static PieceCatalog CreateDefault() => new PieceCatalog(new[]
        {
            //          type id           width depth mount              counter guest  station required movable minLen maxLen
            // Spec 062b: the dry shelf and the fridge are 1.8 m tall (ArtSizeTable.StockHeight): nobody reaches over them (rule 4).
            new PieceDef(ShelfDry,         1200,  500, PieceMount.InLine,   false, false, true,  true,  true,  0,    0, Tall: true),
            new PieceDef(Fridge,           1200,  620, PieceMount.InLine,   false, false, true,  true,  true,  0,    0, Tall: true),
            new PieceDef(Stove,            1200, 1200, PieceMount.InLine,   false, false, true,  false, true,  0,    0),
            new PieceDef(Fryer,             800, 1200, PieceMount.InLine,   false, false, true,  false, true,  0,    0),
            new PieceDef(Griddle,          1020, 1200, PieceMount.InLine,   false, false, true,  false, true,  0,    0),
            new PieceDef(Assembly,          900, 1200, PieceMount.InLine,   false, false, true,  false, true,  0,    0),
            new PieceDef(PlateShelf,       1200, 1200, PieceMount.InLine,   false, false, true,  true,  true,  0,    0),
            new PieceDef(Sink,             1200, 1200, PieceMount.InLine,   false, false, true,  true,  true,  0,    0),
            new PieceDef(Worktop,          1260, 1200, PieceMount.InLine,   true,  false, false, false, true,  300,  6000),
            new PieceDef(FrontCounter,     1000,  700, PieceMount.InLine,   true,  true,  false, false, true,  20,   8000),
            new PieceDef(ServingCounter,   1200,  700, PieceMount.InLine,   true,  true,  true,  true,  true,  0,    0),
            new PieceDef(RegisterModule,   1040,  700, PieceMount.InLine,   true,  true,  true,  true,  true,  0,    0),
            new PieceDef(DispenserModule,   880,  700, PieceMount.InLine,   true,  true,  true,  false, true,  0,    0),
            new PieceDef(SpitModule,        600,  700, PieceMount.InLine,   true,  true,  true,  false, true,  0,    0),
            new PieceDef(DishReturnModule,  640,  700, PieceMount.InLine,   true,  true,  true,  false, true,  0,    0),
            new PieceDef(Trash,             460,  460, PieceMount.Floor,    false, false, true,  true,  true,  0,    0),
            new PieceDef(Terminal,         1000,   20, PieceMount.Wall,     false, false, true,  false, true,  0,    0),
            new PieceDef(MenuBoard,         700,   30, PieceMount.Overhead, false, false, true,  false, true,  0,    0),
            new PieceDef(Bin,               620,  740, PieceMount.Yard,     false, false, true,  false, false, 0,    0),
            // Reserved for later specs (not in the default layout).
            // Spec 062a Part B: the goal board is a station (pressed in first person), so rule 4 checks a cook reaches it; 20 mm deep like the
            // terminal (its panel is 18 mm), so it fits between the back line and the wall.
            new PieceDef(GoalBoard,         800,   20, PieceMount.Wall,     false, false, true,  false, true,  0,    0),
            // Spec 062b: the door sign is a station (pressed to open or close the shop), so rule 4 checks a cook reaches it.
            new PieceDef(DoorSign,          400,   30, PieceMount.Wall,     false, false, true,  false, true,  0,    0),
            new PieceDef(OfficeTerminal,   1000,   20, PieceMount.Wall,     false, false, true,  false, true,  0,    0),
            // Spec 062b: where stored pieces wait. In the yard by the back door, fixed (a Yard row: rule 1 keeps it outside the floor; the spec's
            // sketch said Floor, but the room has no free floor by the back door).
            new PieceDef(StoragePallet,    1200, 1000, PieceMount.Yard,     false, false, false, false, false, 0,    0),
        });
    }
}
