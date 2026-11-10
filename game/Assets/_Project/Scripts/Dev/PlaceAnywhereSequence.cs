using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Object = UnityEngine.Object;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 039, the look tour's place-anywhere part (first person, on the tour client only): the three tools at their homes
    /// (handles into the aisle); the knife picked up and put down and picked up again on the new tops (both front counter runs,
    /// beside the drink dispenser, beside the register, the fryer deck, the plate shelf, the assembly board's back, the sink's
    /// front, a stove's front), each time a shot of the ghost before the click, of the knife lying there (its handle towards the
    /// cook) and after the pick-up; the knife back home; then a tomato dropped in the aisle and walked over in third person
    /// (frames, and the highest the player got logged: it must stay on the floor).
    /// </summary>
    public static class PlaceAnywhereSequence
    {
        private const float AisleZ = TourRules.AisleZ;
        /// <summary>Spec 041 AC3: the top put down on with a right click, and the one turned with the wheel first (two notches).</summary>
        private const string RightClickTop = "front-west";
        private const string WheelTop = "beside-drinks";
        private const int WheelSteps = 2;

        /// <summary>The tops to try: the surface's name, its number among those with that name (by index), and the world x to aim near.</summary>
        private static readonly (string Surface, int Nth, float X, string Shot)[] Tops =
        {
            ("Counter top 1", 0, 6.0f, "front-west"),
            ("Counter top 2", 0, 9.9f, "beside-drinks"),
            ("Counter top 3", 0, 13.4f, "beside-register"),
            ("Counter top 4", 0, 14.4f, "front-east"),
            ("Stove front", 0, 7.5f, "stove-front"),
            ("Fryer deck", 0, 6.44f, "fryer-deck"),
            ("Assembly back", 0, 10.55f, "assembly-back"),
            ("Plates front", 0, 11.64f, "plate-shelf"),
            ("Sink front", 0, 12.9f, "sink-front"),
        };

        public static IEnumerator Run(TourActor actor, CookCapture capture)
        {
            PickupInteractor me = actor.Interactor;
            Knife knife = Object.FindFirstObjectByType<Knife>();
            Spatula spatula = Object.FindFirstObjectByType<Spatula>();
            WipeCloth cloth = Object.FindFirstObjectByType<WipeCloth>();
            if (knife == null)
            {
                actor.CountTimeout("place anywhere: no knife");
                yield break;
            }
            yield return actor.LevelView();
            // The tools at home: the handle into the aisle.
            foreach (Component tool in new Component[] { knife, spatula, cloth })
            {
                if (tool == null) continue;
                Vector3 at = tool.transform.position;
                yield return actor.WalkTo(new Vector3(at.x, 0f, AisleZ), false, false);
                yield return actor.AimAt(at, 1f);
                yield return capture.Shot("home-" + tool.name.ToLowerInvariant().Replace(' ', '-'));
                yield return actor.LevelView();
            }

            Vector3 home = knife.transform.position;
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(home.x, 0f, AisleZ), false, false);
            yield return PickUp(actor, capture, knife.GetComponent<Pickup>(), "knife", null);
            if (!(me.FindHeldPickup() is Pickup inHand) || !inHand.TryGetComponent(out Knife _))
                Debug.LogWarning("[Tour] place anywhere: the hands hold " + (me.FindHeldPickup() != null ? me.FindHeldPickup().name : "nothing")
                    + ", not the knife");
            foreach ((string name, int nth, float x, string shot) in Tops)
            {
                PlaceSurface surface = Find(name, nth);
                if (surface == null)
                {
                    actor.CountTimeout("place anywhere: no surface '" + name + "'");
                    continue;
                }
                int spot = NearestSpot(surface, new Vector3(x, 0f, surface.transform.position.z));
                Vector3 spotAt = surface.SpotWorldPosition(spot);
                yield return actor.LevelView();
                yield return actor.WalkTo(new Vector3(spotAt.x, 0f, AisleZ), false, false);
                Pickup held = me.FindHeldPickup();
                int turn = 0;
                if (shot == RightClickTop || shot == WheelTop)
                {
                    // Spec 041 AC3: the right click puts it down where the ghost shows; on the second top the wheel turns it first.
                    if (shot == WheelTop)
                    {
                        yield return actor.AimAt(spotAt, 3f, () => me.PutDownSurface == surface);
                        turn = WheelSteps;
                        actor.Intents.Turn(turn);
                        yield return null;
                        yield return null;
                        Debug.Log("[Tour] mouse controls: the wheel turned the knife's rest " + me.RestTurn + " steps of " + PlaceTurn.StepDegrees
                            + " degrees");
                        if (me.RestTurn != PlaceTurn.Add(0, turn)) actor.CountTimeout("mouse controls: the wheel did not turn the rest");
                    }
                    yield return actor.PutDown("right click: the knife on " + name + " spot " + spot, spotAt, true,
                        () => me.DevSendPlace(surface.Index, PlaceTurn.Pack(spot, turn)), capture.Before("rightclick-" + shot, null, 0f));
                }
                else
                    yield return actor.Use("put the knife on " + name + " spot " + spot, spotAt, InteractionAction.Place,
                        () => me.DevSendPlace(surface.Index, spot), capture.Before("place-" + shot, null, 0f));
                yield return actor.AimAt(spotAt + Vector3.up * 0.15f, 0.5f);
                yield return capture.Shot("placed-" + shot);
                if (held != null) LogRest(held, actor.Position, name, turn);
                if (held != null) yield return PickUp(actor, capture, held, "the knife from " + name, "picked-" + shot);
            }

            // The knife back home (its spot on the prep worktop).
            PlaceSurface prep = SurfaceUnder(home);
            if (prep != null && me.FindHeldPickup() != null)
            {
                int spot = NearestSpot(prep, home);
                yield return actor.LevelView();
                yield return actor.WalkTo(new Vector3(home.x, 0f, AisleZ), false, false);
                yield return actor.Use("put the knife back home", prep.SpotWorldPosition(spot), InteractionAction.Place,
                    () => me.DevSendPlace(prep.Index, spot));
            }

            yield return TomatoWalk(actor, capture);
            actor.Intents.View = ViewMode.FirstPerson;
            yield return actor.LevelView();
        }

        private static IEnumerator PickUp(TourActor actor, CookCapture capture, Pickup item, string what, string shot)
        {
            if (item == null) yield break;
            yield return actor.Use("pick up " + what, item.transform.position + Vector3.up * 0.03f, InteractionAction.PickUp,
                () => actor.Interactor.DevSendPickup(item.NetworkObject));
            if (shot != null) yield return capture.Shot(shot);
        }

        /// <summary>Logs how the tool came to rest: the angle between its handle and the way to the player (0 = towards the cook); spec
        /// 041: with a wheel turn, the expected angle (the turn's degrees, folded into 0..180).</summary>
        private static void LogRest(Pickup item, Vector3 player, string where, int turn)
        {
            Vector3 to = player - item.transform.position;
            to.y = 0f;
            float angle = Vector3.Angle(to, ToolRest.HandleDirection(item.transform.rotation));
            float expected = Mathf.Abs(Mathf.DeltaAngle(0f, PlaceTurn.Add(0, turn) * PlaceTurn.StepDegrees));
            Debug.Log("[Tour] place anywhere: " + item.name + " on " + where + " at " + item.transform.position.ToString("F2")
                + ", handle " + angle.ToString("0") + " degrees off the cook"
                + (turn != 0 ? " (wheel turn " + turn + " steps: expected about " + expected.ToString("0") + ")" : string.Empty));
        }

        /// <summary>
        /// A tomato from the fridge dropped in the aisle, then walked over in third person with frames; the highest the player's
        /// root got during the walk is logged (it must stay on the floor: items are never stairs).
        /// </summary>
        private static IEnumerator TomatoWalk(TourActor actor, CookCapture capture)
        {
            PickupInteractor me = actor.Interactor;
            StockSlot tomato = null;
            foreach (StockSlot s in Object.FindObjectsByType<StockSlot>(FindObjectsSortMode.None))
                if ((s.IngredientId == "tomato" || (tomato == null && s.IngredientId == "cucumber")) && s.Station != null) tomato = s;
            if (tomato == null)
            {
                actor.CountTimeout("place anywhere: no tomato slot");
                yield break;
            }
            yield return actor.LevelView();
            yield return actor.WalkTo(new Vector3(tomato.Anchor.x + 1f, 0f, AisleZ), false, false);
            yield return actor.Use("take a " + tomato.IngredientId, tomato.Anchor, InteractionAction.Buy,
                () => me.DevSendStation(tomato.Station.Index, tomato.Option));
            yield return actor.LevelView();
            // Face east along the aisle and drop it about 1.2 m ahead.
            yield return actor.WalkTo(new Vector3(4.6f, 0f, AisleZ), false, false);
            yield return actor.WalkTo(new Vector3(5.0f, 0f, AisleZ), false, false);
            yield return actor.Look(90f, -10f, 1f); // facing east, along the aisle
            Pickup dropped = me.FindHeldPickup();
            // Spec 041 AC3: a right click aimed at the floor (no counter spot) drops it at the feet.
            Vector3 floor = actor.Position + Vector3.right * 2.5f;
            floor.y = 0f;
            yield return actor.PutDown("right click: drop the " + tomato.IngredientId + " on the floor", floor, false, me.DevDrop,
                capture.Before("rightclick-drop", "rightclick-drop-fall", 0.8f));
            if (dropped == null)
            {
                actor.CountTimeout("place anywhere: nothing to drop");
                yield break;
            }
            Vector3 lies = dropped.transform.position;
            Debug.Log("[Tour] place anywhere: the " + tomato.IngredientId + " lies at " + lies.ToString("F2"));
            yield return actor.WalkTo(new Vector3(lies.x - 2.2f, 0f, lies.z), false, false);
            actor.Intents.View = ViewMode.ThirdPerson;
            yield return actor.Look(90f, -14f, 1.5f);
            yield return capture.Shot("tomato-floor");
            float ground = actor.Position.y, highest = ground;
            yield return capture.Frames("tomato-walk", 3.0f);
            IEnumerator walk = actor.WalkTo(new Vector3(lies.x + 2.2f, 0f, lies.z), false, false);
            while (walk.MoveNext())
            {
                highest = Mathf.Max(highest, actor.Position.y);
                yield return walk.Current;
            }
            highest = Mathf.Max(highest, actor.Position.y);
            Debug.Log("[Tour] place anywhere: walked over the " + tomato.IngredientId + " from x " + (lies.x - 2.2f).ToString("0.0") + " to "
                + actor.Position.x.ToString("0.0") + ", ground y " + ground.ToString("0.000") + ", highest y " + highest.ToString("0.000")
                + (highest > ground + 0.02f ? " (CLIMBED)" : " (stayed on the floor)"));
            yield return actor.Look(270f, -14f, 1.5f);
            yield return capture.Shot("tomato-behind");
        }

        private static PlaceSurface Find(string name, int nth)
        {
            var found = new List<PlaceSurface>();
            foreach (PlaceSurface s in Object.FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
                if (s.name == name) found.Add(s);
            found.Sort((a, b) => a.Index.CompareTo(b.Index));
            return nth < found.Count ? found[nth] : null;
        }

        private static PlaceSurface SurfaceUnder(Vector3 position)
        {
            foreach (PlaceSurface s in Object.FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
                for (int i = 0; i < s.SpotCount; i++)
                {
                    Vector3 d = position - s.SpotWorldPosition(i);
                    if (new Vector2(d.x, d.z).magnitude < 0.1f && d.y > -0.05f && d.y < 0.3f) return s;
                }
            return null;
        }

        private static int NearestSpot(PlaceSurface surface, Vector3 world)
        {
            return Math.Max(0, PlaceSpots.NearestFree(surface.Size, surface.ToTopPlane(world), null));
        }
    }
}
