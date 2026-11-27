// WorldLayout: turns the bay so the boat sails OUT to sea.
//
// The environment is designed on a "layout map" where the coast runs along +Z
// (land on the -X side). Gameplay always flows along world +Z (the tide moves
// everything toward -Z), so without a turn the boat would sail parallel to the
// beach. Every builder places the land in layout space and converts it with
// ToWorld(): the land swings round behind and to the left of the boat, and the
// open sea, the moon and its glittering path lie straight ahead.
//
// Things tied to the gameplay lane (ocean grid, plankton motes, moon, the
// gameplay camera) are NOT turned: they already live in world space.
//
// Phase 8 widened the play lane to +-22 m. With the turn alone the shoreline
// crossed the boat's start line only ~25 m to the left, right at the new lane
// edge. So the land is also slid sideways by Offset (after turning):
//
//     world = Rotation * layout + Offset
//
// Directions and velocities (no position) use ToWorldDirection: turn only.

#if UNITY_EDITOR
using UnityEngine;

namespace StarlitTides.EditorTools
{
    public static class WorldLayout
    {
        // Degrees the land is turned (anticlockwise seen from above).
        // 0 = the old "sail along the beach" layout, 90 = land directly behind.
        public const float Yaw = 38f;

        public static Quaternion Rotation { get { return Quaternion.Euler(0f, -Yaw, 0f); } }

        // Metres the turned land is slid in world space (-X = further left of the lane).
        public static readonly Vector3 Offset = new Vector3(-18f, 0f, 0f);

        public static Vector3 ToWorld(Vector3 layoutPosition) { return Rotation * layoutPosition + Offset; }
        public static Vector3 ToLayout(Vector3 worldPosition) { return Quaternion.Inverse(Rotation) * (worldPosition - Offset); }
        public static Vector3 ToWorldDirection(Vector3 layoutDirection) { return Rotation * layoutDirection; }
        public static Quaternion ToWorld(Quaternion layoutRotation) { return Rotation * layoutRotation; }

        public static Vector2 ToLayoutXZ(float worldX, float worldZ)
        {
            Vector3 p = ToLayout(new Vector3(worldX, 0f, worldZ));
            return new Vector2(p.x, p.z);
        }

        // Turns a scene object about the world origin (position and rotation).
        public static void Apply(Transform t)
        {
            t.SetPositionAndRotation(ToWorld(t.position), ToWorld(t.rotation));
        }
    }
}
#endif
