using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 阶段 4：把最小可玩循环接进 Map01 场景——权威层、四个设施、HUD 和玩家交互组件。
    /// 物品与商店条目只在缺失时创建，之后由策划调价格。
    /// </summary>
    public static class Map01GameplaySetup
    {
        private const string ItemsFolder = "Assets/Map01/Data/Items";
        private const string GameItemsFolder = "Assets/Game/Data/Items";

        public static void Create(Transform mapRoot, GameObject player, Map01Lighting.Result lighting, Terrain terrain)
        {
            EnsureFolder(ItemsFolder);
            var emptyBattery = Item("EmptyBattery", "empty_battery", "空电池", 2, 0, new Color(0.5f, 0.55f, 0.6f));
            var chargedBattery = Item("ChargedBattery", "charged_battery", "充好的电池", 2, 0, new Color(0.4f, 0.95f, 0.5f));
            var repairPart = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{GameItemsFolder}/Item_MechanicalParts.asset");
            var medical = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{GameItemsFolder}/Item_FirstAidSpray.asset");

            var stations = mapRoot.Find("Interactables");
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            var factory = Map01Layout.FindBuilding("map01_factory");
            Station(stations, "map01_safehouse_recycle", Map01Station.RecycleTerminal, safehouse, "RecycleCounter");
            Station(stations, "map01_safehouse_shop", Map01Station.ShopTerminal, safehouse, "ShopTerminal");
            Station(stations, "map01_safehouse_battery_slot", Map01Station.BatterySlot, safehouse, "BatterySlot");
            Station(stations, "map01_factory_generator", Map01Station.Generator, factory, "Generator");
            var altar = new GameObject("map01_hill_altar");
            altar.transform.SetParent(stations, false);
            var altarPosition = new Vector3(Map01Layout.AltarPosition.x, 0f, Map01Layout.AltarPosition.y);
            altarPosition.y = terrain.SampleHeight(altarPosition) + terrain.transform.position.y;
            altar.transform.position = altarPosition;
            altar.AddComponent<Map01Interactable>().Configure("map01_hill_altar", Map01Station.Altar);

            // 昼夜：接管方向光、环境光和相机背景色；默认黑夜。
            var sun = Array.Find(Object.FindObjectsByType<Light>(FindObjectsSortMode.None), l => l.type == LightType.Directional);
            var daylight = sun.gameObject.AddComponent<Map01Daylight>();
            var daylightSerialized = new SerializedObject(daylight);
            daylightSerialized.FindProperty("sun").objectReferenceValue = sun;
            daylightSerialized.FindProperty("viewCamera").objectReferenceValue = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            var swarms = daylightSerialized.FindProperty("fireflies");
            swarms.arraySize = lighting.Fireflies.Length;
            for (var i = 0; i < lighting.Fireflies.Length; i++) swarms.GetArrayElementAtIndex(i).objectReferenceValue = lighting.Fireflies[i];
            daylightSerialized.ApplyModifiedPropertiesWithoutUndo();

            var game = new GameObject("Map01_Game");
            var authority = game.AddComponent<Map01Authority>();
            var serialized = new SerializedObject(authority);
            serialized.FindProperty("map").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapDefinition>(Map01Content.MapDefinitionPath);
            serialized.FindProperty("tables").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ContentTables>(Map01Content.ContentTablesPath);
            serialized.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerCharacter>();
            serialized.FindProperty("containerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<LootContainer>("Assets/Game/Prefabs/LootContainer.prefab");
            serialized.FindProperty("monsterPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MeleeEnemy>("Assets/Game/Prefabs/MeleeEnemy.prefab");
            serialized.FindProperty("repairPart").objectReferenceValue = repairPart;
            serialized.FindProperty("emptyBattery").objectReferenceValue = emptyBattery;
            serialized.FindProperty("chargedBattery").objectReferenceValue = chargedBattery;
            serialized.FindProperty("identityTag").objectReferenceValue = IdentityTagItem();
            serialized.FindProperty("daylight").objectReferenceValue = daylight;
            serialized.FindProperty("factoryLamp").objectReferenceValue = lighting.FactoryLamp;
            var lamps = serialized.FindProperty("extractionLamps");
            lamps.arraySize = lighting.ExtractionLamps.Count;
            for (var i = 0; i < lighting.ExtractionLamps.Count; i++) lamps.GetArrayElementAtIndex(i).objectReferenceValue = lighting.ExtractionLamps[i];

            var shop = serialized.FindProperty("shop");
            var entries = new[]
            {
                (id: "medical_spray", name: "急救喷雾", price: 30, kind: ShopKind.Item, item: medical, slots: 0, limit: 0),
                (id: "backpack_upgrade", name: "背包扩容 +4 格", price: 60, kind: ShopKind.BackpackUpgrade, item: (ItemDefinition)null, slots: 4, limit: 2),
                (id: "empty_battery", name: "空电池", price: 20, kind: ShopKind.Item, item: emptyBattery, slots: 0, limit: 0)
            };
            shop.arraySize = entries.Length;
            for (var i = 0; i < entries.Length; i++)
            {
                var element = shop.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("entryId").stringValue = entries[i].id;
                element.FindPropertyRelative("displayName").stringValue = entries[i].name;
                element.FindPropertyRelative("price").intValue = entries[i].price;
                element.FindPropertyRelative("kind").enumValueIndex = (int)entries[i].kind;
                element.FindPropertyRelative("item").objectReferenceValue = entries[i].item;
                element.FindPropertyRelative("extraSlots").intValue = entries[i].slots;
                element.FindPropertyRelative("purchaseLimit").intValue = entries[i].limit;
            }

            var markers = new List<Renderer>();
            foreach (Transform marker in mapRoot.Find("UI_Markers"))
            {
                if (marker.name.StartsWith("Marker_")) markers.Add(marker.GetComponent<Renderer>());
            }

            var markerProperty = serialized.FindProperty("extractionMarkers");
            markerProperty.arraySize = markers.Count;
            for (var i = 0; i < markers.Count; i++) markerProperty.GetArrayElementAtIndex(i).objectReferenceValue = markers[i];
            serialized.FindProperty("extractionActiveMaterial").objectReferenceValue = ActiveMarkerMaterial();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var interactor = player.AddComponent<Map01Interactor>();
            var interactorSerialized = new SerializedObject(interactor);
            interactorSerialized.FindProperty("authority").objectReferenceValue = authority;
            interactorSerialized.ApplyModifiedPropertiesWithoutUndo();

            var hud = game.AddComponent<Map01Hud>();
            var hudSerialized = new SerializedObject(hud);
            hudSerialized.FindProperty("authority").objectReferenceValue = authority;
            hudSerialized.FindProperty("interactor").objectReferenceValue = interactor;
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Station(Transform parent, string id, Map01Station kind, BuildingSpec building, string propName)
        {
            var prop = Array.Find(building.Props, p => p.Name == propName);
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(building.Center.x + prop.Center.x, 0f, building.Center.y + prop.Center.y);
            go.AddComponent<Map01Interactable>().Configure(id, kind);
        }

        /// <summary>单机占位：“别人的身份牌”，从深处幸存者遗体上取得。不能回收，只能献祭。</summary>
        public static ItemDefinition IdentityTagItem()
        {
            EnsureFolder(ItemsFolder);
            return Item("IdentityTag", "identity_tag", "别人的身份牌", 1, 0, new Color(0.85f, 0.8f, 0.55f));
        }

        private static ItemDefinition Item(string file, string id, string displayName, int size, int value, Color color)
        {
            var path = $"{ItemsFolder}/Item_{file}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null)
            {
                return item;
            }

            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = displayName;
            item.category = ItemCategory.Consumable;
            item.size = size;
            item.maxStack = 1;
            item.unitWeight = 3f;
            item.stashValue = value;
            item.iconColor = color;
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        private static Material ActiveMarkerMaterial()
        {
            const string path = "Assets/Map01/Materials/M_Map01_ExtractionActive.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.3f, 0.95f, 0.5f) };
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
