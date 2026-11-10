using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038: what the local player can observe right now, as plain data for the pure <see cref="TutorialSteps"/>: how far it walked
    /// and turned since the tutorial started, what its hands hold, and the synced state of the kitchen's stations (the cutting board,
    /// the fryer basket, the assembly board, the register). Only what every client sees anyway: nothing here is decided by the client,
    /// and nothing the tutorial does changes a rule (AGENTS rule 6). Strings and lists are references to the synced values (no copies).
    /// </summary>
    public struct TutorialSnapshot
    {
        /// <summary>Metres walked since the tutorial started.</summary>
        public float Moved;
        /// <summary>Degrees the view turned since the tutorial started (yaw, summed).</summary>
        public float Turned;
        /// <summary>What the hands hold, in either view.</summary>
        public Held Held;
        /// <summary>The cutting board's contents ("potato sticks"), empty when it is empty.</summary>
        public string BoardTitle;
        /// <summary>The fryer basket in use (the first with something in it): its contents, mode byte and heat zone.</summary>
        public string BasketTitle;
        public byte BasketMode;
        public HeatZone BasketZone;
        /// <summary>The fryer's power is on.</summary>
        public bool FryerOn;
        /// <summary>The assembly board's names (the take name; null or empty: an empty board) and its build mode byte (BuildRules).</summary>
        public IReadOnlyList<string> BuildLayers;
        /// <summary>The assembly board's layer count (its looks: the names hold only the take name, "Falafel wrap" once it matches).</summary>
        public int BuildCount;
        public byte BuildMode;
        /// <summary>The register's payment short (Shop.RegisterRules): somebody waits to pay.</summary>
        public ushort Payment;
        /// <summary>Dirty plates in the sink's basin and waiting at the dish return.</summary>
        public int SinkBasin;
        public int DirtyWaiting;
        /// <summary>The spit's byte (SpitRules: the level bits 0-1) and its cone is loaded.</summary>
        public byte SpitFlags;
        /// <summary>Spec 042: the cup on the lemonade tap's spot as this client sees it (DrinkRules.SpotByte; no cup: 0).</summary>
        public byte LemonadeSpot;
        /// <summary>What happened since the last snapshot (<see cref="TutorialWatch"/>).</summary>
        public TutorialEvents Events;
    }
}
