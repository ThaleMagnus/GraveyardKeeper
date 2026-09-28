using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VanillaQuestJournal;

internal static class ModLocalization
{
	private static readonly Dictionary<string, string> English = new Dictionary<string, string>
	{
		{ "tab.quests", "Quests" },
		{ "journal.current", "Current tasks" },
		{ "journal.no_active", "No active tasks" },
		{ "journal.active_count", "Active: {0}" },
		{ "section.favorites", "Favorites" },
		{ "section.all", "All tasks" },
		{ "section.completed", "Completed tasks" },
		{ "empty.favorites", "No favorite tasks" },
		{ "empty.active", "No active tasks" },
		{ "empty.completed", "No completed tasks" },
		{ "details.select", "Select a task on the left." },
		{ "details.dlc_version", "DLC version:" },
		{ "details.requirement", "What is required" },
		{ "details.location", "Character location" },
		{ "details.schedule", "When available" },
		{ "button.disable_navigation", "Disable navigation" },
		{ "button.completed", "Completed" },
		{ "button.favorite_on", "Favorite" },
		{ "button.favorite_add", "Add to favorites" },
		{ "button.navigation_on", "Navigation enabled" },
		{ "button.navigation_enable", "Enable navigation" },
		{ "dlc.base", "BASE GAME" },
		{ "schedule.no_fixed", "Appears without a fixed day" },
		{ "availability.completed_present", "Task completed — character is currently available" },
		{ "availability.completed", "Task completed" },
		{ "availability.present", "Currently on the map" },
		{ "availability.absent", "Currently absent" },
		{ "zone.unknown", "Unknown area" },
		{ "zone.village", "Village" },
		{ "zone.town", "Town" },
		{ "zone.home", "Home and graveyard" },
		{ "zone.church", "Church" },
		{ "zone.tavern", "Tavern" },
		{ "zone.lighthouse", "Lighthouse" },
		{ "zone.witch_hill", "Witch Hill" },
		{ "zone.refugees", "Refugee camp" },
		{ "zone.quarry", "Quarry" },
		{ "zone.swamp", "Swamp" },
		{ "zone.beach", "Beach" },
		{ "day.cycle", "Game cycle day #{0}" },
		{ "day.cycle_inline", "cycle day #{0}" },
		{ "day.sun", "Day of the Sun" },
		{ "day.moon", "Day of the Moon" },
		{ "day.anger", "Day of Wrath" },
		{ "day.gluttony", "Day of Gluttony" },
		{ "day.envy", "Day of Envy" },
		{ "day.lust", "Day of Lust" },
		{ "day.pride", "Day of Pride" },
		{ "day.appearance", "Appearance day: {0}" },
		{ "description.none", "No description" }
	};

	private static readonly Dictionary<string, string> Translated = new Dictionary<string, string>();

	private static string _locale = "en";

	internal static string LocaleCode => _locale;

	internal static void Reload()
	{
		string text = (_locale = GetGameLocale());
		Translated.Clear();
		if (text == "en")
		{
			if (QuestJournalPlugin.Instance != null)
			{
				QuestJournalPlugin.Instance.LogInfoMessage("Localization en: using built-in English strings");
			}
			return;
		}
		string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "languages");
		string text2 = Path.Combine(path, "localization_" + text + ".json");
		if (!File.Exists(text2))
		{
			if (QuestJournalPlugin.Instance != null)
			{
				QuestJournalPlugin.Instance.LogInfoMessage("Localization " + text + ": file not found at " + text2);
			}
			return;
		}
		try
		{
			string json = File.ReadAllText(text2);
			ModLocalizationFile modLocalizationFile = JsonUtility.FromJson<ModLocalizationFile>(json);
			if (modLocalizationFile != null && modLocalizationFile.strings != null)
			{
				ModLocalizationRecord[] strings = modLocalizationFile.strings;
				foreach (ModLocalizationRecord modLocalizationRecord in strings)
				{
					if (modLocalizationRecord != null && !string.IsNullOrEmpty(modLocalizationRecord.key) && !string.IsNullOrEmpty(modLocalizationRecord.translation))
					{
						Translated[modLocalizationRecord.key] = modLocalizationRecord.translation;
					}
				}
			}
			if (Translated.Count == 0)
			{
				LoadFlatRecords(json);
			}
			if (QuestJournalPlugin.Instance != null)
			{
				QuestJournalPlugin.Instance.LogInfoMessage("Localization " + text + ": " + Translated.Count + " strings loaded from " + text2);
			}
		}
		catch (Exception ex)
		{
			if (QuestJournalPlugin.Instance != null)
			{
				QuestJournalPlugin.Instance.LogErrorMessage("Could not read mod localization: " + ex.Message);
			}
		}
	}

	internal static string T(string key)
	{
		if (Translated.TryGetValue(key, out var value))
		{
			return value;
		}
		if (!English.TryGetValue(key, out value))
		{
			return key;
		}
		return value;
	}

	internal static string T(string key, params object[] args)
	{
		return string.Format(T(key), args);
	}

	private static void LoadFlatRecords(string json)
	{
		foreach (Match item in Regex.Matches(json, "\\{\\s*\"key\"\\s*:\\s*\"(?<key>(?:\\\\.|[^\"\\\\])*)\"[\\s\\S]*?\"translation\"\\s*:\\s*\"(?<translation>(?:\\\\.|[^\"\\\\])*)\"\\s*\\}"))
		{
			string text = DecodeJsonString(item.Groups["key"].Value);
			string value = DecodeJsonString(item.Groups["translation"].Value);
			if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(value))
			{
				Translated[text] = value;
			}
		}
	}

	private static string DecodeJsonString(string value)
	{
		value = Regex.Replace(value, "\\\\u(?<code>[0-9a-fA-F]{4})", (Match match) => ((char)Convert.ToInt32(match.Groups["code"].Value, 16)).ToString());
		return value.Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\n", "\n")
			.Replace("\\r", "\r")
			.Replace("\\t", "\t")
			.Replace("\\\\", "\\");
	}

	private static string GetGameLocale()
	{
		string text = null;
		try
		{
			text = GJL.GetCurLng();
		}
		catch
		{
		}
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				text = GameSettings.GetCurrentLanguage();
			}
		}
		catch
		{
		}
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				text = GJL.GetCurrentLocaleCode();
			}
		}
		catch
		{
		}
		if (string.IsNullOrEmpty(text))
		{
			return "en";
		}
		text = text.Trim().ToLowerInvariant().Replace('_', '-');
		if (text == "ptbr" || text.StartsWith("pt-") || text.StartsWith("portugu"))
		{
			return "pt-br";
		}
		if (text.StartsWith("zh") || text.StartsWith("chinese"))
		{
			return "zh-cn";
		}
		int num = text.IndexOf('-');
		if (num > 0)
		{
			text = text.Substring(0, num);
		}
		if (text.Length <= 2)
		{
			return text;
		}
		return text.Substring(0, 2);
	}
}
