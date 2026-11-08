using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Dev
{
    /// <summary>Spec 027: one step of a tour. Immutable; build it with the static factories.</summary>
    public readonly struct TourStep
    {
        public TourStepKind Kind { get; }
        /// <summary>WalkTo target or LookAt point.</summary>
        public Vector3 Point { get; }
        public bool Sprint { get; }
        public bool Crouch { get; }
        public float Yaw { get; }
        public float Pitch { get; }
        public ViewMode ViewMode { get; }
        /// <summary>Wait length or Frames length.</summary>
        public float Seconds { get; }
        public float Interval { get; }
        /// <summary>Shot and Frames file name, or the Act name.</summary>
        public string Name { get; }

        private TourStep(TourStepKind kind, Vector3 point = default, bool sprint = false, bool crouch = false, float yaw = 0f,
            float pitch = 0f, ViewMode view = ViewMode.FirstPerson, float seconds = 0f, float interval = 0f, string name = "")
        {
            Kind = kind;
            Point = point;
            Sprint = sprint;
            Crouch = crouch;
            Yaw = yaw;
            Pitch = pitch;
            ViewMode = view;
            Seconds = seconds;
            Interval = interval;
            Name = name ?? string.Empty;
        }

        public static TourStep WalkTo(float x, float z, bool sprint = false, bool crouch = false) =>
            new TourStep(TourStepKind.WalkTo, new Vector3(x, 0f, z), sprint, crouch);
        public static TourStep Look(float yaw, float pitch) => new TourStep(TourStepKind.Look, yaw: yaw, pitch: pitch);
        public static TourStep LookAt(float x, float y, float z) => new TourStep(TourStepKind.LookAt, new Vector3(x, y, z));
        public static TourStep View(ViewMode mode) => new TourStep(TourStepKind.View, view: mode);
        public static TourStep Wait(float seconds) => new TourStep(TourStepKind.Wait, seconds: seconds);
        public static TourStep Shot(string name) => new TourStep(TourStepKind.Shot, name: name);
        public static TourStep Frames(string name, float seconds, float interval) =>
            new TourStep(TourStepKind.Frames, seconds: seconds, interval: interval, name: name);
        public static TourStep Act(string name) => new TourStep(TourStepKind.Act, name: name);
        public static TourStep WaitObserver() => new TourStep(TourStepKind.WaitObserver);

        /// <summary>Frames only: how many frames the sequence takes (at least 1).</summary>
        public int FrameCount => Interval > 0f ? Mathf.Max(1, Mathf.RoundToInt(Seconds / Interval)) : 1;

        public override string ToString()
        {
            switch (Kind)
            {
                case TourStepKind.WalkTo: return "WalkTo(" + Point.x + ", " + Point.z + (Sprint ? ", sprint" : "") + (Crouch ? ", crouch" : "") + ")";
                case TourStepKind.Look: return "Look(" + Yaw + ", " + Pitch + ")";
                case TourStepKind.LookAt: return "LookAt(" + Point + ")";
                case TourStepKind.View: return "View(" + ViewMode + ")";
                case TourStepKind.Wait: return "Wait(" + Seconds + ")";
                case TourStepKind.Shot: return "Shot(" + Name + ")";
                case TourStepKind.Frames: return "Frames(" + Name + ", " + Seconds + ", " + Interval + ")";
                case TourStepKind.Act: return "Act(" + Name + ")";
                default: return Kind.ToString();
            }
        }
    }
}
