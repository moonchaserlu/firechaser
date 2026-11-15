#if MAP01_PANEL_SHOTS
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 面板截图（只在 Map01CraftingCapture 打的截图包里编译）：IMGUI 只有真正渲染时才会画，
    /// 所以用独立播放器跑一遍，把背包物品页、合成页、工作台各拍一张，然后退出。输出目录由 -craftShots 指定。
    /// </summary>
    public sealed class Map01PanelShots : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            new GameObject("Map01PanelShots").AddComponent<Map01PanelShots>();
        }

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-craftShots");
            var directory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.persistentDataPath;
            yield return new WaitForSecondsRealtime(3f);

            var authority = FindAnyObjectByType<Map01Authority>();
            var flow = FindAnyObjectByType<Map01RunFlow>();
            var panel = FindAnyObjectByType<Map01CraftingPanel>();
            var player = authority.Player;
            var bag = player.Inventory.Inventory;
            foreach (var monster in authority.Monsters) monster.gameObject.SetActive(false);
            player.Exposure.enabled = false;

            // 完成 Q01 解锁燃料配方；再塞一些材料，让格子不空。
            var fuel = player.Placer.Config.fuelItem;
            bag.TakeAll();
            bag.TryAdd(fuel);
            var hearth = authority.FindStation(Map01Station.Hearth);
            player.Motor.Teleport(hearth.transform.position + Vector3.right * 0.8f + Vector3.up * 0.1f);
            authority.Request(new InteractionRequest(hearth.EntityId, InteractionAction.Quest, 10_000));
            foreach (var ingredient in authority.Recipes.First(r => r.recipeId == "fuel_pack").ingredients)
            {
                for (var i = 0; i < ingredient.amount; i++) bag.TryAdd(ingredient.item);
            }

            bag.TryAdd(authority.Recipes.First(r => r.recipeId == "first_aid_spray").ingredients[0].item);

            flow.OpenBackpack();
            yield return Shot(directory, "Map01_Backpack_Items.png");
            panel.Page = BackpackPage.Crafting;
            panel.Select(0);
            yield return Shot(directory, "Map01_Backpack_Crafting.png");
            flow.CloseBackpack();
            yield return null;
            flow.OpenCrafting(authority.FindStation(Map01Station.Workbench));
            yield return Shot(directory, "Map01_Workbench.png");
            Application.Quit();
        }

        private static IEnumerator Shot(string directory, string file)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, file));
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
#endif
