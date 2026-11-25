using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WildernessExtraction.Map01;
using Object = UnityEngine.Object;

namespace WildernessExtraction.MapBuilder.EditorTools
{
    /// <summary>
    /// 地图搭建器：左侧地图列表，右侧素材 / 点位 / 检查三页。
    /// 选中素材后 Scene 里有半透明影子跟着鼠标：绿色可放、红色不可放；左键放置，R 旋转，Shift 不吸附，右键或 Esc 取消。
    /// </summary>
    public sealed class MapBuilderWindow : EditorWindow
    {
        private const int PageSize = 36;
        private const float Cell = 84f;
        private static readonly string[] Tabs = { "素材", "点位", "检查" };
        private static readonly string[] RotationSteps = { "90°", "45°", "15°" };
        private static readonly float[] RotationStepValues = { 90f, 45f, 15f };

        [SerializeField] private int tab;
        [SerializeField] private int groupIndex;
        [SerializeField] private int rotationStep;
        [SerializeField] private bool addColliders = true;
        [SerializeField] private bool edgeSnap = true;
        [SerializeField] private string newMapName = "Map02";
        [SerializeField] private Vector2 newMapSize = new Vector2(120f, 120f);
        [SerializeField] private string pointTags = "";

        private MapBuilderLibrary library;
        private string search = "";
        private int page;
        private Vector2 libraryScroll, listScroll, issueScroll;
        private bool groupSettings;
        private bool hideRoofs;
        private readonly List<GameObject> hiddenRoofs = new List<GameObject>();

        private GameObject brush;
        private MapBuilderLibrary.Group brushGroup;
        private SpawnPointType? pointBrush;
        private GameObject ghost;
        private Material ghostValid, ghostInvalid;
        private float yaw, randomYaw, randomScale = 1f;
        private bool rotationHeld, valid;
        private Vector3 placePosition;
        private Quaternion placeRotation = Quaternion.identity;
        private string hint = "";
        private string status = "";
        private List<MapBuilderChecks.Issue> issues = new List<MapBuilderChecks.Issue>();

        [MenuItem("Wilderness/地图搭建器", priority = 1)]
        public static MapBuilderWindow Open()
        {
            var window = GetWindow<MapBuilderWindow>("地图搭建器");
            window.minSize = new Vector2(640f, 520f);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            library = MapBuilderLibrary.LoadOrCreate();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            ClearBrush();
            ShowRoofs();
            if (ghostValid != null) DestroyImmediate(ghostValid);
            if (ghostInvalid != null) DestroyImmediate(ghostInvalid);
        }

        private void OnInspectorUpdate()
        {
            if (AssetPreview.IsLoadingAssetPreviews())
            {
                Repaint();
            }
        }

        // ---------------- 窗口 ----------------

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(200f)))
                {
                    DrawMapList();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    var root = MapBuilderMaps.FindRoot();
                    DrawViewBar(root);
                    if (root == null)
                    {
                        EditorGUILayout.HelpBox("当前场景不是搭建器地图。从左边打开一张地图，或者新建一张。", MessageType.Info);
                        return;
                    }

                    var selected = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(26f));
                    if (selected != tab)
                    {
                        tab = selected;
                        ClearBrush();
                        if (tab == 2) issues = MapBuilderChecks.Run(root);
                    }

                    if (tab == 0) DrawLibrary(root);
                    else if (tab == 1) DrawPoints(root);
                    else DrawChecks(root);

                    if (!string.IsNullOrEmpty(status))
                    {
                        EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        private void DrawMapList()
        {
            EditorGUILayout.LabelField("地图", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            foreach (var path in MapBuilderMaps.ListScenes())
            {
                var style = path == active ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                if (GUILayout.Button(new GUIContent(System.IO.Path.GetFileNameWithoutExtension(path), path), style))
                {
                    ClearBrush();
                    ShowRoofs();
                    MapBuilderMaps.Open(path);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("新建地图", EditorStyles.miniBoldLabel);
            newMapName = EditorGUILayout.TextField(newMapName);
            newMapSize = EditorGUILayout.Vector2Field(GUIContent.none, newMapSize);
            if (GUILayout.Button("新建空白地图"))
            {
                try
                {
                    if (EditorSceneManagerSave())
                    {
                        ClearBrush();
                        MapBuilderMaps.CreateMap(newMapName.Trim(), Vector2.Max(newMapSize, new Vector2(10f, 10f)));
                        FrameTop(MapBuilderMaps.FindRoot());
                        status = "已新建 " + MapBuilderMaps.ScenePath(newMapName.Trim());
                    }
                }
                catch (Exception e)
                {
                    status = e.Message;
                }

                GUIUtility.ExitGUI();
            }
        }

        private static bool EditorSceneManagerSave() => UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        private void DrawViewBar(MapAuthoringRoot root)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(root == null))
                {
                    if (GUILayout.Button("俯视", EditorStyles.toolbarButton, GUILayout.Width(44f))) FrameTop(root);
                    if (GUILayout.Button("斜俯视（游戏角度）", EditorStyles.toolbarButton, GUILayout.Width(110f))) FrameOblique(root);
                    var roofs = GUILayout.Toggle(hideRoofs, "隐藏屋顶", EditorStyles.toolbarButton, GUILayout.Width(60f));
                    if (roofs != hideRoofs)
                    {
                        hideRoofs = roofs;
                        if (hideRoofs) HideRoofs(root); else ShowRoofs();
                    }

                    GUILayout.FlexibleSpace();
                    if (root != null)
                    {
                        EditorGUIUtility.labelWidth = 34f;
                        var grid = EditorGUILayout.FloatField("网格", root.grid, GUILayout.Width(80f));
                        EditorGUIUtility.labelWidth = 0f;
                        if (!Mathf.Approximately(grid, root.grid))
                        {
                            Undo.RecordObject(root, "地图搭建器：网格");
                            root.grid = Mathf.Max(0.1f, grid);
                        }
                    }

                    if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(40f)) && root != null)
                    {
                        ClearBrush();
                        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(root.gameObject.scene);
                        status = "已保存 " + root.gameObject.scene.path;
                    }
                }
            }
        }

        private void DrawLibrary(MapAuthoringRoot root)
        {
            DrawDropZone();
            if (library.groups.Count == 0)
            {
                EditorGUILayout.HelpBox("把 Project 里的素材文件夹拖到上面的框里。", MessageType.Info);
                return;
            }

            groupIndex = Mathf.Clamp(groupIndex, 0, library.groups.Count - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                var names = library.groups.Select(g => $"{g.name}（{g.assets.Count(a => a)}）").ToArray();
                var next = EditorGUILayout.Popup(groupIndex, names);
                if (next != groupIndex)
                {
                    groupIndex = next;
                    page = 0;
                    libraryScroll = Vector2.zero;
                    ClearBrush();
                }

                if (GUILayout.Button("刷新", GUILayout.Width(44f))) library.Refresh(library.groups[groupIndex]);
                if (GUILayout.Button("移除", GUILayout.Width(44f)))
                {
                    Undo.RecordObject(library, "地图搭建器：移除分组");
                    library.groups.RemoveAt(groupIndex);
                    EditorUtility.SetDirty(library);
                    ClearBrush();
                    GUIUtility.ExitGUI();
                }
            }

            var group = library.groups[groupIndex];
            groupSettings = EditorGUILayout.Foldout(groupSettings, "分组设置（材质 / 缩放 / 随机）", true);
            if (groupSettings)
            {
                EditorGUI.BeginChangeCheck();
                var material = (Material)EditorGUILayout.ObjectField("统一材质", group.materialOverride, typeof(Material), false);
                var scale = EditorGUILayout.FloatField("缩放", group.scale);
                var randomRotate = EditorGUILayout.Toggle("随机朝向", group.randomYaw);
                var randomSize = EditorGUILayout.Slider("随机大小 ±", group.randomScale, 0f, 0.5f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(library, "地图搭建器：分组设置");
                    group.materialOverride = material;
                    group.scale = Mathf.Max(0.01f, scale);
                    group.randomYaw = randomRotate;
                    group.randomScale = randomSize;
                    EditorUtility.SetDirty(library);
                    ClearGhost();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                addColliders = GUILayout.Toggle(addColliders, "自动加碰撞", EditorStyles.miniButton);
                edgeSnap = GUILayout.Toggle(edgeSnap, "边对边吸附", EditorStyles.miniButton);
                EditorGUIUtility.labelWidth = 56f;
                rotationStep = EditorGUILayout.Popup("R 旋转", rotationStep, RotationSteps, GUILayout.Width(120f));
                EditorGUIUtility.labelWidth = 0f;
            }

            var query = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (query != search)
            {
                search = query;
                page = 0;
            }

            var matches = group.assets.Where(a => a != null && a.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var pages = Mathf.Max(1, (matches.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            if (pages > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("上一页")) page--;
                    GUILayout.Label($"{page + 1} / {pages}", EditorStyles.centeredGreyMiniLabel);
                    using (new EditorGUI.DisabledScope(page == pages - 1)) if (GUILayout.Button("下一页")) page++;
                }
            }

            libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);
            var shown = matches.Skip(page * PageSize).Take(PageSize).ToList();
            var columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 230f) / (Cell + 4f)));
            for (var i = 0; i < shown.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var c = i; c < Mathf.Min(i + columns, shown.Count); c++)
                    {
                        DrawAssetCell(shown[c], group);
                    }

                    GUILayout.FlexibleSpace();
                }
            }

            if (shown.Count == 0)
            {
                EditorGUILayout.LabelField(search.Length > 0 ? "没有匹配的素材" : "这个文件夹里没有可摆放的预制体或模型", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
            DrawBrushFooter();
        }

        private void DrawAssetCell(GameObject asset, MapBuilderLibrary.Group group)
        {
            var preview = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Cell)))
            {
                var selected = brush == asset;
                var previous = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.45f, 0.9f, 0.55f);
                if (GUILayout.Button(new GUIContent(preview, AssetDatabase.GetAssetPath(asset)), GUILayout.Width(Cell), GUILayout.Height(Cell)))
                {
                    SelectBrush(asset, group);
                }

                GUI.backgroundColor = previous;
                GUILayout.Label(asset.name, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(Cell));
            }
        }

        private void DrawDropZone()
        {
            var rect = GUILayoutUtility.GetRect(0f, 30f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, "把 Project 里的素材文件夹拖到这里（收录预制体和模型）", EditorStyles.helpBox);
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform))
            {
                return;
            }

            var path = DragAndDrop.paths.Length == 1 ? DragAndDrop.paths[0] : "";
            var folder = AssetDatabase.IsValidFolder(path);
            DragAndDrop.visualMode = folder ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (folder && e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(library, "地图搭建器：导入文件夹");
                var group = library.ImportFolder(path);
                AssetDatabase.SaveAssetIfDirty(library);
                groupIndex = library.groups.IndexOf(group);
                page = 0;
                status = $"已收录 {group.name}：{group.assets.Count} 个素材";
            }

            e.Use();
        }

        private void DrawBrushFooter()
        {
            if (brush != null || pointBrush != null)
            {
                var name = brush != null ? brush.name : MapBuilderPoints.Label(pointBrush.Value);
                if (GUILayout.Button("取消放置 · " + name)) ClearBrush();
                EditorGUILayout.LabelField("左键放置 · R 旋转 · 按住 Shift 不吸附 · 右键 / Esc 取消 · Ctrl+Z 撤销", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawPoints(MapAuthoringRoot root)
        {
            EditorGUILayout.HelpBox("点位用的是 Map01 同一套 MapSpawnPoint：摆好后“导出”成 MapDefinition，刷什么由内容表按种子决定。", MessageType.None);
            var points = MapBuilderPoints.All(root);
            foreach (var type in MapBuilderPoints.Types)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var previous = GUI.backgroundColor;
                    GUI.backgroundColor = pointBrush == type ? new Color(0.45f, 0.9f, 0.55f) : MapBuilderPoints.PointColor(type);
                    if (GUILayout.Button($"放{MapBuilderPoints.Label(type)}", GUILayout.Width(120f), GUILayout.Height(24f)))
                    {
                        ClearBrush();
                        pointBrush = type;
                        FocusScene();
                    }

                    GUI.backgroundColor = previous;
                    var ofType = points.Where(p => p.Type == type).ToArray();
                    GUILayout.Label($"{ofType.Length} 个", GUILayout.Width(40f));
                    if (ofType.Length > 0 && GUILayout.Button("选中全部", EditorStyles.miniButton, GUILayout.Width(64f)))
                    {
                        Selection.objects = ofType.Select(p => (Object)p.gameObject).ToArray();
                    }
                }
            }

            pointTags = EditorGUILayout.TextField(new GUIContent("新点位标签", "逗号分隔，例如 indoor,medical；内容表按标签限定出什么。"), pointTags);
            DrawBrushFooter();
            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("烘焙导航网格"))
                {
                    try
                    {
                        MapBuilderPoints.BakeNavMesh(root);
                        status = "导航网格已烘焙。";
                    }
                    catch (Exception e)
                    {
                        status = e.Message;
                    }
                }

                if (GUILayout.Button("导出点位到 MapDefinition"))
                {
                    var path = MapBuilderPoints.DefinitionPath(root);
                    var map = MapBuilderPoints.Export(root, path, out var report);
                    Debug.Log(report);
                    status = $"已导出 {map.points.Count} 个点位到 {path}（详情见 Console）";
                    EditorGUIUtility.PingObject(map);
                }
            }
        }

        private void DrawChecks(MapAuthoringRoot root)
        {
            if (GUILayout.Button("重新检查", GUILayout.Height(24f))) issues = MapBuilderChecks.Run(root);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("没有发现问题。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"{issues.Count} 个问题（点一下跳过去）", EditorStyles.miniBoldLabel);
            issueScroll = EditorGUILayout.BeginScrollView(issueScroll);
            foreach (var issue in issues)
            {
                using (new EditorGUI.DisabledScope(issue.Target == null))
                {
                    if (GUILayout.Button(issue.Message, EditorStyles.miniButton))
                    {
                        Selection.activeGameObject = issue.Target;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ---------------- 视角与屋顶 ----------------

        private static void FrameTop(MapAuthoringRoot root)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || root == null) return;
            view.LookAt(root.transform.position, Quaternion.Euler(90f, 0f, 0f), Mathf.Max(root.worldSize.x, root.worldSize.y) * 0.6f, true);
        }

        private static void FrameOblique(MapAuthoringRoot root)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || root == null) return;
            var pivot = Selection.activeTransform != null ? Selection.activeTransform.position : view.pivot;
            view.LookAt(pivot, Quaternion.Euler(55f, 0f, 0f), 18f, false);
        }

        private void HideRoofs(MapAuthoringRoot root)
        {
            ShowRoofs();
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var n = renderer.name;
                if (n.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("屋顶"))
                {
                    SceneVisibilityManager.instance.Hide(renderer.gameObject, false);
                    hiddenRoofs.Add(renderer.gameObject);
                }
            }

            status = $"隐藏了 {hiddenRoofs.Count} 个屋顶（名字含 roof / 屋顶 的物体）";
        }

        private void ShowRoofs()
        {
            foreach (var go in hiddenRoofs)
            {
                if (go != null) SceneVisibilityManager.instance.Show(go, false);
            }

            hiddenRoofs.Clear();
        }

        // ---------------- 刷子 ----------------

        private void SelectBrush(GameObject asset, MapBuilderLibrary.Group group)
        {
            ClearBrush();
            brush = asset;
            brushGroup = group;
            yaw = 0f;
            Reroll();
            FocusScene();
        }

        private static void FocusScene()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.wantsMouseMove = true;
            view.Focus();
            view.Repaint();
        }

        private void Reroll()
        {
            randomYaw = brushGroup != null && brushGroup.randomYaw ? UnityEngine.Random.Range(0, 24) * 15f : 0f;
            randomScale = brushGroup != null && brushGroup.randomScale > 0f ? 1f + UnityEngine.Random.Range(-brushGroup.randomScale, brushGroup.randomScale) : 1f;
            ClearGhost();
        }

        private void ClearBrush()
        {
            brush = null;
            brushGroup = null;
            pointBrush = null;
            hint = "";
            ClearGhost();
            SceneView.RepaintAll();
        }

        private void ClearGhost()
        {
            if (ghost != null) DestroyImmediate(ghost);
            ghost = null;
        }

        private float BrushScale => (brushGroup != null ? brushGroup.scale : 1f) * randomScale;

        private void EnsureGhost()
        {
            if (ghost != null || brush == null) return;
            ghost = Instantiate(brush);
            ghost.name = "地图搭建器预览";
            ghost.hideFlags = HideFlags.HideAndDontSave;
            foreach (var collider in ghost.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var light in ghost.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            ghost.transform.localScale = brush.transform.localScale * BrushScale;
        }

        private void PaintGhost(bool ok)
        {
            if (ghost == null) return;
            if (ghostValid == null) ghostValid = GhostMaterial(new Color(0.2f, 0.85f, 0.4f, 0.45f));
            if (ghostInvalid == null) ghostInvalid = GhostMaterial(new Color(0.95f, 0.25f, 0.2f, 0.45f));
            var material = ok ? ghostValid : ghostInvalid;
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (var i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        internal static Material GhostMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);
            material.color = color;
            return material;
        }

        // ---------------- Scene 交互 ----------------

        private void OnSceneGUI(SceneView view)
        {
            var root = MapBuilderMaps.FindRoot();
            if (root == null || EditorApplication.isPlaying)
            {
                ClearGhost();
                return;
            }

            DrawPointLabels(root);
            if (brush == null && pointBrush == null) return;

            var e = Event.current;
            if (e.alt) return;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1))
            {
                ClearBrush();
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.KeyUp && e.keyCode == KeyCode.R)
            {
                rotationHeld = false;
                e.Use();
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.R)
            {
                if (!rotationHeld) yaw = Mathf.Repeat(yaw + RotationStepValues[rotationStep], 360f);
                rotationHeld = true;
                e.Use();
            }

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (pointBrush != null)
            {
                ScenePointBrush(root, ray, e);
                return;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.KeyDown
                || e.type == EventType.KeyUp || e.type == EventType.Layout || ghost == null)
            {
                EnsureGhost();
                Compute(root, ray, e.shift);
                PaintGhost(valid);
            }

            Handles.color = valid ? Color.green : Color.red;
            Handles.DrawWireDisc(placePosition, Vector3.up, 0.4f);
            Handles.Label(placePosition + Vector3.up * 0.6f, hint, EditorStyles.whiteBoldLabel);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (valid)
                {
                    var placed = MapBuilderPlacement.Place(root, new MapBuilderPlacement.Request
                    {
                        asset = brush, group = brushGroup, position = ghost.transform.position, rotation = ghost.transform.rotation,
                        scaleMultiplier = BrushScale, addColliders = addColliders
                    });
                    status = "已放置 " + placed.name;
                    Reroll();
                }

                e.Use();
            }
        }

        private void ScenePointBrush(MapAuthoringRoot root, Ray ray, Event e)
        {
            var type = pointBrush.Value;
            var position = Hit(root, ray, out _, out _);
            if (!e.shift) position = MapBuilderPlacement.SnapToGrid(position, root.transform, root.grid * 0.5f);
            var radius = MapBuilderPoints.DefaultRadius(type);
            Handles.color = MapBuilderPoints.PointColor(type);
            Handles.DrawWireDisc(position, Vector3.up, radius);
            Handles.DrawSolidDisc(position, Vector3.up, 0.25f);
            Handles.Label(position + Vector3.up, "放" + MapBuilderPoints.Label(type), EditorStyles.whiteBoldLabel);
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var tags = pointTags.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
                var point = MapBuilderPoints.Place(root, type, position, radius, tags);
                status = "已放置 " + point.PointId;
                e.Use();
                Repaint();
            }
        }

        /// <summary>射线打到地图根下最近的碰撞体；打不到时落在地图根所在平面上。</summary>
        private static Vector3 Hit(MapAuthoringRoot root, Ray ray, out bool hitSomething, out RaycastHit hit)
        {
            hit = default;
            hitSomething = false;
            var nearest = float.MaxValue;
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && collider.Raycast(ray, out var h, 2000f) && h.distance < nearest)
                {
                    nearest = h.distance;
                    hit = h;
                    hitSomething = true;
                }
            }

            if (hitSomething) return hit.point;
            return new Plane(root.transform.up, root.transform.position).Raycast(ray, out var distance) ? ray.GetPoint(distance) : root.transform.position;
        }

        private void Compute(MapAuthoringRoot root, Ray ray, bool free)
        {
            var point = Hit(root, ray, out var hitSomething, out var hit);
            var wall = hitSomething && Mathf.Abs(hit.normal.y) < 0.3f;
            valid = true;
            if (wall)
            {
                // 贴墙：物体正面朝外，位置就是打到的墙面点（火把、招牌这类）。
                placeRotation = Quaternion.LookRotation(hit.normal, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
                placePosition = point;
                ghost.transform.SetPositionAndRotation(placePosition, placeRotation);
                hint = "贴墙";
                return;
            }

            placeRotation = Quaternion.Euler(0f, yaw + randomYaw, 0f);
            placePosition = free ? point : MapBuilderPlacement.SnapToGrid(point, root.transform, root.grid);
            ghost.transform.SetPositionAndRotation(placePosition, placeRotation);

            // 让模型底面落在打到的表面上（KayKit 原点大多在底部，但不是全部）。
            var bounds = MapBuilderPlacement.WorldBounds(ghost);
            ghost.transform.position += Vector3.up * (point.y - bounds.min.y);
            bounds = MapBuilderPlacement.WorldBounds(ghost);

            var props = MapBuilderPlacement.PlacedProps(root);
            var nearby = props.Select(MapBuilderPlacement.WorldBounds)
                .Where(b => (b.center - bounds.center).sqrMagnitude < 400f).ToList();
            var snapped = false;
            if (!free && edgeSnap)
            {
                var offset = MapBuilderPlacement.EdgeSnapOffset(bounds, nearby);
                if (offset != Vector3.zero)
                {
                    ghost.transform.position += offset;
                    bounds.center += offset;
                    snapped = true;
                }
            }

            placePosition = ghost.transform.position;
            var blocked = !free && nearby.Any(b => MapBuilderPlacement.Overlaps(bounds, b));
            if (blocked)
            {
                valid = false;
                hint = "和已摆的东西重叠（按住 Shift 强制放）";
            }
            else
            {
                hint = free ? "自由放置" : snapped ? "已贴边" : "吸附网格";
            }
        }

        private static void DrawPointLabels(MapAuthoringRoot root)
        {
            if (Event.current.type != EventType.Repaint) return;
            foreach (var point in MapBuilderPoints.All(root))
            {
                Handles.color = MapBuilderPoints.PointColor(point.Type);
                Handles.DrawWireDisc(point.transform.position, Vector3.up, point.Radius);
                Handles.Label(point.transform.position + Vector3.up * 0.8f, MapBuilderPoints.Label(point.Type), EditorStyles.miniBoldLabel);
            }
        }
    }
}
