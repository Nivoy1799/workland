using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Worktown.Game.Player;

namespace Worktown.Game.UI
{
    /// <summary>
    /// "What's new" banner (spec 011): the newest entry of Resources/WhatsNew.json with what is new
    /// and what to test. Opens on start once per build, Enter or the button closes it, F2 reopens it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class WhatsNewView : MonoBehaviour
    {
        private const string ResourceName = "WhatsNew";

        [SerializeField] private StyleSheet _style;

        private VisualElement _backdrop;
        private WhatsNewEntry _newest;
        private bool _open;
        private bool _reportedOpen;
        private PlayerInputReader _input;

        private void Start()
        {
            if (Application.isBatchMode) return;
            var asset = Resources.Load<TextAsset>(ResourceName);
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(asset != null ? asset.text : null);
            _newest = WhatsNewRules.Newest(entries);
            if (_newest == null) return;

            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);
            _backdrop = BuildBanner(_newest);
            documentRoot.Add(_backdrop);

            string seen = SafeGetSeen();
            SetOpen(WhatsNewRules.ShouldShowOnStart(_newest, seen));
        }

        private void OnDisable()
        {
            _backdrop?.RemoveFromHierarchy();
            _backdrop = null;
            Report(false);
        }

        private void Update()
        {
            if (_backdrop == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f2Key.wasPressedThisFrame) SetOpen(!_open);
            else if (_open && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Close();
            // The player may spawn after the banner opened: free the cursor once input exists.
            if (_open && !_reportedOpen) Report(true);
        }

        private void Close()
        {
            SetOpen(false);
            try
            {
                PlayerPrefs.SetString(WhatsNewRules.SeenKey, _newest.id);
                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException e)
            {
                Debug.LogWarning("[WhatsNew] could not store seen id: " + e.Message);
            }
        }

        private void SetOpen(bool open)
        {
            _open = open;
            _backdrop.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            Report(open);
        }

        private void Report(bool open)
        {
            if (open == _reportedOpen) return;
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            if (_input == null) return;
            _reportedOpen = open;
            _input.SetUiOpen(open);
        }

        private static string SafeGetSeen()
        {
            try { return PlayerPrefs.GetString(WhatsNewRules.SeenKey, string.Empty); }
            catch (PlayerPrefsException) { return string.Empty; }
        }

        private VisualElement BuildBanner(WhatsNewEntry entry)
        {
            var backdrop = new VisualElement();
            backdrop.AddToClassList("banner-backdrop");

            var banner = new VisualElement();
            banner.AddToClassList("panel");
            banner.AddToClassList("banner");
            backdrop.Add(banner);

            AddLabel(banner, "banner-kicker", "WHAT'S NEW  ·  " + entry.date);
            AddLabel(banner, "banner-title", entry.title);
            AddLabel(banner, "banner-heading", "New");
            foreach (string item in entry.items) AddLabel(banner, "banner-item", "•  " + item);
            AddLabel(banner, "banner-heading", "Please test");
            foreach (string item in entry.test) AddLabel(banner, "banner-item", "☐  " + item);
            AddLabel(banner, "banner-footer", "Enter or the button closes this.  F2 opens it again.  F1 debug menu.");
            banner.Add(new Button(Close) { text = "Got it" });
            return backdrop;
        }

        private static void AddLabel(VisualElement parent, string className, string text)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            parent.Add(label);
        }
    }
}
