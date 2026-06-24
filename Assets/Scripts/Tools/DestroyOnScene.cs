using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DestroyOnScene : MonoBehaviour
{
    [SerializeField] public string sceneContainsToUnload = "UNLOAD";
    void Start()
    {
        // Needs to be in start instead of awake
        // Only runs once because this script should only be put on singletonPersist
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name.Contains(sceneContainsToUnload))
            Destroy(gameObject);
    }
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
