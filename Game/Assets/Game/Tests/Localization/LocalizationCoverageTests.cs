using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WildernessExtraction.Map01;
using WildernessExtraction.Shelter;
using WildernessExtraction.Tutorial;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tests.Localization
{
    /// <summary>
    /// 英文对照表覆盖检查：运行时代码里要显示的中文、以及物品/配方/任务/教学/场景里的中文文案都必须有英文。
    /// 设置环境变量 LOC_DUMP_MISSING=文件路径 时，把缺的条目按对照表格式写出来，方便补译。
    /// </summary>
    public sealed class LocalizationCoverageTests
    {
        /// <summary>只在旧原型场景 VerticalSlice 里用到的界面，不在发布版里，不要求英文。</summary>
        private static readonly string[] PrototypeOnly = { "SliceHud.cs", "RunHud.cs", "HudText.cs", "AiBotController.cs" };
        private static readonly string[] RuntimeRoots = { "Assets/Game/Runtime", "Assets/Map01/Runtime", "Assets/Shelter/Runtime", "Assets/Tutorial/Runtime" };
        private static readonly string[] Scenes = { GameScenes.Map01, GameScenes.Shelter, GameScenes.Tutorial };
        private static readonly string[] SkipLine =
        {
            "Debug.Log", "throw new", "Exception(", "const string", "[Tooltip", "[Header", "[TextArea", "[MenuItem", "[ContextMenu",
            "[CreateAssetMenu", "Tooltip(", "@\"", "nameof(", "new GameObject(", "GameObject.Find(", ".name ==",
        };
        private static readonly Regex Field = new Regex(@"^\s*(\[[^\]]*\]\s*)*(public|private|protected|internal)\b[^(]*?[^=!<>]=[^=>]");
        private static readonly Regex Literal = new Regex(@"(\$?)""((?:[^""\\\n]|\\.)*)""");

        [Test]
        public void Table_ParsesEscapes_AndHasNoEmptyTranslations()
        {
            var table = Loc.Parse("# c\nzh=一\\n二\nen=One\\nTwo\nzh=空\nen=\n");
            Assert.AreEqual("One\nTwo", table["一\n二"]);
            var asset = Resources.Load<TextAsset>(Loc.TableResource);
            Assert.IsNotNull(asset, "Resources/Localization_en.txt 存在");
            var parsed = Loc.Parse(asset.text);
            Assert.Greater(parsed.Count, 100);
            var empty = parsed.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
            CollectionAssert.IsEmpty(empty, "英文不能留空");
        }

        [Test]
        public void English_TranslatesFormatsAndFallsBack()
        {
            try
            {
                Loc.SetLanguageForTests("en");
                Assert.AreEqual("Backpack", Loc.T("背包"));
                Assert.AreEqual("Health 80/100", Loc.F("生命 {0}/{1}", 80, 100));
                Assert.AreEqual("already english", Loc.T("already english"));
                Loc.SetLanguageForTests("zh");
                Assert.AreEqual("背包", Loc.T("背包"));
            }
            finally
            {
                Loc.SetLanguageForTests(null);
            }
        }

        [Test]
        public void Resolve_FollowsSystemLanguageWhenUnset()
        {
            Assert.AreEqual(GameLanguage.Chinese, Loc.Resolve(0));
            Assert.AreEqual(GameLanguage.English, Loc.Resolve(1));
            Assert.IsTrue(Loc.IsChineseSystem(SystemLanguage.ChineseSimplified));
            Assert.IsFalse(Loc.IsChineseSystem(SystemLanguage.English));
        }

        [Test]
        public void CodeStrings_AllLocalized()
        {
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            var unformatted = new List<string>();
            foreach (var path in RuntimeFiles())
            {
                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || !Loc.HasCjk(line) || SkipLine.Any(line.Contains)) continue;
                    if (Field.IsMatch(line) && !line.Contains("=>")) continue;
                    foreach (Match match in Literal.Matches(StripComment(line)))
                    {
                        var text = Unescape(match.Groups[2].Value);
                        if (!Loc.HasCjk(text)) continue;
                        if (match.Groups[1].Value == "$")
                        {
                            unformatted.Add($"{Path.GetFileName(path)}:{i + 1}: {trimmed}");
                            continue;
                        }

                        if (!Loc.HasEnglish(text)) missing.Add(text);
                    }
                }
            }

            Dump(missing, "code");
            CollectionAssert.IsEmpty(unformatted, "显示用的插值字符串要改成 Loc.F(\"中文 {0}\", x)");
            CollectionAssert.IsEmpty(missing, "代码里的中文缺英文");
        }

        [Test]
        public void AssetStrings_AllHaveEnglish()
        {
            var texts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Load<ItemDefinition>()) texts.Add(item.displayName);
            foreach (var weapon in Load<WeaponConfig>()) texts.Add(weapon.displayName);
            foreach (var book in Load<CraftingRecipeBook>())
            {
                foreach (var recipe in book.recipes)
                {
                    texts.Add(recipe.category);
                    texts.Add(recipe.description);
                }
            }

            foreach (var chain in Load<Map01QuestChain>())
            {
                foreach (var quest in chain.quests)
                {
                    texts.Add(quest.title);
                    texts.Add(quest.goal);
                    texts.Add(quest.completionText);
                    foreach (var step in quest.steps) texts.Add(step);
                }

                foreach (var record in chain.records)
                {
                    texts.Add(record.title);
                    texts.Add(record.body);
                }

                foreach (var supply in chain.supplies) texts.Add(supply.label);
                texts.Add(chain.chainCompleteText);
                texts.Add(chain.hearthIdleText);
                texts.Add(chain.oldMapIdleText);
            }

            foreach (var config in Load<TutorialConfig>())
            {
                texts.Add(config.openingCaption);
                texts.Add(config.endingCaption);
                foreach (var step in config.steps)
                {
                    texts.Add(step.title);
                    foreach (var line in step.lines) texts.Add(line.text);
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game", "Assets/Map01", "Assets/Shelter", "Assets/Tutorial" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null) CollectSceneTexts(prefab.GetComponentsInChildren<Component>(true), texts);
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var scenePath in Scenes)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true)).ToArray();
                    CollectSceneTexts(components, texts);
                }
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            }

            var missing = new SortedSet<string>(texts.Where(t => !string.IsNullOrEmpty(t) && Loc.HasCjk(t) && !Loc.HasEnglish(t)), StringComparer.Ordinal);
            Dump(missing, "assets");
            CollectionAssert.IsEmpty(missing, "资产里的中文缺英文");
        }

        private static void CollectSceneTexts(IEnumerable<Component> components, HashSet<string> texts)
        {
            foreach (var component in components)
            {
                switch (component)
                {
                    case LootContainer or ShelterStation or TutorialCrate:
                        var serialized = new SerializedObject(component);
                        foreach (var name in new[] { "displayName", "crateName" })
                        {
                            var property = serialized.FindProperty(name);
                            if (property != null && property.propertyType == SerializedPropertyType.String) texts.Add(property.stringValue);
                        }

                        break;
                    case Map01Authority authority:
                        foreach (var entry in authority.Shop) texts.Add(entry.displayName);
                        break;
                }
            }
        }

        private static IEnumerable<T> Load<T>() where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) yield return asset;
            }
        }

        private static IEnumerable<string> RuntimeFiles()
        {
            foreach (var root in RuntimeRoots)
            {
                foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(path);
                    if (PrototypeOnly.Contains(name) || path.Replace('\\', '/').Contains("/Editor/")) continue;
                    yield return path;
                }
            }
        }

        private static string StripComment(string line)
        {
            var inString = false;
            for (var i = 0; i < line.Length - 1; i++)
            {
                if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
                if (!inString && line[i] == '/' && line[i + 1] == '/') return line.Substring(0, i);
            }

            return line;
        }

        private static string Unescape(string text) => text.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");

        private static void Dump(IEnumerable<string> missing, string label)
        {
            var path = Environment.GetEnvironmentVariable("LOC_DUMP_MISSING");
            if (string.IsNullOrEmpty(path)) return;
            var builder = new StringBuilder();
            builder.Append("# ").Append(label).Append('\n');
            foreach (var text in missing) builder.Append("zh=").Append(Loc.Escape(text)).Append("\nen=\n");
            File.AppendAllText(path, builder.ToString(), new UTF8Encoding(false));
        }
    }
}
