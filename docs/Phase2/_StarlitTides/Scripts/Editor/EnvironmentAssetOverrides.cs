// =============================================================================
//  EnvironmentAssetOverrides - use downloaded models instead of procedural ones
// -----------------------------------------------------------------------------
//  Create one via  Assets > Create > Starlit Tides > Environment Asset Overrides
//  (anywhere in the project), drag prefabs into the lists, then run
//  Starlit Tides > 4. Build Environment again. Empty lists = procedural models.
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;

namespace StarlitTides.EditorTools
{
    /// <summary>
    /// Optional: lists of downloaded prefabs the builder should use instead of
    /// procedural geometry. Create via Assets > Create > Starlit Tides >
    /// Environment Asset Overrides. Any empty list falls back to procedural.
    /// </summary>
    [CreateAssetMenu(menuName = "Starlit Tides/Environment Asset Overrides", fileName = "EnvironmentAssetOverrides")]
    public class EnvironmentAssetOverrides : ScriptableObject
    {
        public GameObject[] pineTreePrefabs;
        public GameObject[] broadleafTreePrefabs;
        public GameObject[] palmTreePrefabs;
        public GameObject[] rockPrefabs;
        public GameObject[] boatPrefabs;
    }
}
#endif
