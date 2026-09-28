using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VanillaQuestJournal;

[BepInPlugin("ggroy.gyk.vanillaquestjournal", "Vanilla Quest Journal", "1.0.1")]
public sealed class QuestJournalPlugin : BaseUnityPlugin
{
	public const string Guid = "ggroy.gyk.vanillaquestjournal";

	public const string Name = "Vanilla Quest Journal";

	public const string Version = "1.0.1";

	internal static QuestJournalPlugin Instance;

	internal static ConfigEntry<string> TrackedTask;

	internal static ConfigEntry<string> NavigationNpc;

	internal static ConfigEntry<string> FavoriteButton;

	internal static ConfigEntry<string> NavigationButton;

	private Harmony _harmony;

	private Texture2D _arrow;

	private string _cachedNpcId;

	private NavigationTarget _cachedNavigationTarget;

	private float _nextNpcLookup;

	private float _delayedNavigationRefreshAt = -1f;

	private int _navigationResolveFailures;

	private bool _hasLastPlayerPosition;

	private Vector3 _lastPlayerPosition;

	private bool _hasRouteEvaluationPosition;

	private Vector3 _routeEvaluationPosition;

	private bool _movementRouteReevaluation;

	private float _nextMovementRouteCheck;

	internal static GameKey FavoriteGameKey => ControllerButtonToGameKey(FavoriteButton?.Value, GameKey.Select);

	internal static GameKey NavigationGameKey => ControllerButtonToGameKey(NavigationButton?.Value, GameKey.Option1);

	internal void LogInfoMessage(string message)
	{
		base.Logger.LogInfo(message);
	}

	internal void LogErrorMessage(string message)
	{
		base.Logger.LogError(message);
	}

	internal static bool IsTaskTracked(string taskKey)
	{
		return GetTrackedTaskKeys().Contains(taskKey);
	}

	internal static List<string> GetTrackedTaskKeys()
	{
		if (TrackedTask == null || string.IsNullOrEmpty(TrackedTask.Value))
		{
			return new List<string>();
		}
		return TrackedTask.Value.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
	}

	internal static void ToggleTrackedTask(string taskKey)
	{
		List<string> trackedTaskKeys = GetTrackedTaskKeys();
		int num = trackedTaskKeys.IndexOf(taskKey);
		if (num >= 0)
		{
			trackedTaskKeys.RemoveAt(num);
		}
		else
		{
			trackedTaskKeys.Add(taskKey);
		}
		TrackedTask.Value = string.Join("|", trackedTaskKeys.ToArray());
	}

	private void Awake()
	{
		Instance = this;
		ModLocalization.Reload();
		MigrateAndBindConfig();
		_arrow = MakeArrow();
		_harmony = new Harmony("ggroy.gyk.vanillaquestjournal");
		_harmony.PatchAll(typeof(GameGuiPatch));
		_harmony.PatchAll(typeof(DialogClosedNavigationPatch));
		_harmony.PatchAll(typeof(SubsceneNavigationPatch));
		_harmony.PatchAll(typeof(SubsceneGraphNavigationPatch));
		base.Logger.LogInfo("Vanilla Quest Journal 1.0.1 loaded");
	}

	private void MigrateAndBindConfig()
	{
		bool saveOnConfigSet = base.Config.SaveOnConfigSet;
		base.Config.SaveOnConfigSet = false;
		ConfigEntry<string> configEntry = base.Config.Bind("Состояние", "Отслеживаемое задание", "");
		ConfigEntry<string> configEntry2 = base.Config.Bind("Состояние", "Навигатор NPC", "");
		TrackedTask = base.Config.Bind("State", "Tracked tasks", configEntry.Value, "Favorite task IDs separated by |. Game saves are not modified.");
		NavigationNpc = base.Config.Bind("State", "Navigation NPC", configEntry2.Value, "NPC ID selected for the navigation arrow. Game saves are not modified.");
		FavoriteButton = base.Config.Bind("Controller", "Favorite button", "A", new ConfigDescription("Controller button used to add or remove the selected quest from favorites. B is reserved for Back/Close.", new AcceptableValueList<string>("A", "X", "Y")));
		NavigationButton = base.Config.Bind("Controller", "Navigation button", "X", new ConfigDescription("Controller button used to enable or disable navigation for the selected quest. B is reserved for Back/Close.", new AcceptableValueList<string>("A", "X", "Y")));
		base.Config.Remove(configEntry.Definition);
		base.Config.Remove(configEntry2.Definition);
		base.Config.SaveOnConfigSet = saveOnConfigSet;
		base.Config.Save();
	}

	private static GameKey ControllerButtonToGameKey(string button, GameKey fallback)
	{
		switch ((button ?? "").Trim().ToUpperInvariant())
		{
		case "A":
			return GameKey.Select;
		case "X":
			return GameKey.Option1;
		case "Y":
			return GameKey.Option2;
		default:
			return fallback;
		}
	}

	private void OnDestroy()
	{
		if (_harmony != null)
		{
			_harmony.UnpatchSelf();
		}
	}

	private void Update()
	{
		WorldGameObject worldGameObject = ((MainGame.me == null) ? null : MainGame.me.player);
		if (worldGameObject != null && !worldGameObject.is_removed)
		{
			Vector3 position = worldGameObject.transform.position;
			if (_hasLastPlayerPosition && !string.IsNullOrEmpty(NavigationNpc.Value) && (position - _lastPlayerPosition).sqrMagnitude > 250000f)
			{
				InvalidateNavigationTarget(afterTeleport: true);
			}
			else if (!string.IsNullOrEmpty(NavigationNpc.Value) && _cachedNavigationTarget != null && _cachedNavigationTarget.IsTeleport && _hasRouteEvaluationPosition && Time.unscaledTime >= _nextMovementRouteCheck && (position - _routeEvaluationPosition).sqrMagnitude >= 65536f)
			{
				_movementRouteReevaluation = true;
				_routeEvaluationPosition = position;
				_nextMovementRouteCheck = Time.unscaledTime + 0.75f;
			}
			_lastPlayerPosition = position;
			_hasLastPlayerPosition = true;
		}
		else
		{
			_hasLastPlayerPosition = false;
		}
		if (string.IsNullOrEmpty(NavigationNpc.Value))
		{
			if (_cachedNavigationTarget != null || _cachedNpcId != null)
			{
				InvalidateNavigationTarget();
			}
		}
		else if (_delayedNavigationRefreshAt > 0f && Time.unscaledTime >= _delayedNavigationRefreshAt)
		{
			_delayedNavigationRefreshAt = -1f;
			InvalidateNavigationTarget();
		}
		if (MainGame.game_started && Input.GetKeyDown(KeyCode.F6) && !(GUIElements.me == null) && !(GUIElements.me.game_gui == null))
		{
			GUIElements.me.game_gui.OpenOrSelectTab(GameGUI.TabType.Bodies);
		}
	}

	internal void InvalidateNavigationTarget(bool afterTeleport = false)
	{
		_cachedNpcId = null;
		_cachedNavigationTarget = null;
		_navigationResolveFailures = 0;
		_hasRouteEvaluationPosition = false;
		_movementRouteReevaluation = false;
		_nextMovementRouteCheck = 0f;
		_nextNpcLookup = (afterTeleport ? (Time.unscaledTime + 0.75f) : 0f);
		QuestData.ClearNavigationCaches();
	}

	internal void RefreshNavigationAfterDialogue()
	{
		InvalidateNavigationTarget();
		_delayedNavigationRefreshAt = Time.unscaledTime + 1.5f;
	}

	private void OnGUI()
	{
		if (string.IsNullOrEmpty(NavigationNpc.Value) || MainGame.me == null || !MainGame.game_started || (GUIElements.me != null && GUIElements.me.game_gui != null && GUIElements.me.game_gui.is_shown) || Event.current.type != EventType.Repaint)
		{
			return;
		}
		NavigationTarget navigationTarget = ResolveNavigationTarget();
		if (navigationTarget == null || MainGame.me.world_cam == null)
		{
			return;
		}
		Vector3 vector = MainGame.me.world_cam.WorldToScreenPoint(navigationTarget.Transform.position);
		vector.y = (float)Screen.height - vector.y;
		if (vector.z > 0f && vector.x > 30f && vector.x < (float)(Screen.width - 30) && vector.y > 30f && vector.y < (float)(Screen.height - 30))
		{
			GUI.color = new Color(0.96f, 0.82f, 0.32f, 0.95f);
			GUI.DrawTexture(new Rect(vector.x - 8f, vector.y - 34f, 16f, 24f), _arrow);
			GUI.color = Color.white;
			return;
		}
		Vector2 vector2 = new Vector2((float)Screen.width * 0.5f, (float)Screen.height * 0.5f);
		Vector2 vector3 = new Vector2(vector.x, vector.y) - vector2;
		if (vector.z < 0f)
		{
			vector3 = -vector3;
		}
		vector3.Normalize();
		float num = 52f;
		float num2 = Mathf.Min(((float)Screen.width * 0.5f - num) / Mathf.Max(0.001f, Mathf.Abs(vector3.x)), ((float)Screen.height * 0.5f - num) / Mathf.Max(0.001f, Mathf.Abs(vector3.y)));
		Vector2 pivotPoint = vector2 + vector3 * num2;
		float angle = Mathf.Atan2(vector3.y, vector3.x) * 57.29578f - 90f;
		Matrix4x4 matrix = GUI.matrix;
		GUIUtility.RotateAroundPivot(angle, pivotPoint);
		GUI.color = new Color(0.96f, 0.82f, 0.32f, 0.95f);
		GUI.DrawTexture(new Rect(pivotPoint.x - 13f, pivotPoint.y - 18f, 26f, 36f), _arrow);
		GUI.color = Color.white;
		GUI.matrix = matrix;
	}

	private NavigationTarget ResolveNavigationTarget()
	{
		string value = NavigationNpc.Value;
		WorldGameObject worldGameObject = ((MainGame.me == null) ? null : MainGame.me.player);
		if (_cachedNpcId != value)
		{
			_cachedNpcId = value;
			_cachedNavigationTarget = null;
			_nextNpcLookup = 0f;
			_navigationResolveFailures = 0;
			_hasRouteEvaluationPosition = false;
			_movementRouteReevaluation = false;
		}
		if (_cachedNavigationTarget != null && _cachedNavigationTarget.IsUsable && !_movementRouteReevaluation)
		{
			return _cachedNavigationTarget;
		}
		NavigationTarget navigationTarget = ((_cachedNavigationTarget != null && _cachedNavigationTarget.IsUsable) ? _cachedNavigationTarget : null);
		_cachedNavigationTarget = null;
		if (Time.unscaledTime < _nextNpcLookup)
		{
			return null;
		}
		if (QuestData.AreTeleportsTemporarilyLocked())
		{
			_nextNpcLookup = Time.unscaledTime + 0.5f;
			return null;
		}
		bool movementRouteReevaluation = _movementRouteReevaluation;
		_movementRouteReevaluation = false;
		_cachedNavigationTarget = QuestData.FindNavigationTarget(value, !movementRouteReevaluation);
		if (movementRouteReevaluation && navigationTarget != null && navigationTarget.IsTeleport && _cachedNavigationTarget != null && _cachedNavigationTarget.IsTeleport && navigationTarget.Transform != _cachedNavigationTarget.Transform && worldGameObject != null)
		{
			Vector3 vector = navigationTarget.Transform.position - worldGameObject.transform.position;
			Vector3 vector2 = _cachedNavigationTarget.Transform.position - worldGameObject.transform.position;
			float f = vector.x * vector.x + vector.y * vector.y;
			float f2 = vector2.x * vector2.x + vector2.y * vector2.y;
			if (Mathf.Sqrt(f2) + 96f >= Mathf.Sqrt(f))
			{
				_cachedNavigationTarget = navigationTarget;
			}
		}
		if (_cachedNavigationTarget != null)
		{
			_navigationResolveFailures = 0;
			if (worldGameObject != null)
			{
				_routeEvaluationPosition = worldGameObject.transform.position;
				_hasRouteEvaluationPosition = true;
				_nextMovementRouteCheck = Time.unscaledTime + 0.75f;
			}
			return _cachedNavigationTarget;
		}
		_navigationResolveFailures++;
		if (_navigationResolveFailures >= 2)
		{
			base.Logger.LogWarning("Navigation disabled because no safe route was found for " + value);
			NavigationNpc.Value = "";
			InvalidateNavigationTarget();
			return null;
		}
		_nextNpcLookup = Time.unscaledTime + 1f;
		return _cachedNavigationTarget;
	}

	private static Texture2D MakeArrow()
	{
		Texture2D texture2D = new Texture2D(24, 32, TextureFormat.ARGB32, mipChain: false);
		Color color = new Color(0f, 0f, 0f, 0f);
		Color color2 = new Color(0.2f, 0.1f, 0.04f, 1f);
		Color color3 = new Color(0.96f, 0.72f, 0.18f, 1f);
		for (int i = 0; i < 32; i++)
		{
			for (int j = 0; j < 24; j++)
			{
				texture2D.SetPixel(j, i, color);
			}
		}
		for (int k = 2; k < 30; k++)
		{
			int num = ((k < 15) ? Math.Max(2, k * 10 / 15) : 4);
			for (int l = 12 - num; l <= 12 + num; l++)
			{
				texture2D.SetPixel(l, k, (l == 12 - num || l == 12 + num || k == 2 || k == 29) ? color2 : color3);
			}
		}
		texture2D.Apply();
		return texture2D;
	}
}
