using UnityEditor;

#if UNITY_EDITOR
[CustomEditor(typeof(DAudio))]
public class DAudioEditor : MultiTextBoxEditor<DAudio> { }
#endif
