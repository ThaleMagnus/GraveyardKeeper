using HarmonyLib;

namespace VanillaQuestJournal;

[HarmonyPatch(typeof(BaseGUI), "Hide")]
internal static class DialogClosedNavigationPatch
{
	private static void Postfix(BaseGUI __instance)
	{
		if (__instance is DialogGUI && QuestJournalPlugin.Instance != null)
		{
			QuestJournalPlugin.Instance.RefreshNavigationAfterDialogue();
		}
	}
}
