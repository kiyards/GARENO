using UnityEngine;

//This does not need to exist because the interface implementation is going to be independant of the class in all usages
//public interface ISingleton<T> where T : MonoBehaviour
//{
//    public static T _ { get; private set; }
//    protected void Startup(T instance);
//}
namespace Core
{
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        public static T Instance { get; private set; }
        protected void Startup(T instance)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = instance;
        }

        protected void DestroyInstance()
        {
            if (Instance == this)
                Instance = null;
        }
    }
    public abstract class SingletonPersist<T> : MonoBehaviour where T : MonoBehaviour
    {
        // The static instance will persist in the duration of the Domain, even if the gameObject is destroyed.
        // That means it will stay across reloading scenes and changing scenes in editor
        // If we want to reload the singleton, enable reload domain in editor settings.
        public static T Instance = null;
        protected void Startup(T instance)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (Instance == null)
            {
                Instance = instance;
                DontDestroyOnLoad(Instance.gameObject);
                RunOnce();
            }
            else
                Destroy(gameObject);
        }

        protected virtual void RunOnce()
        {
            //Debug.Log($"Starting Up SingletonPersist {_.name} of class {_.GetType().Name}");
        }

        protected void DestroyInstance()
        {
            if (Instance == this)
            {
                Instance = null;
                Destroy(this.gameObject);
            }
        }
    }
}
