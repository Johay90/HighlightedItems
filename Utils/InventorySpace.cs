using System.Collections.Generic;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Components;
using System.Linq;

namespace HighlightedItems.Utils
{
    record ItemToMove(NormalInventoryItem Item, int Width, int Height, double Priority);

    class ItemGroup
    {
        public (int Width, int Height) Dimensions { get; set; }
        public List<NormalInventoryItem> Items { get; set; }
        public int Area { get; set; }
        public int StackSize { get; set; }
    }

    class InventorySpace
    {
        private readonly bool[,] _occupiedCells;
        private readonly bool[,] _ignoredCells;
        public const int Width = 12;
        public const int Height = 5;

        public InventorySpace(bool[,] ignoredCells)
        {
            _occupiedCells = new bool[Height, Width];
            _ignoredCells = ignoredCells;
        }

        public bool CanFitItem(int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || x + width > Width || y + height > Height)
                return false;

            for (int i = y; i < y + height; i++)
            {
                for (int j = x; j < x + width; j++)
                {
                    if (_occupiedCells[i, j] || _ignoredCells[i, j])
                        return false;
                }
            }

            return true;
        }

        public void PlaceItem(int x, int y, int width, int height)
        {
            for (int i = y; i < y + height; i++)
            {
                for (int j = x; j < x + width; j++)
                {
                    _occupiedCells[i, j] = true;
                }
            }
        }

        public bool FindSpace(int width, int height, out int foundX, out int foundY)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (CanFitItem(x, y, width, height))
                    {
                        foundX = x;
                        foundY = y;
                        return true;
                    }
                }
            }

            foundX = -1;
            foundY = -1;
            return false;
        }

        public void InitializeFromInventory(IEnumerable<ServerInventory.InventSlotItem> currentItems)
        {
            foreach (var item in currentItems)
            {
                PlaceItem(item.PosX, item.PosY, item.SizeX, item.SizeY);
            }
        }

        public List<NormalInventoryItem> OptimizeItemSelection(
            List<NormalInventoryItem> highlightedItems,
            IEnumerable<ServerInventory.InventSlotItem> currentInventory)
        {
            InitializeFromInventory(currentInventory);

            var itemGroups = highlightedItems
                .GroupBy(item => (Width: item.ItemWidth, Height: item.ItemHeight))
                .Select(g => new ItemGroup
                {
                    Dimensions = g.Key,
                    Items = g.ToList(),
                    Area = g.Key.Width * g.Key.Height,
                    StackSize = g.Sum(item => item.Item?.GetComponent<Stack>()?.Size ?? 1)
                })
                .OrderByDescending(g => g.Area)
                .ToList();

            var bestCombination = FindBestCombination(itemGroups);
            return bestCombination;
        }

        private List<NormalInventoryItem> FindBestCombination(List<ItemGroup> itemGroups)
        {
            var bestItems = new List<NormalInventoryItem>();
            var bestScore = 0.0;

            foreach (var group in itemGroups.Take(3))
            {
                var tempResult = new List<NormalInventoryItem>();
                var workingSpace = CloneOccupiedCells();

                if (TryPlaceFirstItem(group.Items[0], workingSpace))
                {
                    tempResult.Add(group.Items[0]);
                    FillRemainingSpace(workingSpace, itemGroups, tempResult);

                    var score = CalculatePackingScore(tempResult);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestItems = new List<NormalInventoryItem>(tempResult);
                    }
                }
            }

            return bestItems;
        }

        private bool TryPlaceFirstItem(NormalInventoryItem item, bool[,] space)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (CanPlaceItemAt(x, y, item.ItemWidth, item.ItemHeight, space))
                    {
                        PlaceItemAt(x, y, item.ItemWidth, item.ItemHeight, space);
                        return true;
                    }
                }
            }
            return false;
        }

        private void FillRemainingSpace(bool[,] space, List<ItemGroup> itemGroups, List<NormalInventoryItem> result)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var group in itemGroups)
                {
                    foreach (var item in group.Items)
                    {
                        if (result.Contains(item)) continue;

                        if (TryPlaceItem(item, space))
                        {
                            result.Add(item);
                            changed = true;
                        }
                    }
                }
            } while (changed);
        }

        private bool TryPlaceItem(NormalInventoryItem item, bool[,] space)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (CanPlaceItemAt(x, y, item.ItemWidth, item.ItemHeight, space))
                    {
                        PlaceItemAt(x, y, item.ItemWidth, item.ItemHeight, space);
                        return true;
                    }
                }
            }
            return false;
        }

        private double CalculatePackingScore(List<NormalInventoryItem> items)
        {
            double totalArea = 0;
            double totalItems = items.Count;
            double stackBonus = 0;

            foreach (var item in items)
            {
                var area = item.ItemWidth * item.ItemHeight;
                totalArea += area;

                var stackSize = item.Item?.GetComponent<Stack>()?.Size ?? 1;
                stackBonus += stackSize * area;
            }

            return (totalArea / (Width * Height)) * 0.4 +
                   (totalItems / items.Count) * 0.3 +
                   (stackBonus / (Width * Height)) * 0.3;
        }

        private bool CanPlaceItemAt(int x, int y, int width, int height, bool[,] space)
        {
            if (x + width > Width || y + height > Height) return false;

            for (int i = y; i < y + height; i++)
            {
                for (int j = x; j < x + width; j++)
                {
                    if (space[i, j] || _ignoredCells[i, j])
                        return false;
                }
            }
            return true;
        }

        private void PlaceItemAt(int x, int y, int width, int height, bool[,] space)
        {
            for (int i = y; i < y + height; i++)
            {
                for (int j = x; j < x + width; j++)
                {
                    space[i, j] = true;
                }
            }
        }

        private bool[,] CloneOccupiedCells()
        {
            var clone = new bool[Height, Width];
            for (int i = 0; i < Height; i++)
                for (int j = 0; j < Width; j++)
                    clone[i, j] = _occupiedCells[i, j];
            return clone;
        }
    }
}