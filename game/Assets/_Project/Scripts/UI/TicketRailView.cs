using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Npc;
using Worktown.Sim.Cooking;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 022: the ticket rail board above the counter, a global scene object. The server writes the open orders
    /// (dish id and minutes waited, packed by <see cref="CustomerRules.PackTicket"/>) in queue order and the shift
    /// numbers; every client draws the cards and the HUD and the summary panel read the values. No RPCs; only dish ids,
    /// coarse minutes and whole numbers leave the server (D21).
    /// Spec 032: a ticket's top byte carries the order's modifiers; each card shows the dish on top, its modifiers in plain words
    /// ("+ extra garlic", "no lettuce" with a line through it) and the minutes at the bottom. The summary's new numbers (tips,
    /// reviews, missed by reason) ride on two <see cref="ShiftReport"/> SyncVars (the open shift and the last one).
    /// </summary>
    public sealed class TicketRailView : NetworkBehaviour
    {
        private static readonly Color TitleOpen = Palette.Mustard; // spec 026
        private static readonly Color TitleClosed = Palette.Chrome;
        /// <summary>Spec 032: modifier lines per card.</summary>
        public const int ModLines = 2;

        public readonly SyncList<int> Tickets = new SyncList<int>();
        public readonly SyncVar<bool> ShiftOpen = new SyncVar<bool>(false);
        public readonly SyncVar<bool> SummaryOpen = new SyncVar<bool>(false);
        public readonly SyncVar<int> Day = new SyncVar<int>(0);
        // The open shift so far.
        public readonly SyncVar<int> LiveServed = new SyncVar<int>(0);
        public readonly SyncVar<int> LiveEarned = new SyncVar<int>(0);
        public readonly SyncVar<int> LiveStarsTenths = new SyncVar<int>(0);
        public readonly SyncVar<int> LiveMissed = new SyncVar<int>(0);
        // The last finished shift (the summary); LastDay 0 = none yet.
        public readonly SyncVar<int> LastDay = new SyncVar<int>(0);
        public readonly SyncVar<int> LastServed = new SyncVar<int>(0);
        public readonly SyncVar<int> LastEarned = new SyncVar<int>(0);
        public readonly SyncVar<int> LastStarsTenths = new SyncVar<int>(0);
        public readonly SyncVar<int> LastMissed = new SyncVar<int>(0);
        // Spec 032: tips, reviews and missed guests by reason, the open shift and the last one.
        public readonly SyncVar<ShiftReport> LiveReport = new SyncVar<ShiftReport>(default);
        public readonly SyncVar<ShiftReport> LastReport = new SyncVar<ShiftReport>(default);

        [SerializeField] private TextMesh _title;
        [Tooltip("One text per card, in rail order: the dish, (spec 032) its modifier lines, the minutes.")]
        [SerializeField] private TextMesh[] _cards = new TextMesh[0];
        [Tooltip("The card backs, same order as the texts.")]
        [SerializeField] private Renderer[] _cardBacks = new Renderer[0];
        [Tooltip("Spec 032: the line through each \"no X\" line, two per card (card i: 2i and 2i+1).")]
        [SerializeField] private Renderer[] _strikes = new Renderer[0];

        private int _strikeFrames;
        private float _nextStrike;
        // Spec 032: per card, the modifier lines as shown and which of them are struck through.
        private readonly string[] _modText = new string[CustomerRules.MaxVisible * ModLines];
        private readonly bool[] _struck = new bool[CustomerRules.MaxVisible * ModLines];

        /// <summary>Spec 032 (tests and the tour log): the text of modifier line <paramref name="line"/> of card <paramref name="card"/>, and
        /// whether it is struck through.</summary>
        public string ModText(int card, int line, out bool struck)
        {
            int i = card * ModLines + line;
            struck = i < _struck.Length && _struck[i];
            return i < _modText.Length && _modText[i] != null ? _modText[i] : string.Empty;
        }

        private void Awake()
        {
            Tickets.OnChange += (_, _, _, _, _) => Refresh();
            ShiftOpen.OnChange += (_, _, _) => Refresh();
        }

        public override void OnStartClient()
        {
            Refresh();
        }

        /// <summary>Server: the open orders in queue order; only changed entries are sent.</summary>
        internal void ServerSetTickets(IReadOnlyList<int> tickets)
        {
            for (int i = 0; i < tickets.Count; i++)
            {
                if (i < Tickets.Count)
                {
                    if (Tickets[i] != tickets[i]) Tickets[i] = tickets[i];
                }
                else
                {
                    Tickets.Add(tickets[i]);
                }
            }
            while (Tickets.Count > tickets.Count) Tickets.RemoveAt(Tickets.Count - 1);
            Refresh();
        }

        /// <summary>Server: the shift state and its numbers (only changed values are sent).</summary>
        internal void ServerSetShift(Shift shift)
        {
            ShiftOpen.Value = shift.IsOpen;
            SummaryOpen.Value = shift.SummaryShowing;
            Day.Value = (int)shift.Day;
            LiveServed.Value = shift.Live.Served;
            LiveEarned.Value = Clamp(shift.Live.Earned);
            LiveStarsTenths.Value = shift.Live.AverageStarsTenths;
            LiveMissed.Value = shift.Live.Missed;
            LastDay.Value = (int)shift.LastDay;
            LastServed.Value = shift.Last.Served;
            LastEarned.Value = Clamp(shift.Last.Earned);
            LastStarsTenths.Value = shift.Last.AverageStarsTenths;
            LastMissed.Value = shift.Last.Missed;
            ShiftReport live = ShiftReport.From(shift.Live), last = ShiftReport.From(shift.Last);
            if (!LiveReport.Value.Equals(live)) LiveReport.Value = live;
            if (!LastReport.Value.Equals(last)) LastReport.Value = last;
            Refresh();
        }

        private static int Clamp(long value) => value > int.MaxValue ? int.MaxValue : (int)System.Math.Max(0L, value);

        private void Refresh()
        {
            if (_title != null)
            {
                _title.text = ShiftOpen.Value ? "ORDERS" : "KITCHEN CLOSED";
                _title.color = ShiftOpen.Value ? TitleOpen : TitleClosed;
            }
            for (int i = 0; i < _cards.Length; i++)
            {
                bool used = i < Tickets.Count;
                int ticket = used ? Tickets[i] : 0;
                IReadOnlyList<OrderModifier> mods = used
                    ? CustomerRules.ModifiersOf(CustomerRules.TicketDish(ticket), CustomerRules.TicketModifiers(ticket))
                    : System.Array.Empty<OrderModifier>();
                for (int j = 0; j < ModLines; j++)
                {
                    int k = i * ModLines + j;
                    if (k >= _modText.Length) continue;
                    bool on = j < mods.Count;
                    _modText[k] = on ? CustomerRules.TicketLine(mods[j]) : null;
                    _struck[k] = on && CustomerRules.IsStruck(mods[j]);
                    if (k < _strikes.Length && _strikes[k] != null) _strikes[k].gameObject.SetActive(used && _struck[k]);
                }
                if (_cards[i] != null)
                {
                    _cards[i].gameObject.SetActive(used);
                    if (used) _cards[i].text = CardText(ticket, i);
                }
                if (i < _cardBacks.Length && _cardBacks[i] != null) _cardBacks[i].gameObject.SetActive(used);
            }
            _strikeFrames = 2; // the struck lines are placed once the text is laid out
        }

        /// <summary>
        /// Spec 032: "Falafel wrap / + extra garlic / no lettuce / 12 min" (022's two lines without modifiers); "extra" lines in Leaf,
        /// "no" lines in Terracotta (rich text with the palette's colours), struck through by their line.
        /// </summary>
        private string CardText(int ticket, int card)
        {
            string text = Menu.NameOf(CustomerRules.TicketDish(ticket));
            // Spec 033: a dish handed over already is shown done (Sage); the open drink line follows the modifiers.
            if (CustomerRules.TicketDishServed(ticket)) text = "<color=#" + DoneColour + ">" + text + "</color>";
            for (int j = 0; j < ModLines; j++)
            {
                int k = card * ModLines + j;
                string mod = _modText[k];
                if (mod == null) continue;
                text += "\n<color=#" + (_struck[k] ? NoColour : ExtraColour) + ">" + mod + "</color>";
            }
            string drink = CustomerRules.DrinkLine(CustomerRules.TicketDrink(ticket));
            if (drink.Length > 0) text += "\n<color=#" + DrinkColour + ">" + drink + "</color>";
            return text + "\n" + CustomerRules.TicketMinutes(ticket) + " min";
        }

        private static readonly string DoneColour = ColorUtility.ToHtmlStringRGB(Palette.Ground);
        private static readonly string DrinkColour = ColorUtility.ToHtmlStringRGB(Palette.DeepBlue);

        private static readonly string ExtraColour = ColorUtility.ToHtmlStringRGB(Palette.Leaf);
        private static readonly string NoColour = ColorUtility.ToHtmlStringRGB(Palette.Terracotta);

        /// <summary>
        /// Spec 032: each line through a "no X" spans its words: the line's width from the font's glyph advances, scaled by the text's
        /// drawn width (so it fits any word and any label scale), its height from the font's line height; the card text is centred
        /// (MiddleCenter), so line L of N sits (N/2 - L - 1/2) lines above the text's middle. Once after a change, then four times a
        /// second (the label manager may scale the rail's text with distance).
        /// </summary>
        private void LateUpdate()
        {
            if (_strikeFrames > 0)
            {
                if (--_strikeFrames > 0) return;
            }
            else if (Time.unscaledTime < _nextStrike) return;
            _nextStrike = Time.unscaledTime + 0.25f;
            for (int card = 0; card < _cards.Length; card++)
            {
                TextMesh text = _cards[card];
                if (text == null || !text.gameObject.activeInHierarchy) continue;
                for (int j = 0; j < ModLines; j++)
                {
                    int k = card * ModLines + j;
                    if (k >= _strikes.Length || _strikes[k] == null || !_strikes[k].gameObject.activeSelf) continue;
                    PlaceStrike(text, j + 1, _strikes[k].transform);
                }
            }
        }

        private void PlaceStrike(TextMesh text, int line, Transform strike)
        {
            Font font = text.font;
            if (font == null || !text.TryGetComponent(out MeshRenderer renderer)) return;
            string s = text.text;
            int size = text.fontSize > 0 ? text.fontSize : font.fontSize;
            font.RequestCharactersInTexture(s, size, text.fontStyle);
            int lines = 1, current = 0, widest = 0, lineWidth = 0, width = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                if (i == s.Length || s[i] == '\n')
                {
                    if (current == line) lineWidth = width;
                    if (width > widest) widest = width;
                    if (i < s.Length) lines++;
                    current++;
                    width = 0;
                    continue;
                }
                if (s[i] == '<') // a rich text tag draws nothing
                {
                    int close = s.IndexOf('>', i);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }
                if (font.GetCharacterInfo(s[i], out CharacterInfo info, size, text.fontStyle)) width += info.advance;
            }
            if (widest <= 0 || lineWidth <= 0) return;
            Transform root = transform;
            Bounds b = renderer.bounds;
            float drawnWidth = Mathf.Abs(root.InverseTransformVector(new Vector3(b.size.x, 0f, b.size.z)).x);
            float perPixel = drawnWidth / widest;
            float lineHeight = (font.lineHeight > 0 ? font.lineHeight * (float)size / Mathf.Max(1, font.fontSize) : size) * text.lineSpacing * perPixel;
            Vector3 centre = root.InverseTransformPoint(text.transform.position);
            float y = centre.y + (lines * 0.5f - line - 0.5f) * lineHeight;
            Vector3 local = strike.localPosition;
            strike.localPosition = new Vector3(centre.x, y, local.z);
            strike.localScale = new Vector3(lineWidth * perPixel + 0.02f, strike.localScale.y, 1f);
        }
    }
}
