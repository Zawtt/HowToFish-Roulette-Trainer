using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HowToFish.RouletteTrainer.Bridge;

public sealed class TrainerBehaviour : MonoBehaviour
{
    private RouletteAccess _access;
    private readonly TargetSelector _targets = new();
    private ForceColor _mode;
    private ForceColor _spinRequest;
    private bool _initialized;
    private bool _modeLockedByEnvironment;
    private float _nextConfigPoll;
    private DateTime _modeFileWriteUtc;
    private long _spinStartTicks;
    private int _spinId;
    private int _completedSpins;
    private float _appliedMultiplier;
    private float _spinStartGameTime;
    private bool _correctionEnabled;
    private bool _correctionUsed;
    private int _correctionTicks;
    private int _rescueTicks;
    private float _peakCorrectionAcceleration;
    private int _targetSlot = -1;
    private Camera _labCamera;
    private Camera _originalCamera;
    private LabPayoutProbe _payoutProbe;
    private int _payoutTests;
    private int _payoutSuccesses;

    internal ForceColor Mode => _mode;
    internal int CompletedSpins => _completedSpins;
    internal int PayoutTests => _payoutTests;
    internal int PayoutSuccesses => _payoutSuccesses;
    internal bool CasinoReady => Ready();
    internal bool CasinoPlaying => Ready() && _access.IsPlaying;
    internal int RequestedBetColor => ToBetColor(_mode);

    internal void SetLabMode(string mode) => ApplyMode(mode, "lab-sequence");

    internal void SetCasino(object casino)
    {
        EnsureInitialized();
        if (casino == null) return;
        if (_access != null && ReferenceEquals(_access.Raw, casino)) return;
        try
        {
            _access = new RouletteAccess(casino);
            if (_modeLockedByEnvironment)
                ApplyMode(Environment.GetEnvironmentVariable("HTF_TRAINER_MODE"), "reconnect");
            else
                PollModeFile(force: true);
            TrainerLog.Emit("roulette_found",
                "\"unity\":" + TrainerLog.Quote(Application.unityVersion) +
                ",\"fixedDeltaTime\":" + TrainerLog.Float(Time.fixedDeltaTime));
        }
        catch (Exception ex)
        {
            _access = null;
            ResetToNone("roulette resolution failed: " + ex.GetType().Name);
        }
    }

    internal void CasinoDestroyed(object casino)
    {
        if (_access == null || !ReferenceEquals(_access.Raw, casino)) return;
        _access = null;
        ResetToNone("roulette reference lost");
    }

    internal void SpinStarted()
    {
        EnsureInitialized();
        PollModeFile(force: false);
        if (!Ready())
        {
            ResetToNone("roulette state invalid at spin start");
            return;
        }
        if (!ServerReady())
        {
            ResetToNone("authoritative server unavailable");
            return;
        }

        _spinId++;
        _spinStartTicks = Stopwatch.GetTimestamp();
        _spinStartGameTime = Time.time;
        _spinRequest = _mode;
        _appliedMultiplier = 0f;
        _correctionEnabled = false;
        _correctionUsed = false;
        _correctionTicks = 0;
        _rescueTicks = 0;
        _peakCorrectionAcceleration = 0f;
        _targetSlot = _targets.Select(_spinRequest);
        var originalMultiplier = _access.BallForce == 0f
            ? 0f
            : _access.Ball.linearVelocity.magnitude / _access.BallForce;

        if (_spinRequest != ForceColor.None)
        {
            if (!TryReadInitialMultiplier(out var multiplier))
            {
                ResetToNone("invalid initial velocity multiplier");
                _spinRequest = ForceColor.None;
                _targetSlot = -1;
            }
            else
            {
                _access.Ball.linearVelocity = _access.Spawn.forward * (_access.BallForce * multiplier);
                _appliedMultiplier = multiplier;
                _correctionEnabled = !string.Equals(
                    Environment.GetEnvironmentVariable("HTF_TRAINER_PHYSICAL_CORRECTION"),
                    "0", StringComparison.OrdinalIgnoreCase);
            }
        }

        TrainerLog.Emit("spin_started",
            "\"spin_id\":" + _spinId +
            ",\"requested_color\":" + TrainerLog.Quote(_spinRequest.ToString().ToUpperInvariant()) +
            ",\"target_slot\":" + (_targetSlot >= 0 ? _targetSlot.ToString(TrainerLog.Invariant) : "null") +
            ",\"method_used\":" + TrainerLog.Quote(MethodUsed()) +
            ",\"correction_enabled\":" + (_correctionEnabled ? "true" : "false") +
            ",\"original_multiplier\":" + TrainerLog.Float(originalMultiplier) +
            ",\"applied_multiplier\":" + (_appliedMultiplier > 0f ? TrainerLog.Float(_appliedMultiplier) : "null") +
            ",\"wheel_y\":" + TrainerLog.Float(_access.Wheel.eulerAngles.y));
    }

    internal void FixedUpdateObserved()
    {
        EnsureInitialized();
        if (_access == null) return;
        try
        {
            if (!_access.IsAlive)
            {
                ResetToNone("roulette Unity object destroyed");
                return;
            }
            if (_spinRequest != ForceColor.None && _correctionEnabled && _access.IsPlaying && !_access.Ball.isKinematic)
            {
                ApplyTangentialCorrection();
            }
        }
        catch
        {
            _access = null;
            ResetToNone("roulette state became inconsistent");
        }
    }

    internal void SpinFinal(byte logicalColor)
    {
        EnsureInitialized();
        if (!Ready())
        {
            ResetToNone("roulette unavailable at result");
            return;
        }

        var actualSlot = _access.CurrentSlot;
        var geometricColor = RouletteAccess.SlotColor(actualSlot);
        var requestedColor = ToBetColor(_spinRequest);
        var success = _spinRequest == ForceColor.None
            ? geometricColor == logicalColor && !_correctionUsed
            : actualSlot == _targetSlot && geometricColor == requestedColor && logicalColor == requestedColor;
        var duration = _spinStartTicks == 0 ? 0d
            : (Stopwatch.GetTimestamp() - _spinStartTicks) * 1000d / Stopwatch.Frequency;

        TrainerLog.Emit("spin_final",
            "\"spin_id\":" + _spinId +
            ",\"requested_color\":" + TrainerLog.Quote(_spinRequest.ToString().ToUpperInvariant()) +
            ",\"target_slot\":" + (_targetSlot >= 0 ? _targetSlot.ToString(TrainerLog.Invariant) : "null") +
            ",\"actual_slot\":" + actualSlot +
            ",\"actual_color\":" + logicalColor +
            ",\"geometric_color\":" + geometricColor +
            ",\"method_used\":" + TrainerLog.Quote(MethodUsed()) +
            ",\"correction_used\":" + (_correctionUsed ? "true" : "false") +
            ",\"correction_ticks\":" + _correctionTicks +
            ",\"rescue_ticks\":" + _rescueTicks +
            ",\"peak_correction_acceleration\":" + TrainerLog.Float(_peakCorrectionAcceleration) +
            ",\"spin_duration_ms\":" + duration.ToString("F3", TrainerLog.Invariant) +
            ",\"stable_seconds\":" + TrainerLog.Float(_access.StableTime) +
            ",\"final_relative_angle\":" + TrainerLog.Float(CurrentRelativeAngle()) +
            ",\"final_wheel_speed\":" + TrainerLog.Float(_access.CurrentWheelSpeed) +
            ",\"final_ball_position\":" + VectorJson(_access.Ball.position) +
            ",\"final_ball_velocity\":" + VectorJson(_access.Ball.linearVelocity) +
            ",\"success\":" + (success ? "true" : "false") +
            ",\"error\":" + (success ? "null" : TrainerLog.Quote("requested/physical/logical mismatch")));
        _completedSpins++;
        _spinRequest = ForceColor.None;
        _spinStartTicks = 0;
    }

    private void ApplyTangentialCorrection()
    {
        var elapsed = Time.time - _spinStartGameTime;
        var startSeconds = ReadFloat("HTF_TRAINER_CORRECTION_START_SECONDS", 0.25f, 0f, 5f);
        if (elapsed < startSeconds) return;

        var ball = _access.Ball;
        var radial = ball.position - _access.Wheel.position;
        radial.y = 0f;
        var radius = radial.magnitude;
        if (radius < 0.05f) return;

        // Positive relative yaw corresponds to this tangent for the ball's
        // center-to-rim radial vector in Unity's Y-up coordinate system.
        var increasingYawTangent = new Vector3(radial.z, 0f, -radial.x) / radius;
        var relative = CurrentRelativeAngle();
        if (_targetSlot < 0) return;
        var target = (_targetSlot + 0.5f) * _access.SlotSize;
        var angularError = Mathf.DeltaAngle(relative, target);
        var gain = ReadFloat("HTF_TRAINER_CORRECTION_GAIN", 2.5f, 0.1f, 12f);
        var maxAcceleration = ReadFloat("HTF_TRAINER_CORRECTION_MAX_ACCEL", 20f, 0.5f, 100f);
        var slot = _access.CurrentSlot;
        var rescueDelay = ReadFloat("HTF_TRAINER_RESCUE_DELAY", 0.35f, 0.1f, 1.5f);
        var rescue = slot != _targetSlot && _access.StableTime >= rescueDelay;
        if (rescue)
        {
            maxAcceleration = ReadFloat("HTF_TRAINER_RESCUE_MAX_ACCEL", 45f, maxAcceleration, 100f);
            _rescueTicks++;
        }
        var wheelDegreesPerSecond = _access.CurrentWheelSpeed / Mathf.Max(Time.fixedDeltaTime, 0.00001f);
        var desiredDegreesPerSecond = wheelDegreesPerSecond + angularError * gain;
        var desiredTangentialSpeed = desiredDegreesPerSecond * Mathf.Deg2Rad * radius;
        var currentTangentialSpeed = Vector3.Dot(ball.linearVelocity, increasingYawTangent);
        var acceleration = Mathf.Clamp(
            (desiredTangentialSpeed - currentTangentialSpeed) / Mathf.Max(Time.fixedDeltaTime, 0.00001f),
            -maxAcceleration,
            maxAcceleration);
        if (rescue && Mathf.Abs(angularError) > 0.01f)
        {
            var rescueMinimum = ReadFloat("HTF_TRAINER_RESCUE_MIN_ACCEL", 30f, 1f, maxAcceleration);
            acceleration = Mathf.Sign(angularError) * Mathf.Max(Mathf.Abs(acceleration), rescueMinimum);
        }

        if (Mathf.Abs(acceleration) < 0.001f) return;
        ball.AddForce(increasingYawTangent * acceleration, ForceMode.Acceleration);
        _correctionUsed = true;
        _correctionTicks++;
        _peakCorrectionAcceleration = Mathf.Max(_peakCorrectionAcceleration, Mathf.Abs(acceleration));
    }

    private float CurrentRelativeAngle()
    {
        var direction = _access.Wheel.position - _access.Ball.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0000001f) return 0f;
        var ballY = Quaternion.LookRotation(direction).eulerAngles.y;
        return (ballY - _access.Wheel.eulerAngles.y + 360f) % 360f;
    }

    private string MethodUsed()
    {
        if (_spinRequest == ForceColor.None) return "Original";
        return _correctionEnabled ? "InitialVelocity+TangentialForce" : "InitialVelocity";
    }

    private static float ReadFloat(string name, float fallback, float minimum, float maximum)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Mathf.Clamp(value, minimum, maximum)
            : fallback;
    }

    private static string VectorJson(Vector3 value)
    {
        return "[" + TrainerLog.Float(value.x) + "," + TrainerLog.Float(value.y) + "," +
               TrainerLog.Float(value.z) + "]";
    }

    internal bool TryStartLabSpin()
    {
        if (!Ready() || _access.IsPlaying) return false;
        _access.StartRoulette();
        return true;
    }

    internal bool TryStartLabPayoutSpin(int requestedColor)
    {
        if (!Ready() || _access.IsPlaying) return false;
        _payoutProbe = new LabPayoutProbe();
        if (_payoutProbe.TryStart(_access, requestedColor, out var error)) return true;
        TrainerLog.Emit("payout_test_error", "\"message\":" + TrainerLog.Quote(error));
        _payoutProbe = null;
        return false;
    }

    internal void PayoutCompleted()
    {
        if (_payoutProbe == null) return;
        var success = _payoutProbe.TryComplete(out var after, out var minimum, out var maximum,
            out var afterMultiplier, out var expectedMultiplier, out var error);
        _payoutTests++;
        if (success) _payoutSuccesses++;
        TrainerLog.Emit("payout_test",
            "\"spin_id\":" + _spinId +
            ",\"requested_color\":" + _payoutProbe.RequestedColor +
            ",\"before_worth\":" + _payoutProbe.BeforeWorth +
            ",\"after_worth\":" + after +
            ",\"minimum_worth\":" + minimum +
            ",\"maximum_worth\":" + maximum +
            ",\"before_multiplier\":" + TrainerLog.Float(_payoutProbe.BeforeMultiplier) +
            ",\"after_multiplier\":" + TrainerLog.Float(afterMultiplier) +
            ",\"expected_multiplier\":" + TrainerLog.Float(expectedMultiplier) +
            ",\"success\":" + (success ? "true" : "false") +
            ",\"error\":" + (error == null ? "null" : TrainerLog.Quote(error)));
        _payoutProbe = null;
    }

    internal bool TrySetupLabCamera()
    {
        if (!Ready()) return false;
        if (_labCamera != null) return true;
        var cameras = Resources.FindObjectsOfTypeAll<Camera>();
        _originalCamera = Camera.main;
        if (_originalCamera == null || !_originalCamera.isActiveAndEnabled || _originalCamera.targetTexture != null)
        {
            _originalCamera = null;
            var bestDepth = float.NegativeInfinity;
            foreach (var candidate in cameras)
            {
                if (candidate != null && candidate.isActiveAndEnabled && candidate.targetTexture == null &&
                    candidate.depth >= bestDepth)
                {
                    _originalCamera = candidate;
                    bestDepth = candidate.depth;
                }
            }
        }
        if (_originalCamera == null) return false;

        var host = new GameObject("Roulette Trainer Lab Camera");
        _labCamera = host.AddComponent<Camera>();
        _labCamera.CopyFrom(_originalCamera);
        _labCamera.cullingMask = -1;
        _labCamera.useOcclusionCulling = false;
        _labCamera.depth = _originalCamera.depth + 1f;
        var wheel = _access.Wheel;
        var planarForward = wheel.forward;
        planarForward.y = 0f;
        if (planarForward.sqrMagnitude < 0.01f) planarForward = Vector3.forward;
        planarForward.Normalize();
        _labCamera.transform.position = wheel.position - planarForward * 2.4f + Vector3.up * 1.35f;
        _labCamera.transform.LookAt(wheel.position + Vector3.up * 0.1f);
        _labCamera.ResetWorldToCameraMatrix();
        _labCamera.ResetProjectionMatrix();
        foreach (var candidate in cameras)
        {
            if (candidate != null && candidate != _labCamera && candidate.targetTexture == null)
                candidate.enabled = false;
        }
        TrainerLog.Emit("lab_camera_ready",
            "\"camera_position\":" + VectorJson(_labCamera.transform.position) +
            ",\"wheel_position\":" + VectorJson(wheel.position));
        return true;
    }

    private void Update()
    {
        try
        {
            EnsureInitialized();
            if (!_modeLockedByEnvironment && Time.unscaledTime >= _nextConfigPoll)
            {
                _nextConfigPoll = Time.unscaledTime + 0.25f;
                PollModeFile(force: false);
            }
            if (_access != null && !_access.IsAlive)
            {
                _access = null;
                ResetToNone("roulette reference lost");
            }
        }
        catch (Exception ex)
        {
            ResetToNone("runtime error: " + ex.GetType().Name);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        var environmentMode = Environment.GetEnvironmentVariable("HTF_TRAINER_MODE");
        _modeLockedByEnvironment = !string.IsNullOrWhiteSpace(environmentMode);
        if (_modeLockedByEnvironment)
        {
            ApplyMode(environmentMode, "environment");
        }
        else
        {
            PollModeFile(force: true);
        }
        TrainerLog.Emit("trainer_started",
            "\"version\":\"1.0.0\",\"mode\":" + TrainerLog.Quote(_mode.ToString().ToUpperInvariant()) +
            ",\"log_path\":" + TrainerLog.Quote(TrainerLog.Path));
    }

    private void PollModeFile(bool force)
    {
        if (_modeLockedByEnvironment) return;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "trainer-mode.txt"));
        if (!File.Exists(path))
        {
            if (force) ApplyMode("NONE", "default");
            return;
        }
        var written = File.GetLastWriteTimeUtc(path);
        if (!force && written == _modeFileWriteUtc) return;
        _modeFileWriteUtc = written;
        ApplyMode(File.ReadAllText(path).Trim(), "config");
    }

    private void ApplyMode(string raw, string source)
    {
        ForceColor next;
        if (string.Equals(raw, "BLACK", StringComparison.OrdinalIgnoreCase)) next = ForceColor.Black;
        else if (string.Equals(raw, "RED", StringComparison.OrdinalIgnoreCase)) next = ForceColor.Red;
        else if (string.Equals(raw, "GREEN", StringComparison.OrdinalIgnoreCase)) next = ForceColor.Green;
        else if (string.Equals(raw, "NONE", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(raw)) next = ForceColor.None;
        else
        {
            ResetToNone("unsupported mode in " + source);
            return;
        }
        if (_mode == next) return;
        _mode = next;
        TrainerLog.Emit("mode_changed", "\"mode\":" + TrainerLog.Quote(_mode.ToString().ToUpperInvariant()) +
                                         ",\"source\":" + TrainerLog.Quote(source));
    }

    private static bool TryReadInitialMultiplier(out float multiplier)
    {
        var raw = Environment.GetEnvironmentVariable("HTF_TRAINER_INITIAL_MULTIPLIER");
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("HTF_TRAINER_GREEN_MULTIPLIER");
        if (string.IsNullOrWhiteSpace(raw)) raw = "1.0";
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier) &&
               multiplier >= 0.8f && multiplier <= 1.2f;
    }

    private static int ToBetColor(ForceColor color)
    {
        switch (color)
        {
            case ForceColor.Black: return 0;
            case ForceColor.Red: return 1;
            case ForceColor.Green: return 2;
            default: return -1;
        }
    }

    private bool Ready()
    {
        try { return _access != null && _access.IsAlive; }
        catch { return false; }
    }

    private static bool ServerReady()
    {
        try
        {
            var type = Type.GetType("Server, Assembly-CSharp");
            var instance = type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                           ?? type?.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            var property = instance?.GetType().GetProperty("IsServerInitialized", BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.GetValue(instance) is bool ready && ready;
        }
        catch { return false; }
    }

    private void ResetToNone(string reason)
    {
        var changed = _mode != ForceColor.None;
        _mode = ForceColor.None;
        if (changed || !string.IsNullOrEmpty(reason))
        {
            TrainerLog.Emit("failsafe", "\"mode\":\"NONE\",\"reason\":" + TrainerLog.Quote(reason));
        }
    }
}
