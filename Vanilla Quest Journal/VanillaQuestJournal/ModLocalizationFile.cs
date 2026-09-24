using System;

namespace VanillaQuestJournal;

[Serializable]
public sealed class ModLocalizationFile
{
	public string locale = "";

	public ModLocalizationRecord[] strings = new ModLocalizationRecord[0];
}
