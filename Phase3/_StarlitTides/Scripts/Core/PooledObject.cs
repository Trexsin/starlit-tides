// =============================================================================
//  PooledObject - remembers which prefab a pooled object came from
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    /// <summary>Remembers which prefab a pooled object came from.</summary>
    public class PooledObject : MonoBehaviour
    {
        [HideInInspector] public GameObject prefab;
        [HideInInspector] public bool inPool;
    }
}
