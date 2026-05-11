using UnityEditor;

#if UNITY_EDITOR
[CustomEditor(typeof(DModule))]
public class DModuleEditor : MultiTextBoxEditor<DModule> { }
#endif