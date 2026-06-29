using UnityEditor;

#if UNITY_EDITOR
[CustomEditor(typeof(DCard))]
public class DCardEditor : MultiTextBoxEditor<DCard> { }
#endif
