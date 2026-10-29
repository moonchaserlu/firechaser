using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 合成面板截图：进入 Play 后完成 Q01、塞一些材料、打开工作台，分别拍“可制作”和“未解锁”两张。
    /// IMGUI 只画在 Game 视图里，所以必须用有界面的编辑器跑（不要加 -batchmode）：
    /// <c>Unity.exe -projectPath Game -executeMethod WildernessExtraction.Map01.EditorTools.Map01CraftingCapture.Capture -craftShots &lt;目录&gt;</c>
    /// 拍完自动退出编辑器。
    /// </summary>
    [InitializeOnLoad]
    public static class Map01CraftingCapture
    {
        private const string DirectoryKey = "Map01CraftingCapture.Directory";
        private static int frame;
        private static int step;
        private static int lastFrame = -1;

        static Map01CraftingCapture()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(DirectoryKey, string.Empty)))
            {
                EditorApplication.update += Drive;
            }
        }

        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-craftShots");
            var directory = at >= 0 && at + 1 < args.Length
                ? args[at + 1]
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Map01"));
            Directory.CreateDirectory(directory);
            SessionState.SetString(DirectoryKey, directory);
            EditorSceneManager.OpenScene(Map01Builder.ScenePath);
            EditorApplication.update -= Drive;
            EditorApplication.update += Drive;
            EditorApplication.isPlaying = true;
        }

        private static void Drive()
        {
            if (!EditorApplication.isPlaying) return;
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            var panel = Object.FindAnyObjectByType<Map01CraftingPanel>();
            var flow = Object.FindAnyObjectByType<Map01RunFlow>();
            if (authority == null || panel == null || flow == null) return;
            // 按游戏帧计数；Game 视图要可见才会渲染，截图才会落盘。
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (frame == 0 && step == 0) ShowGameView();
            frame++;
            var directory = SessionState.GetString(DirectoryKey, string.Empty);
            var player = authority.Player;
            var bag = player.Inventory.Inventory;

            switch (step)
            {
                case 0 when frame > 30:
                    foreach (var monster in authority.Monsters) monster.gameObject.SetActive(false);
                    player.Exposure.enabled = false;
                    // 完成 Q01：带燃料去炉火提交，燃料配方随之解锁。
                    var fuel = player.Placer.Config.fuelItem;
                    bag.TakeAll();
                    bag.TryAdd(fuel);
                    var hearth = authority.FindStation(Map01Station.Hearth);
                    player.Motor.Teleport(hearth.transform.position + Vector3.right * 0.8f + Vector3.up * 0.1f);
                    authority.Request(new InteractionRequest(hearth.EntityId, InteractionAction.Quest, 10_000));

                    // 材料：够做燃料包，药品配方缺一点。
                    var recipes = authority.Recipes;
                    Give(bag, recipes.First(r => r.recipeId == "fuel_pack"), 1);
                    var spray = recipes.First(r => r.recipeId == "first_aid_spray");
                    bag.TryAdd(spray.ingredients[0].item);
                    var bench = authority.FindStation(Map01Station.Workbench);
                    player.Motor.Teleport(bench.transform.position + Vector3.forward * 1.2f + Vector3.up * 0.1f);
                    flow.OpenCrafting(bench);
                    panel.Select(0);
                    step = 1;
                    frame = 0;
                    break;
                case 1 when frame > 20:
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, "Map01_Crafting_Ready.png"));
                    Debug.Log($"[CraftingCapture] Ready -> {directory}");
                    step = 2;
                    frame = 0;
                    break;
                case 2 when frame > 10:
                    panel.Select(authority.Recipes.ToList().FindIndex(r => r.recipeId == "first_aid_spray"));
                    step = 3;
                    frame = 0;
                    break;
                case 3 when frame > 20:
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, "Map01_Crafting_Locked.png"));
                    Debug.Log("[CraftingCapture] Locked");
                    step = 4;
                    frame = 0;
                    break;
                case 4 when frame > 20:
                    SessionState.EraseString(DirectoryKey);
                    EditorApplication.update -= Drive;
                    EditorApplication.isPlaying = false;
                    EditorApplication.Exit(0);
                    break;
            }
        }

        private static void ShowGameView()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var view = EditorWindow.GetWindow(type);
            view.Focus();
            view.maximized = true;
        }

        private static void Give(Inventory bag, CraftingRecipe recipe, int times)
        {
            for (var t = 0; t < times; t++)
            {
                foreach (var ingredient in recipe.ingredients)
                {
                    for (var i = 0; i < ingredient.amount; i++) bag.TryAdd(ingredient.item);
                }
            }
        }
    }
}
