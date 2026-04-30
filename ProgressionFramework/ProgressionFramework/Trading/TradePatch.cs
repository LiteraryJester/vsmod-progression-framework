using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ProgressionFramework.Trading;

public class TradePatch
{
    public string Op { get; set; } = "";
    public JsonItemStack? ItemData { get; set; }
    public NatFloat? Price { get; set; }
    public NatFloat? Stock { get; set; }
    public NatFloat? Money { get; set; }

    /// <summary>
    /// Serialize a list of patches to a JSON string for storage in WatchedAttributes.
    /// </summary>
    public static string Serialize(List<TradePatch> patches)
    {
        return JsonConvert.SerializeObject(patches);
    }

    /// <summary>
    /// Deserialize patches from a JSON string stored in WatchedAttributes.
    /// </summary>
    public static List<TradePatch> Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json)) return new List<TradePatch>();
        try
        {
            return JsonConvert.DeserializeObject<List<TradePatch>>(json) ?? new List<TradePatch>();
        }
        catch
        {
            return new List<TradePatch>();
        }
    }

    /// <summary>
    /// Apply a list of patches to a TradeProperties object, modifying its Selling/Buying lists and Money.
    /// </summary>
    public static void ApplyPatches(List<TradePatch> patches, TradeProperties props, IWorldAccessor world)
    {
        foreach (var patch in patches)
        {
            switch (patch.Op)
            {
                case "addSelling":
                    if (patch.ItemData != null)
                    {
                        var item = CreateTradeItem(patch);
                        props.Selling.List = props.Selling.List.Append(item).ToArray();
                    }
                    break;

                case "addBuying":
                    if (patch.ItemData != null)
                    {
                        var item = CreateTradeItem(patch);
                        props.Buying.List = props.Buying.List.Append(item).ToArray();
                    }
                    break;

                case "removeSelling":
                    if (patch.ItemData?.Code != null)
                    {
                        var code = patch.ItemData.Code.ToString();
                        props.Selling.List = props.Selling.List
                            .Where(i => !i.Code.ToString().Equals(code, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                    }
                    break;

                case "removeBuying":
                    if (patch.ItemData?.Code != null)
                    {
                        var code = patch.ItemData.Code.ToString();
                        props.Buying.List = props.Buying.List
                            .Where(i => !i.Code.ToString().Equals(code, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                    }
                    break;

                case "setMoney":
                    if (patch.Money != null)
                    {
                        props.Money = patch.Money;
                    }
                    break;
            }
        }
    }

    private static TradeItem CreateTradeItem(TradePatch patch)
    {
        var item = new TradeItem();
        item.Code = patch.ItemData!.Code;
        item.Type = patch.ItemData.Type;
        item.StackSize = patch.ItemData.StackSize > 0 ? patch.ItemData.StackSize : 1;
        if (patch.ItemData.Attributes != null) item.Attributes = patch.ItemData.Attributes;
        item.Price = patch.Price ?? new NatFloat(1, 0, EnumDistribution.UNIFORM);
        item.Stock = patch.Stock ?? new NatFloat(4, 1, EnumDistribution.UNIFORM);
        return item;
    }

    /// <summary>
    /// Check if a patch with the same op and item code already exists.
    /// </summary>
    public static bool HasDuplicate(List<TradePatch> patches, TradePatch newPatch)
    {
        if (newPatch.ItemData?.Code == null) return false;
        var code = newPatch.ItemData.Code.ToString();
        return patches.Any(p => p.Op == newPatch.Op &&
            p.ItemData?.Code?.ToString()?.Equals(code, StringComparison.OrdinalIgnoreCase) == true);
    }
}
