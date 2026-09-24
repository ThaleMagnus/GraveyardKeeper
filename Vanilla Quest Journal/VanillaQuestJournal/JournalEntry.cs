using UnityEngine;

namespace VanillaQuestJournal;

internal sealed class JournalEntry
{
	public string Key;

	public string NpcId;

	public string NpcName;

	public string FullText;

	public string ShortText;

	public string CharacterLocation;

	public string TaskLocation;

	public string Schedule;

	public string Availability;

	public string DlcLabel;

	public bool IsPresent;

	public bool IsCompleted;

	public Sprite Portrait;

	public Sprite DayIcon;
}
