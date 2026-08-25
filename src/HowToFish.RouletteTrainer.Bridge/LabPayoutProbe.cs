using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace HowToFish.RouletteTrainer.Bridge;

internal sealed class LabPayoutProbe
{
    private object _item;
    private PropertyInfo _totalWorth;
    private PropertyInfo _bettingMultiplier;
    private int _beforeWorth;
    private float _beforeMultiplier;
    private int _requestedColor;

    internal int BeforeWorth => _beforeWorth;
    internal float BeforeMultiplier => _beforeMultiplier;
    internal int RequestedColor => _requestedColor;

    internal bool TryStart(RouletteAccess roulette, int requestedColor, out string error)
    {
        error = null;
        try
        {
            var itemType = Type.GetType("Item, Assembly-CSharp");
            var itemManagerType = Type.GetType("ItemManager, Assembly-CSharp");
            var casinoType = Type.GetType("CasinoManager, Assembly-CSharp");
            var betColorType = Type.GetType("BetColor, Assembly-CSharp");
            var gameInfoType = Type.GetType("GameInfo, Assembly-CSharp");
            if (itemType == null || itemManagerType == null || casinoType == null || betColorType == null || gameInfoType == null)
                throw new InvalidOperationException("required game type missing");

            var itemManager = StaticMember(itemManagerType, "Instance")
                              ?? throw new InvalidOperationException("ItemManager.Instance missing");
            var casino = StaticMember(casinoType, "Instance")
                         ?? throw new InvalidOperationException("CasinoManager.Instance missing");
            var idToItem = gameInfoType.GetMethod("IDToItem", BindingFlags.Static | BindingFlags.Public)
                           ?? throw new MissingMethodException("GameInfo.IDToItem");
            var defaultWorth = itemType.GetProperty("DefaultWorth", BindingFlags.Instance | BindingFlags.Public)
                               ?? throw new MissingMemberException("Item.DefaultWorth");
            object prefab = null;
            for (var id = 0; id <= byte.MaxValue; id++)
            {
                var candidate = idToItem.Invoke(null, new object[] { (byte)id });
                if (candidate != null && defaultWorth.GetValue(candidate) is int worth && worth > 0)
                {
                    prefab = candidate;
                    break;
                }
            }
            if (prefab == null) throw new InvalidOperationException("no positive-worth item prefab found");

            var spawn = itemManagerType.GetMethod("SpawnNewItem", BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new MissingMethodException("ItemManager.SpawnNewItem");
            _item = spawn.Invoke(itemManager, new[]
            {
                prefab,
                (object)(roulette.Wheel.position + Vector3.up * 1.5f),
                Quaternion.identity
            });
            if (_item == null) throw new InvalidOperationException("item spawn returned null");

            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(itemType);
            var list = (IList)Activator.CreateInstance(listType);
            list.Add(_item);
            var setBetItems = casinoType.GetMethod("SetBetItems", BindingFlags.Static | BindingFlags.Public)
                              ?? throw new MissingMethodException("CasinoManager.SetBetItems");
            setBetItems.Invoke(null, new object[] { list });

            _totalWorth = itemType.GetProperty("TotalWorth", BindingFlags.Instance | BindingFlags.Public)
                          ?? throw new MissingMemberException("Item.TotalWorth");
            _bettingMultiplier = itemType.GetProperty("BettingMultiplier",
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException("Item.BettingMultiplier");
            _beforeWorth = Convert.ToInt32(_totalWorth.GetValue(_item));
            _beforeMultiplier = Convert.ToSingle(_bettingMultiplier.GetValue(_item));
            if (_beforeWorth <= 0) throw new InvalidOperationException("spawned item has non-positive TotalWorth");
            _requestedColor = requestedColor;

            var startBet = casinoType.GetMethod("ServerStartBet", BindingFlags.Instance | BindingFlags.Public)
                           ?? throw new MissingMethodException("CasinoManager.ServerStartBet");
            startBet.Invoke(casino, new[] { Enum.ToObject(betColorType, requestedColor) });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            _item = null;
            return false;
        }
    }

    internal bool TryComplete(out int afterWorth, out int minimumWorth, out int maximumWorth,
        out float afterMultiplier, out float expectedMultiplier, out string error)
    {
        afterWorth = 0;
        minimumWorth = 0;
        maximumWorth = 0;
        afterMultiplier = 0f;
        expectedMultiplier = 0f;
        error = null;
        try
        {
            if (_item == null || _totalWorth == null || _bettingMultiplier == null)
                throw new InvalidOperationException("payout item missing");
            afterWorth = Convert.ToInt32(_totalWorth.GetValue(_item));
            var multiplier = _requestedColor == 2 ? 35 : 2;
            minimumWorth = checked(_beforeWorth * multiplier);
            maximumWorth = checked((_beforeWorth + 1) * multiplier - 1);
            afterMultiplier = Convert.ToSingle(_bettingMultiplier.GetValue(_item));
            expectedMultiplier = _beforeMultiplier * multiplier;
            return afterWorth >= minimumWorth && afterWorth <= maximumWorth &&
                   Mathf.Abs(afterMultiplier - expectedMultiplier) <= 0.0001f;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static object StaticMember(Type type, string name)
    {
        return type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
               ?? type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }
}
