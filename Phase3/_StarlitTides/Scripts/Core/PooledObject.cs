// PooledObject: a small tag the PoolManager puts on every pooled object,
// so it knows which prefab the object came from.

using UnityEngine;

namespace StarlitTides
{
    public class PooledObject : MonoBehaviour
    {
        [HideInInspector] public GameObject prefab;
        [HideInInspector] public bool inPool;
    }
}
