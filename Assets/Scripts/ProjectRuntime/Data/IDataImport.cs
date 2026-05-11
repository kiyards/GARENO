using System;

// DXxxxxx should implement this
// And implement editor class like this:
// [CustomEditor(typeof(DXxxxxx))]
// public class DXxxxxxEditor : MultiTextBoxEditor<DXxxxxx> {}
public interface IDataImport
{
    // Data subclasses should implement this
    public static void ImportData(string text) => throw new NotImplementedException();
}
