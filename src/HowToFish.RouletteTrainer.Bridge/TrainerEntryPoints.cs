using System;
using UnityEngine;

namespace HowToFish.RouletteTrainer.Bridge;

public static class TrainerEntryPoints
{
    private static TrainerBehaviour _runtime;

    public static void StartRuntime()
    {
        Safe(() => EnsureRuntime(null));
    }

    public static void OnCasinoAwake(object casino)
    {
        Safe(() =>
        {
            EnsureRuntime(casino);
            _runtime.SetCasino(casino);
        });
    }

    public static void OnCasinoDestroyed(object casino)
    {
        Safe(() => _runtime?.CasinoDestroyed(casino));
    }

    public static void OnSpinStarted(object casino)
    {
        Safe(() =>
        {
            EnsureRuntime(casino);
            _runtime.SetCasino(casino);
            _runtime.SpinStarted();
        });
    }

    public static void OnFixedUpdate(object casino)
    {
        Safe(() =>
        {
            EnsureRuntime(casino);
            _runtime.SetCasino(casino);
            _runtime.FixedUpdateObserved();
        });
    }

    public static void OnSpinFinal(byte color)
    {
        Safe(() => _runtime?.SpinFinal(color));
    }

    public static bool CasinoReady => _runtime != null && _runtime.CasinoReady;
    public static bool CasinoPlaying => _runtime != null && _runtime.CasinoPlaying;
    public static int CompletedSpins => _runtime?.CompletedSpins ?? 0;
    public static int PayoutTests => _runtime?.PayoutTests ?? 0;
    public static int PayoutSuccesses => _runtime?.PayoutSuccesses ?? 0;
    public static int RequestedBetColor => _runtime?.RequestedBetColor ?? -1;

    public static void SetLabMode(string mode)
    {
        Safe(() => _runtime?.SetLabMode(mode));
    }

    public static bool TryStartLabSpin()
    {
        try { return _runtime != null && _runtime.TryStartLabSpin(); }
        catch (Exception ex)
        {
            TrainerLog.Emit("lab_error", "\"message\":" + TrainerLog.Quote(ex.GetType().Name + ": " + ex.Message));
            return false;
        }
    }

    public static bool TryStartLabPayoutSpin(byte requestedColor)
    {
        try { return _runtime != null && _runtime.TryStartLabPayoutSpin(requestedColor); }
        catch (Exception ex)
        {
            TrainerLog.Emit("lab_error", "\"message\":" + TrainerLog.Quote("payout spin: " + ex.GetType().Name + ": " + ex.Message));
            return false;
        }
    }

    public static void OnPayoutCompleted()
    {
        Safe(() => _runtime?.PayoutCompleted());
    }

    public static bool TrySetupLabCamera()
    {
        try { return _runtime != null && _runtime.TrySetupLabCamera(); }
        catch (Exception ex)
        {
            TrainerLog.Emit("lab_error", "\"message\":" + TrainerLog.Quote("camera: " + ex.GetType().Name + ": " + ex.Message));
            return false;
        }
    }

    private static void EnsureRuntime(object casino)
    {
        if (_runtime == null)
        {
            var host = new GameObject("How To Fish Roulette Trainer");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _runtime = host.AddComponent<TrainerBehaviour>();
            host.AddComponent<TrainerLabBehaviour>();
        }
        if (casino != null) _runtime.SetCasino(casino);
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            TrainerLog.Emit("bridge_error", "\"message\":" + TrainerLog.Quote(ex.GetType().Name + ": " + ex.Message));
        }
    }
}
