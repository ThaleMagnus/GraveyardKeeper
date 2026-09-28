using System;

namespace VanillaQuestJournal;

[Serializable]
public sealed class ModLocalizationRecord
{
	public string key = "";

	public string source = "";

	public string translation = "";
}
