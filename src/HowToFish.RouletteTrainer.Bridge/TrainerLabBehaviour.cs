using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HowToFish.RouletteTrainer.Bridge;

public sealed class TrainerLabBehaviour : MonoBehaviour
{
    private IEnumerator Start()
    {
        var rawTarget = Environment.GetEnvironmentVariable("HTF_TRAINER_AUTOTEST_SPINS");
        if (!int.TryParse(rawTarget, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target) || target <= 0)
            yield break;

        Application.runInBackground = true;
        TrainerLog.Emit("lab_start", "\"target_spins\":" + target);
        yield return new WaitForSecondsRealtime(2f);

        var connectionType = Type.GetType("ConnectionManager, Assembly-CSharp");
        var connection = connectionType == null ? null : StaticMember(connectionType, "Instance");
        var createOffline = connectionType?.GetMethod("CreateOfflineLobby", BindingFlags.Instance | BindingFlags.Public);
        if (connection == null || createOffline == null)
        {
            FailAndQuit("ConnectionManager/CreateOfflineLobby not found", 11);
            yield break;
        }
        createOffline.Invoke(connection, null);

        var deadline = Time.realtimeSinceStartup + 20f;
        while (!ServerReady() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!ServerReady())
        {
            FailAndQuit("offline server did not initialize", 12);
            yield break;
        }

        var onlineIsland = Type.GetType("OnlineIslandManager, Assembly-CSharp");
        var teleport = onlineIsland?.GetMethod("TpToSpecificIsland", BindingFlags.Static | BindingFlags.Public);
        if (teleport == null)
        {
            FailAndQuit("roulette island loader not found", 13);
            yield break;
        }
        teleport.Invoke(null, new object[] { (byte)3 });

        deadline = Time.realtimeSinceStartup + 30f;
        while ((!SceneManager.GetSceneByBuildIndex(4).isLoaded || !TrainerEntryPoints.CasinoReady) &&
               Time.realtimeSinceStartup < deadline) yield return null;
        if (!SceneManager.GetSceneByBuildIndex(4).isLoaded || !TrainerEntryPoints.CasinoReady)
        {
            FailAndQuit("roulette scene did not initialize", 14);
            yield break;
        }

        var originalScale = Time.timeScale;
        var captureDirectory = Environment.GetEnvironmentVariable("HTF_TRAINER_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            deadline = Time.realtimeSinceStartup + 5f;
            while (!TrainerEntryPoints.TrySetupLabCamera() && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!TrainerEntryPoints.TrySetupLabCamera())
            {
                FailAndQuit("could not configure lab camera", 16);
                yield break;
            }
        }
        var configuredScale = Environment.GetEnvironmentVariable("HTF_TRAINER_TEST_TIME_SCALE");
        var scale = float.TryParse(configuredScale, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale)
            ? Mathf.Clamp(parsedScale, 1f, 20f)
            : 10f;
        Time.timeScale = scale;
        yield return new WaitForSecondsRealtime(0.5f);

        var completed = 0;
        var validatePayout = string.Equals(Environment.GetEnvironmentVariable("HTF_TRAINER_VALIDATE_PAYOUT"),
            "1", StringComparison.OrdinalIgnoreCase);
        var sequenceRaw = Environment.GetEnvironmentVariable("HTF_TRAINER_MODE_SEQUENCE");
        var modeSequence = string.IsNullOrWhiteSpace(sequenceRaw)
            ? Array.Empty<string>()
            : sequenceRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < target; i++)
        {
            while (TrainerEntryPoints.CasinoPlaying) yield return null;
            if (modeSequence.Length > 0)
                TrainerEntryPoints.SetLabMode(modeSequence[i % modeSequence.Length].Trim());
            yield return new WaitForSecondsRealtime(0.1f);
            var requestedBetColor = TrainerEntryPoints.RequestedBetColor;
            var started = validatePayout
                ? requestedBetColor >= 0 && TrainerEntryPoints.TryStartLabPayoutSpin((byte)requestedBetColor)
                : TrainerEntryPoints.TryStartLabSpin();
            if (!started)
            {
                TrainerLog.Emit("lab_error", "\"message\":\"spin start failed\",\"index\":" + i);
                break;
            }
            deadline = Time.realtimeSinceStartup + 20f;
            var captureIndex = 0;
            var nextCapture = Time.realtimeSinceStartup;
            while (TrainerEntryPoints.CasinoPlaying && Time.realtimeSinceStartup < deadline)
            {
                if (!string.IsNullOrWhiteSpace(captureDirectory) && Time.realtimeSinceStartup >= nextCapture)
                {
                    var capturePath = Path.Combine(captureDirectory,
                        "spin-" + (i + 1).ToString("D2", CultureInfo.InvariantCulture) + "-" +
                        captureIndex.ToString("D3", CultureInfo.InvariantCulture) + ".png");
                    ScreenCapture.CaptureScreenshot(capturePath, 1);
                    captureIndex++;
                    nextCapture = Time.realtimeSinceStartup + 0.25f;
                }
                yield return null;
            }
            if (TrainerEntryPoints.CasinoPlaying)
            {
                TrainerLog.Emit("lab_error", "\"message\":\"spin timeout\",\"index\":" + i);
                break;
            }
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory,
                    "spin-" + (i + 1).ToString("D2", CultureInfo.InvariantCulture) + "-final.png"), 1);
                yield return new WaitForSecondsRealtime(0.25f);
            }
            completed++;
            if (i == 0 && string.Equals(Environment.GetEnvironmentVariable("HTF_TRAINER_TEST_SCENE_RELOAD"),
                    "1", StringComparison.OrdinalIgnoreCase))
            {
                teleport.Invoke(null, new object[] { (byte)0 });
                deadline = Time.realtimeSinceStartup + 30f;
                while (SceneManager.GetSceneByBuildIndex(4).isLoaded && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (SceneManager.GetSceneByBuildIndex(4).isLoaded)
                {
                    FailAndQuit("roulette scene did not unload", 17);
                    yield break;
                }
                teleport.Invoke(null, new object[] { (byte)3 });
                deadline = Time.realtimeSinceStartup + 30f;
                while ((!SceneManager.GetSceneByBuildIndex(4).isLoaded || !TrainerEntryPoints.CasinoReady) &&
                       Time.realtimeSinceStartup < deadline) yield return null;
                if (!SceneManager.GetSceneByBuildIndex(4).isLoaded || !TrainerEntryPoints.CasinoReady)
                {
                    FailAndQuit("roulette scene did not reconnect", 18);
                    yield break;
                }
                TrainerLog.Emit("scene_reload_test", "\"success\":true");
            }
        }

        Time.timeScale = originalScale;
        TrainerLog.Emit("lab_complete",
            "\"completed\":" + completed + ",\"target\":" + target +
            ",\"logged_results\":" + TrainerEntryPoints.CompletedSpins +
            ",\"payout_tests\":" + TrainerEntryPoints.PayoutTests +
            ",\"payout_successes\":" + TrainerEntryPoints.PayoutSuccesses);
        var holdRaw = Environment.GetEnvironmentVariable("HTF_TRAINER_VISUAL_HOLD_SECONDS");
        var holdSeconds = float.TryParse(holdRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHold)
            ? Mathf.Clamp(parsedHold, 0.5f, 60f)
            : 0.5f;
        yield return new WaitForSecondsRealtime(holdSeconds);
        var payoutPassed = !validatePayout ||
                           (TrainerEntryPoints.PayoutTests == target && TrainerEntryPoints.PayoutSuccesses == target);
        Application.Quit(completed == target && TrainerEntryPoints.CompletedSpins == target && payoutPassed ? 0 : 15);
    }

    private static bool ServerReady()
    {
        try
        {
            var type = Type.GetType("Server, Assembly-CSharp");
            var instance = type == null ? null : StaticMember(type, "Instance");
            var property = instance?.GetType().GetProperty("IsServerInitialized", BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.GetValue(instance) is bool ready && ready;
        }
        catch { return false; }
    }

    private static object StaticMember(Type type, string name)
    {
        return type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
               ?? type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static void FailAndQuit(string message, int code)
    {
        TrainerLog.Emit("lab_error", "\"message\":" + TrainerLog.Quote(message));
        Application.Quit(code);
    }
}
