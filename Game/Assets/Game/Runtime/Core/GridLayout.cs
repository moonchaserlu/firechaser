using System;
using System.Collections.Generic;

namespace WildernessExtraction
{
    /// <summary>格子里一件东西的摆放：左上角格子、是否竖放。占 1 格的东西不分横竖；占 2 格的默认横放（2×1），竖放为 1×2。</summary>
    public struct GridPlacement
    {
        public int X;
        public int Y;
        public int Size;
        public bool Rotated;

        public int Width => Size >= 2 && !Rotated ? 2 : 1;
        public int Height => Size >= 2 && Rotated ? 2 : 1;

        public bool Covers(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
    }

    /// <summary>
    /// 网格摆放（纯规则，便于 EditMode 测试）：背包 2×4、箱子 3×4。每样东西占 1 格或 2 格（2 格的能横放、竖放）。
    /// 放不下时会重新整理（先摆 2 格的再摆 1 格的）；总面积不超过网格就一定摆得下。
    /// </summary>
    public sealed class GridLayout<T> where T : class
    {
        private readonly Dictionary<T, GridPlacement> placements = new Dictionary<T, GridPlacement>();
        private readonly List<T> order = new List<T>();

        public GridLayout(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; private set; }
        public int Area => Width * Height;
        public IReadOnlyList<T> Keys => order;
        public int UsedArea { get { var n = 0; foreach (var key in order) n += Math.Max(1, placements[key].Size); return n; } }

        public bool Contains(T key) => key != null && placements.ContainsKey(key);
        public GridPlacement this[T key] => placements[key];
        public bool TryGet(T key, out GridPlacement placement) => placements.TryGetValue(key, out placement);

        /// <summary>网格加高（背包扩容）。</summary>
        public void Grow(int height)
        {
            if (height > Height) Height = height;
        }

        /// <summary>(x, y) 那一格上的东西；空格为 null。</summary>
        public T At(int x, int y)
        {
            foreach (var key in order)
            {
                if (placements[key].Covers(x, y)) return key;
            }

            return null;
        }

        /// <summary>在 (x, y) 放一件 size 格的东西（rotated 表示竖放）是否放得下；ignore 是正在挪动的那件自己。</summary>
        public bool Fits(int x, int y, int size, bool rotated, T ignore = null)
        {
            var probe = new GridPlacement { X = x, Y = y, Size = size, Rotated = rotated };
            if (x < 0 || y < 0 || x + probe.Width > Width || y + probe.Height > Height) return false;
            foreach (var key in order)
            {
                if (key == ignore) continue;
                var other = placements[key];
                if (Overlaps(probe, other)) return false;
            }

            return true;
        }

        /// <summary>放到指定位置（挪动已有的也用它）；放不下返回 false，原位置不变。</summary>
        public bool TryPlace(T key, int size, int x, int y, bool rotated)
        {
            if (key == null || !Fits(x, y, size, rotated, key)) return false;
            if (!placements.ContainsKey(key)) order.Add(key);
            placements[key] = new GridPlacement { X = x, Y = y, Size = size, Rotated = size >= 2 && rotated };
            return true;
        }

        /// <summary>找第一个空位（先横后竖）；没有空位但总面积够时整理一遍再放。放不下返回 false。</summary>
        public bool AutoPlace(T key, int size)
        {
            if (key == null) return false;
            if (placements.ContainsKey(key)) return true;
            if (FirstFree(size, null, out var x, out var y, out var rotated)) return TryPlace(key, size, x, y, rotated);
            if (UsedArea + Math.Max(1, size) > Area) return false;
            var sizes = new Dictionary<T, int>();
            foreach (var existing in order) sizes[existing] = placements[existing].Size;
            sizes[key] = size;
            return Repack(sizes);
        }

        public bool Remove(T key)
        {
            if (key == null || !placements.Remove(key)) return false;
            order.Remove(key);
            return true;
        }

        public void Clear()
        {
            placements.Clear();
            order.Clear();
        }

        /// <summary>整理：清空后先摆 2 格的、再摆 1 格的，按行从上到下。摆不下（总面积超了）时保持原样并返回 false。</summary>
        public bool Repack(IDictionary<T, int> sizes)
        {
            var backup = new Dictionary<T, GridPlacement>(placements);
            var backupOrder = new List<T>(order);
            Clear();
            var keys = new List<T>(sizes.Keys);
            keys.Sort((a, b) => sizes[b].CompareTo(sizes[a]));
            foreach (var key in keys)
            {
                if (FirstFree(sizes[key], null, out var x, out var y, out var rotated) && TryPlace(key, sizes[key], x, y, rotated)) continue;
                placements.Clear();
                order.Clear();
                foreach (var pair in backup) placements[pair.Key] = pair.Value;
                order.AddRange(backupOrder);
                return false;
            }

            return true;
        }

        public bool FirstFree(int size, T ignore, out int x, out int y, out bool rotated)
        {
            for (y = 0; y < Height; y++)
            {
                for (x = 0; x < Width; x++)
                {
                    if (Fits(x, y, size, false, ignore)) { rotated = false; return true; }
                    if (size >= 2 && Fits(x, y, size, true, ignore)) { rotated = true; return true; }
                }
            }

            x = y = 0;
            rotated = false;
            return false;
        }

        private static bool Overlaps(GridPlacement a, GridPlacement b) =>
            a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
    }
}
