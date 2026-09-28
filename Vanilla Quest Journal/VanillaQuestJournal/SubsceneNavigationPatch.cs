using HarmonyLib;

namespace VanillaQuestJournal;

[HarmonyPatch(typeof(SubsceneLoadManager), "Unload")]
internal static class SubsceneNavigationPatch
{
	private static void Prefix()
	{
		if (QuestJournalPlugin.Instance != null)
		{
			QuestJournalPlugin.Instance.InvalidateNavigationTarget();
		}
	}
}
