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

        public static Vector3 ToWorld(Vector3 layoutPosition) { return Rotation * layoutPosition; }
        public static Vector3 ToLayout(Vector3 worldPosition) { return Quaternion.Inverse(Rotation) * worldPosition; }
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
