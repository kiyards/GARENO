//using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using System.Linq;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public struct TransformLite
{
    public Vector3 pos;

    //Euler positioning doesnt fix it (unity probably stores it as eulerangles)
    public Quaternion rot;
    // public Vector3 rot;
}

[System.Serializable]
public struct SerialKeyValuePair<K, V>
{
    public K key;
    public V val;
}
public enum Cardinal
{
    North,
    East,
    South,
    West,
    None
}
public enum Dir8
{
    N,
    E,
    S,
    W,
    NE,
    SE,
    SW,
    NW
}

public static class KongrooUtils
{
    public static Dictionary<K, V> ToDict<K, V>(this SerialKeyValuePair<K, V>[] self)
    {
        if (self.Length == 0)
            return new Dictionary<K, V> { };
        return self.ToDictionary(keyVal => keyVal.key, keyVal => keyVal.val);
    }

    public static SerialKeyValuePair<K, V>[] ToCereal<K, V>(this Dictionary<K, V> self)
    {
        return self.Select(kvPair => new SerialKeyValuePair<K, V>() { key = kvPair.Key, val = kvPair.Value }).ToArray();
    }

    public static float InverseLerp(Vector3 a, Vector3 b, Vector3 value)
    {
        Vector3 AB = b - a;
        Vector3 AV = value - a;
        return Vector3.Dot(AV, AB) / Vector3.Dot(AB, AB);
    }
    public static Vector2 Vector2FromAngle(float angle)
    {
        angle *= Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
    //This is an extension method that cannot mutate the original because its a stuct 
    public static Vector2 Vector2Rotate(this Vector2 v, float degrees)
    {
        if (degrees == 0)
            return v;
        float sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
        float cos = Mathf.Cos(degrees * Mathf.Deg2Rad);

        float tx = v.x;
        float ty = v.y;
        v.x = (cos * tx) - (sin * ty);
        v.y = (sin * tx) + (cos * ty);
        return v;
    }
    public static Vector3 Vector3Rotate(this Vector3 v, Vector3 axis, float degrees)
    {
        if (degrees == 0 || axis == Vector3.zero)
            return v;

        Quaternion rotation = Quaternion.AngleAxis(degrees, axis.normalized);
        return rotation * v;
    }
    public static Vector2 ClampMagnitude(Vector2 v, float max, float min)
    {
        double sm = v.sqrMagnitude;
        if (sm > max * (double)max) return v.normalized * max;
        else if (sm < min * (double)min) return v.normalized * min;
        return v;
    }
    public static IEnumerable<T> ToDebuggableList<T>(this IEnumerable<T> enumerable)
    {
        return enumerable
#if UNITY_EDITOR
        .ToList()
#endif
        ;
    }

    //TODO: test if works
    public static IEnumerable<X> ShuffleArray<X>(IEnumerable<X> enumerable)
    {
        var array = enumerable.ToArray();
        int currentIndex = array.Length;
        while (currentIndex != 0)
        {
            //Pick random index
            int rand = Random.Range(0, currentIndex);
            currentIndex--;

            X temp1 = array[rand];
            X temp2 = array[currentIndex];

            array[currentIndex] = temp1;
            array[rand] = temp2;
        }

        return array.Cast<X>();
    }

    // Fisher Yates
    public static void ShuffleInPlace<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
    public static T Pop<T>(this List<T> list, int index)
    {
        if (list.Count <= index) return default;
        T thing = list[index];
        list.RemoveAt(index);
        return thing;
    }

    //public static T[] ShuffleArray<T>(T[] array)
    //{
    //    int currentIndex = array.Length;
    //    while (currentIndex != 0)
    //    {
    //        //Pick random index
    //        int rand = Random.Range(0, currentIndex);
    //        currentIndex--;
    //        T temp1 = array[rand];
    //        T temp2 = array[currentIndex];

    //        array[currentIndex] = temp1;
    //        array[rand] = temp2;
    //    }

    //    return array;
    //}

    //TODO: test if works
    public static T RandomElement<T>(this IEnumerable<T> enumerable)
    {
        return enumerable.ElementAt(Random.Range(0, enumerable.Count()));
    }

    //public static T RandomElement<T>(this List<T> list)
    //{
    //    return list[Random.Range(0, list.Count)];
    //}
    //public static T RandomElement<T>(this T[] arr)
    //{
    //    return arr[Random.Range(0, arr.Length)];
    //}

    public static void DestroyAllWithTag(string tag)
    {
        foreach (var go in GameObject.FindGameObjectsWithTag("DrawnLine"))
            GameObject.Destroy(go);
    }

    public static bool Approx(float current, float target, float epsilon)
    {
        return (current > target - epsilon && current < target + epsilon);
    }

    public static float RemapRange(float value, float inputA, float inputB, float outputA, float outputB, bool clamped = true)
    {
        if (inputA - inputB == 0 || outputA - outputB == 0) return value;
        float remapped = (value - inputA) / (inputB - inputA) * (outputB - outputA) + outputA;
        if (clamped)
        {
            if (outputA < outputB)
                return Mathf.Clamp(remapped, outputA, outputB);
            else
                return Mathf.Clamp(remapped, outputB, outputA);
        }
        return remapped;
    }

    public static Vector3 SlerpCenter(Vector3 p1, Vector3 p2, Vector3 center, float t)
    {
        var startNormalized = p1 - center;
        var endNormalized = p2 - center;
        return Vector3.Slerp(startNormalized, endNormalized, t) + center;
    }
    //TODO: Make own version of slerp using ray(direction, origin and radius)    

    public static void DrawGizmoCircle(Vector2 center, float radius, Color color, int segments = 200)
    {
        Gizmos.color = color;
        float angle = 0;
        float increment = 2 * Mathf.PI / segments;
        for (int i = 0; i < segments; i++)
        {
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 firstLoc = direction * radius + center;
            angle += increment;
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 secondLoc = direction * radius + center;
            Gizmos.DrawLine(firstLoc, secondLoc);
        }
    }


    public static void DrawDebugCircle(Vector2 center, float radius, Color color, float duration, int segments = 200)
    {
        float angle = 0;
        float increment = 2 * Mathf.PI / segments;
        for (int i = 0; i < segments; i++)
        {
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 firstLoc = direction * radius + center;
            angle += increment;
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 secondLoc = direction * radius + center;
            Debug.DrawLine(firstLoc, secondLoc, color, duration);
        }
    }

    public static void DrawDebugCircle(Vector2 center, float radius, Color color, int segments = 200, float duration = 0)
    {
        if (duration == 0) duration = Time.deltaTime;
        DrawDebugCircle(center, radius, color, segments, duration);
    }

    public static void DrawGraphLine(ref Vector2 oldPoint, Vector2 newPoint, Vector2? iOrigin = null)
    {
        var origin = iOrigin ?? Vector2.zero;
        Debug.DrawLine(oldPoint, newPoint, Color.green, 99999f);
        oldPoint = newPoint;
    }

    public static void DebugDrawSphere(Vector3 center, float rad)
    {
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        plane.transform.position = center;
    }

    public static void DrawWireCapsule(Vector3 _pos, Vector3 _pos2, float _radius, float _height,
        Color _color = default)
    {
#if UNITY_EDITOR
        if (_color != default) Handles.color = _color;

        var forward = _pos2 - _pos;
        var _rot = Quaternion.LookRotation(forward);
        var pointOffset = _radius / 2f;
        var length = forward.magnitude;
        var center2 = new Vector3(0f, 0, length);

        Matrix4x4 angleMatrix = Matrix4x4.TRS(_pos, _rot, Handles.matrix.lossyScale);

        using (new Handles.DrawingScope(angleMatrix))
        {
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, _radius);
            Handles.DrawWireArc(Vector3.zero, Vector3.up, Vector3.left * pointOffset, -180f, _radius);
            Handles.DrawWireArc(Vector3.zero, Vector3.left, Vector3.down * pointOffset, -180f, _radius);
            Handles.DrawWireDisc(center2, Vector3.forward, _radius);
            Handles.DrawWireArc(center2, Vector3.up, Vector3.right * pointOffset, -180f, _radius);
            Handles.DrawWireArc(center2, Vector3.left, Vector3.up * pointOffset, -180f, _radius);

            DrawLine(_radius, 0f, length);
            DrawLine(-_radius, 0f, length);
            DrawLine(0f, _radius, length);
            DrawLine(0f, -_radius, length);
        }
#endif
    }


    public static List<Vector2> DirectionVectors = new List<Vector2>() {
        Vector2.up,
        Vector2.right,
        Vector2.down,
        Vector2.left,
        new Vector2(1,1),
        new Vector2(1,-1),
        new Vector2(-1,-1),
        new Vector2(-1,1),
    };
    public static List<Vector2> CardinalVectors = new List<Vector2>
    {
        Vector2.up,
        Vector2.right,
        Vector2.down,
        Vector2.left,
        Vector2.zero
    };
    public static Cardinal ToCardinal(this Vector2 vec)
    {
        int i = 0;
        for (; i < CardinalVectors.Count; i++)
        {
            if (CardinalVectors[i] == vec)
                return (Cardinal)i;
        }
        return Cardinal.None;
        //throw new System.Exception("No cardinal vector found");
    }
    public static Vector2 ToVec(this Cardinal val)
    {
        return DirectionVectors[(int)val];
    }

    public static Vector2Int ToVecInt(this Cardinal val)
    {
        return Vector2Int.RoundToInt(DirectionVectors[(int)val]);
    }
    public static Vector2 ToVec(this Dir8 val)
    {
        return DirectionVectors[(int)val].normalized;
    }
    public static Vector2Int ToVecInt(this Dir8 val)
    {
        return Vector2Int.RoundToInt(DirectionVectors[(int)val]);
    }
    public static Vector2 RoundTo8Dir(this Vector2 v)
    {
        if (v.sqrMagnitude < 1e-7) return Vector2.zero;

        Vector2 n = v.normalized;

        int bestIndex = 0;
        float bestDot = float.NegativeInfinity;

        for (int i = 0; i < DirectionVectors.Count; i++)
        {
            // Make sure diagonals are normalized when comparing
            Vector2 d = DirectionVectors[i].normalized;

            float dot = Vector2.Dot(n, d);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = i;
            }
        }

        // Return a normalized 8-dir result
        return DirectionVectors[bestIndex].normalized;
    }
    public static Color SetAlpha(this Color col, float alpha)
    {
        col.a = alpha;
        return col;
    }
    public static Color MultiplyAlpha(this Color col, float alpha)
    {
        col.a *= Mathf.Clamp01(alpha);
        return col;
    }

    //public static IEnumerable<Cardinal> AllCardinals()
    //{
    //    for (int i = 0; i < (int)Cardinal.LENGTH; i++)
    //    {
    //        var currentCardinal = (Cardinal)i;
    //        yield return currentCardinal;
    //    }
    //}
    public static int GetEnumLength<T>() where T : System.Enum
    {
        return System.Enum.GetNames(typeof(T)).Length;
    }
    public static IEnumerable<T> GetEnumValues<T>() where T : System.Enum
    {
        return System.Enum.GetValues(typeof(T)).Cast<T>();
    }

    private static void DrawLine(float arg1, float arg2, float forward)
    {
#if UNITY_EDITOR
        Handles.DrawLine(new Vector3(arg1, arg2, 0f), new Vector3(arg1, arg2, forward));
#endif
    }

    public static IEnumerable<T> FindObjectsWithTagAndType<T>(string tag)
    {
        return GameObject.FindGameObjectsWithTag(tag).Select(g => g.GetComponent<T>()).Where(o => o != null);
    }

    // Cant overload outside of the class
    // https://stackoverflow.com/questions/7376674/c-sharp-overloading-operator-outside-the-class
    // public static bool operator ==(LayerMask input, LayerMask other)
    // {
    //     return true;
    // }
    public static bool IsGameObjectInMask(this LayerMask self, GameObject otherObject)
    {
        // Shifting 1 by the integer representation of the gameobjects layer turns it into a mask representation ( 2  4  8  16  32 )
        // A bitwise and with the mask ONLY flips a 1 where mask and object match
        int sumMask = self.value & (1<< otherObject.layer);

        //If ANY value is flipped to one i.e > 0, a hit should be made
        return sumMask > 0;
    }

    /// <summary>
    /// Calls the action after a 1 frame delay.
    /// Lambda syntax example: StartCoroutine(KongrooUtils.NextFrame(() => Foo()));
    /// </summary>
    public static IEnumerator NextFrame(System.Action action)
    {
        yield return null;
        action();
    }
}



/*
Vector Range Attribute by Just a Pixel (Danny Goodayle @DGoodayle) - http://www.justapixel.co.uk
Copyright (c) 2015
USAGE
[VectorRange(minX, maxX, minY, maxY, clamped)]
public Vector2 yourVector;
*/

#if UNITY_EDITOR
public class VectorRangeAttribute : PropertyAttribute
{
    public readonly float fMinX, fMaxX, fMinY, fMaxY;
    public readonly bool bClamp;

    public VectorRangeAttribute(float fMinX, float fMaxX, float fMinY, float fMaxY, bool bClamp)
    {
        this.fMinX = fMinX;
        this.fMaxX = fMaxX;
        this.fMinY = fMinY;
        this.fMaxY = fMaxY;
        this.bClamp = bClamp;
    }
}
#endif

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(VectorRangeAttribute))]
public class VectorRangeAttributeDrawer : PropertyDrawer
{
    const int helpHeight = 30;
    const int textHeight = 16;

    VectorRangeAttribute rangeAttribute
    {
        get { return (VectorRangeAttribute)attribute; }
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        Color previous = GUI.color;
        GUI.color = !IsValid(property) ? Color.red : Color.white;
        Rect textFieldPosition = position;
        textFieldPosition.width = position.width;
        textFieldPosition.height = position.height;
        EditorGUI.BeginChangeCheck();
        Vector2 val = EditorGUI.Vector2Field(textFieldPosition, label, property.vector2Value);
        if (EditorGUI.EndChangeCheck())
        {
            if (rangeAttribute.bClamp)
            {
                val.x = Mathf.Clamp(val.x, rangeAttribute.fMinX, rangeAttribute.fMaxX);
                val.y = Mathf.Clamp(val.y, rangeAttribute.fMinY, rangeAttribute.fMaxY);
            }

            property.vector2Value = val;
        }

        Rect helpPosition = position;
        helpPosition.y += 16;
        helpPosition.height = 16;
        DrawHelpBox(helpPosition, property);
        GUI.color = previous;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!IsValid(property))
        {
            return 32;
        }

        return base.GetPropertyHeight(property, label);
    }

    void DrawHelpBox(Rect position, SerializedProperty prop)
    {
        // No need for a help box if the pattern is valid.
        if (IsValid(prop))
            return;

        EditorGUI.HelpBox(position,
            string.Format("Invalid Range X [{0}]-[{1}] Y [{2}]-[{3}]", rangeAttribute.fMinX, rangeAttribute.fMaxX,
                rangeAttribute.fMinY, rangeAttribute.fMaxY), MessageType.Error);
    }

    bool IsValid(SerializedProperty prop)
    {
        Vector2 vector = prop.vector2Value;
        return vector.x >= rangeAttribute.fMinX && vector.x <= rangeAttribute.fMaxX &&
               vector.y >= rangeAttribute.fMinY && vector.y <= rangeAttribute.fMaxY;
    }
}
#endif