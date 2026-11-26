// =============================================================================
//  PoolManager - object pooling for orbs, obstacles and particle effects
// -----------------------------------------------------------------------------
//  Instantiate() and Destroy() allocate memory and cause garbage-collector
//  stutters. Instead, finished objects are switched off and stored in a
//  stack per prefab; the next Spawn() re-uses one. After a few seconds of
//  play the game creates no new objects at all.
//
//      GameObject go = PoolManager.Spawn(prefab, position, rotation);
//      PoolManager.Despawn(go);
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarlitTides
{
    public static class PoolManager
    {
        private static readonly Dictionary<GameObject, Stack<GameObject>> Pools = new Dictionary<GameObject, Stack<GameObject>>();
        private static Transform _root;

        // Pools hold scene objects, so empty them whenever a scene loads
        // (e.g. Retry reloads the scene) and when entering Play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pools.Clear();
            _root = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) { Pools.Clear(); _root = null; }
        }

        private static Transform Root
        {
            get
            {
                if (_root == null) _root = new GameObject("[Pool]").transform;
                return _root;
            }
        }

        /// <summary>Gets an inactive copy of 'prefab' (or creates one), places it and activates it.</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            if (!Pools.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                Pools[prefab] = stack;
            }

            GameObject go = null;
            while (stack.Count > 0 && go == null) go = stack.Pop();   // skip anything destroyed externally

            if (go == null)
            {
                go = Object.Instantiate(prefab, position, rotation, Root);
                var po = go.GetComponent<PooledObject>();
                if (po == null) po = go.AddComponent<PooledObject>();
                po.prefab = prefab;
            }
            else
            {
                go.transform.SetPositionAndRotation(position, rotation);
            }

            go.GetComponent<PooledObject>().inPool = false;
            go.SetActive(true);   // OnEnable runs with the correct position
            return go;
        }

        /// <summary>Returns an object to its pool (or destroys it if it was never pooled).</summary>
        public static void Despawn(GameObject go)
        {
            if (go == null) return;
            var po = go.GetComponent<PooledObject>();
            if (po == null || po.prefab == null) { Object.Destroy(go); return; }
            if (po.inPool) return;   // already returned

            po.inPool = true;
            go.SetActive(false);
            go.transform.SetParent(Root, false);
            if (!Pools.TryGetValue(po.prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                Pools[po.prefab] = stack;
            }
            stack.Push(go);
        }

        /// <summary>Creates 'count' inactive copies up front so the first spawns don't stutter.</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;
            var temp = new List<GameObject>(count);
            for (int i = 0; i < count; i++) temp.Add(Spawn(prefab, new Vector3(0, -500, 0), Quaternion.identity));
            foreach (GameObject go in temp) Despawn(go);
        }
    }

}
