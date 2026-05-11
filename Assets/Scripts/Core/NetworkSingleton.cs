using Mirror;
using UnityEngine;

namespace Core
{
    public class NetworkSingleton<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        public static T Instance { get; private set; }
        /// <summary>
        /// This runs in Awake, Use this for gameobjects in the scene already
        /// </summary>
        protected void Startup(T instance)
        {
            Instance = instance;
        }
        /// <summary>
        /// This needs to run in OnStartLocalPlayer because it runs after network initialization
        /// Use this for gameobjects spawned by the network
        /// </summary>
        protected void StartupSpawned(T instance)
        {
            if (instance.isLocalPlayer)
                Instance = instance;
        }

        public void DestroyInstance()
        {
            Instance = null;
            Destroy(this.gameObject);
        }
    }
    public class NetworkSingletonPersist<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        public static T Instance = null;
        /// <summary>
        /// Run in Awake
        /// </summary>
        protected void Startup(T instance)
        {
            if (!Application.isPlaying) return;
            if (Instance == null)
            {
                Instance = instance;
                DontDestroyOnLoad(Instance.gameObject);
                RunOnce();
            }
            else
                Destroy(this.gameObject);
        }
        protected virtual void RunOnce()
        {
            //Debug.Log($"Starting Up SingletonPersist {_.name} of class {_.GetType().Name}");
        }
    }
}