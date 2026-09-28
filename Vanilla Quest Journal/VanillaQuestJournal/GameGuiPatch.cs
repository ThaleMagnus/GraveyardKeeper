using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VanillaQuestJournal;

[HarmonyPatch(typeof(GameGUI), "Init")]
internal static class GameGuiPatch
{
	private static readonly FieldInfo TabsField = AccessTools.Field(typeof(GameGUI), "_tabs");

	private static readonly FieldInfo GuiTabsField = AccessTools.Field(typeof(GameGUI), "TABS");

	private static readonly FieldInfo GridField = AccessTools.Field(typeof(GameGUI), "_tabs_grid");

	private static void Postfix(GameGUI __instance)
	{
		try
		{
			Dictionary<GameGUI.TabType, GameTabItemGUI> dictionary = (Dictionary<GameGUI.TabType, GameTabItemGUI>)TabsField.GetValue(__instance);
			Dictionary<GameGUI.TabType, BaseGameGUI> dictionary2 = (Dictionary<GameGUI.TabType, BaseGameGUI>)GuiTabsField.GetValue(__instance);
			if (!dictionary.ContainsKey(GameGUI.TabType.Bodies))
			{
				GameObject gameObject = new GameObject("QuestJournalPanel");
				gameObject.transform.SetParent(__instance.transform, worldPositionStays: false);
				QuestJournalPanel questJournalPanel = gameObject.AddComponent<QuestJournalPanel>();
				NPCsListGUI nPCsListGUI = dictionary2[GameGUI.TabType.NPCs] as NPCsListGUI;
				questJournalPanel.SetLayoutHost((nPCsListGUI == null || nPCsListGUI.scroll == null) ? null : nPCsListGUI.scroll.panel);
				questJournalPanel.Init();
				questJournalPanel.Hide(play_hide_sound: false);
				GameTabItemGUI gameTabItemGUI = dictionary[GameGUI.TabType.NPCs];
				GameObject gameObject2 = UnityEngine.Object.Instantiate(gameTabItemGUI.gameObject, gameTabItemGUI.transform.parent);
				gameObject2.name = "QuestJournal";
				gameObject2.transform.SetSiblingIndex(gameTabItemGUI.transform.GetSiblingIndex() + 1);
				GameTabItemGUI component = gameObject2.GetComponent<GameTabItemGUI>();
				component.Init(GameGUI.TabType.Bodies, questJournalPanel);
				LocalizedLabel component2 = gameObject2.GetComponent<LocalizedLabel>();
				if (component2 != null)
				{
					UnityEngine.Object.Destroy(component2);
				}
				UILabel componentInChildren = gameObject2.GetComponentInChildren<UILabel>(includeInactive: true);
				if (componentInChildren != null)
				{
					componentInChildren.text = ModLocalization.T("tab.quests");
				}
				questJournalPanel.SetTabLabel(componentInChildren);
				Dictionary<GameGUI.TabType, BaseGameGUI> dictionary3 = new Dictionary<GameGUI.TabType, BaseGameGUI>();
				Dictionary<GameGUI.TabType, GameTabItemGUI> dictionary4 = new Dictionary<GameGUI.TabType, GameTabItemGUI>();
				GameGUI.TabType[] array = new GameGUI.TabType[3]
				{
					GameGUI.TabType.Inventory,
					GameGUI.TabType.Techs,
					GameGUI.TabType.NPCs
				};
				foreach (GameGUI.TabType key in array)
				{
					dictionary3.Add(key, dictionary2[key]);
					dictionary4.Add(key, dictionary[key]);
				}
				dictionary3.Add(GameGUI.TabType.Bodies, questJournalPanel);
				dictionary4.Add(GameGUI.TabType.Bodies, component);
				dictionary3.Add(GameGUI.TabType.Map, dictionary2[GameGUI.TabType.Map]);
				dictionary4.Add(GameGUI.TabType.Map, dictionary[GameGUI.TabType.Map]);
				GuiTabsField.SetValue(__instance, dictionary3);
				TabsField.SetValue(__instance, dictionary4);
				UITable uITable = (UITable)GridField.GetValue(__instance);
				if (uITable != null)
				{
					uITable.Reposition();
				}
				QuestJournalPlugin.Instance.LogInfoMessage("Quest tab attached after NPC tab");
			}
		}
		catch (Exception ex)
		{
			QuestJournalPlugin.Instance.LogErrorMessage("Could not attach quest tab: " + ex);
		}
	}
}
