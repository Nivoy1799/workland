using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 039 part 2: the physics layers (ProjectSettings/TagManager.asset) and the query masks. Loose items (every Pickup
    /// prefab: food, cookware, tools, boxes, bags) lie on <see cref="Items"/>; the player's body on <see cref="Player"/>, the
    /// customers on <see cref="Npc"/>. The collision matrix (DynamicsManager.asset) lets neither of them touch Items, so a
    /// CharacterController walks through a tomato on the floor instead of climbing it; Items still meet the static world.
    /// The matrix is a project setting, the same on the server and every client, so prediction and the server agree.
    /// Queries that must see items (the aim, the place spots, the server's overlap checks) use <see cref="Interact"/>, which
    /// names Items explicitly.
    /// </summary>
    public static class PhysicsLayers
    {
        public const int Items = 8;
        public const int Player = 9;
        public const int Npc = 10;

        public const string ItemsName = "Items";
        public const string PlayerName = "Player";
        public const string NpcName = "Npc";

        public const int ItemsMask = 1 << Items;

        /// <summary>The aim ray, the place-spot probe and the pickup searches: everything raycastable, items included.</summary>
        public const int Interact = Physics.DefaultRaycastLayers | ItemsMask;

        /// <summary>
        /// What a body on <paramref name="layer"/> stands on or bumps into (its collision-matrix row, without Ignore Raycast):
        /// the player's ground and headroom probes use it, so they ignore items exactly like the controller does.
        /// </summary>
        public static int BodyMask(int layer)
        {
            int mask = 0;
            for (int other = 0; other < 32; other++)
                if (!Physics.GetIgnoreLayerCollision(layer, other)) mask |= 1 << other;
            return mask & Physics.DefaultRaycastLayers;
        }
    }
}
