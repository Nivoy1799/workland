using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b, pure: decides where one item is drawn each frame and which tween starts (docs/feel.md 1–2), for every
    /// viewer. Inputs are what the client already has: the synced holder, the networked root's pose, the holder's hand or
    /// view pose (<see cref="HeldGoalSource"/>) and the pose drawn last. A predicted look (the owner's click) overrides the
    /// synced state until it ends. After a put-down the networked position needs a few ticks to arrive, so the look waits
    /// where it was drawn (or on the predicted spot) until the root has stopped moving for <see cref="RestQuiet"/> (at most
    /// <see cref="RestSettleMax"/>), then settles onto it; it never chases the root through its interpolation from the hold
    /// point above the head. <c>Pickup</c> feeds it and an <c>ItemTween</c> draws the result.
    /// </summary>
    public sealed class ItemLook
    {
        public const int NoHolder = -1;
        /// <summary>s: the longest the look waits for the networked position after a put-down before settling anyway.</summary>
        public const float RestSettleMax = 0.3f;
        /// <summary>s: the networked position counts as arrived after this long without moving.</summary>
        public const float RestQuiet = 0.05f;
        /// <summary>s: a confirmed put-into keeps its look at most this long while the despawn is on its way.</summary>
        public const float ConfirmedIntoMax = 1f;

        private bool _hasGoal;
        private ItemGoalKind _goalKind;
        private int _goalHolder = NoHolder;
        private int _seenHolder = NoHolder;
        private bool _settling;
        private Pose _settleLatch;
        private float _settleDeadline;
        private bool _settleSawMove;
        private float _lastRootMove = float.NegativeInfinity;
        private Pose _lastRoot;
        private bool _hasLastRoot;
        private bool _hasPrediction;
        private ItemGoalKind _predictedKind;
        private Pose _predictedPose;
        private int _predictedHolder = NoHolder;
        private bool _predictionConfirmed;
        private float _predictionKeepUntil;

        /// <summary>A predicted look is drawn (pending, or confirmed and waiting for the state to catch up).</summary>
        public bool HasPrediction => _hasPrediction;
        /// <summary>Waiting for the networked position after a put-down.</summary>
        public bool IsSettling => _settling;

        /// <summary>Forgets everything (spawned, pooled or despawned); <paramref name="syncedHolder"/> is the holder now.</summary>
        public void Reset(int syncedHolder)
        {
            _hasGoal = false;
            _settling = false;
            _hasLastRoot = false;
            _hasPrediction = false;
            _seenHolder = syncedHolder;
        }

        /// <summary>Predicts the item in <paramref name="holder"/>'s hands (a pick-up).</summary>
        public void PredictHeld(int holder) => Predict(ItemGoalKind.Hand, default, holder);

        /// <summary>Predicts the item lying at <paramref name="pose"/> (a place or a drop).</summary>
        public void PredictRest(Pose pose) => Predict(ItemGoalKind.Rest, pose, NoHolder);

        /// <summary>Predicts the item just above the cookware's contents (a put-into).</summary>
        public void PredictInto(Pose pose) => Predict(ItemGoalKind.Into, pose, NoHolder);

        private void Predict(ItemGoalKind kind, Pose pose, int holder)
        {
            _hasPrediction = true;
            _predictedKind = kind;
            _predictedPose = pose;
            _predictedHolder = holder;
            _predictionConfirmed = false;
        }

        /// <summary>
        /// The prediction is over. Denied: the look follows the real state again (it eases back). Confirmed: a pick-up simply
        /// continues (the synced holder is you now); a place keeps its spot until the networked position has arrived there; a
        /// put-into stays above the cookware until the food is despawned (at most <see cref="ConfirmedIntoMax"/>).
        /// </summary>
        public void EndPrediction(bool confirmed, float now)
        {
            if (!_hasPrediction) return;
            if (!confirmed || _predictedKind == ItemGoalKind.Hand)
            {
                _hasPrediction = false;
                return;
            }
            if (_predictedKind == ItemGoalKind.Rest)
            {
                _hasPrediction = false;
                BeginSettling(_predictedPose, now);
                return;
            }
            _predictionConfirmed = true;
            _predictionKeepUntil = now + ConfirmedIntoMax;
        }

        /// <summary>Who holds it as this client draws it (a predicted pick-up counts, a predicted put-down is nobody).</summary>
        public int DrawnHolder(int syncedHolder)
        {
            if (_hasPrediction && _predictedKind == ItemGoalKind.Hand) return _predictedHolder;
            if (_hasPrediction) return NoHolder;
            return syncedHolder;
        }

        /// <summary>
        /// This frame's goal and the tween to start: <paramref name="syncedHolder"/> and <paramref name="root"/> are the synced
        /// state, <paramref name="drawn"/> the pose drawn last (if <paramref name="hasDrawn"/>), <paramref name="held"/> the
        /// holder's hand or view pose.
        /// </summary>
        public ItemFrame Resolve(int syncedHolder, Pose root, bool hasDrawn, Pose drawn, float now, HeldGoalSource held)
        {
            bool rootMoved = TrackRoot(root, now);
            if (syncedHolder != _seenHolder)
            {
                // Put down (or taken from someone): wait for the networked position before settling on it.
                if (syncedHolder == NoHolder && !_settling) BeginSettling(hasDrawn ? drawn : root, now);
                _seenHolder = syncedHolder;
            }

            ItemGoalKind kind;
            int holder = NoHolder;
            Pose goal;
            bool settleNow = false;
            if (_hasPrediction && _predictedKind == ItemGoalKind.Into && _predictionConfirmed && now > _predictionKeepUntil)
                _hasPrediction = false;
            if (_hasPrediction && _predictedKind != ItemGoalKind.Hand)
            {
                kind = _predictedKind;
                goal = _predictedPose;
            }
            else
            {
                holder = DrawnHolder(syncedHolder);
                if (holder != NoHolder)
                {
                    _settling = false; // held (or predicted held): nothing lies anywhere to settle on
                    if (held == null || !held(holder, out kind, out goal))
                    {
                        kind = ItemGoalKind.HoldPoint;
                        goal = root;
                    }
                }
                else
                {
                    kind = ItemGoalKind.Rest;
                    goal = RestGoal(root, rootMoved, hasDrawn, drawn, now, out settleNow);
                }
            }

            TweenStyle begin = TweenStyle.None;
            if (_hasGoal && (kind != _goalKind || holder != _goalHolder)) begin = FeelRules.StyleFor(_goalKind, _goalHolder, kind, holder);
            if (begin == TweenStyle.None && settleNow) begin = TweenStyle.Settle;
            _hasGoal = true;
            _goalKind = kind;
            _goalHolder = holder;
            return new ItemFrame(kind, holder, goal, begin);
        }

        private Pose RestGoal(Pose root, bool rootMoved, bool hasDrawn, Pose drawn, float now, out bool settleNow)
        {
            settleNow = false;
            // The server moved a lying item: wait until it stopped, then settle onto the new place.
            if (!_settling && rootMoved && hasDrawn) BeginSettling(drawn, now);
            if (!_settling) return root;
            bool arrived = _settleSawMove && now - _lastRootMove >= RestQuiet;
            if (!arrived && now < _settleDeadline) return _settleLatch;
            _settling = false;
            settleNow = (root.position - _settleLatch.position).sqrMagnitude > FeelRules.MoveEpsilon * FeelRules.MoveEpsilon
                        || Quaternion.Angle(root.rotation, _settleLatch.rotation) > 2f;
            return root;
        }

        private void BeginSettling(Pose latch, float now)
        {
            _settling = true;
            _settleLatch = latch;
            _settleDeadline = now + RestSettleMax;
            _settleSawMove = false;
        }

        /// <summary>Notes whether the networked transform moved since the last frame.</summary>
        private bool TrackRoot(Pose root, float now)
        {
            bool moved = _hasLastRoot && ((root.position - _lastRoot.position).sqrMagnitude > 1e-6f
                                          || Quaternion.Angle(root.rotation, _lastRoot.rotation) > 0.2f);
            _lastRoot = root;
            _hasLastRoot = true;
            if (moved)
            {
                _lastRootMove = now;
                _settleSawMove = true;
            }
            return moved;
        }
    }
}
