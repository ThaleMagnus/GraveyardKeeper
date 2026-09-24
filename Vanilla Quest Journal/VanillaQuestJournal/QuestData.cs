using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VanillaQuestJournal;

internal static class QuestData
{
	private sealed class TeleportLink
	{
		public WorldGameObject A;

		public WorldGameObject B;

		public string LocationA;

		public string LocationB;
	}

	private const string OutsideLocation = "world";

	private static bool _locationsLoaded;

	private static string _locationsLocale;

	private static readonly Dictionary<string, string> LocationOverrides = new Dictionary<string, string>();

	private static readonly Dictionary<string, WorldGameObject> NpcLookupCache = new Dictionary<string, WorldGameObject>();

	private static Dictionary<string, WorldGameObject> _activeWorldNpcMap;

	private static float _nextNpcCacheRefresh;

	private static List<WorldGameObject> _teleportEndpoints;

	private static int _teleportWorldObjectCount = -1;

	private static readonly FieldInfo EnvironmentPresetField = AccessTools.Field(typeof(GameSave), "_environment_preset");

	private static readonly Dictionary<string, string> TeleportUnlockCrafts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "tp_mortuary_hatch", "hatch_from_morgue" } };

	private static readonly Dictionary<string, string[]> NpcObjectAliases = new Dictionary<string, string[]>
	{
		{
			"npc_light_keeper",
			new string[2] { "npc_lighthouse_keeper", "npc_lightkeeper" }
		},
		{
			"npc_mrs",
			new string[2] { "npc_actress", "npc_ms_charm" }
		},
		{
			"npc_blacksmith",
			new string[1] { "npc_krezvold" }
		},
		{
			"npc_ghost",
			new string[2] { "talking_skull", "npc_gerry" }
		}
	};

	private static readonly Dictionary<string, string> ZoneNames = new Dictionary<string, string>
	{
		{ "village", "zone.village" },
		{ "town", "zone.town" },
		{ "home", "zone.home" },
		{ "church", "zone.church" },
		{ "tavern", "zone.tavern" },
		{ "lighthouse", "zone.lighthouse" },
		{ "witch_hill", "zone.witch_hill" },
		{ "refugees", "zone.refugees" },
		{ "quarry", "zone.quarry" },
		{ "swamp", "zone.swamp" },
		{ "beach", "zone.beach" }
	};

	internal static void ReloadLocalizedData()
	{
		_locationsLoaded = false;
		_locationsLocale = null;
		LocationOverrides.Clear();
	}

	internal static void ClearNavigationCaches()
	{
		NpcLookupCache.Clear();
		_activeWorldNpcMap = null;
		_nextNpcCacheRefresh = 0f;
		_teleportEndpoints = null;
		_teleportWorldObjectCount = -1;
	}

	public static List<JournalEntry> ReadEntries()
	{
		EnsureLocationOverrides();
		List<JournalEntry> list = new List<JournalEntry>();
		if (MainGame.me == null || MainGame.me.save == null || MainGame.me.save.known_npcs == null)
		{
			return list;
		}
		foreach (KnownNPC npc in MainGame.me.save.known_npcs.npcs)
		{
			if (npc == null || npc.tasks == null)
			{
				continue;
			}
			ObjectDefinition dataOrNull = GameBalance.me.GetDataOrNull<ObjectDefinition>(npc.npc_id);
			foreach (KnownNPC.TaskState task in npc.tasks)
			{
				if (task != null && (task.state == KnownNPC.TaskState.State.Visible || task.state == KnownNPC.TaskState.State.Complete))
				{
					bool flag = task.state == KnownNPC.TaskState.State.Complete;
					string text = GameText("task_" + task.id);
					if (text == "task_" + task.id)
					{
						text = task.GetTaskText();
					}
					string text2 = ReadableTask(text);
					WorldGameObject worldGameObject = FindNpcInWorld(npc.npc_id);
					string configuredLocation = GetConfiguredLocation(npc.npc_id, worldGameObject);
					string text3 = DayFromTask(text);
					if (string.IsNullOrEmpty(text3))
					{
						text3 = DayText((dataOrNull == null) ? "" : dataOrNull.day_icon);
					}
					string text4 = ((dataOrNull != null && !string.IsNullOrEmpty(dataOrNull.npc_alias)) ? dataOrNull.npc_alias : npc.npc_id);
					Sprite sprite = EasySpritesCollection.GetSprite("char_" + text4);
					string dlcLabel = (task.is_dlc_stories_task ? "STRANGER SINS" : (task.is_dlc_refugee_task ? "GAME OF CRONE" : (task.is_dlc_souls_task ? "BETTER SAVE SOUL" : ModLocalization.T("dlc.base"))));
					list.Add(new JournalEntry
					{
						Key = npc.npc_id + ":" + task.id,
						NpcId = npc.npc_id,
						NpcName = GameText(npc.npc_id),
						FullText = text2,
						ShortText = Trim(text2, 92),
						CharacterLocation = EnsureTrailingPunctuation(configuredLocation),
						TaskLocation = "",
						Schedule = (string.IsNullOrEmpty(text3) ? ModLocalization.T("schedule.no_fixed") : text3),
						Availability = ((!flag) ? ((worldGameObject != null) ? ModLocalization.T("availability.present") : ModLocalization.T("availability.absent")) : ((worldGameObject != null) ? ModLocalization.T("availability.completed_present") : ModLocalization.T("availability.completed"))),
						DlcLabel = dlcLabel,
						IsPresent = (worldGameObject != null),
						IsCompleted = flag,
						Portrait = sprite,
						DayIcon = GetDayIcon((dataOrNull == null) ? "" : dataOrNull.day_icon, text)
					});
				}
			}
		}
		return list.OrderBy((JournalEntry e) => e.IsCompleted).ThenByDescending(IsKeeperQuest).ThenBy((JournalEntry e) => e.NpcName)
			.ThenBy((JournalEntry e) => e.FullText)
			.ToList();
	}

	private static bool IsKeeperQuest(JournalEntry entry)
	{
		string text = entry.NpcId ?? "";
		return text.IndexOf("keeper", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static string GetConfiguredLocation(string npcId, WorldGameObject worldNpc)
	{
		if (!string.IsNullOrEmpty(npcId) && LocationOverrides.TryGetValue(npcId, out var value))
		{
			return value;
		}
		foreach (string npcObjectId in GetNpcObjectIds(npcId))
		{
			if (LocationOverrides.TryGetValue(npcObjectId, out value))
			{
				return value;
			}
		}
		if (worldNpc == null)
		{
			return null;
		}
		if (!string.IsNullOrEmpty(worldNpc.obj_id) && LocationOverrides.TryGetValue(worldNpc.obj_id, out value))
		{
			return value;
		}
		if (worldNpc.obj_def != null)
		{
			if (!string.IsNullOrEmpty(worldNpc.obj_def.id) && LocationOverrides.TryGetValue(worldNpc.obj_def.id, out value))
			{
				return value;
			}
			if (!string.IsNullOrEmpty(worldNpc.obj_def.npc_alias) && LocationOverrides.TryGetValue(worldNpc.obj_def.npc_alias, out value))
			{
				return value;
			}
		}
		return null;
	}

	public static WorldGameObject FindNpcInWorld(string npcId)
	{
		if (string.IsNullOrEmpty(npcId))
		{
			return null;
		}
		RefreshNpcCachesIfNeeded();
		if (NpcLookupCache.TryGetValue(npcId, out var value))
		{
			if (IsNpcCurrentlyPresent(value))
			{
				return value;
			}
			NpcLookupCache.Remove(npcId);
		}
		foreach (string npcObjectId in GetNpcObjectIds(npcId))
		{
			if (_activeWorldNpcMap.TryGetValue(npcObjectId, out var value2) && IsNpcCurrentlyPresent(value2))
			{
				NpcLookupCache[npcId] = value2;
				return value2;
			}
		}
		foreach (string npcObjectId2 in GetNpcObjectIds(npcId))
		{
			List<WorldGameObject> worldGameObjectsByObjId = WorldMap.GetWorldGameObjectsByObjId(npcObjectId2);
			if (worldGameObjectsByObjId != null)
			{
				WorldGameObject worldGameObject = worldGameObjectsByObjId.FirstOrDefault(IsNpcCurrentlyPresent);
				if (worldGameObject != null)
				{
					NpcLookupCache[npcId] = worldGameObject;
					return worldGameObject;
				}
			}
		}
		NpcLookupCache[npcId] = null;
		return null;
	}

	public static NavigationTarget FindNavigationTarget(string npcId, bool writeLog = true)
	{
		WorldGameObject worldGameObject = FindNpcInWorld(npcId);
		WorldGameObject worldGameObject2 = ((MainGame.me == null) ? null : MainGame.me.player);
		if (worldGameObject == null || worldGameObject2 == null || worldGameObject2.is_removed)
		{
			if (writeLog)
			{
				LogNavigation("NPC unavailable", npcId, worldGameObject, null);
			}
			return null;
		}
		bool sameRegion;
		WorldGameObject worldGameObject3 = FindTeleportStep(worldGameObject2, worldGameObject, out sameRegion, writeLog);
		if (sameRegion)
		{
			if (writeLog)
			{
				LogNavigation("Direct NPC", npcId, worldGameObject, null);
			}
			return new NavigationTarget(worldGameObject.transform, null, worldGameObject, requiresNpcPresence: true);
		}
		if (worldGameObject3 != null)
		{
			if (writeLog)
			{
				LogNavigation("Teleport " + worldGameObject3.custom_tag, npcId, worldGameObject, worldGameObject3);
			}
			return new NavigationTarget(worldGameObject3.transform, null, worldGameObject3);
		}
		if (writeLog)
		{
			LogNavigation("No teleport route", npcId, worldGameObject, null);
		}
		return null;
	}

	internal static bool IsNpcCurrentlyPresent(WorldGameObject npc)
	{
		if (!IsWorldObjectInLoadedScene(npc))
		{
			return false;
		}
		if (string.Equals(npc.cur_gd_point, "default_destroy_point", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(npc.cur_gd_point) && npc.cur_gd_point.StartsWith("gd_stock", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		int day;
		bool flag = TryGetScheduledDay((npc.obj_def == null) ? "" : npc.obj_def.day_icon, out day);
		if (flag && (MainGame.me == null || MainGame.me.save == null || MainGame.me.save.day_of_week != day))
		{
			return false;
		}
		if (!flag)
		{
			return true;
		}
		if (npc.gameObject.activeInHierarchy)
		{
			return true;
		}
		ChunkedGameObject component = npc.GetComponent<ChunkedGameObject>();
		if (component != null && (component.always_active || component.active_now_because_of_movement || component.active_now_because_of_events || component.active_now_because_of_work))
		{
			return true;
		}
		if (npc.obj_def != null && npc.obj_def.always_active)
		{
			return true;
		}
		return !string.IsNullOrEmpty(npc.cur_gd_point);
	}

	private static bool TryGetScheduledDay(string dayIcon, out int day)
	{
		day = -1;
		if (string.IsNullOrEmpty(dayIcon))
		{
			return false;
		}
		Match match = Regex.Match(dayIcon, "(?:^|[^a-z])d([1-6])(?:[^0-9]|$)", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			day = int.Parse(match.Groups[1].Value) - 1;
			return true;
		}
		Match match2 = Regex.Match(dayIcon, "sin[_-]?([1-6])", RegexOptions.IgnoreCase);
		if (!match2.Success)
		{
			return false;
		}
		day = int.Parse(match2.Groups[1].Value) - 1;
		return true;
	}

	internal static bool IsWorldObjectInLoadedScene(WorldGameObject w)
	{
		if (w == null || w.is_removed)
		{
			return false;
		}
		GameObject gameObject = w.gameObject;
		if (gameObject == null || gameObject.transform.parent == null)
		{
			return false;
		}
		Scene scene = gameObject.scene;
		if (scene.IsValid() && scene.isLoaded)
		{
			return !string.IsNullOrEmpty(scene.name);
		}
		return false;
	}

	internal static bool IsGDPointInLoadedScene(GDPoint point)
	{
		if (point == null || point.gameObject == null)
		{
			return false;
		}
		List<GDPoint> gd_points = WorldMap.gd_points;
		if (gd_points == null || !gd_points.Contains(point))
		{
			return false;
		}
		Scene scene = point.gameObject.scene;
		if (scene.IsValid())
		{
			return scene.isLoaded;
		}
		return false;
	}

	private static WorldGameObject FindTeleportStep(WorldGameObject player, WorldGameObject npc, out bool sameRegion, bool writeLog)
	{
		sameRegion = false;
		List<WorldGameObject> objs = WorldMap.objs;
		if (objs == null)
		{
			return null;
		}
		if (_teleportEndpoints == null || _teleportWorldObjectCount != objs.Count)
		{
			_teleportEndpoints = new List<WorldGameObject>();
			_teleportWorldObjectCount = objs.Count;
			foreach (WorldGameObject item in objs)
			{
				if (IsWorldObjectInLoadedScene(item) && !string.IsNullOrEmpty(item.custom_tag))
				{
					string custom_tag = item.custom_tag;
					if (custom_tag.StartsWith("tp_", StringComparison.OrdinalIgnoreCase) && (custom_tag.EndsWith("_a", StringComparison.OrdinalIgnoreCase) || custom_tag.EndsWith("_b", StringComparison.OrdinalIgnoreCase)))
					{
						_teleportEndpoints.Add(item);
					}
				}
			}
		}
		if (_teleportEndpoints.Count == 0)
		{
			return null;
		}
		string playerNavigationLocation = GetPlayerNavigationLocation(player);
		string objectNavigationLocation = GetObjectNavigationLocation(npc);
		if (string.IsNullOrEmpty(playerNavigationLocation) || string.IsNullOrEmpty(objectNavigationLocation))
		{
			if (writeLog)
			{
				LogLocationGraph("unknown location", playerNavigationLocation, objectNavigationLocation, 0);
			}
			return null;
		}
		if (string.Equals(playerNavigationLocation, objectNavigationLocation, StringComparison.OrdinalIgnoreCase))
		{
			sameRegion = true;
			return null;
		}
		Dictionary<string, WorldGameObject> dictionary = new Dictionary<string, WorldGameObject>();
		foreach (WorldGameObject teleportEndpoint in _teleportEndpoints)
		{
			if (!dictionary.ContainsKey(teleportEndpoint.custom_tag))
			{
				dictionary.Add(teleportEndpoint.custom_tag, teleportEndpoint);
			}
		}
		List<TeleportLink> list = new List<TeleportLink>();
		foreach (KeyValuePair<string, WorldGameObject> item2 in dictionary)
		{
			if (!item2.Key.EndsWith("_a", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string text = item2.Key.Substring(0, item2.Key.Length - 2) + "_b";
			if (dictionary.TryGetValue(text, out var value))
			{
				string teleportInteriorLocation = GetTeleportInteriorLocation(text);
				if (!string.IsNullOrEmpty(teleportInteriorLocation))
				{
					list.Add(new TeleportLink
					{
						A = item2.Value,
						B = value,
						LocationA = "world",
						LocationB = teleportInteriorLocation
					});
				}
			}
		}
		Queue<string> queue = new Queue<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, WorldGameObject> dictionary2 = new Dictionary<string, WorldGameObject>(StringComparer.OrdinalIgnoreCase);
		queue.Enqueue(playerNavigationLocation);
		hashSet.Add(playerNavigationLocation);
		while (queue.Count > 0)
		{
			string current4 = queue.Dequeue();
			IEnumerable<TeleportLink> enumerable = list;
			if (string.Equals(current4, playerNavigationLocation, StringComparison.OrdinalIgnoreCase))
			{
				enumerable = list.OrderBy((TeleportLink link) => TeleportEndpointDistanceFromPlayer(link, current4, player));
			}
			foreach (TeleportLink item3 in enumerable)
			{
				string text2;
				WorldGameObject worldGameObject;
				if (string.Equals(item3.LocationA, current4, StringComparison.OrdinalIgnoreCase))
				{
					text2 = item3.LocationB;
					worldGameObject = item3.A;
				}
				else
				{
					if (!string.Equals(item3.LocationB, current4, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					text2 = item3.LocationA;
					worldGameObject = item3.B;
				}
				if (!IsTeleportEndpointAvailable(worldGameObject) || !hashSet.Add(text2))
				{
					continue;
				}
				dictionary2[text2] = (string.Equals(current4, playerNavigationLocation, StringComparison.OrdinalIgnoreCase) ? worldGameObject : dictionary2[current4]);
				if (string.Equals(text2, objectNavigationLocation, StringComparison.OrdinalIgnoreCase))
				{
					if (writeLog)
					{
						LogLocationGraph("path", playerNavigationLocation, objectNavigationLocation, list.Count);
					}
					return dictionary2[text2];
				}
				queue.Enqueue(text2);
			}
		}
		if (writeLog)
		{
			LogLocationGraph("no path", playerNavigationLocation, objectNavigationLocation, list.Count);
		}
		return null;
	}

	internal static bool AreTeleportsTemporarilyLocked()
	{
		WorldGameObject worldGameObject = ((MainGame.me == null) ? null : MainGame.me.player);
		if (worldGameObject != null)
		{
			return worldGameObject.GetParam("lock_tp") > 0.5f;
		}
		return false;
	}

	private static string GetPlayerNavigationLocation(WorldGameObject player)
	{
		try
		{
			string text = ((EnvironmentPresetField == null || MainGame.me == null || MainGame.me.save == null) ? null : (EnvironmentPresetField.GetValue(MainGame.me.save) as string));
			return string.IsNullOrEmpty(text) ? "world" : text.ToLowerInvariant();
		}
		catch
		{
			return GetObjectNavigationLocation(player);
		}
	}

	private static string GetObjectNavigationLocation(WorldGameObject obj)
	{
		if (obj == null || _teleportEndpoints == null || _teleportEndpoints.Count == 0)
		{
			return null;
		}
		WorldGameObject worldGameObject = null;
		float num = float.MaxValue;
		Vector3 position = obj.transform.position;
		foreach (WorldGameObject teleportEndpoint in _teleportEndpoints)
		{
			if (!(teleportEndpoint == null))
			{
				Vector3 vector = teleportEndpoint.transform.position - position;
				float num2 = vector.x * vector.x + vector.y * vector.y;
				if (!(num2 >= num))
				{
					num = num2;
					worldGameObject = teleportEndpoint;
				}
			}
		}
		if (worldGameObject == null || string.IsNullOrEmpty(worldGameObject.custom_tag))
		{
			return null;
		}
		if (worldGameObject.custom_tag.EndsWith("_a", StringComparison.OrdinalIgnoreCase))
		{
			return "world";
		}
		return GetTeleportInteriorLocation(worldGameObject.custom_tag);
	}

	private static string GetTeleportInteriorLocation(string tag)
	{
		if (string.IsNullOrEmpty(tag))
		{
			return null;
		}
		string[] array = tag.Split('_');
		if (array.Length < 3 || !string.Equals(array[0], "tp", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string a = array[array.Length - 1];
		if (!string.Equals(a, "a", StringComparison.OrdinalIgnoreCase) && !string.Equals(a, "b", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if (!string.IsNullOrEmpty(array[1]))
		{
			return array[1].ToLowerInvariant();
		}
		return null;
	}

	private static float TeleportEndpointDistanceFromPlayer(TeleportLink link, string location, WorldGameObject player)
	{
		WorldGameObject worldGameObject = (string.Equals(link.LocationA, location, StringComparison.OrdinalIgnoreCase) ? link.A : link.B);
		if (worldGameObject == null || player == null)
		{
			return float.MaxValue;
		}
		Vector3 vector = worldGameObject.transform.position - player.transform.position;
		return vector.x * vector.x + vector.y * vector.y;
	}

	private static bool IsTeleportEndpointAvailable(WorldGameObject endpoint)
	{
		if (!IsWorldObjectInLoadedScene(endpoint) || endpoint.is_removed)
		{
			return false;
		}
		string text = endpoint.custom_tag ?? "";
		foreach (KeyValuePair<string, string> requirement in TeleportUnlockCrafts)
		{
			KeyValuePair<string, string> keyValuePair = requirement;
			if (text.StartsWith(keyValuePair.Key, StringComparison.OrdinalIgnoreCase))
			{
				List<string> list = ((MainGame.me == null || MainGame.me.save == null) ? null : MainGame.me.save.completed_one_time_crafts);
				if (list == null || !list.Any(delegate(string id)
				{
					KeyValuePair<string, string> keyValuePair2 = requirement;
					return string.Equals(id, keyValuePair2.Value, StringComparison.OrdinalIgnoreCase);
				}))
				{
					return false;
				}
			}
		}
		try
		{
			return !endpoint.IsDisabled();
		}
		catch
		{
			return true;
		}
	}

	private static void LogLocationGraph(string result, string playerLocation, string npcLocation, int links)
	{
		if (!(QuestJournalPlugin.Instance == null))
		{
			QuestJournalPlugin.Instance.LogInfoMessage(string.Format("Location graph {0}: player={1}, npc={2}, endpoints={3}, links={4}", result, playerLocation ?? "?", npcLocation ?? "?", (_teleportEndpoints != null) ? _teleportEndpoints.Count : 0, links));
		}
	}

	private static bool TryGetNavigationArea(Vector3 position, out uint area)
	{
		area = 0u;
		GDPoint gDPoint = null;
		float num = float.MaxValue;
		List<GDPoint> gd_points = WorldMap.gd_points;
		if (gd_points == null)
		{
			return false;
		}
		foreach (GDPoint item in gd_points)
		{
			if (!(item == null))
			{
				Vector3 vector = item.transform.position - position;
				float num2 = vector.x * vector.x + vector.y * vector.y;
				if (!(num2 >= num))
				{
					num = num2;
					gDPoint = item;
				}
			}
		}
		if (gDPoint == null)
		{
			return false;
		}
		try
		{
			area = gDPoint.node.Area;
			return true;
		}
		catch (Exception ex)
		{
			if (QuestJournalPlugin.Instance != null)
			{
				QuestJournalPlugin.Instance.LogErrorMessage("Could not read GD navigation area: " + ex.Message);
			}
			return false;
		}
	}

	private static GDPoint FindNearestGDPoint(Vector3 position)
	{
		List<GDPoint> gd_points = WorldMap.gd_points;
		GDPoint result = null;
		float num = float.MaxValue;
		if (gd_points == null)
		{
			return null;
		}
		foreach (GDPoint item in gd_points)
		{
			if (IsGDPointInLoadedScene(item))
			{
				float sqrMagnitude = (item.transform.position - position).sqrMagnitude;
				if (!(sqrMagnitude >= num))
				{
					num = sqrMagnitude;
					result = item;
				}
			}
		}
		return result;
	}

	private static void LogNavigation(string result, string npcId, WorldGameObject npc, WorldGameObject teleport)
	{
		if (!(QuestJournalPlugin.Instance == null))
		{
			string text = ((npc == null) ? "null" : $"obj={npc.obj_id}, pos={npc.transform.position}, gd={npc.cur_gd_point}, activeSelf={npc.gameObject.activeSelf}, activeHierarchy={npc.gameObject.activeInHierarchy}");
			string text2 = ((teleport == null) ? "" : (", stepPos=" + teleport.transform.position));
			QuestJournalPlugin.Instance.LogInfoMessage("Navigation " + result + " for " + npcId + ": " + text + text2);
		}
	}

	private static List<GDPoint> FindGDRoute(GDPoint from, GDPoint to)
	{
		Queue<GDPoint> queue = new Queue<GDPoint>();
		Dictionary<GDPoint, GDPoint> dictionary = new Dictionary<GDPoint, GDPoint>();
		queue.Enqueue(from);
		dictionary[from] = null;
		while (queue.Count > 0 && dictionary.Count < 4096)
		{
			GDPoint gDPoint = queue.Dequeue();
			if (gDPoint == to)
			{
				break;
			}
			if (gDPoint.next_gd_points == null)
			{
				continue;
			}
			foreach (GDPoint next_gd_point in gDPoint.next_gd_points)
			{
				if (!(next_gd_point == null) && !dictionary.ContainsKey(next_gd_point))
				{
					dictionary[next_gd_point] = gDPoint;
					queue.Enqueue(next_gd_point);
				}
			}
		}
		if (!dictionary.ContainsKey(to))
		{
			return null;
		}
		List<GDPoint> list = new List<GDPoint>();
		GDPoint gDPoint2 = to;
		while (gDPoint2 != null)
		{
			list.Add(gDPoint2);
			gDPoint2 = dictionary[gDPoint2];
		}
		list.Reverse();
		return list;
	}

	private static IEnumerable<string> GetNpcObjectIds(string npcId)
	{
		if (NpcObjectAliases.TryGetValue(npcId, out var aliases))
		{
			try
			{
				string[] array = aliases;
				for (int i = 0; i < array.Length; i++)
				{
					yield return array[i];
				}
			}
			finally
			{
			}
		}
		yield return npcId;
		ObjectDefinition def = ((GameBalance.me == null) ? null : GameBalance.me.GetDataOrNull<ObjectDefinition>(npcId));
		if (def != null)
		{
			if (!string.IsNullOrEmpty(def.id) && def.id != npcId)
			{
				yield return def.id;
			}
			if (!string.IsNullOrEmpty(def.npc_alias) && def.npc_alias != npcId)
			{
				yield return def.npc_alias;
			}
		}
	}

	private static void RefreshNpcCachesIfNeeded()
	{
		if (_activeWorldNpcMap == null || !(Time.unscaledTime < _nextNpcCacheRefresh))
		{
			_nextNpcCacheRefresh = Time.unscaledTime + 2f;
			NpcLookupCache.Clear();
			_activeWorldNpcMap = BuildWorldNpcMap();
		}
	}

	private static Dictionary<string, WorldGameObject> BuildWorldNpcMap()
	{
		Dictionary<string, WorldGameObject> dictionary = new Dictionary<string, WorldGameObject>();
		if (MainGame.me == null)
		{
			return dictionary;
		}
		List<WorldGameObject> objs = WorldMap.objs;
		if (objs == null)
		{
			return dictionary;
		}
		foreach (WorldGameObject item in objs)
		{
			if (IsWorldObjectInLoadedScene(item) && item.obj_def != null && item.obj_def.IsNPC())
			{
				if (!string.IsNullOrEmpty(item.obj_id) && !dictionary.ContainsKey(item.obj_id))
				{
					dictionary.Add(item.obj_id, item);
				}
				if (!string.IsNullOrEmpty(item.obj_def.id) && !dictionary.ContainsKey(item.obj_def.id))
				{
					dictionary.Add(item.obj_def.id, item);
				}
				if (!string.IsNullOrEmpty(item.obj_def.npc_alias) && !dictionary.ContainsKey(item.obj_def.npc_alias))
				{
					dictionary.Add(item.obj_def.npc_alias, item);
				}
			}
		}
		return dictionary;
	}

	private static void EnsureLocationOverrides()
	{
		string localeCode = ModLocalization.LocaleCode;
		if (_locationsLoaded && string.Equals(_locationsLocale, localeCode, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		_locationsLoaded = true;
		_locationsLocale = localeCode;
		LocationOverrides.Clear();
		try
		{
			string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string path = Path.Combine(directoryName, "languages");
			string path2 = Path.Combine(path, "npc_locations_" + localeCode + ".json");
			if (!File.Exists(path2))
			{
				path2 = Path.Combine(path, "npc_locations_en.json");
			}
			if (!File.Exists(path2))
			{
				return;
			}
			string input = File.ReadAllText(path2);
			foreach (Match item in Regex.Matches(input, "\"npcId\"\\s*:\\s*\"(?<id>(?:\\\\.|[^\"])*)\"\\s*,\\s*\"location\"\\s*:\\s*\"(?<location>(?:\\\\.|[^\"])*)\""))
			{
				string text = DecodeJsonText(item.Groups["id"].Value);
				string value = DecodeJsonText(item.Groups["location"].Value);
				if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(value))
				{
					LocationOverrides[text] = value;
				}
			}
		}
		catch (Exception ex)
		{
			if (QuestJournalPlugin.Instance != null)
			{
				QuestJournalPlugin.Instance.LogErrorMessage("Could not read NPC locations: " + ex.Message);
			}
		}
	}

	private static string DecodeJsonText(string value)
	{
		return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
	}

	private static Sprite GetDayIcon(string dayIcon, string taskText)
	{
		Match match = Regex.Match((dayIcon ?? "") + " " + (taskText ?? ""), "(?:\\(|\\b)d([1-6])(?:\\)|\\b)", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			Sprite sprite = EasySpritesCollection.GetSprite("icon_font_sin_" + int.Parse(match.Groups[1].Value));
			if (sprite != null)
			{
				return sprite;
			}
		}
		if (string.IsNullOrEmpty(dayIcon))
		{
			return null;
		}
		Sprite sprite2 = EasySpritesCollection.GetSprite(dayIcon);
		if (sprite2 != null)
		{
			return sprite2;
		}
		string[] array = new string[3]
		{
			"icon_" + dayIcon,
			"day_" + dayIcon,
			"hud_" + dayIcon
		};
		foreach (string sprite_name in array)
		{
			sprite2 = EasySpritesCollection.GetSprite(sprite_name);
			if (sprite2 != null)
			{
				return sprite2;
			}
		}
		return null;
	}

	private static string GameText(string key)
	{
		string text = GJL.L(key);
		if (!string.IsNullOrEmpty(text))
		{
			return text.Replace("&#xA;", "\n").Replace("’", "'");
		}
		return key;
	}

	private static string FriendlyZone(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return ModLocalization.T("zone.unknown");
		}
		foreach (KeyValuePair<string, string> zoneName in ZoneNames)
		{
			if (id.IndexOf(zoneName.Key, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return ModLocalization.T(zoneName.Value);
			}
		}
		string[] array = new string[3]
		{
			id,
			"zone_" + id,
			"world_zone_" + id
		};
		foreach (string text in array)
		{
			string text2 = GameText(text);
			if (text2 != text)
			{
				return StripTags(text2);
			}
		}
		return id.Replace('_', ' ');
	}

	private static string DayText(string icon)
	{
		if (string.IsNullOrEmpty(icon))
		{
			return "";
		}
		string text = icon.ToLowerInvariant();
		Match match = Regex.Match(text, "d([1-6])");
		if (match.Success)
		{
			return ModLocalization.T("day.cycle", int.Parse(match.Groups[1].Value));
		}
		if (text.Contains("sun"))
		{
			return ModLocalization.T("day.sun");
		}
		if (text.Contains("moon"))
		{
			return ModLocalization.T("day.moon");
		}
		if (text.Contains("anger") || text.Contains("mars"))
		{
			return ModLocalization.T("day.anger");
		}
		if (text.Contains("glutton") || text.Contains("jupiter"))
		{
			return ModLocalization.T("day.gluttony");
		}
		if (text.Contains("envy") || text.Contains("mercur"))
		{
			return ModLocalization.T("day.envy");
		}
		if (text.Contains("lust") || text.Contains("venus"))
		{
			return ModLocalization.T("day.lust");
		}
		if (text.Contains("pride") || text.Contains("saturn"))
		{
			return ModLocalization.T("day.pride");
		}
		return ModLocalization.T("day.appearance", StripTags(icon));
	}

	private static string DayFromTask(string text)
	{
		Match match = Regex.Match(text ?? "", "\\(d([1-6])\\)", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return "";
		}
		return ModLocalization.T("day.cycle", int.Parse(match.Groups[1].Value));
	}

	private static string ReadableTask(string text)
	{
		string input = StripTags(text);
		input = Regex.Replace(input, "<([^>]+)>", "$1");
		return Regex.Replace(input, "\\(d([1-6])\\)", (Match m) => ModLocalization.T("day.cycle_inline", int.Parse(m.Groups[1].Value)), RegexOptions.IgnoreCase);
	}

	private static string EnsureTrailingPunctuation(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		text = text.Trim();
		char c = text[text.Length - 1];
		if (c != '.' && c != '!' && c != '?' && c != '…')
		{
			return text + ".";
		}
		return text;
	}

	private static string StripTags(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return ModLocalization.T("description.none");
		}
		while (true)
		{
			int num = text.IndexOf('[');
			int num2 = ((num < 0) ? (-1) : text.IndexOf(']', num));
			if (num < 0 || num2 < 0)
			{
				break;
			}
			text = text.Remove(num, num2 - num + 1);
		}
		return text.Replace("(*)", "").Replace("(*2)", "").Trim();
	}

	private static string Trim(string value, int max)
	{
		if (value.Length > max)
		{
			return value.Substring(0, max - 1).TrimEnd() + "…";
		}
		return value;
	}
}
