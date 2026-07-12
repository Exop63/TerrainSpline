// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using System.Collections.Generic;
using System.IO;
using System.Linq;
using TerrainSplinePlugin.Editor.Core;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Main editor window for the Terrain Spline Plugin.
    /// Provides UI for managing splines, configuring brush/paint settings,
    /// and applying operations to terrain.
    /// Opened via Tools > Terrain Spline Tool.
    /// </summary>
    public class TerrainSplineWindow : EditorWindow
    {
        // ─────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────
        private Terrain targetTerrain;
        private List<TerrainSplineData> splineAssets = new List<TerrainSplineData>();
        private TerrainSplineData selectedSpline;
        private Vector2 scrollPosition;
        private Vector2 splineListScroll;
        
        private ReorderableList splineReorderableList;
        private int renamingIndex = -1;

        // New spline creation

        private string newSplineName = "MySpline";

        // Leveling
        private float levelingValue = 0f;

        // Foldout states
        private bool showTargets = true;
        private bool showMode = true;
        private bool showBrush = true;
        private bool showHeight = true;
        private bool showPaint = true;
        private bool showSplineList = true;
        private bool showLeveling = false;

        // Styles
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private GUIStyle sectionHeaderStyle;
        private GUIStyle modernLabelStyle;
        private bool stylesInitialized;

        // Unity Icons (cached)
        private Texture2D iconTerrain;
        private Texture2D iconPath;
        private Texture2D iconShape;
        private Texture2D iconBrush;
        private Texture2D iconHeight;
        private Texture2D iconPaint;
        private Texture2D iconTarget;
        private Texture2D iconList;
        private Texture2D iconSettings;
        private Texture2D iconAdd;
        private Texture2D iconRefresh;
        private Texture2D iconUndo;
        private Texture2D iconRedo;
        private Texture2D iconApply;
        private Texture2D iconLeveling;

        // Gizmo State
        private static Vector3 _gizmoPosition = Vector3.zero;
        private static Quaternion _gizmoRotation = Quaternion.identity;
        private static Vector3 _gizmoScale = Vector3.one;

        // ─────────────────────────────────────────────
        // Menu Item
        // ─────────────────────────────────────────────

        [MenuItem("Tools/Terrain Spline Tool")]
        public static void ShowWindow()
        {
            var window = GetWindow<TerrainSplineWindow>("Terrain Spline Tool");
            window.minSize = new Vector2(320, 500);
        }

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void OnEnable()
        {
            RefreshSplineAssets();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            if (TerrainSplineTool.OwnerWindow == this)
                TerrainSplineTool.OwnerWindow = null;
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(2, 0, 2, 2)
            };

            boxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(4, 4, 2, 4)
            };

            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };

            modernLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = true
            };

            // Cache Unity built-in icons
            iconTerrain = EditorGUIUtility.FindTexture("Terrain Icon");
            iconPath = EditorGUIUtility.FindTexture("d_EditCollider");
            iconShape = EditorGUIUtility.FindTexture("d_Grid.FillTool");
            iconBrush = EditorGUIUtility.FindTexture("d_PreTextureMIBC");
            iconHeight = EditorGUIUtility.FindTexture("d_TerrainInspector.TerrainToolRaise");
            iconPaint = EditorGUIUtility.FindTexture("d_TerrainInspector.TerrainToolSplat");
            iconTarget = EditorGUIUtility.FindTexture("d_FilterByType");
            iconList = EditorGUIUtility.FindTexture("d_Favorite");
            iconSettings = EditorGUIUtility.FindTexture("d_Settings");
            iconAdd = EditorGUIUtility.FindTexture("d_CreateAddNew");
            iconRefresh = EditorGUIUtility.FindTexture("d_Refresh");
            iconUndo = EditorGUIUtility.FindTexture("d_back");
            iconRedo = EditorGUIUtility.FindTexture("d_forward");
            iconApply = EditorGUIUtility.FindTexture("d_Valid");
            iconLeveling = EditorGUIUtility.FindTexture("d_TerrainInspector.TerrainToolSetHeight");

            stylesInitialized = true;
        }

        private GUIContent IconContent(string text, Texture2D icon)
        {
            return icon != null ? new GUIContent(" " + text, icon) : new GUIContent(text);
        }

        // ─────────────────────────────────────────────
        // Main GUI
        // ─────────────────────────────────────────────

        private void OnGUI()
        {
            InitStyles();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawHeader();
            EditorGUILayout.Space(4);
            DrawTargetsSection();
            EditorGUILayout.Space(2);
            DrawSplineListSection();
            EditorGUILayout.Space(2);

            if (selectedSpline != null)
            {
                DrawModeSection();
                EditorGUILayout.Space(2);
                DrawBrushSection();
                EditorGUILayout.Space(2);

                if (selectedSpline.applyHeight)
                    DrawHeightSection();
                if (selectedSpline.applyPaint)
                    DrawPaintSection();

                EditorGUILayout.Space(4);
                DrawActionsSection();
            }

            EditorGUILayout.Space(2);
            DrawLevelingSection();

            EditorGUILayout.EndScrollView();
        }

        // ─────────────────────────────────────────────
        // Header
        // ─────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUIContent headerContent = IconContent("Terrain Spline Tool", iconTerrain);
            GUILayout.Label(headerContent, headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(IconContent("Refresh", iconRefresh), EditorStyles.toolbarButton, GUILayout.Width(75)))
            {
                RefreshSplineAssets();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────
        // Targets Section
        // ─────────────────────────────────────────────

        private void DrawTargetsSection()
        {
            showTargets = EditorGUILayout.Foldout(showTargets, IconContent("Targets", iconTarget), true, EditorStyles.foldoutHeader);
            if (!showTargets) return;

            EditorGUILayout.BeginVertical(boxStyle);

            targetTerrain = (Terrain)EditorGUILayout.ObjectField("Terrain", targetTerrain, typeof(Terrain), true);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(IconContent("Find Refs", iconRefresh), GUILayout.Height(22)))
            {
                targetTerrain = FindObjectOfType<Terrain>();
                if (targetTerrain != null)
                    Debug.Log($"[TerrainSpline] Found terrain: {targetTerrain.name}");
                else
                    Debug.LogWarning("[TerrainSpline] No terrain found in scene.");
            }
            EditorGUILayout.EndHorizontal();

            // Show terrain info
            if (targetTerrain != null)
            {
                EditorGUI.indentLevel++;
                TerrainData td = targetTerrain.terrainData;
                EditorGUILayout.LabelField("Size", $"{td.size.x} x {td.size.y} x {td.size.z}");
                EditorGUILayout.LabelField("Heightmap", $"{td.heightmapResolution} x {td.heightmapResolution}");
                EditorGUILayout.LabelField("Layers", $"{td.alphamapLayers}");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Spline List Section
        // ─────────────────────────────────────────────

        private void DrawSplineListSection()
        {
            showSplineList = EditorGUILayout.Foldout(showSplineList, IconContent($"Splines ({splineAssets.Count})", iconList), true, EditorStyles.foldoutHeader);
            if (!showSplineList) return;

            EditorGUILayout.BeginVertical(boxStyle);

            // Create buttons
            EditorGUILayout.BeginHorizontal();
            newSplineName = EditorGUILayout.TextField(newSplineName, GUILayout.Width(120));
            if (GUILayout.Button(IconContent("Add", iconAdd), GUILayout.Height(20)))
            {
                CreateNewSpline(SplineMode.Path, newSplineName);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Spline list
            if (splineAssets.Count == 0)
            {
                EditorGUILayout.HelpBox("No splines created yet. Click 'Add' to create one.", MessageType.Info);
            }
            else
            {
                splineListScroll = EditorGUILayout.BeginScrollView(splineListScroll, GUILayout.MaxHeight(300));
                
                if (splineReorderableList == null)
                    InitReorderableList();

                splineReorderableList.DoLayoutList();

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Mode Section
        // ─────────────────────────────────────────────

        private void DrawModeSection()
        {
            showMode = EditorGUILayout.Foldout(showMode, IconContent("Operations", iconSettings), true, EditorStyles.foldoutHeader);
            if (!showMode) return;

            EditorGUILayout.BeginVertical(boxStyle);

            // Operation toggles
            EditorGUILayout.BeginHorizontal();
            bool newApplyHeight = GUILayout.Toggle(selectedSpline.applyHeight, IconContent("Height", iconHeight), "Button", GUILayout.Height(24));
            if (newApplyHeight != selectedSpline.applyHeight)
            {
                EditorApplication.delayCall += () => { if (selectedSpline != null) { selectedSpline.applyHeight = newApplyHeight; Repaint(); } };
            }

            bool newApplyPaint = GUILayout.Toggle(selectedSpline.applyPaint, IconContent("Paint", iconPaint), "Button", GUILayout.Height(24));
            if (newApplyPaint != selectedSpline.applyPaint)
            {
                EditorApplication.delayCall += () => { if (selectedSpline != null) { selectedSpline.applyPaint = newApplyPaint; Repaint(); } };
            }
            EditorGUILayout.EndHorizontal();

            // Closed loop (only for Path mode)
            if (selectedSpline.splineMode == SplineMode.Path)
            {
                selectedSpline.isClosedLoop = EditorGUILayout.Toggle("Closed Loop", selectedSpline.isClosedLoop);
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Toggle("Closed Loop", true);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.HelpBox("Shape mode always uses closed loop.", MessageType.Info);
            }

            EditorUtility.SetDirty(selectedSpline);
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Brush Section
        // ─────────────────────────────────────────────

        private void DrawBrushSection()
        {
            showBrush = EditorGUILayout.Foldout(showBrush, IconContent("Brush", iconBrush), true, EditorStyles.foldoutHeader);
            if (!showBrush) return;

            EditorGUILayout.BeginVertical(boxStyle);

            Undo.RecordObject(selectedSpline, "Change Brush Settings");

            selectedSpline.brushSize = EditorGUILayout.Slider("Size (m)", selectedSpline.brushSize, 0.1f, 200f);
            selectedSpline.brushHardness = EditorGUILayout.Slider("Hardness", selectedSpline.brushHardness, 0f, 1f);
            selectedSpline.brushStrength = EditorGUILayout.Slider("Strength", selectedSpline.brushStrength, 0f, 1f);
            selectedSpline.sampleStep = EditorGUILayout.Slider("Sample Step (m)", selectedSpline.sampleStep, 0.05f, 30f);

            // Visual falloff hint
            EditorGUILayout.Space(2);
            string hardnessDesc = selectedSpline.brushHardness < 0.3f ? "Soft (gradual)" :
                                  selectedSpline.brushHardness < 0.7f ? "Medium (linear)" : "Hard (sharp)";
            EditorGUILayout.LabelField("Falloff", hardnessDesc, EditorStyles.miniLabel);

            EditorUtility.SetDirty(selectedSpline);
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Height Section
        // ─────────────────────────────────────────────

        private void DrawHeightSection()
        {
            showHeight = EditorGUILayout.Foldout(showHeight, IconContent("Height", iconHeight), true, EditorStyles.foldoutHeader);
            if (!showHeight) return;

            EditorGUILayout.BeginVertical(boxStyle);

            Undo.RecordObject(selectedSpline, "Change Height Settings");

            selectedSpline.heightOffset = EditorGUILayout.FloatField("Height Offset", selectedSpline.heightOffset);

            if (selectedSpline.splineMode == SplineMode.Shape)
            {
                selectedSpline.shapeUseSplineHeight = EditorGUILayout.Toggle("Use Spline Heights", selectedSpline.shapeUseSplineHeight);

                if (!selectedSpline.shapeUseSplineHeight)
                {
                    selectedSpline.shapeFillHeight = EditorGUILayout.FloatField("Fill Height", selectedSpline.shapeFillHeight);
                }

                selectedSpline.shapeEdgeFalloff = EditorGUILayout.Slider("Edge Falloff (m)", selectedSpline.shapeEdgeFalloff, 0f, 50f);
            }

            EditorUtility.SetDirty(selectedSpline);
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Paint Section
        // ─────────────────────────────────────────────

        private void DrawPaintSection()
        {
            showPaint = EditorGUILayout.Foldout(showPaint, IconContent("Paint", iconPaint), true, EditorStyles.foldoutHeader);
            if (!showPaint) return;

            EditorGUILayout.BeginVertical(boxStyle);

            Undo.RecordObject(selectedSpline, "Change Paint Settings");

            Terrain t = selectedSpline.targetTerrain ?? targetTerrain;

            if (t != null && t.terrainData.terrainLayers != null && t.terrainData.terrainLayers.Length > 0)
            {
                TerrainLayer[] layers = t.terrainData.terrainLayers;

                // Layer palette with thumbnails
                EditorGUILayout.LabelField("Layer Palette", EditorStyles.boldLabel);

                int cols = Mathf.Max(1, (int)(EditorGUIUtility.currentViewWidth - 40) / 68);
                int selectedLayer = selectedSpline.paintLayerIndex;

                for (int i = 0; i < layers.Length; i++)
                {
                    if (i % cols == 0)
                        EditorGUILayout.BeginHorizontal();

                    bool isLayerSelected = (i == selectedLayer);
                    GUI.backgroundColor = isLayerSelected ? Color.cyan : Color.white;

                    Texture2D thumbnail = layers[i] != null && layers[i].diffuseTexture != null
                        ? AssetPreview.GetAssetPreview(layers[i].diffuseTexture) ?? layers[i].diffuseTexture
                        : Texture2D.grayTexture;

                    GUIContent content = new GUIContent(thumbnail, layers[i] != null ? layers[i].name : $"Layer {i}");

                    if (GUILayout.Button(content, GUILayout.Width(64), GUILayout.Height(64)))
                    {
                        selectedSpline.paintLayerIndex = i;
                    }

                    GUI.backgroundColor = Color.white;

                    if (i % cols == cols - 1 || i == layers.Length - 1)
                        EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(4);
            }
            else
            {
                EditorGUILayout.HelpBox("No terrain layers found. Add layers to your terrain first.", MessageType.Warning);
            }

            selectedSpline.paintLayerIndex = EditorGUILayout.IntField("Layer Index", selectedSpline.paintLayerIndex);
            selectedSpline.paintStrength = EditorGUILayout.Slider("Paint Strength", selectedSpline.paintStrength, 0f, 1f);
            selectedSpline.paintBlend = EditorGUILayout.Slider("Paint Blend", selectedSpline.paintBlend, 0f, 1f);

            EditorUtility.SetDirty(selectedSpline);
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Actions Section
        // ─────────────────────────────────────────────

        private void DrawActionsSection()
        {
            EditorGUILayout.BeginVertical(boxStyle);

            EditorGUILayout.LabelField(IconContent("Actions", iconApply), headerStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(IconContent("Undo", iconUndo), GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => Undo.PerformUndo();
            }

            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button(IconContent("Apply", iconApply), GUILayout.Height(30)))
            {
                if (selectedSpline != null)
                {
                    TerrainPreviewManager.CommitPreview();
                    
                    // Ensure terrain reference
                    if (selectedSpline.targetTerrain == null)
                        selectedSpline.targetTerrain = targetTerrain;

                    TerrainModifier.ApplySpline(selectedSpline);
                    Debug.Log($"[TerrainSpline] Applied '{selectedSpline.displayName}' to terrain.");
                }
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button(IconContent("Redo", iconRedo), GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => Undo.PerformRedo();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Leveling Section
        // ─────────────────────────────────────────────

        private void DrawLevelingSection()
        {
            showLeveling = EditorGUILayout.Foldout(showLeveling, IconContent("Leveling", iconLeveling), true, EditorStyles.foldoutHeader);
            if (!showLeveling) return;

            EditorGUILayout.BeginVertical(boxStyle);

            levelingValue = EditorGUILayout.Slider("Value (m)", levelingValue, -100f, 100f);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Offset", GUILayout.Height(24)))
            {
                if (targetTerrain != null)
                    TerrainModifier.OffsetTerrain(targetTerrain, levelingValue);
            }

            if (GUILayout.Button("Level", GUILayout.Height(24)))
            {
                if (targetTerrain != null)
                    TerrainModifier.LevelTerrain(targetTerrain, levelingValue);
            }

            if (GUILayout.Button("Clear Paint", GUILayout.Height(24)))
            {
                if (targetTerrain != null)
                {
                    if (EditorUtility.DisplayDialog("Clear Paint",
                        "Reset all terrain paint to the first layer?", "Clear", "Cancel"))
                    {
                        TerrainModifier.ClearPaint(targetTerrain);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        // Spline Management
        // ─────────────────────────────────────────────

        private void CreateNewSpline(SplineMode mode, string customName)
        {
            // Save to project Assets folder (not inside the package, which may be immutable)
            string dir = "Assets/TerrainSplineData";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "TerrainSplineData");
            }

            // Create unique name
            string baseName = string.IsNullOrEmpty(customName) ? (mode == SplineMode.Path ? "Path" : "Shape") : customName;
            int index = 0;
            string assetName = baseName;
            
            if (AssetDatabase.LoadAssetAtPath<TerrainSplineData>($"{dir}/{assetName}.asset") != null)
            {
                index = 1;
                assetName = $"{baseName}_{index:D2}";
            }

            // Check for existing file and increment
            while (AssetDatabase.LoadAssetAtPath<TerrainSplineData>($"{dir}/{assetName}.asset") != null)
            {
                index++;
                assetName = $"{baseName}_{index:D2}";
            }

            TerrainSplineData newSpline = CreateInstance<TerrainSplineData>();
            newSpline.displayName = assetName;
            newSpline.splineMode = mode;
            newSpline.targetTerrain = targetTerrain;

            if (mode == SplineMode.Shape)
                newSpline.isClosedLoop = true;

            AssetDatabase.CreateAsset(newSpline, $"{dir}/{assetName}.asset");
            AssetDatabase.SaveAssets();

            RefreshSplineAssets();
            selectedSpline = newSpline;
            Selection.activeObject = newSpline;

            Debug.Log($"[TerrainSpline] Created new {mode} spline: {assetName}");
        }

        private void DeleteSpline(TerrainSplineData spline)
        {
            if (selectedSpline == spline)
                selectedSpline = null;

            string path = AssetDatabase.GetAssetPath(spline);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
            }

            RefreshSplineAssets();
        }

        public void RefreshSplineAssets()
        {
            splineAssets.Clear();

            string[] guids = AssetDatabase.FindAssets("t:TerrainSplineData");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TerrainSplineData data = AssetDatabase.LoadAssetAtPath<TerrainSplineData>(path);
                if (data != null)
                    splineAssets.Add(data);
            }

            // Sort by orderIndex, then by name
            splineAssets.Sort((a, b) => 
            {
                int orderComp = a.orderIndex.CompareTo(b.orderIndex);
                if (orderComp != 0) return orderComp;
                return string.Compare(a.displayName, b.displayName, System.StringComparison.Ordinal);
            });

            InitReorderableList();
        }

        private void InitReorderableList()
        {
            splineReorderableList = new ReorderableList(splineAssets, typeof(TerrainSplineData), true, false, false, false);
            
            splineReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= splineAssets.Count) return;
                TerrainSplineData spline = splineAssets[index];
                if (spline == null) return;

                Event e = Event.current;
                
                // Handle right click menu
                if ((e.type == EventType.ContextClick || (e.type == EventType.MouseDown && e.button == 1)) && rect.Contains(e.mousePosition))
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Rename"), false, () => { renamingIndex = index; Repaint(); });
                    menu.AddItem(new GUIContent("Smooth"), false, () => { SplineOperations.SmoothSpline(spline); });
                    
                    if (index < splineAssets.Count - 1)
                    {
                        int targetIdx = index + 1; // Need local copy for closure
                        menu.AddItem(new GUIContent("Merge Down"), false, () => { MergeSplinesDown(index, targetIdx); });
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent("Merge Down"));
                    }
                    
                    menu.ShowAsContext();
                    e.Use();
                }

                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                
                bool isSelected = selectedSpline == spline;
                Texture2D listIcon = spline.splineMode == SplineMode.Path ? iconPath : iconShape;
                string label = spline.displayName;

                Rect selectRect = new Rect(rect.x, rect.y, rect.width - 65, rect.height);
                
                if (renamingIndex == index)
                {
                    GUI.SetNextControlName("RenameField");
                    string newName = EditorGUI.TextField(selectRect, spline.displayName);
                    if (newName != spline.displayName)
                    {
                        Undo.RecordObject(spline, "Rename Spline");
                        spline.displayName = newName;
                        EditorUtility.SetDirty(spline);
                    }
                    
                    if (e.isKey && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
                    {
                        renamingIndex = -1;
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDown && !selectRect.Contains(e.mousePosition))
                    {
                        renamingIndex = -1;
                        Repaint();
                    }
                }
                else
                {
                    GUI.backgroundColor = isSelected ? new Color(0.25f, 0.5f, 0.9f, 0.4f) : Color.clear;
                    EditorGUI.DrawRect(selectRect, GUI.backgroundColor);
                    GUI.backgroundColor = Color.white;

                    // Draw icon + label
                    Rect iconRect = new Rect(selectRect.x + 2, selectRect.y + 1, 14, 14);
                    if (listIcon != null) GUI.DrawTexture(iconRect, listIcon, ScaleMode.ScaleToFit);
                    Rect textRect = new Rect(selectRect.x + 20, selectRect.y, selectRect.width - 20, selectRect.height);

                    if (GUI.Button(textRect, label, isSelected ? EditorStyles.boldLabel : EditorStyles.label))
                    {
                        EditorApplication.delayCall += () =>
                        {
                            selectedSpline = spline;
                            Selection.activeObject = spline;
                            if (targetTerrain != null && spline.targetTerrain == null)
                                spline.targetTerrain = targetTerrain;
                            Repaint();
                        };
                    }
                    GUI.backgroundColor = Color.white;
                }

                Rect editRect = new Rect(rect.x + rect.width - 60, rect.y, 40, rect.height);
                
                bool isEditing = TerrainSplineTool.ActiveSpline == spline;
                if (isEditing) GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                
                if (GUI.Button(editRect, isEditing ? "Done" : "Edit", EditorStyles.miniButton))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (isEditing)
                        {
                            TerrainSplineTool.ActiveSpline = null;
                        }
                        else
                        {
                            selectedSpline = spline;
                            if (targetTerrain != null && spline.targetTerrain == null)
                                spline.targetTerrain = targetTerrain;
                            ActivateSplineTool(spline);
                        }
                        Repaint();
                    };
                }
                GUI.backgroundColor = Color.white;

                Rect deleteRect = new Rect(rect.x + rect.width - 18, rect.y, 18, rect.height);
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUI.Button(deleteRect, "✕", EditorStyles.miniButton))
                {
                    EditorApplication.delayCall += () => DeleteSpline(spline);
                }
                GUI.backgroundColor = Color.white;
            };

            splineReorderableList.onReorderCallback = (ReorderableList list) =>
            {
                for (int i = 0; i < splineAssets.Count; i++)
                {
                    if (splineAssets[i].orderIndex != i)
                    {
                        Undo.RecordObject(splineAssets[i], "Reorder Spline");
                        splineAssets[i].orderIndex = i;
                        EditorUtility.SetDirty(splineAssets[i]);
                    }
                }
                AssetDatabase.SaveAssets();
            };
        }

        private void MergeSplinesDown(int sourceIndex, int targetIndex)
        {
            TerrainSplineData sourceSpline = splineAssets[sourceIndex];
            TerrainSplineData targetSpline = splineAssets[targetIndex];
            
            float weldDist = EditorPrefs.GetFloat("TerrainSpline_MergeWeldDistance", 1.0f);
            
            if (EditorUtility.DisplayDialog("Merge Splines", 
                $"Merge '{sourceSpline.displayName}' down into '{targetSpline.displayName}'?\n\n" +
                $"This will weld endpoints (if within {weldDist}m) and DELETE '{targetSpline.displayName}'.", 
                "Merge", "Cancel"))
            {
                SplineOperations.MergeSplines(sourceSpline, targetSpline, weldDist);
                RefreshSplineAssets();
            }
        }

        private void ActivateSplineTool(TerrainSplineData spline)
        {
            selectedSpline = spline;
            TerrainSplineTool.ActiveSpline = spline;
            TerrainSplineTool.OwnerWindow = this;

            // Activate the tool
            if (EditorWindow.HasOpenInstances<SceneView>())
            {
                SceneView.lastActiveSceneView.Focus();
            }

            Debug.Log($"[TerrainSpline] Editing: {spline.displayName} — Ctrl+Click to add nodes");
        }

        // ─────────────────────────────────────────────
        // Scene View Integration
        // ─────────────────────────────────────────────

        /// <summary>
        /// Draw all spline gizmos in scene view.
        /// </summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            if (splineAssets == null) return;

            foreach (var spline in splineAssets)
            {
                if (spline == null || spline.points.Count < 2) continue;

                bool isActive = spline == selectedSpline;
                DrawSplineGizmo(spline, isActive, sceneView);
            }
        }

        private void DrawSplineGizmo(TerrainSplineData data, bool isActive, SceneView sceneView)
        {
            Color splineColor = isActive
                ? (data.splineMode == SplineMode.Path ? new Color(1f, 0.85f, 0.2f) : new Color(0.3f, 1f, 0.5f))
                : new Color(0.6f, 0.6f, 0.6f, 0.4f);

            // Draw bezier segments
            for (int i = 0; i < data.SegmentCount; i++)
            {
                SplinePoint a = data.points[i];
                SplinePoint b = data.points[(i + 1) % data.points.Count];

                Handles.DrawBezier(
                    a.position, b.position,
                    a.TangentOutWorld, b.TangentInWorld,
                    splineColor, null, isActive ? 3f : 1.5f);
            }

            // Calculate spline center for gizmo placement
            Vector3 center = Vector3.zero;
            if (data.points.Count > 0)
            {
                for (int i = 0; i < data.points.Count; i++)
                    center += data.points[i].position;
                center /= data.points.Count;
            }

            Vector3 pivot = center;
            Quaternion rotation = Quaternion.identity;

            // The user requested that the 'X' key (which toggles Local/Global) switches between the selected node and the object center.
            if (Tools.pivotRotation == PivotRotation.Local && TerrainSplineTool.SelectedNodeIndex >= 0 && TerrainSplineTool.SelectedNodeIndex < data.points.Count)
            {
                pivot = data.points[TerrainSplineTool.SelectedNodeIndex].position;
                rotation = data.points[TerrainSplineTool.SelectedNodeIndex].rotation;
                
                // Prevent invalid Quaternion errors
                float sqrMag = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
                if (sqrMag < 0.001f)
                {
                    rotation = Quaternion.identity;
                }
                else
                {
                    rotation = rotation.normalized;
                }
            }

            if (GUIUtility.hotControl == 0)
            {
                _gizmoPosition = pivot;
                _gizmoRotation = rotation;
                _gizmoScale = Vector3.one;
            }

            float gizmoHandleSize = HandleUtility.GetHandleSize(pivot);

            if (!isActive)
            {
                // Only draw clickable gizmo and labels when SceneView gizmos are enabled
                if (sceneView != null && !sceneView.drawGizmos) return;

                Color inactiveGizmoColor = data.splineMode == SplineMode.Path
                    ? new Color(1f, 0.85f, 0.3f, 0.9f)
                    : new Color(0.4f, 1f, 0.4f, 0.9f);

                Handles.color = inactiveGizmoColor;

                // Modern dot gizmo
                if (Handles.Button(pivot, Quaternion.identity, gizmoHandleSize * 0.1f, gizmoHandleSize * 0.15f, Handles.DotHandleCap))
                {
                    if (targetTerrain != null && data.targetTerrain == null)
                        data.targetTerrain = targetTerrain;
                    ActivateSplineTool(data);
                }

                // Modern name label with semi-transparent pill background
                Handles.BeginGUI();
                Vector2 screenPos = HandleUtility.WorldToGUIPoint(center + Vector3.up * gizmoHandleSize * 0.25f);
                
                GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10,
                    fontStyle = FontStyle.Bold
                };
                labelStyle.normal.textColor = new Color(inactiveGizmoColor.r, inactiveGizmoColor.g, inactiveGizmoColor.b, 0.95f);

                Vector2 labelSize = labelStyle.CalcSize(new GUIContent(data.displayName));
                Rect labelRect = new Rect(screenPos.x - labelSize.x * 0.5f, screenPos.y - labelSize.y - 2, labelSize.x, labelSize.y);

                // Pill-shaped background
                Rect bgRect = new Rect(labelRect.x - 6, labelRect.y - 2, labelRect.width + 12, labelRect.height + 4);
                EditorGUI.DrawRect(bgRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));
                // Subtle top/bottom highlight lines
                EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y, bgRect.width, 1), new Color(inactiveGizmoColor.r, inactiveGizmoColor.g, inactiveGizmoColor.b, 0.3f));

                GUI.Label(labelRect, data.displayName, labelStyle);
                Handles.EndGUI();
            }
            else
            {
                // Active spline: modern label above pivot
                if (sceneView == null || sceneView.drawGizmos)
                {
                    Handles.BeginGUI();
                    Vector2 screenPos = HandleUtility.WorldToGUIPoint(pivot + Vector3.up * gizmoHandleSize * 0.3f);
                    
                    GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 11
                    };
                    labelStyle.normal.textColor = splineColor;

                    string label = data.displayName;
                    Vector2 labelSize = labelStyle.CalcSize(new GUIContent(label));
                    Rect labelRect = new Rect(screenPos.x - labelSize.x * 0.5f, screenPos.y - labelSize.y - 2, labelSize.x, labelSize.y);

                    // Background pill
                    Rect bgRect = new Rect(labelRect.x - 8, labelRect.y - 2, labelRect.width + 16, labelRect.height + 4);
                    EditorGUI.DrawRect(bgRect, new Color(0.08f, 0.08f, 0.08f, 0.9f));
                    // Accent line
                    EditorGUI.DrawRect(new Rect(bgRect.x, bgRect.y + bgRect.height - 2, bgRect.width, 2), new Color(splineColor.r, splineColor.g, splineColor.b, 0.6f));

                    GUI.Label(labelRect, label, labelStyle);
                    Handles.EndGUI();
                }
            }

            // Draw nodes
            if (isActive)
            {
                for (int i = 0; i < data.points.Count; i++)
                {
                    SplinePoint pt = data.points[i];
                    float handleSize = HandleUtility.GetHandleSize(pt.position);
                    bool isNodeSelected = (i == TerrainSplineTool.SelectedNodeIndex);

                    if (isNodeSelected)
                    {
                        // Glow ring for selected node
                        Handles.color = new Color(0.3f, 1f, 0.5f, 0.15f);
                        Handles.DrawSolidDisc(pt.position, Camera.current != null ? Camera.current.transform.forward : Vector3.forward, handleSize * 0.18f);
                        Handles.color = new Color(0.3f, 1f, 0.5f, 0.5f);
                        Handles.DrawWireDisc(pt.position, Camera.current != null ? Camera.current.transform.forward : Vector3.forward, handleSize * 0.18f, 2f);
                        // Inner solid dot
                        Handles.color = new Color(0.2f, 1f, 0.4f);
                        Handles.DotHandleCap(0, pt.position, Quaternion.identity, handleSize * 0.06f, EventType.Repaint);
                    }
                    else
                    {
                        // Clean white dot for unselected
                        Handles.color = new Color(1f, 1f, 1f, 0.85f);
                        Handles.DotHandleCap(0, pt.position, Quaternion.identity, handleSize * 0.04f, EventType.Repaint);
                    }
                }

                // If not in edit mode, show whole-spline transform handles
                if (TerrainSplineTool.ActiveSpline != data && data.points.Count > 0)
                {
                    EditorGUI.BeginChangeCheck();

                    if (Tools.current == Tool.Move)
                    {
                        Vector3 newPos = Handles.PositionHandle(_gizmoPosition, _gizmoRotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(data, "Move Spline");

                            // Snap to terrain if Ctrl is held
                            if (Event.current.control)
                            {
                                Ray ray = new Ray(newPos + Vector3.up * 1000f, Vector3.down);
                                if (TerrainSplineTool.RaycastTerrain(data, ray, out Vector3 hitPoint, out Vector3 hitNormal))
                                {
                                    newPos = hitPoint;
                                }
                            }

                            Vector3 delta = newPos - _gizmoPosition;
                            _gizmoPosition = newPos;

                            for (int i = 0; i < data.points.Count; i++)
                            {
                                SplinePoint pt = data.points[i];
                                pt.position += delta;
                                data.points[i] = pt;
                            }
                            EditorUtility.SetDirty(data);
                        }
                    }
                    else if (Tools.current == Tool.Rotate)
                    {
                        Quaternion newRot = Handles.RotationHandle(_gizmoRotation, _gizmoPosition);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(data, "Rotate Spline");
                            
                            Quaternion deltaRot = newRot * Quaternion.Inverse(_gizmoRotation);
                            _gizmoRotation = newRot;

                            for (int i = 0; i < data.points.Count; i++)
                            {
                                SplinePoint pt = data.points[i];
                                Vector3 localPos = pt.position - _gizmoPosition;
                                pt.position = _gizmoPosition + deltaRot * localPos;
                                pt.tangentIn = deltaRot * pt.tangentIn;
                                pt.tangentOut = deltaRot * pt.tangentOut;
                                pt.rotation = deltaRot * pt.rotation;
                                data.points[i] = pt;
                            }
                            EditorUtility.SetDirty(data);
                        }
                    }
                    else if (Tools.current == Tool.Scale)
                    {
                        Vector3 newScale = Handles.ScaleHandle(_gizmoScale, _gizmoPosition, _gizmoRotation, HandleUtility.GetHandleSize(_gizmoPosition));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(data, "Scale Spline");

                            Vector3 deltaScale = new Vector3(
                                _gizmoScale.x == 0f ? 1f : newScale.x / _gizmoScale.x,
                                _gizmoScale.y == 0f ? 1f : newScale.y / _gizmoScale.y,
                                _gizmoScale.z == 0f ? 1f : newScale.z / _gizmoScale.z
                            );
                            _gizmoScale = newScale;

                            for (int i = 0; i < data.points.Count; i++)
                            {
                                SplinePoint pt = data.points[i];
                                
                                // Transform to pivot local space
                                Vector3 offset = pt.position - _gizmoPosition;
                                Vector3 localOffset = Quaternion.Inverse(_gizmoRotation) * offset;
                                
                                localOffset.x *= deltaScale.x;
                                localOffset.y *= deltaScale.y;
                                localOffset.z *= deltaScale.z;
                                
                                pt.position = _gizmoPosition + _gizmoRotation * localOffset;

                                Vector3 localTanIn = Quaternion.Inverse(_gizmoRotation) * pt.tangentIn;
                                localTanIn.x *= deltaScale.x;
                                localTanIn.y *= deltaScale.y;
                                localTanIn.z *= deltaScale.z;
                                pt.tangentIn = _gizmoRotation * localTanIn;

                                Vector3 localTanOut = Quaternion.Inverse(_gizmoRotation) * pt.tangentOut;
                                localTanOut.x *= deltaScale.x;
                                localTanOut.y *= deltaScale.y;
                                localTanOut.z *= deltaScale.z;
                                pt.tangentOut = _gizmoRotation * localTanOut;

                                pt.scale = Vector3.Scale(pt.scale, deltaScale);
                                data.points[i] = pt;
                            }
                            EditorUtility.SetDirty(data);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Public accessor so the tool can get the current selected spline.
        /// </summary>
        public TerrainSplineData SelectedSpline => selectedSpline;

        /// <summary>
        /// Public accessor for target terrain.
        /// </summary>
        public Terrain TargetTerrain => targetTerrain;
    }
}
