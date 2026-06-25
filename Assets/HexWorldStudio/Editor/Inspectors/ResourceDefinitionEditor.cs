using System.Collections.Generic;
using HexWorldStudio.Content;
using UnityEditor;
using UnityEngine;

namespace HexWorldStudio.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for <see cref="ResourceDefinition"/>. Two
    /// responsibilities:
    /// <list type="number">
    /// <item>Route the <c>Prefab</c> field through
    /// <see cref="PermissivePrefabField"/> so Prefab Variants of
    /// FBX / Model assets can be drag-dropped onto it (the same
    /// workaround used for <see cref="BiomeDefinitionEditor"/> and
    /// <see cref="TileFeatureDefinitionEditor"/>).</item>
    /// <item>When <c>ReplaceTile</c> is on (ADR-0023), auto-sync the
    /// <c>ReplacementPrefabs</c> array to the cross-product of
    /// <c>AllowedBiomes</c> × <c>AllowedFeatures</c>: one prefab slot
    /// per combo the resource can land on. Existing prefab assignments
    /// for combos that survive the resync are preserved by (Biome,
    /// Feature) key match.</item>
    /// </list>
    /// </summary>
    [CustomEditor(typeof(ResourceDefinition))]
    [CanEditMultipleObjects]
    internal sealed class ResourceDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ResourceDefinition resource = (ResourceDefinition)target;

            // Draw every default field EXCEPT Prefab (permissive picker
            // below) and ReplacementPrefabs (custom layout further down).
            DrawPropertiesExcluding(serializedObject, "Prefab", "ReplacementPrefabs", "m_Script");

            // Permissive Prefab field — accepts Variants-of-FBX via drag-drop.
            PermissivePrefabField.Draw(serializedObject.FindProperty("Prefab"));

            EditorGUILayout.Space();

            // Replace-tile section — only meaningful when the flag is on.
            if (resource.ReplaceTile)
            {
                SyncReplacementEntries(resource);
                DrawReplacementEntries();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // Rebuilds resource.ReplacementPrefabs from the cross-product
        // of AllowedBiomes × AllowedFeatures, preserving the Prefab
        // reference of any entry whose (Biome, Feature) key survives.
        // Only writes back when the entry set actually changed, so the
        // SerializedObject isn't marked dirty on every repaint.
        private static void SyncReplacementEntries(ResourceDefinition resource)
        {
            BiomeDefinition[] biomes = resource.AllowedBiomes;
            TileFeatureDefinition[] features = resource.AllowedFeatures;

            // Build the expected pair list. Wildcards (empty / null
            // arrays) collapse the corresponding axis to a single null
            // entry — buyer's prefab on that slot applies to every
            // value of that axis.
            var expected = new List<(BiomeDefinition, TileFeatureDefinition)>();
            bool biomeWildcard = biomes == null || biomes.Length == 0;
            bool featureWildcard = features == null || features.Length == 0;

            if (biomeWildcard && featureWildcard)
            {
                expected.Add((null, null));
            }
            else if (biomeWildcard)
            {
                for (int f = 0; f < features.Length; f++) expected.Add((null, features[f]));
            }
            else if (featureWildcard)
            {
                for (int b = 0; b < biomes.Length; b++) expected.Add((biomes[b], null));
            }
            else
            {
                for (int b = 0; b < biomes.Length; b++)
                    for (int f = 0; f < features.Length; f++)
                        expected.Add((biomes[b], features[f]));
            }

            ResourceReplacementEntry[] current = resource.ReplacementPrefabs ?? new ResourceReplacementEntry[0];

            // Quick path: if counts match and every expected pair already
            // exists in `current` (same order), no resync needed.
            if (current.Length == expected.Count)
            {
                bool identical = true;
                for (int i = 0; i < expected.Count; i++)
                {
                    if (current[i].Biome != expected[i].Item1 || current[i].Feature != expected[i].Item2)
                    {
                        identical = false; break;
                    }
                }
                if (identical) return;
            }

            // Resync needed: build the new entries, preserving existing
            // Prefab refs by (Biome, Feature) key match.
            var rebuilt = new ResourceReplacementEntry[expected.Count];
            for (int i = 0; i < expected.Count; i++)
            {
                GameObject keepPrefab = null;
                for (int j = 0; j < current.Length; j++)
                {
                    if (current[j].Biome == expected[i].Item1 && current[j].Feature == expected[i].Item2)
                    {
                        keepPrefab = current[j].Prefab;
                        break;
                    }
                }
                rebuilt[i] = new ResourceReplacementEntry
                {
                    Biome = expected[i].Item1,
                    Feature = expected[i].Item2,
                    Prefab = keepPrefab,
                };
            }

            Undo.RecordObject(resource, "Sync Resource Replacement Entries");
            resource.ReplacementPrefabs = rebuilt;
            EditorUtility.SetDirty(resource);
        }

        // Renders the auto-synced ReplacementPrefabs array as a list
        // of (Biome label + Feature label + Prefab slot) rows, grouped
        // visually by biome. The biome / feature labels are read-only
        // — they reflect the cross-product and can't be edited inline
        // (buyer changes them by editing AllowedBiomes / AllowedFeatures
        // above, which triggers a resync on the next repaint).
        private void DrawReplacementEntries()
        {
            SerializedProperty arrayProp = serializedObject.FindProperty("ReplacementPrefabs");
            if (arrayProp == null) return;

            EditorGUILayout.LabelField("Replacement Prefabs (per biome × feature combo)", EditorStyles.boldLabel);

            if (arrayProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Add at least one biome to AllowedBiomes or one feature to AllowedFeatures above. " +
                    "Each combo gets its own prefab slot here.",
                    MessageType.Info);
                return;
            }

            BiomeDefinition currentGroupBiome = null;
            bool firstRow = true;

            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                SerializedProperty entry = arrayProp.GetArrayElementAtIndex(i);
                SerializedProperty biomeProp = entry.FindPropertyRelative("Biome");
                SerializedProperty featureProp = entry.FindPropertyRelative("Feature");
                SerializedProperty prefabProp = entry.FindPropertyRelative("Prefab");

                Object biomeObj = biomeProp.objectReferenceValue;
                Object featureObj = featureProp.objectReferenceValue;

                // Visually group consecutive rows that share a biome.
                BiomeDefinition biomeRef = biomeObj as BiomeDefinition;
                if (firstRow || biomeRef != currentGroupBiome)
                {
                    if (!firstRow) EditorGUILayout.Space(2);
                    string biomeLabel = biomeRef != null
                        ? (string.IsNullOrEmpty(biomeRef.BiomeName) ? biomeRef.name : biomeRef.BiomeName)
                        : "(Any biome)";
                    EditorGUILayout.LabelField(biomeLabel, EditorStyles.miniBoldLabel);
                    currentGroupBiome = biomeRef;
                    firstRow = false;
                }

                TileFeatureDefinition featureRef = featureObj as TileFeatureDefinition;
                string featureLabel = featureRef != null
                    ? (string.IsNullOrEmpty(featureRef.FeatureName) ? featureRef.name : featureRef.FeatureName)
                    : "(Any feature)";

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("  " + featureLabel, GUILayout.Width(180));
                    EditorGUI.BeginChangeCheck();
                    Object newPrefab = EditorGUILayout.ObjectField(prefabProp.objectReferenceValue, typeof(GameObject), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        prefabProp.objectReferenceValue = newPrefab;
                    }
                }
            }
        }
    }
}
