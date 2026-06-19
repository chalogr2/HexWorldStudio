using System.Collections.Generic;
using System.Linq;
using HexWorldStudio.Content;
using UnityEditor;
using UnityEngine;

namespace HexWorldStudio.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for <see cref="BiomeDefinition"/> that adds a
    /// 2D Whittaker-style chart of every land biome's climate envelope
    /// (T × M) above the default fields, with drag handles on the
    /// active biome's rectangle and a coverage diagnostic row. Click
    /// any other biome's rectangle to ping it in the Project view.
    /// Also routes the <c>Prefab</c> field through
    /// <see cref="PermissivePrefabField"/> so Prefab Variants of FBX
    /// models can be assigned (Unity 6's default picker rejects those).
    /// </summary>
    [CustomEditor(typeof(BiomeDefinition))]
    [CanEditMultipleObjects]
    internal sealed class BiomeDefinitionEditor : UnityEditor.Editor
    {
        private const float ChartSize = 220f;
        private const float ChartLeftPadding = 24f;   // room for "1.0 / 0.0" Y-axis ticks
        private const float ChartBottomPadding = 18f; // room for "0.0 / 1.0" X-axis ticks
        private const float EdgePixelThreshold = 6f;
        private const float SnapIncrement = 0.05f;

        private enum DragMode { None, LeftEdge, RightEdge, TopEdge, BottomEdge, Interior }

        private BiomeDefinition[] _allBiomes;
        private DragMode _dragMode = DragMode.None;
        private Vector2 _dragStartMouse;
        private Vector4 _dragStartEnvelope; // (minT, maxT, minM, maxM)

        private void OnEnable()
        {
            RefreshBiomeList();
        }

        private void RefreshBiomeList()
        {
            string[] guids = AssetDatabase.FindAssets("t:BiomeDefinition");
            var list = new List<BiomeDefinition>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BiomeDefinition b = AssetDatabase.LoadAssetAtPath<BiomeDefinition>(path);
                if (b != null) list.Add(b);
            }
            _allBiomes = list.ToArray();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            BiomeDefinition biome = (BiomeDefinition)target;

            if (biome.IsWater)
            {
                EditorGUILayout.HelpBox(
                    "Water biomes ignore the climate envelope — they're classified by topology (Coast vs Ocean based on neighbor land tiles).",
                    MessageType.Info);
            }
            else
            {
                if (_allBiomes == null) RefreshBiomeList();
                DrawEnvelopeChart(biome);
                DrawCoverageDiagnostics();
                EditorGUILayout.Space();
            }

            // Draw every default field except Prefab.
            DrawPropertiesExcluding(serializedObject, "Prefab", "m_Script");

            // Permissive Prefab field (accepts Variants-of-FBX via drag-drop, bypassing the two-pane picker filter).
            SerializedProperty prefabProp = serializedObject.FindProperty("Prefab");
            PermissivePrefabField.Draw(prefabProp);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawEnvelopeChart(BiomeDefinition active)
        {
            EditorGUILayout.LabelField("Climate Envelope (Temperature ↑ × Moisture →)", EditorStyles.boldLabel);

            float blockHeight = ChartSize + ChartBottomPadding + 4f;
            Rect block = GUILayoutUtility.GetRect(ChartSize + ChartLeftPadding, blockHeight, GUILayout.ExpandWidth(false));
            Rect chartRect = new Rect(block.x + ChartLeftPadding, block.y, ChartSize, ChartSize);

            // Background + grid.
            EditorGUI.DrawRect(chartRect, new Color(0.18f, 0.18f, 0.18f, 1f));
            DrawGrid(chartRect);

            // Axis tick labels.
            GUIStyle tickStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) },
                alignment = TextAnchor.MiddleRight,
            };
            EditorGUI.LabelField(new Rect(block.x, chartRect.yMin - 4, ChartLeftPadding - 2, 14), "1.0", tickStyle);
            EditorGUI.LabelField(new Rect(block.x, chartRect.yMax - 10, ChartLeftPadding - 2, 14), "0.0", tickStyle);
            GUIStyle xTickStyle = new GUIStyle(tickStyle) { alignment = TextAnchor.MiddleLeft };
            EditorGUI.LabelField(new Rect(chartRect.xMin, chartRect.yMax + 2, 24, 14), "0.0", xTickStyle);
            GUIStyle xTickStyleRight = new GUIStyle(tickStyle) { alignment = TextAnchor.MiddleRight };
            EditorGUI.LabelField(new Rect(chartRect.xMax - 24, chartRect.yMax + 2, 24, 14), "1.0", xTickStyleRight);

            // Draw every land biome rectangle. Skip water (their envelopes are unused).
            if (_allBiomes != null)
            {
                for (int i = 0; i < _allBiomes.Length; i++)
                {
                    BiomeDefinition b = _allBiomes[i];
                    if (b == null || b.IsWater) continue;
                    DrawBiomeRect(b, chartRect, isActive: b == active);
                }
            }

            HandleChartMouse(chartRect, active);
        }

        private static void DrawGrid(Rect chart)
        {
            Color grid = new Color(0.32f, 0.32f, 0.32f, 1f);
            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                float x = chart.x + t * chart.width;
                float y = chart.y + t * chart.height;
                EditorGUI.DrawRect(new Rect(x, chart.yMin, 1, chart.height), grid);
                EditorGUI.DrawRect(new Rect(chart.xMin, y, chart.width, 1), grid);
            }
        }

        private static void DrawBiomeRect(BiomeDefinition b, Rect chartRect, bool isActive)
        {
            Rect br = EnvelopeToScreen(b, chartRect);

            Color fill = b.Color;
            fill.a = isActive ? 0.78f : 0.42f;
            EditorGUI.DrawRect(br, fill);

            // 1px border (2px on active).
            int t = isActive ? 2 : 1;
            Color border = isActive ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            EditorGUI.DrawRect(new Rect(br.xMin, br.yMin, br.width, t), border);
            EditorGUI.DrawRect(new Rect(br.xMin, br.yMax - t, br.width, t), border);
            EditorGUI.DrawRect(new Rect(br.xMin, br.yMin, t, br.height), border);
            EditorGUI.DrawRect(new Rect(br.xMax - t, br.yMin, t, br.height), border);

            // Label centered in rect (skip if rectangle is too tiny to read).
            if (br.width > 24 && br.height > 12)
            {
                GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                };
                string label = string.IsNullOrEmpty(b.BiomeName) ? b.name : b.BiomeName;
                EditorGUI.LabelField(br, label, labelStyle);
            }
        }

        private static Rect EnvelopeToScreen(BiomeDefinition b, Rect chartRect)
        {
            float minT = Mathf.Clamp01(b.MinTemperature);
            float maxT = Mathf.Clamp01(b.MaxTemperature);
            float minM = Mathf.Clamp01(b.MinMoisture);
            float maxM = Mathf.Clamp01(b.MaxMoisture);
            float x = chartRect.x + minM * chartRect.width;
            float y = chartRect.y + (1f - maxT) * chartRect.height;
            float w = (maxM - minM) * chartRect.width;
            float h = (maxT - minT) * chartRect.height;
            return new Rect(x, y, w, h);
        }

        private void HandleChartMouse(Rect chartRect, BiomeDefinition active)
        {
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!chartRect.Contains(e.mousePosition)) return;

                    Rect activeRect = EnvelopeToScreen(active, chartRect);
                    _dragMode = GetDragMode(e.mousePosition, activeRect);

                    if (_dragMode != DragMode.None)
                    {
                        _dragStartMouse = e.mousePosition;
                        _dragStartEnvelope = new Vector4(
                            active.MinTemperature, active.MaxTemperature,
                            active.MinMoisture,    active.MaxMoisture);
                        e.Use();
                    }
                    else
                    {
                        // Click on another biome's rect → ping it in the Project view.
                        if (_allBiomes != null)
                        {
                            for (int i = 0; i < _allBiomes.Length; i++)
                            {
                                BiomeDefinition b = _allBiomes[i];
                                if (b == null || b == active || b.IsWater) continue;
                                if (EnvelopeToScreen(b, chartRect).Contains(e.mousePosition))
                                {
                                    EditorGUIUtility.PingObject(b);
                                    Selection.activeObject = b;
                                    e.Use();
                                    break;
                                }
                            }
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_dragMode == DragMode.None) return;
                    ApplyDrag(chartRect, active, e.mousePosition);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (_dragMode != DragMode.None)
                    {
                        _dragMode = DragMode.None;
                        e.Use();
                    }
                    break;
            }
        }

        private void ApplyDrag(Rect chartRect, BiomeDefinition active, Vector2 mouse)
        {
            Vector2 delta = mouse - _dragStartMouse;
            float dT = -delta.y / chartRect.height; // Y is flipped (1.0 at top)
            float dM = delta.x / chartRect.width;

            float minT = _dragStartEnvelope.x;
            float maxT = _dragStartEnvelope.y;
            float minM = _dragStartEnvelope.z;
            float maxM = _dragStartEnvelope.w;

            switch (_dragMode)
            {
                case DragMode.LeftEdge:
                    minM = Snap(Mathf.Clamp(minM + dM, 0f, maxM - SnapIncrement));
                    break;
                case DragMode.RightEdge:
                    maxM = Snap(Mathf.Clamp(maxM + dM, minM + SnapIncrement, 1f));
                    break;
                case DragMode.TopEdge:
                    maxT = Snap(Mathf.Clamp(maxT + dT, minT + SnapIncrement, 1f));
                    break;
                case DragMode.BottomEdge:
                    minT = Snap(Mathf.Clamp(minT + dT, 0f, maxT - SnapIncrement));
                    break;
                case DragMode.Interior:
                    float widthM = maxM - minM;
                    float widthT = maxT - minT;
                    minM = Snap(Mathf.Clamp(minM + dM, 0f, 1f - widthM));
                    maxM = minM + widthM;
                    minT = Snap(Mathf.Clamp(minT + dT, 0f, 1f - widthT));
                    maxT = minT + widthT;
                    break;
            }

            serializedObject.FindProperty("MinTemperature").floatValue = minT;
            serializedObject.FindProperty("MaxTemperature").floatValue = maxT;
            serializedObject.FindProperty("MinMoisture").floatValue = minM;
            serializedObject.FindProperty("MaxMoisture").floatValue = maxM;
            serializedObject.ApplyModifiedProperties();
        }

        private static float Snap(float v) => Mathf.Round(v / SnapIncrement) * SnapIncrement;

        private static DragMode GetDragMode(Vector2 mouse, Rect rect)
        {
            bool insideY = mouse.y >= rect.yMin - EdgePixelThreshold && mouse.y <= rect.yMax + EdgePixelThreshold;
            bool insideX = mouse.x >= rect.xMin - EdgePixelThreshold && mouse.x <= rect.xMax + EdgePixelThreshold;

            bool nearLeft   = insideY && Mathf.Abs(mouse.x - rect.xMin) <= EdgePixelThreshold;
            bool nearRight  = insideY && Mathf.Abs(mouse.x - rect.xMax) <= EdgePixelThreshold;
            bool nearTop    = insideX && Mathf.Abs(mouse.y - rect.yMin) <= EdgePixelThreshold;
            bool nearBottom = insideX && Mathf.Abs(mouse.y - rect.yMax) <= EdgePixelThreshold;

            if (nearLeft)   return DragMode.LeftEdge;
            if (nearRight)  return DragMode.RightEdge;
            if (nearTop)    return DragMode.TopEdge;
            if (nearBottom) return DragMode.BottomEdge;
            if (rect.Contains(mouse)) return DragMode.Interior;
            return DragMode.None;
        }

        private void DrawCoverageDiagnostics()
        {
            if (_allBiomes == null || _allBiomes.Length == 0) return;

            BiomeEnvelopeCoverage.Result result = BiomeEnvelopeCoverage.Compute(_allBiomes);

            if (result.GapFraction > 0.01f)
            {
                EditorGUILayout.HelpBox(
                    $"{result.GapFraction * 100f:F0}% of (T, M) space is uncovered. Land tiles falling into those gaps are assigned the first non-water biome as a defensive fallback.",
                    MessageType.Warning);
            }

            if (result.OverlapPairs.Count > 0)
            {
                string overlaps = string.Join(", ",
                    result.OverlapPairs.Take(3).Select(p =>
                        $"{Label(p.A)} & {Label(p.B)}"));
                if (result.OverlapPairs.Count > 3)
                {
                    overlaps += $" (+{result.OverlapPairs.Count - 3} more)";
                }
                EditorGUILayout.HelpBox(
                    $"Overlaps: {overlaps}. First-match-wins resolves these at runtime in WorldGenerationSettings.Biomes order — fine if intentional.",
                    MessageType.Info);
            }
        }

        private static string Label(BiomeDefinition b)
            => string.IsNullOrEmpty(b.BiomeName) ? b.name : b.BiomeName;
    }
}
