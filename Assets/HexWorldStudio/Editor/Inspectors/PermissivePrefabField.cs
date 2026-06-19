using UnityEditor;
using UnityEngine;

namespace HexWorldStudio.Editor.Inspectors
{
    /// <summary>
    /// Draws an inspector field for a <c>GameObject</c>-typed
    /// <see cref="SerializedProperty"/> that accepts drag-drop of ANY
    /// GameObject-rooted asset — including Prefab Variants whose
    /// source is an FBX / Model importer. The default Unity 6 two-pane
    /// Object Picker classifies those as content-type "Model" and
    /// rejects them on <c>GameObject</c> fields with "Content type
    /// mismatch"; this helper bypasses that validation by accepting
    /// drag-drop directly on the field rect.
    /// </summary>
    /// <remarks>
    /// The standard <see cref="EditorGUI.ObjectField"/> circle picker
    /// still appears and works for regular Prefabs and scene
    /// GameObjects. Drag-drop is the escape hatch for the Variant /
    /// Model case the picker rejects.
    /// </remarks>
    internal static class PermissivePrefabField
    {
        public static void Draw(SerializedProperty prefabProperty, string label = "Prefab")
        {
            if (prefabProperty == null) return;

            Rect rect = EditorGUILayout.GetControlRect(true);

            // Standard ObjectField — handles regular Prefabs + scene GameObjects via the picker.
            EditorGUI.BeginChangeCheck();
            Object newValue = EditorGUI.ObjectField(rect, label, prefabProperty.objectReferenceValue, typeof(GameObject), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                prefabProperty.objectReferenceValue = newValue;
            }

            // Drag-drop bypass: accept any GameObject reference dropped on the field rect,
            // even when the two-pane picker would have rejected it as "Content type mismatch".
            Event evt = Event.current;
            if (rect.Contains(evt.mousePosition)
                && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
            {
                GameObject dragged = FirstGameObject(DragAndDrop.objectReferences);
                if (dragged != null)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        prefabProperty.objectReferenceValue = dragged;
                        evt.Use();
                    }
                }
            }
        }

        private static GameObject FirstGameObject(Object[] objects)
        {
            if (objects == null) return null;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is GameObject go) return go;
            }
            return null;
        }
    }
}
