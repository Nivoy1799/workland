using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: Haven Nine as plain data: districts, footfall curves, places, the walking graph, the play area (a polygon,
    /// counter-clockwise), safe spots, the reference and start place, the fountain node, the Dome and the fall height.
    /// The constructor copies every list (the catalog is immutable) and validates (throws <see cref="ArgumentException"/>).
    /// <see cref="CreateDefault"/> numbers are provisional; tuning changes only those. Integer millimetres throughout.
    /// </summary>
    public sealed class TownCatalog
    {
        public const int MaxPermille = 5000;
        /// <summary>A place's node lies at most this far from its anchor (30 m).</summary>
        public const long MaxNodeDistanceMm = 30000;
        public const int MinWidthMm = 1500;
        public const int MaxWidthMm = 20000;
        public const int Hours = 24;

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, DistrictDef> _districts = new Dictionary<string, DistrictDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, FootfallCurve> _curves = new Dictionary<string, FootfallCurve>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlaceDef> _places = new Dictionary<string, PlaceDef>(StringComparer.Ordinal);
        private readonly Dictionary<int, PathNode> _nodes = new Dictionary<int, PathNode>();

        public TownCatalog(IEnumerable<DistrictDef> districts, IEnumerable<FootfallCurve> curves, IEnumerable<PlaceDef> places,
            IEnumerable<PathNode> nodes, IEnumerable<PathEdge> edges, IEnumerable<TownPoint> playArea,
            IEnumerable<SafeSpot> safeSpots, string referencePlaceId, string startPlaceId, int fountainNodeId,
            TownPoint domeCentre, int domeRadiusXMm, int domeRadiusZMm, int domeCrownMm, int fallHeightMm)
        {
            Districts = Copy(districts, nameof(districts)).Select(d => d with
            {
                Areas = CopyList(d.Areas, d.Id + " areas"),
                Neighbours = CopyList(d.Neighbours, d.Id + " neighbours"),
            }).ToList().AsReadOnly();
            Curves = Copy(curves, nameof(curves)).Select(c => c with { HourPermille = CopyList(c.HourPermille, c.Id + " hours") })
                .ToList().AsReadOnly();
            Places = Copy(places, nameof(places)).Select(p => p with { Channels = CopyList(p.Channels, p.Id + " channels") })
                .ToList().AsReadOnly();
            Nodes = Copy(nodes, nameof(nodes));
            Edges = Copy(edges, nameof(edges));
            PlayArea = (playArea ?? throw new ArgumentException("playArea is required.", nameof(playArea))).ToList().AsReadOnly();
            SafeSpots = Copy(safeSpots, nameof(safeSpots));
            ReferencePlaceId = referencePlaceId;
            StartPlaceId = startPlaceId;
            FountainNodeId = fountainNodeId;
            DomeCentre = domeCentre;
            DomeRadiusXMm = domeRadiusXMm;
            DomeRadiusZMm = domeRadiusZMm;
            DomeCrownMm = domeCrownMm;
            FallHeightMm = fallHeightMm;
            Validate();
        }

        public IReadOnlyList<DistrictDef> Districts { get; }
        public IReadOnlyList<FootfallCurve> Curves { get; }
        public IReadOnlyList<PlaceDef> Places { get; }
        public IReadOnlyList<PathNode> Nodes { get; }
        public IReadOnlyList<PathEdge> Edges { get; }
        /// <summary>The play area polygon, counter-clockwise (x east, z north).</summary>
        public IReadOnlyList<TownPoint> PlayArea { get; }
        public IReadOnlyList<SafeSpot> SafeSpots { get; }
        /// <summary>The place whose location factor is exactly 1000 (spec 052's numbers are "at the reference place").</summary>
        public string ReferencePlaceId { get; }
        public string StartPlaceId { get; }
        public int FountainNodeId { get; }
        public TownPoint DomeCentre { get; }
        public int DomeRadiusXMm { get; }
        public int DomeRadiusZMm { get; }
        /// <summary>The Dome's crown above the lake (mm).</summary>
        public int DomeCrownMm { get; }
        /// <summary>A player below this height (mm above the lake) is put back at the nearest safe spot.</summary>
        public int FallHeightMm { get; }

        public DistrictDef District(string id) =>
            id != null && _districts.TryGetValue(id, out DistrictDef? d) ? d : throw new ArgumentException("Unknown district '" + id + "'.");

        public FootfallCurve Curve(string id) =>
            id != null && _curves.TryGetValue(id, out FootfallCurve? c) ? c : throw new ArgumentException("Unknown curve '" + id + "'.");

        public PlaceDef Place(string id) =>
            id != null && _places.TryGetValue(id, out PlaceDef? p) ? p : throw new ArgumentException("Unknown place '" + id + "'.");

        public PathNode Node(int id) =>
            _nodes.TryGetValue(id, out PathNode? n) ? n : throw new ArgumentException("Unknown node " + id + ".");

        /// <summary>True when a place with this id exists.</summary>
        public bool HasPlace(string id) => id != null && _places.ContainsKey(id);

        /// <summary>True when the point lies inside the play area or on its edge.</summary>
        public bool InPlayArea(TownPoint p) => Polygon.Contains(PlayArea, p);

        /// <summary>True when the point lies on the play area's edge.</summary>
        public bool OnPlayAreaEdge(TownPoint p) => Polygon.OnEdge(PlayArea, p);

        private void Validate()
        {
            if (PlayArea.Count < 3) throw new ArgumentException("The play area needs at least 3 points.");
            if (Polygon.TwiceSignedArea(PlayArea) <= 0) throw new ArgumentException("The play area must be counter-clockwise with a positive area.");

            foreach (FootfallCurve c in Curves)
            {
                Id(c.Id, "curve");
                Add(_curves, c.Id, c, "curve");
                if (c.HourPermille.Count != Hours) throw new ArgumentException(c.Id + ": a curve needs " + Hours + " hourly values, not " + c.HourPermille.Count + ".");
                if (c.HourPermille.Any(v => v < 0)) throw new ArgumentException(c.Id + ": hourly values must be >= 0.");
                int sum = c.HourPermille.Sum();
                if (sum != 1000) throw new ArgumentException(c.Id + ": hourly values must sum to 1000, not " + sum + ".");
            }

            foreach (DistrictDef d in Districts)
            {
                Id(d.Id, "district");
                Add(_districts, d.Id, d, "district");
                Name(d.Name, d.Id);
                if (!_curves.ContainsKey(d.CurveId ?? "")) throw new ArgumentException(d.Id + ": unknown curve '" + d.CurveId + "'.");
                if (d.DayFootfall < 0) throw new ArgumentException(d.Id + ": DayFootfall must be >= 0.");
                Permille(d.CeilingPermille, d.Id + " CeilingPermille");
                foreach (TownRect r in d.Areas)
                {
                    if (r.X0Mm >= r.X1Mm || r.Z0Mm >= r.Z1Mm) throw new ArgumentException(d.Id + ": area " + r + " needs X0 < X1 and Z0 < Z1.");
                    // 064 review: only the four corners are tested. That is exact for a convex play area (the default is a
                    // rectangle); a concave play area may let an area cover a notch whose corners are all inside.
                    if (r.Corners().Any(p => !InPlayArea(p))) throw new ArgumentException(d.Id + ": area " + r + " is outside the play area.");
                }
            }
            for (int i = 0; i < Districts.Count; i++)
                for (int j = i + 1; j < Districts.Count; j++)
                    foreach (TownRect a in Districts[i].Areas)
                        foreach (TownRect b in Districts[j].Areas)
                            if (a.Overlaps(b)) throw new ArgumentException(Districts[i].Id + " and " + Districts[j].Id + ": areas overlap.");
            foreach (DistrictDef d in Districts)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string n in d.Neighbours)
                {
                    if (!_districts.TryGetValue(n ?? "", out DistrictDef? other)) throw new ArgumentException(d.Id + ": unknown neighbour '" + n + "'.");
                    if (n == d.Id) throw new ArgumentException(d.Id + ": a district is not its own neighbour.");
                    if (!seen.Add(n!)) throw new ArgumentException(d.Id + ": duplicate neighbour " + n + ".");
                    if (!other.Neighbours.Contains(d.Id)) throw new ArgumentException(d.Id + " lists " + n + " as a neighbour, but not the other way round.");
                }
            }

            foreach (PathNode n in Nodes)
            {
                if (_nodes.ContainsKey(n.Id)) throw new ArgumentException("Duplicate node id " + n.Id + ".");
                _nodes[n.Id] = n;
                if (!_districts.TryGetValue(n.DistrictId ?? "", out DistrictDef? home)) throw new ArgumentException("Node " + n.Id + ": unknown district '" + n.DistrictId + "'.");
                if (!InPlayArea(n.At)) throw new ArgumentException("Node " + n.Id + " is outside the play area.");
                // 064 review: a node lies inside one of its district's areas (or on its border); a district without any area
                // is check C8's finding, not a constructor error.
                if (home.Areas.Count > 0 && !home.Areas.Any(r => r.Contains(n.At))) throw new ArgumentException("Node " + n.Id + " at " + n.At + " is outside its district " + home.Id + ".");
            }
            foreach (PathEdge e in Edges)
            {
                if (!_nodes.TryGetValue(e.A, out PathNode? a)) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": unknown node " + e.A + ".");
                if (!_nodes.TryGetValue(e.B, out PathNode? b)) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": unknown node " + e.B + ".");
                if (e.A == e.B) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": an edge needs two different nodes.");
                if (e.WidthMm < MinWidthMm || e.WidthMm > MaxWidthMm) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": width must be " + MinWidthMm + ".." + MaxWidthMm + " mm.");
                bool ride = e.Kind == PathEdgeKind.Lift || e.Kind == PathEdgeKind.Funicular;
                if (!Enum.IsDefined(typeof(PathEdgeKind), e.Kind)) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": unknown kind.");
                if (ride && e.FixedSeconds <= 0) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": a lift or funicular needs FixedSeconds > 0.");
                if (!ride && e.FixedSeconds != 0) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": only lifts and funiculars have FixedSeconds.");
                if (!ride && a.At == b.At) throw new ArgumentException("Edge " + e.A + "-" + e.B + ": a walked edge needs a length.");
            }

            foreach (PlaceDef p in Places)
            {
                Id(p.Id, "place");
                Add(_places, p.Id, p, "place");
                Name(p.Name, p.Id);
                if (!Enum.IsDefined(typeof(PlaceKind), p.Kind)) throw new ArgumentException(p.Id + ": unknown kind.");
                if (!_districts.TryGetValue(p.DistrictId ?? "", out DistrictDef? d)) throw new ArgumentException(p.Id + ": unknown district '" + p.DistrictId + "'.");
                if (!InPlayArea(p.Anchor)) throw new ArgumentException(p.Id + ": anchor is outside the play area.");
                if (p.Kind == PlaceKind.Border)
                {
                    if (!OnPlayAreaEdge(p.Anchor)) throw new ArgumentException(p.Id + ": a border anchor must lie on the play-area edge.");
                }
                else if (!d.Areas.Any(r => r.Contains(p.Anchor)))
                    throw new ArgumentException(p.Id + ": anchor is outside its district " + d.Id + ".");
                if (!_nodes.TryGetValue(p.NodeId, out PathNode? node)) throw new ArgumentException(p.Id + ": unknown node " + p.NodeId + ".");
                if (TownPaths.Distance(node.At, p.Anchor) > MaxNodeDistanceMm) throw new ArgumentException(p.Id + ": node " + p.NodeId + " is more than 30 m from the anchor.");
                if (p.FromStage < 1 || p.FromStage > 5) throw new ArgumentException(p.Id + ": FromStage must be 1..5.");
                if (p.AreaM2 < 0) throw new ArgumentException(p.Id + ": AreaM2 must be >= 0.");
                Permille(p.FrontagePermille, p.Id + " FrontagePermille");
                Permille(p.RentPermille, p.Id + " RentPermille");
                var channels = new HashSet<string>(StringComparer.Ordinal);
                foreach (string ch in p.Channels)
                {
                    Id(ch, p.Id + " channel");
                    if (!channels.Add(ch)) throw new ArgumentException(p.Id + ": duplicate channel " + ch + ".");
                }
            }

            var spotIds = new HashSet<int>();
            var spotDistricts = new HashSet<string>(StringComparer.Ordinal);
            foreach (SafeSpot s in SafeSpots)
            {
                if (!spotIds.Add(s.Id)) throw new ArgumentException("Duplicate safe spot id " + s.Id + ".");
                if (!_nodes.TryGetValue(s.NodeId, out PathNode? n)) throw new ArgumentException("Safe spot " + s.Id + ": unknown node " + s.NodeId + ".");
                spotDistricts.Add(n.DistrictId);
            }
            foreach (DistrictDef d in Districts)
                if (!spotDistricts.Contains(d.Id)) throw new ArgumentException(d.Id + ": a district needs at least one safe spot.");

            if (!_places.ContainsKey(ReferencePlaceId ?? "")) throw new ArgumentException("Unknown reference place '" + ReferencePlaceId + "'.");
            if (!_places.ContainsKey(StartPlaceId ?? "")) throw new ArgumentException("Unknown start place '" + StartPlaceId + "'.");
            if (!_nodes.ContainsKey(FountainNodeId)) throw new ArgumentException("Unknown fountain node " + FountainNodeId + ".");
            if (DomeRadiusXMm <= 0 || DomeRadiusZMm <= 0 || DomeCrownMm <= 0) throw new ArgumentException("The Dome radii and crown must be > 0.");
            if (Nodes.Count > 0 && FallHeightMm >= Nodes.Min(n => n.HeightMm)) throw new ArgumentException("The fall height must be below every node.");
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string name) where T : class
        {
            if (items == null) throw new ArgumentException(name + " are required.", name);
            var list = items.ToList();
            if (list.Any(i => i == null)) throw new ArgumentException(name + " contain null.", name);
            return list.AsReadOnly();
        }

        private static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T>? items, string name)
        {
            if (items == null) throw new ArgumentException(name + " are required.");
            return items.ToList().AsReadOnly();
        }

        private static void Id(string id, string kind)
        {
            if (id == null || !IdPattern.IsMatch(id)) throw new ArgumentException("Bad " + kind + " id '" + id + "': use ^[a-z0-9_]{1,32}$.");
        }

        private static void Add<T>(Dictionary<string, T> map, string id, T value, string kind)
        {
            if (map.ContainsKey(id)) throw new ArgumentException("Duplicate " + kind + " id " + id + ".");
            map[id] = value;
        }

        private static void Name(string name, string id)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(id + ": Name is required.");
            string? word = ContentRules.FindForbidden(name);
            if (word != null) throw new ArgumentException(id + ": name '" + name + "' contains the forbidden word '" + word + "' (D27).");
        }

        private static void Permille(int value, string what)
        {
            if (value < 0 || value > MaxPermille) throw new ArgumentException(what + ": permille must be 0.." + MaxPermille + ".");
        }

        /// <summary>
        /// The default Haven Nine (spec 064 "Default data"; provisional numbers). Dome radii 465 x 385 m, not the town plan's
        /// 385 x 320: an ellipse of 770 x 640 m cannot hold the corners of a 640 x 480 m play area (check C7; see the spec's
        /// Implementation notes).
        /// </summary>
        public static TownCatalog CreateDefault()
        {
            var curves = new[]
            {
                Curve("commute", 5, 3, 2, 2, 5, 15, 45, 95, 110, 80, 35, 40, 40, 35, 30, 35, 50, 95, 105, 70, 30, 25, 30, 18),
                Curve("shops", 2, 1, 1, 1, 1, 3, 10, 25, 40, 58, 80, 85, 95, 90, 80, 78, 75, 80, 70, 55, 35, 20, 10, 5),
                Curve("market", 1, 1, 1, 2, 10, 30, 70, 95, 110, 110, 100, 90, 75, 60, 45, 40, 35, 35, 30, 25, 15, 10, 5, 5),
                Curve("evening", 15, 8, 4, 2, 2, 3, 8, 15, 20, 25, 30, 40, 50, 45, 40, 45, 60, 80, 100, 105, 100, 85, 70, 48),
                Curve("night", 90, 70, 50, 30, 15, 8, 8, 12, 15, 15, 15, 20, 20, 15, 15, 22, 30, 40, 50, 65, 85, 100, 110, 100),
            };

            // HeightMm is the table's first (base) value; the nodes carry the real heights.
            var districts = new[]
            {
                D("station_square", "Station Square", new[] { TownRect.Metres(-210, -40, -150, 40) }, 10000, true, 1400, "commute",
                    "main_street", "tower_rise", "low_ring"),
                D("main_street", "Main Street", new[] { TownRect.Metres(-150, -20, -20, 20), TownRect.Metres(20, -20, 150, 20) }, 10000, true, 1200, "shops",
                    "station_square", "fountain_cross", "market_hall", "lakefront", "homes"),
                D("fountain_cross", "Fountain Cross", new[] { TownRect.Metres(-20, -20, 20, 20) }, 6400, true, 1600, "shops",
                    "main_street", "market_hall", "garden_promenade"),
                D("market_hall", "Market Hall", new[] { TownRect.Metres(-70, -90, -20, -20) }, 6000, true, 1000, "market",
                    "main_street", "fountain_cross", "workshop_lane"),
                D("garden_promenade", "Garden Promenade", new[] { TownRect.Metres(-15, 20, 15, 170), TownRect.Metres(-15, -140, 15, -20) }, 6400, false, 700, "evening",
                    "fountain_cross", "greenhouse_hill", "festival_meadow"),
                D("lakefront", "Lakefront", new[] { TownRect.Metres(150, -120, 260, 140) }, 2000, false, 600, "evening",
                    "main_street", "homes"),
                D("homes", "Homes", new[] { TownRect.Metres(30, 25, 150, 130), TownRect.Metres(30, -130, 150, -25) }, 3000, false, 400, "commute",
                    "main_street", "lakefront"),
                D("workshop_lane", "Workshop Lane", new[] { TownRect.Metres(-150, -170, -75, -95) }, 7000, false, 350, "shops",
                    "market_hall"),
                D("greenhouse_hill", "Greenhouse Hill", new[] { TownRect.Metres(-140, 175, 120, 280) }, 15000, false, 150, "market",
                    "garden_promenade"),
                D("festival_meadow", "Festival Meadow", new[] { TownRect.Metres(-60, -200, 120, -145) }, 5000, false, 100, "shops",
                    "garden_promenade"),
                D("tower_rise", "Tower Rise", new[] { TownRect.Metres(-380, -40, -220, 170) }, 16000, false, 500, "shops",
                    "station_square", "low_ring"),
                D("low_ring", "Low Ring", new[] { TownRect.Metres(-380, -200, -220, -40) }, 10000, false, 450, "night",
                    "station_square", "tower_rise"),
            };

            var nodes = new[]
            {
                // Station Square
                N(101, -180, 0, 10000, "station_square"), N(102, -202, 0, 10000, "station_square"),
                N(103, -185, 32, 10000, "station_square"), N(104, -185, -32, 10000, "station_square"),
                N(105, -205, 20, 10000, "station_square"),
                // Main Street, axis z = 0; 202-204 are the shop's flat pad (s1's height)
                N(201, -150, 0, 10000, "main_street"), N(202, -115, 0, 8400, "main_street"), N(203, -80, 0, 8400, "main_street"),
                N(204, -45, 0, 8400, "main_street"), N(205, -22, 0, 7378, "main_street"),
                N(206, 22, 0, 5755, "main_street"), N(207, 55, 0, 4787, "main_street"), N(208, 90, 0, 3760, "main_street"),
                N(209, 120, 0, 2880, "main_street"), N(210, 148, 0, 2059, "main_street"),
                // Fountain Cross
                N(301, 0, 0, 6400, "fountain_cross"), N(302, -15, -15, 6400, "fountain_cross"), N(303, 12, 12, 6400, "fountain_cross"),
                // Market Hall
                N(401, -28, -28, 6200, "market_hall"), N(402, -45, -55, 6000, "market_hall"), N(403, -65, -80, 6000, "market_hall"),
                // Garden Promenade, axis x = 0: north 6.4 to 12 m, south 6.4 to 5 m
                N(501, 0, 22, 6475, "garden_promenade"), N(502, 0, 60, 7893, "garden_promenade"), N(503, 0, 95, 9200, "garden_promenade"),
                N(504, 0, 130, 10507, "garden_promenade"), N(505, 0, 168, 11925, "garden_promenade"),
                N(511, 0, -22, 6377, "garden_promenade"), N(512, 0, -60, 5933, "garden_promenade"), N(513, 0, -100, 5467, "garden_promenade"),
                N(514, 0, -138, 5023, "garden_promenade"),
                // Lakefront (lake level 0 east of x 200; the pier reaches x 255; 607 is the cleaning-boat jetty; 064 review: 605
                // is the airlock jetty, a deck over the water from the shore at 604 to Airlock 2 at the play-area edge)
                N(601, 165, 0, 2000, "lakefront"), N(602, 255, 0, 2000, "lakefront"), N(603, 205, 60, 1000, "lakefront"),
                N(604, 190, 120, 2000, "lakefront"), N(605, 240, 130, 2000, "lakefront"), N(606, 180, -90, 2000, "lakefront"),
                N(607, 240, -60, 2000, "lakefront"),
                // Homes
                N(701, 55, 40, 4600, "homes"), N(702, 95, 80, 4200, "homes"), N(703, 135, 115, 3600, "homes"),
                N(704, 90, -40, 3800, "homes"), N(705, 60, -100, 4400, "homes"), N(706, 130, -110, 3400, "homes"),
                // Workshop Lane
                N(801, -80, -100, 6600, "workshop_lane"), N(802, -110, -130, 7000, "workshop_lane"), N(803, -140, -150, 7000, "workshop_lane"),
                // Greenhouse Hill: foot, the east path to the tunnel, switchback terraces to the top
                N(901, 0, 182, 12500, "greenhouse_hill"), N(902, 60, 205, 15300, "greenhouse_hill"), N(903, 110, 258, 18500, "greenhouse_hill"),
                N(904, -120, 215, 23300, "greenhouse_hill"), N(905, 90, 235, 32700, "greenhouse_hill"), N(906, -120, 255, 42100, "greenhouse_hill"),
                N(907, -40, 265, 45000, "greenhouse_hill"),
                // Festival Meadow
                N(1001, 0, -152, 5000, "festival_meadow"), N(1002, -40, -170, 5000, "festival_meadow"),
                N(1003, 40, -182, 5000, "festival_meadow"), N(1004, 90, -178, 5000, "festival_meadow"),
                // Tower Rise: 1101 is the switchback's turn (064 review: at x -222, inside Tower Rise; it was in no district)
                N(1101, -222, 100, 13000, "tower_rise"), N(1102, -235, 35, 16000, "tower_rise"), N(1103, -228, 20, 16000, "tower_rise"),
                N(1104, -300, 60, 16000, "tower_rise"), N(1105, -300, 140, 16000, "tower_rise"), N(1106, -350, 100, 16000, "tower_rise"),
                N(1107, -360, 150, 16000, "tower_rise"),
                // Low Ring
                N(1201, -240, -60, 10000, "low_ring"), N(1202, -300, -100, 10000, "low_ring"), N(1203, -355, -120, 10000, "low_ring"),
                N(1204, -300, -170, 10000, "low_ring"),
            };

            const int main = 12000, prom = 10000, path = 5000, pier = 4000;
            var edges = new[]
            {
                // Station Square
                E(201, 101, main), E(101, 102, path), E(101, 103, path), E(101, 104, path), E(103, 105, path), E(102, 105, path),
                // Main Street
                E(201, 202, main), E(202, 203, main), E(203, 204, main), E(204, 205, main), E(205, 301, main),
                E(301, 206, main), E(206, 207, main), E(207, 208, main), E(208, 209, main), E(209, 210, main), E(210, 601, main),
                // Fountain Cross, Market Hall (the market steps beside the ramp route), Workshop Lane
                E(301, 302, path), E(301, 303, path), E(302, 401, path), E(401, 402, path), E(402, 403, path),
                new PathEdge(204, 401, PathEdgeKind.Stairs, 4000, 0),
                E(403, 801, path), E(801, 802, path), E(802, 803, path),
                // Garden Promenade
                E(301, 501, prom), E(501, 502, prom), E(502, 503, prom), E(503, 504, prom), E(504, 505, prom),
                E(301, 511, prom), E(511, 512, prom), E(512, 513, prom), E(513, 514, prom),
                // Greenhouse Hill: switchback ramps plus the funicular from the Promenade's north end
                R(505, 901), R(901, 902), R(902, 903), R(902, 904), R(904, 905), R(905, 906), R(906, 907),
                new PathEdge(505, 907, PathEdgeKind.Funicular, 4000, 60),
                // Festival Meadow
                E(514, 1001, prom), E(1001, 1002, path), E(1001, 1003, path), E(1003, 1004, path),
                // Lakefront
                E(601, 602, pier), E(601, 603, path), E(603, 604, path), E(604, 605, pier), E(601, 606, path), E(601, 607, pier),
                // Homes
                E(207, 701, path), E(701, 702, path), E(702, 703, path), E(703, 604, path),
                E(208, 704, path), E(704, 705, path), E(704, 706, path), E(706, 606, path),
                // Tower Rise: a switchback ramp plus the lift (20 s) from Station Square
                R(103, 1101), R(1101, 1102), new PathEdge(105, 1103, PathEdgeKind.Lift, 3000, 20),
                E(1103, 1102, path), E(1102, 1104, path), E(1104, 1105, path), E(1104, 1106, path), E(1106, 1107, path),
                // Low Ring
                E(104, 1201, path), E(1201, 1202, path), E(1202, 1203, path), E(1202, 1204, path),
            };

            string[] pitch = { "corner_counter" };
            string[] small = { "corner_counter", "street_counter" };
            string[] restaurant = { "street_counter", "delivery", "custom_orders", "tables", "buffet" };
            var places = new List<PlaceDef>
            {
                // Pitches: stage 1, outdoor
                P("c1", "Station Pitch", PlaceKind.Pitch, "station_square", -170, -10, 101, 1, 6, 1100, 1100, true, pitch),
                P("c2", "Main Street Pitch", PlaceKind.Pitch, "main_street", -130, -6, 202, 1, 6, 1000, 1000, true, pitch),
                P("c3", "Fountain Corner Pitch", PlaceKind.Pitch, "fountain_cross", 8, -8, 301, 1, 6, 1300, 1400, true, pitch),
                P("c4", "Market Door Pitch", PlaceKind.Pitch, "market_hall", -30, -22, 401, 1, 6, 1000, 1000, true, pitch),
                P("c5", "Pier Pitch", PlaceKind.Pitch, "lakefront", 250, 3, 602, 1, 6, 900, 800, true, pitch),
                P("c6", "Beach Pitch", PlaceKind.Pitch, "lakefront", 210, 55, 603, 1, 6, 800, 700, true, pitch),
                P("c7", "Park Pitch", PlaceKind.Pitch, "garden_promenade", 8, 95, 503, 1, 6, 900, 800, true, pitch),
                P("c8", "Arena Gate Pitch", PlaceKind.Pitch, "festival_meadow", -35, -160, 1002, 1, 6, 1200, 600, true, pitch) with { EventDaysOnly = true },
                // Small places: stage 2; s1 is the shop (anchor at its front door, 2.8 m north of Main Street's axis)
                new PlaceDef("s1", "Main Street Shop", PlaceKind.Small, "main_street", new TownPoint(-80000, 2800), 203, 2, 75, 1000, 1000, false, false, small),
                P("s2", "Main Street West Shop", PlaceKind.Small, "main_street", -118, -8, 202, 2, 70, 1000, 1000, false, small),
                P("s3", "Side Lane Shop", PlaceKind.Small, "main_street", -40, 15, 204, 2, 60, 900, 900, false, small),
                P("s4", "Station Shop", PlaceKind.Small, "station_square", -175, 32, 103, 2, 80, 1100, 1150, false, small),
                P("s5", "Promenade Shop", PlaceKind.Small, "garden_promenade", -8, 130, 504, 2, 65, 900, 850, true, small),
                P("s6", "Workshop Lane Shop", PlaceKind.Small, "workshop_lane", -105, -125, 802, 2, 90, 700, 600, false, small),
                // Restaurants: stage 3
                P("r1", "Fountain Corner Restaurant", PlaceKind.Restaurant, "fountain_cross", 15, 15, 303, 3, 260, 1300, 1400, true, restaurant),
                P("r2", "Lakefront Restaurant", PlaceKind.Restaurant, "lakefront", 190, -80, 606, 3, 220, 900, 900, true, restaurant),
                P("r3", "Garden Restaurant", PlaceKind.Restaurant, "garden_promenade", 10, 60, 502, 3, 180, 1000, 1000, true, restaurant),
                P("r4", "Station Restaurant", PlaceKind.Restaurant, "station_square", -195, -32, 104, 3, 200, 1100, 1150, false, restaurant),
                // Stage five: big jobs, no channels
                P("sky_deck", "Sky Deck", PlaceKind.StageFive, "tower_rise", -300, 145, 1105, 5, 400, 1000, 0, false),
                P("wreck_market", "Wreck Night Market", PlaceKind.StageFive, "low_ring", -365, -120, 1203, 5, 800, 1000, 0, false),
                P("arena", "Arena", PlaceKind.StageFive, "festival_meadow", -45, -185, 1002, 5, 3000, 1000, 0, true),
                P("meadow_ground", "Meadow Ground", PlaceKind.StageFive, "festival_meadow", 90, -185, 1004, 5, 5000, 1000, 0, true),
                P("ingredient_lab", "Ingredient Lab", PlaceKind.StageFive, "greenhouse_hill", -40, 270, 907, 5, 600, 1000, 0, false),
                // Civic
                P("station", "Station", PlaceKind.Civic, "station_square", -205, 0, 102, 1, 0, 1000, 0, false),
                P("town_hall", "Town Hall", PlaceKind.Civic, "station_square", -160, 30, 103, 1, 0, 1000, 0, false),
                P("printer_bar", "Printer Bar", PlaceKind.Civic, "station_square", -160, -30, 104, 1, 0, 1000, 0, false),
                P("museum", "Museum", PlaceKind.Civic, "main_street", -145, 12, 201, 1, 0, 1000, 0, false),
                P("market_floor", "Market Floor", PlaceKind.Civic, "market_hall", -45, -55, 402, 1, 0, 1000, 0, false),
                P("fountain", "Fountain", PlaceKind.Civic, "fountain_cross", 0, 0, 301, 1, 0, 1000, 0, true),
                P("robot_workshop", "Robot Workshop", PlaceKind.Civic, "workshop_lane", -120, -140, 802, 1, 0, 1000, 0, false),
                P("crew_house", "Crew House", PlaceKind.Civic, "homes", 90, 70, 702, 1, 0, 1000, 0, false),
                P("betting_hall", "Betting Hall", PlaceKind.Civic, "low_ring", -300, -95, 1202, 1, 0, 1000, 0, false),
                P("dream_booths", "Dream Booths", PlaceKind.Civic, "low_ring", -305, -165, 1204, 1, 0, 1000, 0, false),
                // Monorail stops (no edges; riding it is a later spec)
                P("stop_station", "Station Stop", PlaceKind.Stop, "station_square", -205, 25, 102, 1, 0, 1000, 0, false),
                P("stop_lakefront", "Lakefront Stop", PlaceKind.Stop, "lakefront", 170, 25, 601, 1, 0, 1000, 0, false),
                P("stop_arena", "Arena Stop", PlaceKind.Stop, "festival_meadow", 0, -160, 1001, 1, 0, 1000, 0, false),
                P("stop_workshop", "Workshop Stop", PlaceKind.Stop, "workshop_lane", -140, -160, 803, 1, 0, 1000, 0, false),
                // Delivery addresses
                P("home_1", "Home 1", PlaceKind.Address, "homes", 60, 50, 701, 1, 0, 1000, 0, false),
                P("home_2", "Home 2", PlaceKind.Address, "homes", 100, 90, 702, 1, 0, 1000, 0, false),
                P("home_3", "Home 3", PlaceKind.Address, "homes", 140, 120, 703, 1, 0, 1000, 0, false),
                P("home_4", "Home 4", PlaceKind.Address, "homes", 95, -45, 704, 1, 0, 1000, 0, false),
                P("home_5", "Home 5", PlaceKind.Address, "homes", 65, -105, 705, 1, 0, 1000, 0, false),
                P("home_6", "Home 6", PlaceKind.Address, "homes", 135, -115, 706, 1, 0, 1000, 0, false),
                P("tower_1", "Tower Flat 1", PlaceKind.Address, "tower_rise", -305, 60, 1104, 1, 0, 1000, 0, false),
                P("tower_2", "Tower Flat 2", PlaceKind.Address, "tower_rise", -345, 100, 1106, 1, 0, 1000, 0, false),
                P("low_1", "Low Ring Door 1", PlaceKind.Address, "low_ring", -245, -55, 1201, 1, 0, 1000, 0, false),
                P("low_2", "Low Ring Door 2", PlaceKind.Address, "low_ring", -290, -175, 1204, 1, 0, 1000, 0, false),
                // Border points (anchor on the play-area edge)
                P("wreck_bulkhead", "Wreck Bulkhead", PlaceKind.Border, "low_ring", -380, -120, 1203, 1, 0, 1000, 0, false),
                P("wildbelt_lookout", "Wildbelt Lookout", PlaceKind.Border, "festival_meadow", 40, -200, 1003, 1, 0, 1000, 0, false),
                P("water_gate_3", "Water Gate 3", PlaceKind.Border, "lakefront", 260, -60, 607, 1, 0, 1000, 0, false),
                P("tunnel_door", "Tunnel Door", PlaceKind.Border, "greenhouse_hill", 110, 280, 903, 1, 0, 1000, 0, false),
                P("airlock_1", "Airlock 1", PlaceKind.Border, "tower_rise", -380, 150, 1107, 1, 0, 1000, 0, false),
                P("airlock_2", "Airlock 2", PlaceKind.Border, "lakefront", 260, 130, 605, 1, 0, 1000, 0, false),
                P("airlock_3", "Airlock 3", PlaceKind.Border, "festival_meadow", 90, -200, 1004, 1, 0, 1000, 0, false),
            };

            var safeSpots = new[]
            {
                new SafeSpot(1, 101), new SafeSpot(2, 203), new SafeSpot(3, 301), new SafeSpot(4, 402), new SafeSpot(5, 502),
                new SafeSpot(6, 601), new SafeSpot(7, 701), new SafeSpot(8, 802), new SafeSpot(9, 901), new SafeSpot(10, 1001),
                new SafeSpot(11, 1104), new SafeSpot(12, 1202),
            };

            // The play area: x -380..260, z -200..280 (640 x 480 m), counter-clockwise.
            var playArea = new[] { TownPoint.Metres(-380, -200), TownPoint.Metres(260, -200), TownPoint.Metres(260, 280), TownPoint.Metres(-380, 280) };
            int lowest = nodes.Min(n => n.HeightMm);
            return new TownCatalog(districts, curves, places, nodes, edges, playArea, safeSpots, "s1", "s1", 301,
                TownPoint.Metres(-60, 40), 465000, 385000, 250000, lowest - 10000);
        }

        private static FootfallCurve Curve(string id, params int[] hours) => new FootfallCurve(id, hours);

        private static DistrictDef D(string id, string name, TownRect[] areas, int heightMm, bool core, int dayFootfall, string curve,
            params string[] neighbours) => new DistrictDef(id, name, areas, heightMm, core, dayFootfall, curve, 1300, neighbours);

        private static PathNode N(int id, int x, int z, int heightMm, string district) => new PathNode(id, TownPoint.Metres(x, z), heightMm, district);

        private static PathEdge E(int a, int b, int widthMm) => new PathEdge(a, b, PathEdgeKind.Path, widthMm, 0);

        private static PathEdge R(int a, int b) => new PathEdge(a, b, PathEdgeKind.Ramp, 5000, 0);

        private static PlaceDef P(string id, string name, PlaceKind kind, string district, int x, int z, int node, int fromStage, int areaM2,
            int frontage, int rent, bool outdoor, params string[] channels) =>
            new PlaceDef(id, name, kind, district, TownPoint.Metres(x, z), node, fromStage, areaM2, frontage, rent, outdoor, false, channels);

        /// <summary>Integer polygon helpers (inclusive edges); internal, tested through the catalog.</summary>
        internal static class Polygon
        {
            public static long TwiceSignedArea(IReadOnlyList<TownPoint> poly)
            {
                long sum = 0;
                for (int i = 0; i < poly.Count; i++)
                {
                    TownPoint a = poly[i], b = poly[(i + 1) % poly.Count];
                    sum += (long)a.XMm * b.ZMm - (long)b.XMm * a.ZMm;
                }
                return sum;
            }

            public static bool OnEdge(IReadOnlyList<TownPoint> poly, TownPoint p)
            {
                for (int i = 0; i < poly.Count; i++)
                    if (OnSegment(poly[i], poly[(i + 1) % poly.Count], p)) return true;
                return false;
            }

            public static bool Contains(IReadOnlyList<TownPoint> poly, TownPoint p)
            {
                if (poly.Count < 3) return false;
                if (OnEdge(poly, p)) return true;
                bool inside = false;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    TownPoint a = poly[i], b = poly[j];
                    if ((a.ZMm > p.ZMm) != (b.ZMm > p.ZMm))
                    {
                        // Does the ray to +x cross edge a-b? Compare p.x with the crossing x, exactly in integers.
                        long dz = (long)b.ZMm - a.ZMm;
                        long lhs = ((long)p.XMm - a.XMm) * dz;
                        long rhs = ((long)b.XMm - a.XMm) * ((long)p.ZMm - a.ZMm);
                        if (dz > 0 ? lhs < rhs : lhs > rhs) inside = !inside;
                    }
                }
                return inside;
            }

            private static bool OnSegment(TownPoint a, TownPoint b, TownPoint p)
            {
                long cross = ((long)b.XMm - a.XMm) * ((long)p.ZMm - a.ZMm) - ((long)b.ZMm - a.ZMm) * ((long)p.XMm - a.XMm);
                if (cross != 0) return false;
                return p.XMm >= Math.Min(a.XMm, b.XMm) && p.XMm <= Math.Max(a.XMm, b.XMm)
                    && p.ZMm >= Math.Min(a.ZMm, b.ZMm) && p.ZMm <= Math.Max(a.ZMm, b.ZMm);
            }
        }
    }
}
