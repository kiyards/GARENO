using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpriteSorter : MonoBehaviour
{
    [SerializeField] SpriteRenderer sprite;
    [SerializeField] bool setOnStartOnly = false;
    private void Start()
    {
        if (setOnStartOnly)
            sprite.sortingOrder = Mathf.CeilToInt(100 * -transform.position.y);
    }
    private void Update()
    {
        if (!setOnStartOnly)
            sprite.sortingOrder = Mathf.CeilToInt(100 * -transform.position.y);
    }
}
