using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048: loads the player's settings at start (SettingsStore; a scripted dev run gets the defaults), makes them current, and
    /// carries out what the engine needs whenever they change (GameSettings.Version): the quality preset (render scale, shadows), the
    /// resolution and window mode (only once the player picked them, never in a scripted run), vsync and the frame cap (kept every frame:
    /// FishNet sets its own 500 fps cap whenever a server or client starts, spec 045), the master volume (AudioListener), the UI scale
    /// (the UI's PanelSettings) and motion blur (a global URP volume, made only once it is turned on). The camera rig, the input readers
    /// and the world labels read the rest from GameSettings.Current. <see cref="Save"/> writes the current settings (the settings
    /// screen calls it when it closes or a binding changes). Off in batch mode (the headless server and the load-test bots).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class SettingsApplier : MonoBehaviour
    {
        private static SettingsApplier _instance;

        private SettingsStore _store;
        private int _applied = -1;
        private int _targetFrameRate = -1;
        private int _vSyncCount;
        private UniversalRenderPipelineAsset _pipeline;
        private RenderPipelineAsset _qualityPipelineBefore;
        private bool _ownPipeline;
        private float _originalRenderScale;
        private float _originalShadowDistance;
        private int _originalCascades;
        private PanelSettings _panel;
        private float _originalPanelScale = 1f;
        private Volume _blurVolume;
        private VolumeProfile _blurProfile;
        private bool _displayApplied;
        private WindowMode _lastWindow;
        private int _lastWidth;
        private int _lastHeight;

        /// <summary>The settings are stored on this PC (false in a scripted run: changes stay in memory).</summary>
        public static bool Persistent => _instance != null && _instance._store != null && _instance._store.Persistent;

        /// <summary>Writes <see cref="GameSettings.Current"/> to this PC (nothing in a scripted run). False when nothing was written.</summary>
        public static bool Save()
        {
            if (_instance == null || _instance._store == null) return false;
            bool saved = _instance._store.Save(GameSettings.Current);
            Debug.Log(saved ? "[Settings] saved" : "[Settings] not saved (" + (_instance._store.Persistent ? "write failed" : "scripted run") + ")");
            return saved;
        }

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            _instance = this;
            bool persistent = SettingsRules.UsesSavedSettings(Net.Rules.LaunchArgs.Parse(Environment.GetCommandLineArgs()));
            _store = new SettingsStore(new PlayerPrefsSettings(), persistent);
            GameSettings.Use(_store.Load());
            if (!persistent) Debug.Log("[Settings] scripted run: the defaults, nothing is read or stored");
            _lastWindow = SettingsRules.FromFullScreenMode(Screen.fullScreenMode);
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            UIDocument document = GetComponent<UIDocument>();
            _panel = document != null ? document.panelSettings : null;
            if (_panel != null) _originalPanelScale = _panel.scale;
            // 048 review: never change the project's pipeline asset (in the Editor that would dirty it on disk). A copy is made and
            // used for this quality level instead; the presets change the copy.
            var shared = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (shared != null)
            {
                _qualityPipelineBefore = QualitySettings.renderPipeline;
                _pipeline = Instantiate(shared);
                _pipeline.name = shared.name + " (settings)";
                QualitySettings.renderPipeline = _pipeline;
                _ownPipeline = true;
                _originalRenderScale = _pipeline.renderScale;
                _originalShadowDistance = _pipeline.shadowDistance;
                _originalCascades = _pipeline.shadowCascadeCount;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            // The pipeline copy goes (the quality level gets back what it had); the panel is a project asset: its own scale back.
            if (_ownPipeline)
            {
                if (QualitySettings.renderPipeline == _pipeline) QualitySettings.renderPipeline = _qualityPipelineBefore;
                if (_pipeline != null) Destroy(_pipeline);
                _pipeline = null;
                _ownPipeline = false;
            }
            if (_panel != null) _panel.scale = _originalPanelScale;
            if (_blurProfile != null) Destroy(_blurProfile);
        }

        private void LateUpdate()
        {
            if (_applied != GameSettings.Version) Apply(GameSettings.Current);
            // FishNet (NetworkManager.UpdateFramerate) sets Application.targetFrameRate whenever a server or client starts or stops.
            if (Application.targetFrameRate != _targetFrameRate) Application.targetFrameRate = _targetFrameRate;
            if (QualitySettings.vSyncCount != _vSyncCount) QualitySettings.vSyncCount = _vSyncCount;
        }

        private void Apply(GameSettings s)
        {
            bool first = _applied < 0;
            _applied = GameSettings.Version;
            _targetFrameRate = SettingsRules.TargetFrameRate(s.FrameCap);
            _vSyncCount = SettingsRules.VSyncCount(s.VSync);
            AudioListener.volume = s.MasterVolume;
            if (_panel != null) _panel.scale = _originalPanelScale * s.UiScale;
            ApplyQuality(s.Quality);
            ApplyMotionBlur(s.MotionBlur);
            if (Persistent) ApplyDisplay(s, first);
            if (first) Debug.Log("[Settings] applied: " + SettingsRules.FrameCapText(s.FrameCap) + (s.VSync ? ", vsync" : string.Empty));
        }

        private void ApplyQuality(QualityPreset preset)
        {
            if (_pipeline == null) return;
            _pipeline.renderScale = SettingsRules.RenderScale(preset, _originalRenderScale);
            _pipeline.shadowDistance = SettingsRules.ShadowDistance(preset) ?? _originalShadowDistance;
            _pipeline.shadowCascadeCount = SettingsRules.ShadowCascades(preset) ?? _originalCascades;
        }

        private void ApplyMotionBlur(bool on)
        {
            if (!on && _blurVolume == null) return; // never made for a player who keeps it off
            if (_blurVolume == null)
            {
                _blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                MotionBlur blur = _blurProfile.Add<MotionBlur>(true);
                blur.mode.Override(MotionBlurMode.CameraOnly);
                blur.quality.Override(MotionBlurQuality.Medium);
                blur.intensity.Override(SettingsRules.MotionBlurIntensity);
                var go = new GameObject("Settings Volume (motion blur)");
                go.transform.SetParent(transform, false);
                _blurVolume = go.AddComponent<Volume>();
                _blurVolume.isGlobal = true;
                _blurVolume.priority = 100f;
                _blurVolume.sharedProfile = _blurProfile;
            }
            _blurVolume.enabled = on;
        }

        /// <summary>Only once the player picked a resolution or window mode, and only when it differs from what the screen has.</summary>
        private void ApplyDisplay(GameSettings s, bool first)
        {
            if (!s.DisplaySet) return;
            bool changed = first || s.Window != _lastWindow || s.ResolutionWidth != _lastWidth || s.ResolutionHeight != _lastHeight;
            if (!changed && _displayApplied) return;
            _displayApplied = true;
            _lastWindow = s.Window;
            _lastWidth = s.ResolutionWidth;
            _lastHeight = s.ResolutionHeight;
            int width = s.ResolutionWidth > 0 ? s.ResolutionWidth : Screen.currentResolution.width;
            int height = s.ResolutionHeight > 0 ? s.ResolutionHeight : Screen.currentResolution.height;
            FullScreenMode mode = SettingsRules.ToFullScreenMode(s.Window);
            if (Screen.width == width && Screen.height == height && Screen.fullScreenMode == mode) return;
            Screen.SetResolution(width, height, mode);
            Debug.Log("[Settings] display " + SettingsRules.ResolutionText(width, height) + ", " + SettingsRules.WindowText(s.Window));
        }
    }
}
