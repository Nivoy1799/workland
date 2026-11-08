using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027: one scripted visual check. The tour client runs <see cref="Steps"/>; the observer client walks
    /// <see cref="ObserverRoute"/>, faces <see cref="ObserverYaw"/> and, if <see cref="HasObserverCamera"/>, watches from
    /// a fixed camera and takes the tour's Frames sequences when the tour player moves, plus a shot every
    /// <see cref="ObserverShotEvery"/> seconds (0 = none).
    /// </summary>
    public sealed class Tour
    {
        public string Name { get; }
        public IReadOnlyList<TourStep> Steps { get; }
        public IReadOnlyList<Vector3> ObserverRoute { get; }
        public float ObserverYaw { get; }
        public bool HasObserverCamera { get; }
        public Vector3 ObserverCameraPosition { get; }
        public float ObserverCameraYaw { get; }
        public float ObserverCameraPitch { get; }
        public float ObserverShotEvery { get; }
        /// <summary>The whole tour must finish in this time, or it counts as timed out.</summary>
        public float TimeoutSeconds { get; }

        public Tour(string name, IReadOnlyList<TourStep> steps, IReadOnlyList<Vector3> observerRoute, float observerYaw,
            bool hasObserverCamera, Vector3 observerCameraPosition, float observerCameraYaw, float observerCameraPitch,
            float observerShotEvery, float timeoutSeconds)
        {
            Name = name;
            Steps = steps;
            ObserverRoute = observerRoute;
            ObserverYaw = observerYaw;
            HasObserverCamera = hasObserverCamera;
            ObserverCameraPosition = observerCameraPosition;
            ObserverCameraYaw = observerCameraYaw;
            ObserverCameraPitch = observerCameraPitch;
            ObserverShotEvery = observerShotEvery;
            TimeoutSeconds = timeoutSeconds;
        }

        /// <summary>Where the observer's player stands (the last route point).</summary>
        public Vector3 ObserverSpot => ObserverRoute.Count > 0 ? ObserverRoute[ObserverRoute.Count - 1] : Vector3.zero;
    }
}
