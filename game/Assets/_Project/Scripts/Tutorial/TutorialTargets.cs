using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, client: the scene objects behind each <see cref="TutorialTarget"/> (a stock slot, a station, a button, a piece of cookware,
    /// the knife). Looked up once and again only when one is gone (FishNet respawns scene objects on a restart); the plate is the free plate
    /// nearest the player, else the plate shelf.
    /// </summary>
    public sealed class TutorialTargets
    {
        private StockSlot _potato, _wrap, _falafel, _lettuce;
        private Vessel _board, _assembly;
        private Knife _knife;
        private Station _fryer, _assemblyStation, _counter, _register, _drinks, _dishReturn, _sink, _terminal, _spit, _plateShelf;
        private StationButton _fryerPower, _spitKnob;
        private readonly StationButton[] _fryerButtons = new StationButton[FryerRules.BasketsPerFryer];
        private readonly Vessel[] _baskets = new Vessel[FryerRules.BasketsPerFryer];
        private float _nextLookup;
        private bool _found;

        public Vessel Board => _board;
        public Vessel Assembly => _assembly;
        public Station Fryer => _fryer;
        public Station Register => _register;
        public Station Sink => _sink;
        public Station DishReturn => _dishReturn;
        public Station Spit => _spit;

        /// <summary>The basket <paramref name="i"/> of the fryer (0 or 1), or null.</summary>
        public Vessel Basket(int i) => i >= 0 && i < _baskets.Length ? _baskets[i] : null;

        /// <summary>Finds the objects (at most once a second while one is missing).</summary>
        public void Refresh(float now)
        {
            if (_found && _board != null && _assembly != null && _fryer != null && _counter != null && _baskets[0] != null) return;
            if (now < _nextLookup) return;
            _nextLookup = now + 1f;
            foreach (StockSlot slot in Object.FindObjectsByType<StockSlot>(FindObjectsSortMode.None))
            {
                if (slot.Station == null) continue;
                switch (slot.IngredientId)
                {
                    case "potato": _potato = slot; break;
                    case "wrap": _wrap = slot; break;
                    case "falafel_mix": _falafel = slot; break;
                    case "lettuce": _lettuce = slot; break;
                }
            }
            foreach (Station s in Object.FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                switch (s.Kind)
                {
                    case StationKind.Fryer: _fryer = s; break;
                    case StationKind.Assembly: _assemblyStation = s; break;
                    case StationKind.Counter: _counter = s; break;
                    case StationKind.Register: _register = s; break;
                    case StationKind.DrinkDispenser: _drinks = s; break;
                    case StationKind.DishReturn: _dishReturn = s; break;
                    case StationKind.Sink: _sink = s; break;
                    case StationKind.Terminal: _terminal = s; break;
                    case StationKind.Spit: _spit = s; break;
                    case StationKind.PlateShelf: _plateShelf = s; break;
                }
            }
            foreach (Vessel v in Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (v.Kind == VesselKind.Board) _board = v;
                else if (v.Kind == VesselKind.Assembly) _assembly = v;
            }
            foreach (FryerBasket fb in Object.FindObjectsByType<FryerBasket>(FindObjectsSortMode.None))
                if (fb.Fryer == _fryer && fb.Basket >= 0 && fb.Basket < _baskets.Length) _baskets[fb.Basket] = fb.GetComponent<Vessel>();
            foreach (StationButton b in Object.FindObjectsByType<StationButton>(FindObjectsSortMode.None))
            {
                if (b.Station == null) continue;
                if (b.Station == _fryer)
                {
                    if (b.Option == FryerRules.PowerOption) _fryerPower = b;
                    else if (FryerRules.TryParseBasketOption(b.Option, out int basket, out bool lower) && lower && basket < _fryerButtons.Length)
                        _fryerButtons[basket] = b;
                }
                else if (b.Station == _spit && b.Option == SpitRules.KnobOption) _spitKnob = b;
            }
            _knife = Object.FindFirstObjectByType<Knife>();
            _found = true;
        }

        /// <summary>The object to light up for <paramref name="target"/> (null: none or not found). <paramref name="basket"/>: the basket in use.</summary>
        public Component Resolve(TutorialTarget target, int basket, Vector3 player)
        {
            switch (target)
            {
                case TutorialTarget.PotatoSlot: return _potato;
                case TutorialTarget.CuttingBoard: return _board;
                case TutorialTarget.Knife: return _knife;
                case TutorialTarget.FryerPower: return _fryerPower;
                case TutorialTarget.FryerBasket: return Basket(basket) ?? Basket(0);
                case TutorialTarget.FryerButton: return basket >= 0 && basket < _fryerButtons.Length && _fryerButtons[basket] != null ? _fryerButtons[basket] : _fryerButtons[0];
                case TutorialTarget.Plate: return NearestPlate(player);
                case TutorialTarget.WrapSlot: return _wrap;
                case TutorialTarget.FalafelSlot: return _falafel;
                case TutorialTarget.LettuceSlot: return _lettuce;
                case TutorialTarget.AssemblyBoard: return _assembly;
                case TutorialTarget.Sauce: return Slot(_assemblyStation, 0);
                case TutorialTarget.WrapPaper: return Slot(_assemblyStation, BuildRules.DispenserCount + BuildRules.Paper);
                case TutorialTarget.Counter: return _counter;
                case TutorialTarget.Register: return _register;
                case TutorialTarget.CupStack: return Slot(_drinks, DrinkRules.CupTool);
                case TutorialTarget.LemonadeTap: return Slot(_drinks, 0);
                case TutorialTarget.LidStack: return Slot(_drinks, DrinkRules.LidTool);
                case TutorialTarget.DishReturn: return _dishReturn;
                case TutorialTarget.Sink: return _sink;
                case TutorialTarget.Terminal: return _terminal;
                case TutorialTarget.SpitKnob: return _spitKnob != null ? (Component)_spitKnob : _spit;
                case TutorialTarget.SpitCone: return Slot(_spit, SpitRules.ConeTool);
                default: return null;
            }
        }

        private static Component Slot(Station station, int option)
        {
            if (station == null) return null;
            return station.TryGetSlot(option, out StockSlot slot) && slot != null ? (Component)slot : station;
        }

        /// <summary>The free plate (not held, nothing in it) nearest <paramref name="player"/>, else the plate shelf.</summary>
        private Component NearestPlate(Vector3 player)
        {
            Vessel best = null;
            float bestDistance = float.MaxValue;
            foreach (Vessel v in Object.FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (v.Kind != VesselKind.Plate || v.ContentCount > 0) continue;
                if (v.TryGetComponent(out Worktown.Game.Net.Pickup p) && p.IsHeld) continue;
                float d = (v.transform.position - player).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = v;
                }
            }
            return best != null ? (Component)best : _plateShelf;
        }
    }
}
