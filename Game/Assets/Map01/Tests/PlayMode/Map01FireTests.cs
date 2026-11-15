using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>火把与炉火：提供小范围安全光、光焰闪烁、熄灭时一并关闭；不挡路、不进河；越远越稀。</summary>
    public sealed class Map01FireTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

        private Map01Authority authority;
        private PlayerCharacter player;
        private SafeLightSource[] torches;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SuppressMonsters = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            authority = Object.FindAnyObjectByType<Map01Authority>();
            player = authority.Player;
            torches = GameObject.Find("Map01_Root/Lighting/Torches").GetComponentsInChildren<SafeLightSource>();
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SuppressMonsters = false;
        }

        [Test]
        public void Torches_AreSmallFixedLights_OutOfTheWay()
        {
            Assert.GreaterOrEqual(torches.Length, 12, "地图上应有一批火把");
            var lamp = GameObject.Find("Lamp_BridgeSouth").GetComponent<SafeLightSource>();
            foreach (var torch in torches)
            {
                var flat = new Vector2(torch.transform.position.x, torch.transform.position.z);
                Assert.AreEqual(SafeLightKind.FixedLamp, torch.Kind);
                Assert.IsTrue(torch.IsLit, $"{torch.name} 应点亮");
                Assert.Less(torch.Radius, lamp.Radius, "火把半径小于路灯");
                Assert.IsFalse(Map01Layout.IsInRiver(flat, 1f), $"{torch.name} 不在河里");
                Assert.Greater(Map01Layout.DistanceToRoads(flat), Map01Layout.RoadWidth * 0.5f, $"{torch.name} 不在路面上");
                foreach (var building in Map01Layout.Buildings)
                {
                    var local = flat - building.Center;
                    Assert.IsFalse(Mathf.Abs(local.x) < building.Size.x * 0.5f && Mathf.Abs(local.y) < building.Size.y * 0.5f, $"{torch.name} 不在 {building.Id} 里");
                }

                Assert.AreEqual(0, torch.GetComponentsInChildren<Collider>().Length, $"{torch.name} 没有碰撞");
            }
        }

        [Test]
        public void Torches_GetSparser_AwayFromSafehouse()
        {
            var safehouse = Map01Layout.FindBuilding("map01_safehouse").Center;
            var deep = torches.Where(t => Distance(t.transform.position, safehouse) > 300f).ToArray();
            var near = torches.Where(t => Distance(t.transform.position, safehouse) <= 300f).ToArray();
            Assert.Greater(deep.Length, 0, "深区也有少量点缀");
            Assert.Less(deep.Length, near.Length);

            // 深区任意两支火把的光圈之间都隔着一大段黑暗，走不成安全链。
            foreach (var a in deep)
            {
                foreach (var b in deep)
                {
                    if (a == b || a.name.Contains("Warehouse") && b.name.Contains("Warehouse")) continue;
                    Assert.Greater(Vector3.Distance(a.transform.position, b.transform.position), 30f, $"{a.name} 与 {b.name} 不该挨着");
                }
            }
        }

        [UnityTest]
        public IEnumerator StandingByTorch_ProtectsFromDarkness_InsideRadiusOnly()
        {
            var torch = torches.First(t => t.name == "Torch_DeepSouthWest");
            var center = torch.transform.position;
            Assert.IsTrue(LightRegistry.Scene.IsLit(center + new Vector3(torch.Radius - 0.5f, 0f, 0f)), "光圈内算有光");
            Assert.IsFalse(LightRegistry.Scene.IsLit(center + new Vector3(torch.Radius + 1f, 0f, 0f)), "光圈外仍是黑暗");

            player.Motor.Teleport(center + new Vector3(1f, 0.5f, 0f));
            var before = player.Health.Current;
            yield return new WaitForSeconds(3.5f);
            Assert.AreEqual(before, player.Health.Current, "站在火把旁不掉血");

            torch.SetLit(false);
            yield return null;
            Assert.IsFalse(LightRegistry.Scene.IsLit(center), "熄灭后不再提供保护");
        }

        [UnityTest]
        public IEnumerator Flame_Flickers_AndGoesOutWithTheLight()
        {
            var torch = torches[0];
            var light = torch.GetComponentInChildren<Light>();
            var flicker = torch.GetComponent<FireFlicker>();
            var particles = torch.GetComponentsInChildren<ParticleSystem>();
            Assert.IsNotNull(flicker);
            Assert.GreaterOrEqual(particles.Length, 2, "火焰和火星");
            Assert.IsTrue(particles.All(p => p.isPlaying), "点亮时粒子在播放");

            var range = torch.Radius * 1.25f;
            float low = float.MaxValue, high = float.MinValue;
            for (var i = 0; i < 40; i++)
            {
                yield return null;
                low = Mathf.Min(low, light.intensity);
                high = Mathf.Max(high, light.intensity);
                Assert.AreEqual(range, light.range, range * 0.05f, "照射距离只轻微抖动");
            }

            Assert.Greater(high, low, "亮度应有变化");
            Assert.GreaterOrEqual(low, flicker.BaseIntensity * (1f - flicker.IntensityJitter) - 0.01f);
            Assert.LessOrEqual(high, flicker.BaseIntensity * (1f + flicker.IntensityJitter) + 0.01f);

            torch.SetLit(false);
            yield return null;
            Assert.IsFalse(light.enabled, "熄灭后点光源关闭");
            Assert.IsFalse(particles.Any(p => p.gameObject.activeInHierarchy), "熄灭后火焰隐藏");

            torch.SetLit(true);
            yield return null;
            Assert.IsTrue(light.enabled);
            Assert.IsTrue(particles.All(p => p.isPlaying), "重新点亮后粒子恢复");
        }

        [Test]
        public void Hearth_IsARealFire_NotALampPost()
        {
            var hearth = GameObject.Find("Hearth_Safehouse");
            Assert.IsNull(hearth.transform.Find("Post"), "炉火不该带灯柱");
            Assert.IsNotNull(hearth.GetComponent<FireFlicker>());
            Assert.IsTrue(hearth.GetComponentsInChildren<ParticleSystem>().All(p => p.isPlaying));
            Assert.AreEqual(5f, hearth.GetComponent<SafeLightSource>().Radius, 0.01f, "炉火半径不变");
        }

        [Test]
        public void ColdFirePit_HasNoLightOrFlame()
        {
            var pit = GameObject.Find("ColdFirePit");
            Assert.IsNotNull(pit, "树林里有熄灭的旧火堆");
            Assert.Greater(pit.GetComponentsInChildren<Renderer>().Length, 8);
            Assert.AreEqual(0, pit.GetComponentsInChildren<Light>().Length);
            Assert.AreEqual(0, pit.GetComponentsInChildren<ParticleSystem>().Length);
        }

        private static float Distance(Vector3 position, Vector2 other)
        {
            return Vector2.Distance(new Vector2(position.x, position.z), other);
        }
    }
}
