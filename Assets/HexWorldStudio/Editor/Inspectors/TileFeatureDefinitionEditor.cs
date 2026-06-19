using HexWorldStudio.Content;
using UnityEditor;

namespace HexWorldStudio.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for <see cref="TileFeatureDefinition"/> — same
    /// default fields, but the <c>Prefab</c> slot is routed through
    /// <see cref="PermissivePrefabField"/> so Prefab Variants whose
    /// source is an FBX / Model importer can be drag-dropped onto the
    /// field (the default Unity 6 two-pane Object Picker rejects those
    /// with "Content type mismatch").
    /// </summary>
    [CustomEditor(typeof(TileFeatureDefinition))]
    [CanEditMultipleObjects]
    internal sealed class TileFeatureDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "Prefab", "m_Script");
            PermissivePrefabField.Draw(serializedObject.FindProperty("Prefab"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
