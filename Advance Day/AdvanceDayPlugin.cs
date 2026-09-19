using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin(PluginId, "Advance Day", "2.0.0")]
public sealed class AdvanceDayPlugin : BaseUnityPlugin
{
    public const string PluginId = "com.thalethegreat.advanceday";
    private static AdvanceDayPlugin instance;
    private ConfigEntry<KeyboardShortcut> keyboardHotkey;
    private ConfigEntry<string> controllerBinding;
    private ConfigEntry<KeyboardShortcut> keyboardStopHotkey;
    private ConfigEntry<KeyCode> controllerStopButton;
    private ConfigEntry<bool> autoSave;
    private ConfigEntry<float> speed;
    private ConfigEntry<bool> showProgress;
    private readonly List<KeyCode[]> controllerCombos = new List<KeyCode[]>();
    private string lastBinding;
    private bool controllerHeld;
    private Harmony harmony;
    private AdvanceSession session;
    private MainGame runningGame;
    private GameSave runningSave;
    private BaseCharacterComponent runningPlayer;
    private float previousScale, previousFixedStep, previousMaximumStep;
    private float appliedScale, appliedFixedStep, appliedMaximumStep;
    private bool ownsTiming;
    private string notice;
    private float noticeUntil;
    private bool savePending;
    private float saveRequestedAt;

    private void Awake()
    {
        instance = this;
        keyboardHotkey = Config.Bind("Controls", "Keyboard Hotkey",
            new KeyboardShortcut(KeyCode.Q, KeyCode.LeftShift), "Advance to the following day's morning; press again to cancel.");
        controllerBinding = Config.Bind("Controls", "Controller Binding", "JoystickButton8+JoystickButton9",
            "Controller combo; + joins required buttons, ; separates alternatives. Press again to cancel.");
        keyboardStopHotkey = Config.Bind("Controls", "Keyboard Stop Hotkey",
            new KeyboardShortcut(KeyCode.Escape), "Stop an active advance. Set to None to disable this shortcut.");
        controllerStopButton = Config.Bind("Controls", "Controller Stop Button", KeyCode.JoystickButton1,
            "Stop an active advance using a controller button. Set to None to disable. Button numbering depends on the controller.");
        autoSave = Config.Bind("Options", "Auto Save After Day Advance", false,
            "Use the game's save routine after a completed advance. Interrupted advances do not autosave.");
        speed = Config.Bind("Options", "Fast Forward Speed", 10f,
            new ConfigDescription("Requested simulation speed. Frame and physics limits may reduce actual speed.", new AcceptableValueRange<float>(1f, 50f)));
        showProgress = Config.Bind("Options", "Show Progress", true, "Show progress and a cancel button while advancing.");
        try
        {
            // Only suppress human control. Setting control_enabled=false would stop
            // EnvironmentEngine AND GameLogics, so it must remain under game control.
            harmony = new Harmony(PluginId);
            harmony.Patch(AccessTools.Method(typeof(BaseCharacterComponent), "PlayerControlIsDisabled"),
                postfix: new HarmonyMethod(typeof(AdvanceDayPlugin), nameof(PlayerControlPostfix)));
            RefreshBindings();
            Logger.LogInfo("Advance Day 2.0.0 loaded: full-world fast-forward.");
        }
        catch (Exception ex)
        {
            Logger.LogError("Advance Day could not initialize: " + ex);
            enabled = false;
        }
    }

    private static void PlayerControlPostfix(BaseCharacterComponent __instance, ref bool __result)
    {
        if (instance != null && instance.session != null &&
            ReferenceEquals(__instance, instance.runningPlayer))
            __result = true;
    }

    private void Update()
    {
        try
        {
            RefreshBindings();
            bool held = ControllerPressed();
            bool pressed = keyboardHotkey.Value.IsDown() || (held && !controllerHeld);
            controllerHeld = held;
            if (session != null)
            {
                if (pressed || keyboardStopHotkey.Value.IsDown() ||
                    (controllerStopButton.Value != KeyCode.None && UnityInput.Current.GetKeyDown(controllerStopButton.Value)))
                    Finish(false, "Advance cancelled. Time already elapsed is retained.");
                else
                    CheckSession();
                return;
            }
            if (savePending && Time.realtimeSinceStartup - saveRequestedAt > 30f)
            {
                savePending = false;
                Tell("Save callback did not arrive; check the game log before saving again.");
            }
            if (pressed && !savePending)
                Begin();
        }
        catch (Exception ex)
        {
            Finish(false, "Advance stopped due to an error. See BepInEx/LogOutput.log.");
            Logger.LogError(ex);
        }
    }

    private void LateUpdate()
    {
        // Runs after the normal Update callbacks, so schedules and world updates
        // get the frame in which the clock crosses the destination.
        if (session == null) return;
        try { CheckSession(); }
        catch (Exception ex)
        {
            Finish(false, "Advance stopped due to an error. See BepInEx/LogOutput.log.");
            Logger.LogError(ex);
        }
    }

    private static string BlockReason()
    {
        if (!MainGame.game_started || MainGame.game_starting || MainGame.me == null ||
            MainGame.me.save == null || MainGame.me.player == null || MainGame.me.player_char == null)
            return "Load a game before advancing.";
        if (LoadingGUI.is_shown || MainGame.paused || !BaseGUI.all_guis_closed)
            return "A menu, dialogue, or loading screen is open.";
        if (MainGame.me.player.is_dead)
            return "The player is dead.";
        if (EnvironmentEngine.me == null || TimeOfDay.me == null || EnvironmentEngine.me.IsTimeStopped())
            return "The game has stopped its world clock.";
        var player = MainGame.me.player_char;
        if (!player.can_be_locally_controlled || player.player_controlled_by_script || player.playing_animation)
            return "A player animation or scripted sequence is running.";
        if (MainGame.me.build_mode_logics != null && MainGame.me.build_mode_logics.IsBuilding())
            return "Build mode is active.";
        return null;
    }

    private void Begin()
    {
        string reason = BlockReason();
        if (reason != null) { Tell(reason); return; }
        if (Time.timeScale <= 0f) { Tell("The game is paused."); return; }
        runningGame = MainGame.me;
        runningSave = runningGame.save;
        runningPlayer = runningGame.player_char;
        session = new AdvanceSession(runningSave.day, TimeOfDay.me.GetTimeK(), Time.realtimeSinceStartup);
        previousScale = Time.timeScale;
        previousFixedStep = Time.fixedDeltaTime;
        previousMaximumStep = Time.maximumDeltaTime;
        ownsTiming = true;
        appliedScale = Mathf.Clamp(speed.Value, 1f, 50f);
        if (float.IsNaN(appliedScale)) appliedScale = 10f;
        // Preserve normal physics granularity; never multiply the physics step
        // by the acceleration factor. Limit Update jumps to 0.1 simulated second.
        appliedFixedStep = Mathf.Min(previousFixedStep, 1f / 60f);
        appliedMaximumStep = Mathf.Min(previousMaximumStep, 0.1f);
        Time.fixedDeltaTime = appliedFixedStep;
        Time.maximumDeltaTime = appliedMaximumStep;
        Time.timeScale = appliedScale;
        runningPlayer.StopMovement();
        runningPlayer.movement_dir = Vector2.zero;
        Logger.LogInfo("Advancing naturally from day " + runningSave.day + " at " + TimeOfDay.me.GetTimeK() +
            " to day " + session.TargetDay + " at " + AdvanceSession.Morning + ".");
    }

    private void CheckSession()
    {
        if (!ReferenceEquals(MainGame.me, runningGame) || MainGame.me == null ||
            !ReferenceEquals(MainGame.me.save, runningSave) ||
            !ReferenceEquals(MainGame.me.player_char, runningPlayer))
        { Finish(false, "Advance stopped: game session changed."); return; }
        string reason = BlockReason();
        if (reason != null) { Finish(false, "Advance stopped: " + reason); return; }
        if (!Mathf.Approximately(Time.timeScale, appliedScale) ||
            !Mathf.Approximately(Time.fixedDeltaTime, appliedFixedStep) ||
            !Mathf.Approximately(Time.maximumDeltaTime, appliedMaximumStep))
        { Finish(false, "Advance stopped: another system changed the time settings."); return; }
        switch (session.Observe(runningSave.day, TimeOfDay.me.GetTimeK(), Time.realtimeSinceStartup))
        {
            case AdvanceResult.Complete:
                Finish(true, "Next morning reached."); break;
            case AdvanceResult.Stalled:
                Finish(false, "Advance stopped: the world clock is not progressing."); break;
            case AdvanceResult.ClockChanged:
                Finish(false, "Advance stopped: the world clock changed unexpectedly."); break;
        }
    }

    private void Finish(bool completed, string message)
    {
        bool wasRunning = session != null;
        var game = runningGame;
        var save = runningSave;
        session = null; // Release human control even if restoration or saving fails.
        runningGame = null;
        runningSave = null;
        runningPlayer = null;
        RestoreTiming();
        if (!wasRunning) return;
        Tell(message);
        if (!completed || !autoSave.Value || game == null || MainGame.me != game ||
            !ReferenceEquals(game.save, save)) return;
        try
        {
            savePending = true;
            saveRequestedAt = Time.realtimeSinceStartup;
            // The game dereferences this callback: passing null (as in v1.2) fails.
            PlatformSpecific.SaveGame(game.save_slot, save, slot =>
            {
                if (MainGame.me == game && ReferenceEquals(game.save, save) && slot != null)
                    game.save_slot = slot;
                savePending = false;
                Logger.LogInfo("Game save routine completed; check game log for any disk errors.");
            });
        }
        catch (Exception ex)
        {
            savePending = false;
            Logger.LogError("Autosave failed: " + ex);
            Tell("Morning reached, but autosave failed. Save normally in game.");
        }
    }

    private void RestoreTiming()
    {
        if (!ownsTiming) return;
        ownsTiming = false;
        // Do not overwrite a pause/cutscene/other mod's newly assigned values.
        if (Mathf.Approximately(Time.timeScale, appliedScale)) Time.timeScale = previousScale;
        if (Mathf.Approximately(Time.fixedDeltaTime, appliedFixedStep)) Time.fixedDeltaTime = previousFixedStep;
        if (Mathf.Approximately(Time.maximumDeltaTime, appliedMaximumStep)) Time.maximumDeltaTime = previousMaximumStep;
    }

    private void OnDisable() { Finish(false, "Advance stopped: plugin disabled."); }
    private void OnDestroy()
    {
        Finish(false, "Advance stopped: plugin unloaded.");
        if (harmony != null) harmony.UnpatchSelf();
        if (instance == this) instance = null;
    }
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) Finish(false, "Advance stopped: game lost focus.");
    }

    private void Tell(string message)
    {
        notice = message;
        noticeUntil = Time.realtimeSinceStartup + 6f;
        Logger.LogInfo(message);
    }

    private void OnGUI()
    {
        if (showProgress == null || !showProgress.Value) return;
        if (session != null)
        {
            GUI.Box(new Rect(16, 100, 340, 82), "Advancing to next morning — " + session.ProgressPercent + "%");
            GUI.Label(new Rect(28, 125, 316, 22), "Stop binding or start shortcut cancels.");
            if (GUI.Button(new Rect(28, 150, 316, 24), "Cancel advance"))
                Finish(false, "Advance cancelled. Time already elapsed is retained.");
        }
        else if (Time.realtimeSinceStartup < noticeUntil)
            GUI.Box(new Rect(16, 100, 550, 52), notice);
    }

    private void RefreshBindings()
    {
        if (lastBinding == controllerBinding.Value) return;
        lastBinding = controllerBinding.Value;
        controllerCombos.Clear();
        controllerHeld = false;
        foreach (string combo in (lastBinding ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(combo)) continue;
            var keys = new List<KeyCode>();
            bool valid = true;
            foreach (string part in combo.Split('+'))
            {
                KeyCode key;
                if (!Enum.TryParse(part.Trim(), true, out key) || !Enum.IsDefined(typeof(KeyCode), key) || key == KeyCode.None)
                { valid = false; break; }
                keys.Add(key);
            }
            // Never turn a malformed two-button combo into a single-button trigger.
            if (valid && keys.Count > 0) controllerCombos.Add(keys.ToArray());
            else Logger.LogWarning("Ignored invalid controller combo: " + combo);
        }
    }

    private bool ControllerPressed()
    {
        foreach (KeyCode[] combo in controllerCombos)
        {
            bool held = true;
            foreach (KeyCode key in combo) held &= UnityInput.Current.GetKey(key);
            if (held) return true;
        }
        return false;
    }
}
