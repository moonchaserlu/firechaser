using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WildernessExtraction.Map01;

namespace WildernessExtraction.Shelter.Tests
{
    /// <summary>
    /// 安全屋守灯人：出发前选一个只管这一趟的 buff；没选就出发要先确认；
    /// buff 进地图生效（视野 / 怪物发现距离），回到安全屋清空。
    /// </summary>
    public sealed class LampkeeperBuffTests
    {
        private string savePath;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"firechaser_lampkeeper_test_{System.Guid.NewGuid():N}.json");
            PlayerProfile.PathOverride = savePath;
            RaidTransfer.Reset();
            RaidBuffs.Clear();
            Map01Authority.SuppressMonsters = true;
            yield return LoadScene(GameScenes.Shelter);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerProfile.PathOverride = null;
            Map01Authority.SuppressMonsters = false;
            RaidTransfer.Reset();
            RaidBuffs.Clear();
            Time.timeScale = 1f;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        [UnityTest]
        public IEnumerator Lampkeeper_SitsInHall_StationOpensBuffPanel()
        {
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            var station = ShelterStation.Find(ShelterStationKind.Lampkeeper);
            Assert.IsNotNull(station, "大厅里有守灯人");
            Assert.IsNotNull(GameObject.Find("Lampkeeper"), "守灯人模型摆进了安全屋");
            Assert.IsTrue(LightRegistry.Scene.IsLit(station.transform.position), "守灯人坐在亮处");

            shelter.OpenStation(station);
            Assert.AreEqual(station, shelter.OpenPanel);
            Assert.AreEqual(RaidBuffKind.None, shelter.SelectedBuff, "进安全屋时还没选 buff");
            shelter.SelectBuff(RaidBuffKind.Dim);
            Assert.AreEqual(RaidBuffKind.Dim, shelter.SelectedBuff);
            shelter.SelectBuff(RaidBuffKind.Everbright);
            Assert.AreEqual(RaidBuffKind.Everbright, shelter.SelectedBuff, "再选会替换，只能带一个");
            shelter.ClosePanel();
        }

        [UnityTest]
        public IEnumerator DepartWithoutBuff_AsksFirst_ThenCanStillGo()
        {
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            shelter.OpenStation(ShelterStation.Find(ShelterStationKind.Exit));
            Assert.IsFalse(shelter.RequestDepart(), "没选 buff 不直接出发");
            Assert.IsTrue(shelter.ConfirmingDepartWithoutBuff, "弹出“没有选择 buff，确定要出发吗？”");
            Assert.IsFalse(RaidTransfer.Active);

            // “回去找守灯人”：确认框关掉，打开守灯人面板。
            shelter.OpenLampkeeper();
            Assert.IsFalse(shelter.ConfirmingDepartWithoutBuff);
            Assert.AreEqual(ShelterStationKind.Lampkeeper, shelter.OpenPanel.Kind);
            shelter.ClosePanel();

            // 再来一次，这次确认不带 buff 出发。
            shelter.OpenStation(ShelterStation.Find(ShelterStationKind.Exit));
            shelter.RequestDepart();
            shelter.ConfirmDepart();
            yield return WaitFor<Map01Authority>();
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            yield return null;
            yield return null;
            var vision = authority.Player.GetComponent<PlayerVision>();
            Assert.AreEqual(PlayerVision.DimScale, vision.Scale, 0.001f, "没 buff：视野 75%");
            Assert.AreEqual(1f, PlayerVision.DetectionScaleOf(authority.Player), 0.001f, "没 buff：发现距离不变");
        }

        [UnityTest]
        public IEnumerator Everbright_AppliesInMap_ClearedAfterReturn()
        {
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            shelter.SelectBuff(RaidBuffKind.Everbright);
            shelter.OpenStation(ShelterStation.Find(ShelterStationKind.Exit));
            Assert.IsTrue(shelter.RequestDepart(), "选了 buff 直接出发，不用确认");
            yield return WaitFor<Map01Authority>();
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            yield return null;
            yield return null;
            var vision = authority.Player.GetComponent<PlayerVision>();
            Assert.AreEqual(1f, vision.Scale, 0.001f, "长明：视野 100%");
            Assert.AreEqual(1f, vision.CameraScale, 0.001f, "跟随镜头高度跟着变");
            Assert.AreEqual(PlayerVision.EverbrightDetectionScale, PlayerVision.DetectionScaleOf(authority.Player), 0.001f, "长明：更容易被发现");

            // 死了回安全屋：buff 清空，下次要重新选。
            authority.Player.Health.TakeDamage(100000f);
            yield return null;
            Object.FindAnyObjectByType<Map01RunFlow>().Restart();
            yield return WaitFor<ShelterController>();
            shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            Assert.AreEqual(RaidBuffKind.None, shelter.SelectedBuff, "回到安全屋 buff 清空");
        }

        [UnityTest]
        public IEnumerator Dim_KeepsNarrowView_HarderToSpot()
        {
            var shelter = Object.FindAnyObjectByType<ShelterController>();
            yield return null;
            shelter.SelectBuff(RaidBuffKind.Dim);
            Assert.IsTrue(shelter.RequestDepart());
            yield return WaitFor<Map01Authority>();
            var authority = Object.FindAnyObjectByType<Map01Authority>();
            yield return null;
            yield return null;
            var vision = authority.Player.GetComponent<PlayerVision>();
            Assert.AreEqual(PlayerVision.DimScale, vision.Scale, 0.001f, "昏暗：视野 75%");
            Assert.AreEqual(RaidBuffs.DimDetectionScale, PlayerVision.DetectionScaleOf(authority.Player), 0.001f, "昏暗：更难被发现");
        }

        private static IEnumerator LoadScene(string path)
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
        }

        private static IEnumerator WaitFor<T>() where T : Object
        {
            for (var frame = 0; frame < 600; frame++)
            {
                yield return null;
                if (Object.FindAnyObjectByType<T>() != null) yield break;
            }

            Assert.Fail($"等待 {typeof(T).Name} 所在场景加载超时");
        }
    }
}
