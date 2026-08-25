using System;
using System.Reflection;
using UnityEngine;

namespace HowToFish.RouletteTrainer.Bridge;

internal sealed class RouletteAccess
{
    private readonly object _casino;
    private readonly FieldInfo _ball;
    private readonly FieldInfo _spawn;
    private readonly FieldInfo _wheel;
    private readonly FieldInfo _angle;
    private readonly FieldInfo _ballForce;
    private readonly FieldInfo _slotSize;
    private readonly FieldInfo _currentWheelSpeed;
    private readonly FieldInfo _stableTime;
    private readonly FieldInfo _isPlaying;
    private readonly MethodInfo _startRoulette;

    internal RouletteAccess(object casino)
    {
        _casino = casino ?? throw new ArgumentNullException(nameof(casino));
        var type = casino.GetType();
        _ball = Field(type, "_ball");
        _spawn = Field(type, "_ballSpawnPoint");
        _wheel = Field(type, "_wheel");
        _angle = Field(type, "_ballAngleObject");
        _ballForce = Field(type, "_ballForce");
        _slotSize = Field(type, "_slotSize");
        _currentWheelSpeed = Field(type, "_curWheelSpeed");
        _stableTime = Field(type, "_timeInSameSlot");
        _isPlaying = Field(type, "_isPlaying");
        _startRoulette = type.GetMethod("ServerStartRoulette", BindingFlags.Instance | BindingFlags.Public)
                         ?? throw new MissingMethodException(type.FullName, "ServerStartRoulette");
    }

    internal object Raw => _casino;
    internal Rigidbody Ball => (Rigidbody)_ball.GetValue(_casino);
    internal Transform Spawn => (Transform)_spawn.GetValue(_casino);
    internal Transform Wheel => (Transform)_wheel.GetValue(_casino);
    internal Transform Angle => (Transform)_angle.GetValue(_casino);
    internal float BallForce => (float)_ballForce.GetValue(_casino);
    internal float SlotSize => (float)_slotSize.GetValue(_casino);
    internal float CurrentWheelSpeed => (float)_currentWheelSpeed.GetValue(_casino);
    internal float StableTime => (float)_stableTime.GetValue(_casino);
    internal bool IsPlaying => (bool)_isPlaying.GetValue(_casino);
    internal bool IsAlive => _casino is UnityEngine.Object obj && obj != null && Ball != null && Wheel != null;

    internal void StartRoulette() => _startRoulette.Invoke(_casino, null);

    internal int CurrentSlot
    {
        get
        {
            var direction = Wheel.position - Angle.position;
            if (direction.sqrMagnitude < 0.0000001f) return 0;
            var ballY = Quaternion.LookRotation(direction).eulerAngles.y;
            var relative = (ballY - Wheel.eulerAngles.y + 360f) % 360f;
            return Mathf.FloorToInt(relative / SlotSize);
        }
    }

    internal static int SlotColor(int slot)
    {
        if (slot == 0) return 2;
        return slot % 2 > 0 ? 0 : 1;
    }

    private static FieldInfo Field(Type type, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new MissingFieldException(type.FullName, name);
    }
}
