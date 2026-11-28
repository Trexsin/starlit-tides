// PoolManager: re-uses objects instead of creating and destroying them.
//
// Instantiate() and Destroy() allocate memory, and the garbage collector
// cleaning that up causes small stutters. So when an orb or obstacle is done,
// we switch it off and keep it in a stack. Next time we need one, we take it
// from the stack and switch it back on.
//
//   GameObject orb = PoolManager.Spawn(orbPrefab, position, rotation);
//   PoolManager.Despawn(orb);

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarlitTides
{
    public static class PoolManager
    {
        // One stack of switched-off objects for each prefab.
        static Dictionary<GameObject, Stack<GameObject>> pools = new Dictionary<GameObject, Stack<GameObject>>();
        static Transform poolParent;

        // Static data survives between Play sessions, so clear it when the game
        // starts and whenever a scene is (re)loaded, e.g. when you press Retry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPools()
        {
            pools.Clear();
            poolParent = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                pools.Clear();
                poolParent = null;
            }
        }

        // Keeps the Hierarchy tidy: all pooled objects live under "[Pool]".
        static Transform PoolParent
        {
            get
            {
                if (poolParent == null) poolParent = new GameObject("[Pool]").transform;
                return poolParent;
            }
        }

        static Stack<GameObject> GetStack(GameObject prefab)
        {
            Stack<GameObject> stack;
            if (!pools.TryGetValue(prefab, out stack))
            {
                stack = new Stack<GameObject>();
                pools[prefab] = stack;
            }
            return stack;
        }

        // Takes a switched-off copy from the pool (or makes a new one) and switches it on.
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            Stack<GameObject> stack = GetStack(prefab);

            // Skip anything that was destroyed by something else.
            GameObject obj = null;
            while (stack.Count > 0 && obj == null) obj = stack.Pop();

            if (obj == null)
            {
                obj = Object.Instantiate(prefab, position, rotation, PoolParent);
                PooledObject tag = obj.GetComponent<PooledObject>();
                if (tag == null) tag = obj.AddComponent<PooledObject>();
                tag.prefab = prefab;
            }
            else
            {
                obj.transform.SetPositionAndRotation(position, rotation);
            }

            obj.GetComponent<PooledObject>().inPool = false;
            // Position first, then activate, so OnEnable sees the right position.
            obj.SetActive(true);
            return obj;
        }

        // Switches the object off and puts it back in its pool.
        public static void Despawn(GameObject obj)
        {
            if (obj == null) return;

            PooledObject tag = obj.GetComponent<PooledObject>();
            if (tag == null || tag.prefab == null)
            {
                // Not from a pool, so just destroy it.
                Object.Destroy(obj);
                return;
            }
            if (tag.inPool) return;   // already returned

            tag.inPool = true;
            obj.SetActive(false);
            obj.transform.SetParent(PoolParent, false);
            GetStack(tag.prefab).Push(obj);
        }

        // Makes some copies at the start so the first spawns don't stutter.
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;

            List<GameObject> made = new List<GameObject>();
            for (int i = 0; i < count; i++)
                made.Add(Spawn(prefab, new Vector3(0f, -500f, 0f), Quaternion.identity));

            foreach (GameObject obj in made) Despawn(obj);
        }
    }
}
