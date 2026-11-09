using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Menu = Worktown.Game.Npc.Menu;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027a AC2 to AC4 on the built scene (docs/feel.md 6): a back line, a cook aisle 1.2 to 1.5 m wide, a front counter
    /// between the aisle and the guests, every control facing the aisle, the dish flow in order and close together, the
    /// spawns in the aisle, a staff gap of at least 1 m, nothing overlapping, everything in reach from the aisle, and a
    /// customer served across the counter. Directions come from the scene: "towards the street" is from the plate shelf
    /// to the serving counter; "right" is the cook's right while facing the back line.
    /// </summary>
    public class FloorPlanTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        /// <summary>About half a customer's width: guests keep this far from the aisle and the furniture.</summary>
        private const float GuestRadius = 0.3f;
        /// <summary>The player's CharacterController radius (GrayboxSceneBuilder.CreatePlayerPrefab; spec 027a: 0.3 m).</summary>
        private const float PlayerRadius = 0.3f;
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private static readonly Type CustomerServiceType = typeof(CustomerRules).Assembly.GetType("Worktown.Game.Npc.CustomerService");

        /// <summary>The floor plan as read from the scene.</summary>
        private sealed class Plan
        {
            public Scene Scene;
            public List<Station> Stations = new List<Station>();
            public Station Serving, Shelf;
            public List<Station> BackStations = new List<Station>();
            public List<PlaceSurface> Worktops = new List<PlaceSurface>();
            public List<BoxCollider> FrontRuns = new List<BoxCollider>();
            public List<BoxCollider> Walls = new List<BoxCollider>();
            public Vector3 Street;  // horizontal unit vector from the back line towards the street
            public Vector3 Right;   // the cook's right while facing the back line
            public float AisleStart, AisleEnd; // along Street: the back line's front (buttons included) and the counter's back
            public float AisleLeft, AisleRight; // along Right: the side walls' inner faces
            public float CounterStreetSide;     // along Street: the front counter's street side

            public float U(Vector3 p) => Vector3.Dot(p, Street);
            public float V(Vector3 p) => Vector3.Dot(p, Right);
            public float AisleCentre => (AisleStart + AisleEnd) * 0.5f;

            /// <summary>The point on the aisle's centre line nearest to <paramref name="target"/> (at floor height).</summary>
            public Vector3 AislePointNear(Vector3 target)
            {
                float v = Mathf.Clamp(V(target), AisleLeft + PlayerRadius, AisleRight - PlayerRadius);
                return Street * AisleCentre + Right * v;
            }

            public bool InAisle(Vector3 p, float margin) =>
                U(p) > AisleStart - margin && U(p) < AisleEnd + margin && V(p) > AisleLeft - margin && V(p) < AisleRight + margin;
        }

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static float MaxAlong(Bounds b, Vector3 axis) =>
            Vector3.Dot(b.center, axis) + Mathf.Abs(axis.x) * b.extents.x + Mathf.Abs(axis.y) * b.extents.y + Mathf.Abs(axis.z) * b.extents.z;

        private static float MinAlong(Bounds b, Vector3 axis) =>
            Vector3.Dot(b.center, axis) - Mathf.Abs(axis.x) * b.extents.x - Mathf.Abs(axis.y) * b.extents.y - Mathf.Abs(axis.z) * b.extents.z;

        private static Bounds BodyOf(Station station) => station.transform.Find("Body").GetComponent<BoxCollider>().bounds;

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        private static Plan Open()
        {
            var plan = new Plan { Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) };
            Scene scene = plan.Scene;
            plan.Stations = All<Station>(scene);
            foreach (Station s in plan.Stations)
            {
                if (s.Kind == StationKind.Counter) plan.Serving = s;
                else plan.BackStations.Add(s);
                if (s.Kind == StationKind.PlateShelf) plan.Shelf = s;
            }
            Assert.IsNotNull(plan.Serving, "no serving counter");
            Assert.IsNotNull(plan.Shelf, "no plate shelf");
            foreach (PlaceSurface surface in All<PlaceSurface>(scene))
                if (surface.GetComponentInParent<Station>() == null) plan.Worktops.Add(surface);
            foreach (BoxCollider box in All<BoxCollider>(scene))
            {
                if (box.name.StartsWith("Front counter")) plan.FrontRuns.Add(box);
                else if (box.name.StartsWith("Back wall") || box.name.StartsWith("Side wall")) plan.Walls.Add(box);
            }
            Assert.AreEqual(2, plan.FrontRuns.Count, "the plain front counter either side of the serving counter");
            Assert.AreEqual(4, plan.Walls.Count, "the back wall, two side wall pieces at the staff gap end, one at the other end");

            plan.Street = Flat(plan.Serving.transform.position - plan.Shelf.transform.position);
            plan.Right = Vector3.Cross(Vector3.up, -plan.Street);

            plan.AisleStart = float.MinValue;
            foreach (Station s in plan.BackStations) plan.AisleStart = Mathf.Max(plan.AisleStart, MaxAlong(BodyOf(s), plan.Street));
            foreach (PlaceSurface w in plan.Worktops) plan.AisleStart = Mathf.Max(plan.AisleStart, MaxAlong(w.GetComponent<BoxCollider>().bounds, plan.Street));
            foreach (StationButton b in All<StationButton>(scene))
                plan.AisleStart = Mathf.Max(plan.AisleStart, MaxAlong(b.GetComponent<BoxCollider>().bounds, plan.Street));
            plan.AisleEnd = MinAlong(BodyOf(plan.Serving), plan.Street);
            plan.CounterStreetSide = MaxAlong(BodyOf(plan.Serving), plan.Street);
            foreach (BoxCollider run in plan.FrontRuns)
            {
                plan.AisleEnd = Mathf.Min(plan.AisleEnd, MinAlong(run.bounds, plan.Street));
                plan.CounterStreetSide = Mathf.Max(plan.CounterStreetSide, MaxAlong(run.bounds, plan.Street));
            }

            plan.AisleLeft = float.MinValue;
            plan.AisleRight = float.MaxValue;
            foreach (BoxCollider wall in plan.Walls)
            {
                if (!wall.name.StartsWith("Side wall")) continue;
                if (wall.name.Contains("left")) plan.AisleLeft = Mathf.Max(plan.AisleLeft, MaxAlong(wall.bounds, plan.Right));
                else plan.AisleRight = Mathf.Min(plan.AisleRight, MinAlong(wall.bounds, plan.Right));
            }
            Assert.Less(plan.AisleLeft, plan.AisleRight, "side walls at both ends");
            return plan;
        }

        private static void Close(Plan plan) => EditorSceneManager.CloseScene(plan.Scene, true);

        private static Transform Find(Scene scene, string root, string child)
        {
            GameObject go = Array.Find(scene.GetRootGameObjects(), g => g.name == root);
            Assert.IsNotNull(go, root);
            Transform t = go.transform.Find(child);
            Assert.IsNotNull(t, root + "/" + child);
            return t;
        }

        /// <summary>Street entry, queue head, queue direction and the way waiting guests face, from the CustomerService.</summary>
        private static (Vector3 entry, Vector3 head, Vector3 direction, Vector3 facing) Queue(Scene scene)
        {
            Assert.IsNotNull(CustomerServiceType);
            List<Component> services = VesselTestKit.AllOf(scene, CustomerServiceType);
            Assert.AreEqual(1, services.Count);
            var so = new SerializedObject(services[0]);
            var entry = (Transform)so.FindProperty("_entry").objectReferenceValue;
            var head = (Transform)so.FindProperty("_queueHead").objectReferenceValue;
            Assert.IsNotNull(entry);
            Assert.IsNotNull(head);
            return (entry.position, head.position, so.FindProperty("_queueDirection").vector3Value, so.FindProperty("_queueFacing").vector3Value);
        }

        private static List<Vector3> Slots(Vector3 head, Vector3 direction)
        {
            var slots = new List<Vector3>();
            for (int i = 0; i < CustomerRules.MaxVisible; i++) slots.Add(CustomerRules.QueueSlot(head, direction, i));
            return slots;
        }

        /// <summary>Points every 5 cm along a straight walk.</summary>
        private static IEnumerable<Vector3> Along(Vector3 from, Vector3 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.05f));
            for (int i = 0; i <= steps; i++) yield return Vector3.Lerp(from, to, i / (float)steps);
        }

        private static bool InsideXZ(Bounds b, Vector3 p, float margin) =>
            p.x > b.min.x - margin && p.x < b.max.x + margin && p.z > b.min.z - margin && p.z < b.max.z + margin;

        // ---- AC2 ----

        [Test]
        public void Aisle_ClearWidthIsOnePointTwoToOnePointFiveMetres()
        {
            Plan plan = Open();
            try
            {
                float width = plan.AisleEnd - plan.AisleStart;
                Assert.That(width, Is.InRange(1.2f, 1.5f), "clear aisle between the back line (buttons included) and the front counter");
                Assert.Greater(plan.AisleRight - plan.AisleLeft, 10f, "the aisle runs along the whole back line");
            }
            finally { Close(plan); }
        }

        /// <summary>Spec 027a (orchestrator): two cooks pass each other in the aisle, 10 cm to spare.</summary>
        [Test]
        public void Aisle_TwoPlayersCanPassEachOther()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab);
            var controller = prefab.GetComponent<CharacterController>();
            Assert.IsNotNull(controller);
            Assert.AreEqual(PlayerRadius, controller.radius, 1e-4f, "the radius these tests assume");
            Plan plan = Open();
            try
            {
                float needed = 2f * (controller.radius + controller.skinWidth) + 0.1f;
                Assert.GreaterOrEqual(plan.AisleEnd - plan.AisleStart, needed, "two players side by side in the aisle");
            }
            finally { Close(plan); }
        }

        [Test]
        public void FrontCounter_LiesBetweenEveryQueueSlotAndTheAisle_GuestsFaceIt()
        {
            Plan plan = Open();
            try
            {
                (Vector3 _, Vector3 head, Vector3 direction, Vector3 facing) = Queue(plan.Scene);
                float counterLeft = float.MaxValue, counterRight = float.MinValue;
                foreach (BoxCollider run in plan.FrontRuns)
                {
                    counterLeft = Mathf.Min(counterLeft, MinAlong(run.bounds, plan.Right));
                    counterRight = Mathf.Max(counterRight, MaxAlong(run.bounds, plan.Right));
                }
                foreach (Vector3 slot in Slots(head, direction))
                {
                    Assert.GreaterOrEqual(plan.U(slot) - GuestRadius, plan.CounterStreetSide, slot + ": on the street side of the counter");
                    Assert.That(plan.V(slot), Is.InRange(counterLeft + GuestRadius, counterRight - GuestRadius), slot + ": the counter is between it and the aisle");
                }
                Assert.AreEqual(plan.V(plan.Serving.transform.position), plan.V(head), 0.05f, "the queue's head stands at the serving counter");
                Assert.Less(Mathf.Abs(Vector3.Dot(Flat(direction), plan.Street)), 0.05f, "the queue runs along the counter");
                Assert.Greater(Vector3.Dot(Flat(facing), -plan.Street), 0.7f, "waiting guests face the counter and the cook");
            }
            finally { Close(plan); }
        }

        [Test]
        public void GuestPath_EntryQueueExit_NeverEntersTheAisle_OrTheFurniture()
        {
            Plan plan = Open();
            try
            {
                (Vector3 entry, Vector3 head, Vector3 direction, Vector3 _) = Queue(plan.Scene);
                var furniture = new List<Bounds>();
                foreach (Station s in plan.Stations) furniture.Add(BodyOf(s));
                foreach (PlaceSurface w in plan.Worktops) furniture.Add(w.GetComponent<BoxCollider>().bounds);
                foreach (BoxCollider b in plan.FrontRuns) furniture.Add(b.bounds);
                foreach (BoxCollider b in plan.Walls) furniture.Add(b.bounds);
                Assert.Greater(plan.U(entry), plan.CounterStreetSide, "the entry is on the street side");
                // Customers walk straight from the entry to their slot, between slots, and back to the entry (Customer.ServerWalk).
                List<Vector3> slots = Slots(head, direction);
                var walks = new List<(Vector3, Vector3)>();
                for (int i = 0; i < slots.Count; i++)
                {
                    walks.Add((entry, slots[i]));
                    if (i > 0) walks.Add((slots[i], slots[i - 1]));
                }
                foreach ((Vector3 from, Vector3 to) in walks)
                    foreach (Vector3 p in Along(from, to))
                    {
                        Assert.IsFalse(plan.InAisle(p, GuestRadius), "a guest walking " + from + " to " + to + " enters the aisle at " + p);
                        foreach (Bounds b in furniture)
                            Assert.IsFalse(InsideXZ(b, p, GuestRadius), "a guest walking " + from + " to " + to + " walks through furniture at " + p);
                    }
            }
            finally { Close(plan); }
        }

        [Test]
        public void Controls_LabelsAndOpenSides_FaceTheAisle()
        {
            Plan plan = Open();
            try
            {
                int buttons = 0;
                foreach (StationButton button in All<StationButton>(plan.Scene))
                {
                    buttons++;
                    Station station = button.GetComponentInParent<Station>();
                    Assert.Greater(Vector3.Dot(Flat(-button.transform.forward), plan.Street), 0.7f, button.name + " faces the aisle");
                    Assert.GreaterOrEqual(MinAlong(button.GetComponent<BoxCollider>().bounds, plan.Street), MaxAlong(BodyOf(station), plan.Street) - 0.001f,
                        button.name + " sits on the aisle side of " + station.name);
                }
                Assert.AreEqual(11, buttons, "two per burner on two stoves; spec 029: the fryer's knob and a button per basket");

                foreach (Station station in plan.Stations)
                {
                    // A station's front (controls, crate opening, the serving counter's cook side) is its local -z.
                    Vector3 intoAisle = station == plan.Serving ? -plan.Street : plan.Street;
                    Assert.Greater(Vector3.Dot(Flat(-station.transform.forward), intoAisle), 0.7f, station.name + " faces the aisle");
                    // The catalog wrappers show their front (knobs, oven door, crate opening) on their local +z.
                    Transform art = station.transform.Find("Art_" + Worktown.Game.Art.ArtCatalogRules.StationKey(station.Kind));
                    Assert.IsNotNull(art, station.name);
                    Assert.Greater(Vector3.Dot(Flat(art.forward), intoAisle), 0.7f, station.name + ": the model's front faces the aisle");
                    foreach (TextMesh label in station.GetComponentsInChildren<TextMesh>(true))
                        Assert.Greater(Vector3.Dot(Flat(-label.transform.forward), intoAisle), 0.7f, station.name + " " + label.name + " reads from the aisle");
                }

                // Cabinet modules: the worktops' doors and the front counter's doors face the aisle (the cook side).
                int modules = 0;
                foreach (Transform t in All<Transform>(plan.Scene))
                {
                    bool worktop = t.name.StartsWith("Module ") && t.parent != null && t.parent.GetComponent<PlaceSurface>() != null;
                    bool counter = t.name.StartsWith("Counter module ");
                    if (!worktop && !counter) continue;
                    modules++;
                    Assert.Greater(Vector3.Dot(Flat(t.forward), worktop ? plan.Street : -plan.Street), 0.7f, t.parent.name + " " + t.name + " faces the aisle");
                }
                Assert.Greater(modules, 10);

                List<Component> boards = VesselTestKit.AllOf(plan.Scene, VesselTestKit.VesselType).FindAll(v => VesselTestKit.KindOf(v) == VesselKind.Board);
                Assert.AreEqual(1, boards.Count);
                Assert.Greater(Vector3.Dot(Flat(-boards[0].transform.forward), plan.Street), 0.7f, "the board's working edge faces the aisle");
                Assert.Less(plan.U(boards[0].transform.position), plan.AisleStart, "the board lies on the back line");

                GameObject rail = Array.Find(plan.Scene.GetRootGameObjects(), g => g.name == "TicketRail");
                Assert.IsNotNull(rail);
                Assert.Greater(Vector3.Dot(Flat(-rail.transform.forward), -plan.Street), 0.7f, "the ticket rail reads from the aisle");
                Assert.That(plan.U(rail.transform.position), Is.InRange(plan.AisleEnd, plan.CounterStreetSide), "the ticket rail hangs above the front counter");
                Assert.Greater(rail.transform.position.y - 0.6f, 1.8f, "the rail clears the guests' heads");
            }
            finally { Close(plan); }
        }

        [Test]
        public void DishFlow_StockPrepHeatPlate_LeftToRight_AndClose()
        {
            Plan plan = Open();
            try
            {
                // Spec 028b: the stock is the dry shelf and the fridge; the crates are gone.
                var stock = plan.BackStations.FindAll(s => StockLayout.IsStock(s.Kind));
                var stoves = plan.BackStations.FindAll(s => s.Kind == StationKind.Stove);
                Assert.AreEqual(0, plan.Stations.FindAll(s => s.Kind == StationKind.Crate).Count, "no crates any more");
                Assert.AreEqual(1, stock.FindAll(s => s.Kind == StationKind.Fridge).Count, "one fridge");
                Assert.AreEqual(1, stock.FindAll(s => s.Kind == StationKind.Shelf).Count, "one dry shelf");
                Assert.AreEqual(2, stoves.Count);
                stoves.Sort((a, b) => plan.V(a.transform.position).CompareTo(plan.V(b.transform.position)));
                Component board = VesselTestKit.AllOf(plan.Scene, VesselTestKit.VesselType).Find(v => VesselTestKit.KindOf(v) == VesselKind.Board);
                float lastStock = float.MinValue;
                foreach (Station s in stock) lastStock = Mathf.Max(lastStock, plan.V(s.transform.position));
                float boardV = plan.V(board.transform.position), stoveA = plan.V(stoves[0].transform.position);
                float stoveB = plan.V(stoves[1].transform.position), shelf = plan.V(plan.Shelf.transform.position);

                Assert.Less(lastStock, boardV, "stock before prep");
                Assert.Less(boardV, stoveA, "prep before heat");
                Assert.Less(stoveB, shelf, "heat before plating");
                Assert.LessOrEqual(boardV - lastStock, 3f, "stock to board");
                Assert.LessOrEqual(stoveA - boardV, 3f, "board to stove A");
                Assert.LessOrEqual(shelf - stoveB, 3f, "stove B to the plate shelf");
                // Spec 029: the fryer between prep and the stoves (stock, prep, fryer, stoves, plates), a step from each.
                var fryers = plan.BackStations.FindAll(s => s.Kind == StationKind.Fryer);
                Assert.AreEqual(1, fryers.Count, "one fryer");
                float fryer = plan.V(fryers[0].transform.position);
                Assert.Less(boardV, fryer, "prep before the fryer");
                Assert.Less(fryer, stoveA, "the fryer before the stoves");
                Assert.LessOrEqual(fryer - boardV, 3f, "board to the fryer");
                Assert.LessOrEqual(stoveA - fryer, 1.5f, "the fryer next to stove A");

                Vector3 serving = plan.Serving.transform.position;
                Assert.LessOrEqual(Vector3.Distance(plan.AislePointNear(plan.Shelf.transform.position), plan.AislePointNear(serving)), 2.5f,
                    "the walk from the plate shelf to the serving counter");
                Vector2 flat = new Vector2(serving.x - plan.Shelf.transform.position.x, serving.z - plan.Shelf.transform.position.z);
                Assert.LessOrEqual(flat.magnitude, 2.5f + 1e-3f, "plate shelf and serving counter, centre to centre");
            }
            finally { Close(plan); }
        }

        // ---- Spec 028b AC3: compact stock ----

        /// <summary>The 027a kitchen's aisle (between the side walls' inner faces, x -2.02 to 13.54).</summary>
        private const float SpecAisleLength027a = 15.56f;

        [Test]
        public void Stock_FridgeAndShelf_AreAtMostTwoAndAHalfMetresFromTheBoard_AndTheKitchenIsCompact()
        {
            Plan plan = Open();
            try
            {
                Component board = VesselTestKit.AllOf(plan.Scene, VesselTestKit.VesselType).Find(v => VesselTestKit.KindOf(v) == VesselKind.Board);
                Vector3 b = board.transform.position;
                foreach (Station s in plan.BackStations.FindAll(s => StockLayout.IsStock(s.Kind)))
                {
                    Vector3 p = s.transform.position;
                    float flat = new Vector2(p.x - b.x, p.z - b.z).magnitude;
                    Assert.LessOrEqual(flat, 2.5f, s.name + " to the board, centre to centre (feel.md 6: a few steps)");
                }
                Assert.LessOrEqual(plan.AisleRight - plan.AisleLeft, SpecAisleLength027a + 1.5f, "no longer than the 027a kitchen plus 1.5 m");
            }
            finally { Close(plan); }
        }

        // ---- Spec 029 AC4: the fryer ----

        [Test]
        public void Fryer_ControlsFaceTheAisle_BasketsHangInItsWells_AndAreInReach()
        {
            Plan plan = Open();
            try
            {
                Station fryer = plan.BackStations.Find(s => s.Kind == StationKind.Fryer);
                Assert.IsNotNull(fryer, "no fryer");
                Type contentsType = typeof(StationRules).Assembly.GetType("Worktown.Game.Cooking.StationContents");
                Type pickupType = typeof(StationRules).Assembly.GetType("Worktown.Game.Net.Pickup");
                Assert.IsNotNull(fryer.GetComponent(contentsType), "the fryer's light byte");
                Bounds body = BodyOf(fryer);
                var options = new HashSet<int>();
                foreach (StationButton button in fryer.GetComponentsInChildren<StationButton>())
                {
                    Assert.AreSame(fryer, button.Station);
                    Assert.IsTrue(options.Add(button.Option), button.name + ": one button per option");
                    Assert.Greater(Vector3.Dot(Flat(-button.transform.forward), plan.Street), 0.7f, button.name + " faces the aisle");
                    Assert.GreaterOrEqual(MinAlong(button.GetComponent<BoxCollider>().bounds, plan.Street), MaxAlong(body, plan.Street) - 0.001f,
                        button.name + " sits on the fryer's front");
                }
                CollectionAssert.AreEquivalent(new[] { FryerRules.PowerOption, FryerRules.BasketOption(0), FryerRules.BasketOption(1) }, options);
                Assert.Less(body.max.y, Worktown.Game.Art.FryerArt.OilHeight, "the body's collider stays under the oil, so a lowered basket can be aimed at");

                var baskets = All<FryerBasket>(plan.Scene);
                Assert.AreEqual(FryerRules.BasketsPerFryer, baskets.Count);
                var seen = new HashSet<int>();
                foreach (FryerBasket basket in baskets)
                {
                    Assert.AreSame(fryer, basket.Fryer, basket.name);
                    Assert.IsTrue(seen.Add(basket.Basket), basket.name + ": one basket per index");
                    Assert.AreEqual(VesselKind.Basket, VesselTestKit.KindOf(basket.GetComponent(VesselTestKit.VesselType)), basket.name);
                    Assert.IsNull(basket.GetComponent(pickupType), basket.name + " is never carried");
                    Assert.IsTrue(Array.Exists(basket.GetComponents<Component>(), c => c != null && c.GetType().Name == "NetworkObject"), basket.name + " is networked");
                    Vector3 p = basket.transform.position;
                    Assert.IsTrue(p.x > body.min.x && p.x < body.max.x && p.z > body.min.z && p.z < body.max.z, basket.name + " hangs in the fryer");
                    Transform rig = basket.transform.Find("Rig");
                    Assert.IsNotNull(rig, basket.name + " has a rig");
                    Assert.IsNotNull(rig.GetComponent<BoxCollider>(), basket.name + ": the collider moves with the basket");
                    Assert.IsNotNull(rig.Find("Contents"), basket.name + " draws its contents in the rig");
                    Transform grip = Array.Find(rig.GetComponentsInChildren<Transform>(true), t => t.name == "Grip");
                    Assert.IsNotNull(grip, basket.name + " has a handle");
                    Assert.Greater(plan.U(grip.position), plan.U(p) + 0.2f, basket.name + ": the handle points to the aisle");
                    Vector3 cook = plan.AislePointNear(p);
                    Assert.IsTrue(Worktown.Game.Net.Rules.PickupRules.IsInReach(cook, p), basket.name + " is out of reach from the aisle");
                }
                // Basket 0 on the cook's left, the way its button is.
                FryerBasket left = baskets.Find(b => b.Basket == 0), right = baskets.Find(b => b.Basket == 1);
                Assert.Less(plan.V(left.transform.position), plan.V(right.transform.position));
            }
            finally { Close(plan); }
        }

        [Test]
        public void StockSlots_OneIngredientEach_FaceTheAisle_AndAreInReach()
        {
            Plan plan = Open();
            try
            {
                var ids = new HashSet<string>();
                int slots = 0;
                foreach (Station station in plan.BackStations.FindAll(s => StockLayout.IsStock(s.Kind)))
                {
                    Assert.AreEqual(StockLayout.SlotsOf(station.Kind).Count, station.SlotCount, station.name);
                    Bounds body = BodyOf(station);
                    for (int option = 0; option < station.SlotCount; option++)
                    {
                        Assert.IsTrue(station.TryGetSlot(option, out StockSlot slot), station.name + " slot " + option);
                        slots++;
                        Assert.AreSame(station, slot.Station);
                        Assert.AreEqual(option, slot.Option, slot.name + ": the option is its index");
                        Assert.AreEqual(StockLayout.SlotsOf(station.Kind)[option], slot.IngredientId, slot.name);
                        Assert.IsTrue(ids.Add(slot.IngredientId), slot.IngredientId + " is in two slots");
                        Assert.Greater(slot.transform.childCount, 0, slot.name + " shows its item");
                        Assert.IsEmpty(slot.GetComponentsInChildren<Collider>(true), slot.name + ": the items never block a ray");
                        Assert.AreSame(slot, station.NearestSlot(slot.Anchor), slot.name + ": a ray on it means it");
                        // The open side faces the aisle: the items stand just inside the body's aisle-side face.
                        Vector3 a = slot.Anchor;
                        Assert.IsTrue(body.Contains(a), slot.name + " stands inside " + station.name);
                        Assert.That(MaxAlong(body, plan.Street) - plan.U(a), Is.InRange(0.05f, 0.3f), slot.name + " stands at the open front");
                        Vector3 cook = plan.AislePointNear(a);
                        Assert.IsTrue(PickupRules.IsInReach(cook, a), slot.name + " is out of reach from the aisle (" + Vector3.Distance(cook, a).ToString("0.00") + " m)");
                    }
                }
                Assert.AreEqual(DemoIngredients.All.Count, slots, "every ingredient has its slot");
            }
            finally { Close(plan); }
        }

        [Test]
        public void Spawns_AreInTheAisle_FacingTheBackLine()
        {
            Plan plan = Open();
            try
            {
                GameObject parent = Array.Find(plan.Scene.GetRootGameObjects(), g => g.name == "SpawnPoints");
                Assert.IsNotNull(parent);
                Assert.Greater(parent.transform.childCount, 0);
                foreach (Transform spawn in parent.transform)
                {
                    Assert.IsTrue(plan.InAisle(spawn.position, -PlayerRadius), spawn.name + " at " + spawn.position + " is not inside the aisle");
                    Assert.Greater(Vector3.Dot(Flat(spawn.forward), -plan.Street), 0.7f, spawn.name + " faces the back line");
                }
            }
            finally { Close(plan); }
        }

        [Test]
        public void Walls_BackWallAndSideWalls_StaffGapAtLeastOneMetre()
        {
            Plan plan = Open();
            try
            {
                BoxCollider back = plan.Walls.Find(w => w.name.StartsWith("Back wall"));
                Assert.IsNotNull(back);
                Assert.AreEqual(2.4f, back.bounds.size.y, 0.1f, "the back wall is about 2.4 m high");
                foreach (Station s in plan.BackStations)
                    Assert.LessOrEqual(MaxAlong(back.bounds, plan.Street), MinAlong(BodyOf(s), plan.Street) + 0.001f, "the back wall stands behind " + s.name);

                // The largest free stretch along each end, from the back wall's inner face to the counter's street side.
                float from = MaxAlong(back.bounds, plan.Street), to = plan.CounterStreetSide;
                float LargestGap(string side, out float gapStart, out float gapEnd)
                {
                    var pieces = plan.Walls.FindAll(w => w.name.StartsWith("Side wall") && w.name.Contains(side))
                        .ConvertAll(w => (MinAlong(w.bounds, plan.Street), MaxAlong(w.bounds, plan.Street)));
                    Assert.IsNotEmpty(pieces, "a side wall at the " + side + " end");
                    pieces.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                    float best = 0f, at = from;
                    gapStart = gapEnd = at;
                    foreach ((float min, float max) in pieces)
                    {
                        if (min - at > best)
                        {
                            best = min - at;
                            gapStart = at;
                            gapEnd = min;
                        }
                        at = Mathf.Max(at, max);
                    }
                    if (to - at > best)
                    {
                        best = to - at;
                        gapStart = at;
                        gapEnd = to;
                    }
                    return best;
                }
                Assert.Greater(to, from);
                float left = LargestGap("left", out float start, out float end);
                Assert.GreaterOrEqual(left, 1.0f, "the staff gap at the left end");
                Assert.IsTrue(start < plan.AisleCentre && end > plan.AisleCentre && end <= plan.AisleEnd + 0.01f,
                    "the staff gap opens the aisle, not the guest side");
                Assert.Less(LargestGap("right", out _, out _), 0.05f, "the right end is closed");
                foreach (BoxCollider side in plan.Walls.FindAll(w => w.name.StartsWith("Side wall")))
                    Assert.That(side.bounds.size.y, Is.InRange(0.9f, 1.5f), side.name + " is a low wall");
            }
            finally { Close(plan); }
        }

        [Test]
        public void Nothing_Overlaps_KitchenWallsCounterAndStreet()
        {
            Plan plan = Open();
            try
            {
                var boxes = new List<(string Name, Bounds Bounds)>();
                foreach (Station s in plan.Stations) boxes.Add((s.name, BodyOf(s)));
                foreach (PlaceSurface w in plan.Worktops) boxes.Add((w.name, w.GetComponent<BoxCollider>().bounds));
                foreach (BoxCollider b in plan.FrontRuns) boxes.Add((b.name, b.bounds));
                foreach (BoxCollider b in plan.Walls) boxes.Add((b.name, b.bounds));
                const float touch = 0.001f;
                bool Overlap(Bounds a, Bounds b) =>
                    a.min.x < b.max.x - touch && a.max.x > b.min.x + touch && a.min.z < b.max.z - touch && a.max.z > b.min.z + touch;
                for (int i = 0; i < boxes.Count; i++)
                    for (int j = i + 1; j < boxes.Count; j++)
                        Assert.IsFalse(Overlap(boxes[i].Bounds, boxes[j].Bounds), boxes[i].Name + " overlaps " + boxes[j].Name);

                var others = new List<(string Name, Bounds Bounds)>();
                foreach (string rootName in new[] { "Street", "Boxes" })
                {
                    GameObject root = Array.Find(plan.Scene.GetRootGameObjects(), g => g.name == rootName);
                    Assert.IsNotNull(root, rootName);
                    foreach (Transform child in root.transform)
                    {
                        Renderer[] renderers = child.GetComponentsInChildren<Renderer>();
                        if (renderers.Length == 0 || child.name.StartsWith("Art_Road")) continue; // the road is flat ground
                        Bounds b = renderers[0].bounds;
                        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                        others.Add((child.name, b));
                    }
                }
                foreach ((string name, Bounds kitchen) in boxes)
                    foreach ((string other, Bounds b) in others)
                        Assert.IsFalse(Overlap(kitchen, b), name + " overlaps " + other);
            }
            finally { Close(plan); }
        }

        // ---- AC3 ----

        [Test]
        public void EveryButtonCrateSurfaceAndTheServingPoint_IsInReachFromTheAisle()
        {
            Plan plan = Open();
            try
            {
                void Reach(string what, Vector3 target)
                {
                    Vector3 cook = plan.AislePointNear(target);
                    Assert.IsTrue(PickupRules.IsInReach(cook, target), what + " at " + target + " is out of reach from the aisle (" + cook + ", "
                        + Vector3.Distance(cook, target).ToString("0.00") + " m)");
                }
                foreach (StationButton button in All<StationButton>(plan.Scene)) Reach(button.name, button.transform.position);
                foreach (Station station in plan.Stations) Reach(station.name, station.transform.position);
                foreach (StockSlot slot in All<StockSlot>(plan.Scene)) Reach(slot.name, slot.Anchor); // spec 028b: reach is to the slot
                int spots = 0;
                foreach (PlaceSurface surface in All<PlaceSurface>(plan.Scene))
                    for (int i = 0; i < surface.SpotCount; i++, spots++) Reach(surface.name + " spot " + i, surface.SpotWorldPosition(i));
                Assert.Greater(spots, 11);
                Assert.AreEqual(2.5f, PickupRules.MaxReach, "spec 027a changes no rule numbers");
            }
            finally { Close(plan); }
        }

        // ---- AC4 ----

        [Test]
        public void Customer_QueuesOrders_IsServedAcrossTheCounter_AndLeavesToTheStreet()
        {
            Plan plan = Open();
            try
            {
                (Vector3 entry, Vector3 head, Vector3 direction, Vector3 _) = Queue(plan.Scene);
                var queue = new CustomerQueue();
                queue.Add(41, "snack", Menu.Fries);
                queue.Add(42, "meal", Menu.TomatoSalad);
                Vector3 first = CustomerRules.QueueSlot(head, direction, queue.IndexOf(41));
                Vector3 second = CustomerRules.QueueSlot(head, direction, queue.IndexOf(42));

                // The cook stands in the aisle at the serving counter; the counter is between the cook and the first guest.
                Station serving = plan.Serving;
                Vector3 cook = plan.AislePointNear(serving.transform.position);
                Assert.IsTrue(plan.InAisle(cook, 0f));
                Assert.IsTrue(StationRules.IsInReach(cook, serving.transform.position), "the cook reaches the serving counter from the aisle");
                Bounds body = BodyOf(serving);
                Assert.Less(plan.U(cook), MinAlong(body, plan.Street), "the cook is on the aisle side of the serving counter");
                Assert.Greater(plan.U(first), MaxAlong(body, plan.Street), "the guest is on the street side of the serving counter");
                Assert.That(plan.V(first), Is.InRange(MinAlong(body, plan.Right), MaxAlong(body, plan.Right)), "the guest waits across the serving counter");

                // Fries are served: the fries order is picked and pays; the guest leaves, the next one moves up.
                Assert.IsTrue(queue.TryPick(d => OrderRules.Match(MenuKitchen.ExactFries(), Menu.ById(d)), out QueuedCustomer picked, out OrderMatch match));
                Assert.AreEqual(41, picked.Id);
                Assert.AreEqual(OrderMatch.Exact, match);
                Assert.Greater(OrderRules.PayPermille(match, 4), 0);
                Assert.IsTrue(queue.Remove(41));
                Assert.AreEqual(head, CustomerRules.QueueSlot(head, direction, queue.IndexOf(42)), "the second guest moves up to the head");
                Assert.AreNotEqual(first, second);

                // The way out is the way in: back to the street entry, east of the shop and never through the aisle.
                foreach (Vector3 p in Along(first, entry)) Assert.IsFalse(plan.InAisle(p, GuestRadius), "the leaving guest crosses the aisle at " + p);
                Assert.Greater(plan.V(entry), plan.AisleRight, "the street entry lies beyond the shop's closed end");
                Assert.Greater(plan.U(entry), plan.CounterStreetSide, "the street entry is on the street side");
            }
            finally { Close(plan); }
        }
    }
}
