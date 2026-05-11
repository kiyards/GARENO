using UnityEditor;

#if UNITY_EDITOR
[CustomEditor(typeof(DWorld))]
public class DWorldEditor : MultiTextBoxEditor<DWorld> { }
#endif