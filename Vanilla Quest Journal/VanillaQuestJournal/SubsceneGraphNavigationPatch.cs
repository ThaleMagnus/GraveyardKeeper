using HarmonyLib;

namespace VanillaQuestJournal;

[HarmonyPatch(typeof(SubsceneLoadManager), "UpdateGraph")]
internal static class SubsceneGraphNavigationPatch
{
	private static void Postfix()
	{
		if (QuestJournalPlugin.Instance != null)
		{
			QuestJournalPlugin.Instance.InvalidateNavigationTarget();
		}
	}
}
