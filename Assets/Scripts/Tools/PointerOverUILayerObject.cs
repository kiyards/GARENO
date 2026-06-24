using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

//https://stackoverflow.com/questions/57010713/unity-ispointerovergameobject-issue
public static class PointerOverUILayerObject
{
    public static bool IsPointerOverUIObject()
    {
        return IsPointerOverUIObject(Input.mousePosition);
    }
    public static bool IsPointerOverUIObject(Vector2 pointerPos)
    {
        PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
        eventDataCurrentPosition.position = new Vector2(pointerPos.x, pointerPos.y);
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventDataCurrentPosition, results);

        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].gameObject.layer == 5) //5 = UI layer
            {
                return true;
            }
        }

        return false;
    }
    public static bool IsPointerOverUIObject(Vector2 pointerPos, GameObject target)
    {
        PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
        eventDataCurrentPosition.position = new Vector2(pointerPos.x, pointerPos.y);
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventDataCurrentPosition, results);

        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].gameObject == target) return true;
        }

        return false;
    }
}