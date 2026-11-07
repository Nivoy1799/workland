using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Worktown.Game.Net;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 018, owner only: in first person the visual children of the food you hold (the FoodVisual and its
    /// fallback cube) are drawn at the <see cref="PlayerCameraRig.ViewHold"/> anchor in the lower right of the
    /// view, and its label above your head is hidden. The networked object, its collider and the server's
    /// hold point do not move; other players still see the food above your head. In third person, on drop,
    /// or when the food leaves your hands, the children go back to where they were.
    /// The pose is applied right before the main camera renders, after every transform (network, smoother,
    /// Cinemachine) has moved this frame, so the food does not trail the view.
    /// </summary>
    public sealed class HeldItemView : MonoBehaviour
    {
        private PlayerCameraRig _rig;
        private PickupInteractor _interactor;
        private Camera _camera;

        private Pickup _held;
        private readonly List<Transform> _moved = new List<Transform>();
        private readonly List<Vector3> _localPositions = new List<Vector3>();
        private readonly List<Quaternion> _localRotations = new List<Quaternion>();
        private readonly List<Renderer> _hiddenLabels = new List<Renderer>();

        public void Activate(PlayerCameraRig rig, PickupInteractor interactor)
        {
            if (_rig != null || rig == null || interactor == null) return;
            _rig = rig;
            _interactor = interactor;
            _camera = rig.ViewHold != null ? rig.ViewHold.GetComponentInParent<Camera>() : null;
            if (_camera == null) Debug.LogWarning("[View] no ViewHold anchor under a camera; held food stays above the head");
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void LateUpdate()
        {
            if (_rig == null) return;
            Pickup held = _rig.IsFirstPerson && _camera != null ? _interactor.FindHeldPickup() : null;
            if (held == _held) return;
            Release();
            if (held != null) Capture(held);
        }

        private void Capture(Pickup held)
        {
            _held = held;
            foreach (Transform child in held.transform)
            {
                if (child.TryGetComponent(out TextMesh _))
                {
                    // The name and stars above your head: you would look through them.
                    if (child.TryGetComponent(out Renderer label) && label.enabled)
                    {
                        label.enabled = false;
                        _hiddenLabels.Add(label);
                    }
                    continue;
                }
                _moved.Add(child);
                _localPositions.Add(child.localPosition);
                _localRotations.Add(child.localRotation);
            }
        }

        private void Release()
        {
            // A despawned and destroyed food needs nothing back; a pooled one gets its pose and label back.
            if (_held != null)
            {
                for (int i = 0; i < _moved.Count; i++)
                    if (_moved[i] != null) _moved[i].SetLocalPositionAndRotation(_localPositions[i], _localRotations[i]);
                foreach (Renderer label in _hiddenLabels)
                    if (label != null) label.enabled = true;
            }
            _held = null;
            _moved.Clear();
            _localPositions.Clear();
            _localRotations.Clear();
            _hiddenLabels.Clear();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (_held == null || cam != _camera || _rig.ViewHold == null) return;
            Transform anchor = _rig.ViewHold;
            for (int i = 0; i < _moved.Count; i++)
            {
                if (_moved[i] == null) continue;
                _moved[i].SetPositionAndRotation(anchor.TransformPoint(_localPositions[i]), anchor.rotation * _localRotations[i]);
            }
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            Release();
        }
    }
}
