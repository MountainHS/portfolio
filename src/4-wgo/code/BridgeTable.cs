using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Jay.FileIO;

namespace WeGotOne
{
    public class BridgeTable
    {
        private const int BridgeCount = 15;

        private readonly Dictionary<int, BridgeRef> bridges;

        public static async Task<BridgeTable> LoadAsync(
            IEnumerable<ItemRef> items)
        {
            List<BridgeDto> bridgeDtos = await TSVReader.ReadTableAsync<BridgeDto>("BridgeData", true);
            return new BridgeTable(bridgeDtos, items);
        }

        public BridgeTable(List<BridgeDto> bridgeDtos, IEnumerable<ItemRef> items)
        {
            if (bridgeDtos == null)
            {
                throw new ArgumentNullException(nameof(bridgeDtos));
            }

            Dictionary<int, ItemRef> itemById = CreateItemLookup(items);
            bridges = CreateBridgeReferences(bridgeDtos, itemById);
        }

        public BridgeRef Get(int bridgeIndex)
        {
            return bridges[bridgeIndex];
        }

        public bool TryGet(int bridgeIndex, out BridgeRef bridgeRef)
        {
            return bridges.TryGetValue(bridgeIndex, out bridgeRef);
        }

        private static Dictionary<int, ItemRef> CreateItemLookup(
            IEnumerable<ItemRef> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var itemById = new Dictionary<int, ItemRef>();
            foreach (ItemRef item in items)
            {
                if (item == null)
                {
                    throw new InvalidOperationException("ItemRef 목록에 null이 포함되어 있습니다.");
                }

                if (itemById.ContainsKey(item.ItemId))
                {
                    throw new InvalidOperationException($"중복된 ItemId가 있습니다: {item.ItemId}");
                }

                itemById.Add(item.ItemId, item);
            }

            return itemById;
        }

        private static Dictionary<int, BridgeRef> CreateBridgeReferences(
            List<BridgeDto> bridgeDtos,
            Dictionary<int, ItemRef> itemById)
        {
            var bridgeByIndex = new Dictionary<int, BridgeRef>();

            foreach (BridgeDto bridgeDto in bridgeDtos)
            {
                if (bridgeDto.BridgeIndex < 1 || bridgeDto.BridgeIndex > BridgeCount)
                {
                    throw new InvalidOperationException($"BridgeIndex 범위를 벗어났습니다: {bridgeDto.BridgeIndex}");
                }

                if (bridgeByIndex.ContainsKey(bridgeDto.BridgeIndex))
                {
                    throw new InvalidOperationException($"중복된 BridgeIndex가 있습니다: {bridgeDto.BridgeIndex}");
                }

                if (bridgeDto.RequiredValue <= 0)
                {
                    throw new InvalidOperationException(
                        $"RequiredValue는 0보다 커야 합니다: {bridgeDto.BridgeIndex}");
                }

                Dictionary<int, int> minimumRequirements = ParseMinimumRequirements(
                    bridgeDto.MinimumItems,
                    bridgeDto.BridgeIndex);

                foreach (KeyValuePair<int, int> requirement in minimumRequirements)
                {
                    if (!itemById.TryGetValue(requirement.Key, out ItemRef item))
                    {
                        throw new InvalidOperationException(
                            $"존재하지 않는 ItemId가 요구됩니다: {requirement.Key}");
                    }

                    if (item.BuildValue <= 0)
                    {
                        throw new InvalidOperationException(
                            $"BuildValue가 0 이하인 ItemId가 요구됩니다: {requirement.Key}");
                    }
                }

                bridgeByIndex.Add(
                    bridgeDto.BridgeIndex,
                    new BridgeRef(
                        bridgeDto.BridgeIndex,
                        bridgeDto.RequiredValue,
                        minimumRequirements));
            }

            for (int bridgeIndex = 1; bridgeIndex <= BridgeCount; bridgeIndex++)
            {
                if (!bridgeByIndex.ContainsKey(bridgeIndex))
                {
                    throw new InvalidOperationException($"누락된 BridgeIndex가 있습니다: {bridgeIndex}");
                }
            }

            return bridgeByIndex;
        }

        private static Dictionary<int, int> ParseMinimumRequirements(
            string minimumItems,
            int bridgeIndex)
        {
            var minimumRequirements = new Dictionary<int, int>();
            if (minimumItems == string.Empty)
            {
                return minimumRequirements;
            }

            if (minimumItems == null)
            {
                throw new InvalidOperationException(
                    $"BridgeIndex {bridgeIndex}의 MinimumItems가 없습니다.");
            }

            string[] entries = minimumItems.Split(',');
            foreach (string entry in entries)
            {
                string[] values = entry.Split(':');
                if (values.Length != 2 ||
                    !int.TryParse(
                        values[0],
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out int itemId) ||
                    !int.TryParse(
                        values[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int minimumCount) ||
                    minimumCount <= 0)
                {
                    throw new InvalidOperationException(
                        $"BridgeIndex {bridgeIndex}의 MinimumItems 형식이 올바르지 않습니다.");
                }

                if (minimumRequirements.ContainsKey(itemId))
                {
                    throw new InvalidOperationException(
                        $"BridgeIndex {bridgeIndex}에 중복된 ItemId가 있습니다: {itemId}");
                }

                minimumRequirements.Add(itemId, minimumCount);
            }

            return minimumRequirements;
        }
    }
}
