using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VanillaQuestJournal;

internal sealed class QuestJournalPanel : BaseGameGUI
{
	private Vector2 _listScroll;

	private bool _scrollToSelection;

	private string _selectedKey;

	private bool _favoritesExpanded = true;

	private bool _activeExpanded = true;

	private bool _completedExpanded;

	private UIPanel _layoutHost;

	private UILabel _tabLabel;

	private List<JournalEntry> _entries = new List<JournalEntry>();

	private GUIStyle _title;

	private GUIStyle _section;

	private GUIStyle _currentTitle;

	private GUIStyle _listSection;

	private GUIStyle _sectionToggle;

	private GUIStyle _card;

	private GUIStyle _selectedCard;

	private GUIStyle _body;

	private GUIStyle _muted;

	private GUIStyle _inlineMuted;

	private GUIStyle _tag;

	private GUIStyle _dlcTag;

	private GUIStyle _button;

	private GUIStyle _choiceButton;

	private GUIStyle _choiceButtonOn;

	private GUIStyle _counter;

	private Texture2D _paper;

	private Texture2D _cardTexture;

	private Texture2D _selectedTexture;

	private Texture2D _panelTexture;

	private Texture2D _bluePanel;

	private Texture2D _buttonTexture;

	private Texture2D _buttonHoverTexture;

	private Texture2D _dividerTexture;

	protected override bool OnPressedUp()
	{
		return MoveSelection(-1);
	}

	protected override bool OnPressedDown()
	{
		return MoveSelection(1);
	}

	protected override bool OnPressedSelect()
	{
		return HandleControllerAction(GameKey.Select);
	}

	protected override bool OnPressedOption1()
	{
		return HandleControllerAction(GameKey.Option1);
	}

	protected override bool OnPressedOption2()
	{
		return HandleControllerAction(GameKey.Option2);
	}

	protected override bool OnPressedBack()
	{
		if (GUIElements.me != null && GUIElements.me.game_gui != null)
		{
			GUIElements.me.game_gui.OnClosePressed();
		}
		else
		{
			base.OnClosePressed();
		}
		return true;
	}

	private bool HandleControllerAction(GameKey key)
	{
		JournalEntry journalEntry = _entries.FirstOrDefault((JournalEntry e) => e.Key == _selectedKey);
		if (journalEntry == null)
		{
			return false;
		}
		if (key == QuestJournalPlugin.FavoriteGameKey)
		{
			if (!journalEntry.IsCompleted)
			{
				QuestJournalPlugin.ToggleTrackedTask(journalEntry.Key);
				Refresh();
				_scrollToSelection = true;
			}
			return true;
		}
		if (key == QuestJournalPlugin.NavigationGameKey)
		{
			if (journalEntry.IsPresent)
			{
				bool flag = QuestJournalPlugin.NavigationNpc.Value == journalEntry.NpcId;
				QuestJournalPlugin.NavigationNpc.Value = flag ? "" : journalEntry.NpcId;
			}
			return true;
		}
		return false;
	}

	private bool MoveSelection(int direction)
	{
		List<JournalEntry> visibleEntries = GetVisibleEntries();
		if (visibleEntries.Count == 0)
		{
			return true;
		}
		int num = visibleEntries.FindIndex((JournalEntry e) => e.Key == _selectedKey);
		if (num < 0)
		{
			num = ((direction < 0) ? 0 : (-1));
		}
		num = (num + direction + visibleEntries.Count) % visibleEntries.Count;
		_selectedKey = visibleEntries[num].Key;
		_scrollToSelection = true;
		return true;
	}

	private List<JournalEntry> GetVisibleEntries()
	{
		List<JournalEntry> list = _entries.Where((JournalEntry entry) => !entry.IsCompleted).ToList();
		List<JournalEntry> list2 = new List<JournalEntry>();
		if (_favoritesExpanded)
		{
			foreach (string trackedTaskKey in QuestJournalPlugin.GetTrackedTaskKeys())
			{
				JournalEntry journalEntry = list.FirstOrDefault((JournalEntry entry) => entry.Key == trackedTaskKey);
				if (journalEntry != null)
				{
					list2.Add(journalEntry);
				}
			}
		}
		if (_activeExpanded)
		{
			list2.AddRange(list.Where((JournalEntry entry) => !QuestJournalPlugin.IsTaskTracked(entry.Key)));
		}
		if (_completedExpanded)
		{
			list2.AddRange(_entries.Where((JournalEntry entry) => entry.IsCompleted));
		}
		return list2;
	}

	public override void OpenFromGameGUI()
	{
		base.OpenFromGameGUI();
		ModLocalization.Reload();
		QuestData.ReloadLocalizedData();
		if (_tabLabel != null)
		{
			_tabLabel.text = ModLocalization.T("tab.quests");
		}
		Refresh();
	}

	public override void UpdateLocalizedLabels()
	{
		ModLocalization.Reload();
		QuestData.ReloadLocalizedData();
		if (_tabLabel != null)
		{
			_tabLabel.text = ModLocalization.T("tab.quests");
		}
		if (base.is_shown)
		{
			Refresh();
		}
	}

	internal void SetTabLabel(UILabel tabLabel)
	{
		_tabLabel = tabLabel;
	}

	internal void SetLayoutHost(UIPanel layoutHost)
	{
		_layoutHost = layoutHost;
	}

	private void Refresh()
	{
		_entries = QuestData.ReadEntries();
		if (_entries.Count > 0 && (_selectedKey == null || !_entries.Any((JournalEntry e) => e.Key == _selectedKey)))
		{
			_selectedKey = _entries[0].Key;
		}
	}

	private void EnsureStyles()
	{
		if (!(_paper != null))
		{
			_paper = Solid(new Color(0f, 0f, 0f, 0f));
			_cardTexture = Solid(new Color(0.12f, 0.13f, 0.18f, 0.94f));
			_selectedTexture = Solid(new Color(0.43f, 0.29f, 0.12f, 0.86f));
			_panelTexture = Solid(new Color(0.04f, 0.05f, 0.08f, 0.18f));
			_buttonTexture = Solid(new Color(0.12f, 0.13f, 0.18f, 0.98f));
			_buttonHoverTexture = Solid(new Color(0.23f, 0.25f, 0.32f, 0.98f));
			_title = new GUIStyle(GUI.skin.label)
			{
				fontSize = 19,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleLeft
			};
			_title.normal.textColor = new Color(0.96f, 0.84f, 0.56f);
			_section = new GUIStyle(_title)
			{
				fontSize = 17,
				wordWrap = false
			};
			_body = new GUIStyle(GUI.skin.label)
			{
				fontSize = 15,
				wordWrap = true,
				richText = true
			};
			_body.normal.textColor = new Color(0.93f, 0.88f, 0.76f);
			_muted = new GUIStyle(_body)
			{
				fontSize = 13
			};
			_muted.normal.textColor = new Color(0.7f, 0.66f, 0.56f);
			_inlineMuted = new GUIStyle(_muted)
			{
				alignment = TextAnchor.MiddleLeft
			};
			_tag = new GUIStyle(_muted)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold
			};
			_tag.normal.textColor = new Color(0.98f, 0.78f, 0.3f);
			_dlcTag = new GUIStyle(_tag)
			{
				alignment = TextAnchor.MiddleLeft
			};
			_currentTitle = new GUIStyle(_section);
			_currentTitle.normal.textColor = _dlcTag.normal.textColor;
			_listSection = new GUIStyle(_section)
			{
				fontSize = 16,
				alignment = TextAnchor.MiddleLeft
			};
			_listSection.normal.textColor = _muted.normal.textColor;
			_sectionToggle = new GUIStyle(_listSection);
			_sectionToggle.normal.background = _paper;
			_sectionToggle.hover.background = _panelTexture;
			_sectionToggle.active.background = _selectedTexture;
			_sectionToggle.hover.textColor = _listSection.normal.textColor;
			_sectionToggle.active.textColor = _listSection.normal.textColor;
			_sectionToggle.focused.textColor = _listSection.normal.textColor;
			_sectionToggle.padding = new RectOffset(4, 4, 2, 2);
			_card = new GUIStyle(GUI.skin.button)
			{
				normal = 
				{
					background = _cardTexture,
					textColor = _body.normal.textColor
				},
				hover = 
				{
					background = _selectedTexture
				},
				alignment = TextAnchor.MiddleLeft,
				fontSize = 14,
				wordWrap = true,
				padding = new RectOffset(72, 10, 8, 8)
			};
			_selectedCard = new GUIStyle(_card);
			_selectedCard.normal.background = _selectedTexture;
			_button = new GUIStyle(GUI.skin.button)
			{
				fontSize = 14,
				fontStyle = FontStyle.Bold,
				fixedHeight = 34f
			};
			_button.normal.background = _buttonTexture;
			_button.normal.textColor = _body.normal.textColor;
			_button.hover.background = _buttonHoverTexture;
			_button.hover.textColor = Color.white;
			_button.active.background = _selectedTexture;
			_button.active.textColor = Color.white;
			_choiceButton = new GUIStyle(_button)
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = 14
			};
			_choiceButtonOn = new GUIStyle(_choiceButton);
			_choiceButtonOn.normal.background = _selectedTexture;
			_choiceButtonOn.normal.textColor = new Color(1f, 0.82f, 0.32f);
			_counter = new GUIStyle(_muted)
			{
				alignment = TextAnchor.MiddleRight,
				fixedHeight = 34f
			};
			Color textColor = _muted.normal.textColor;
			textColor.a = 1f;
			_dividerTexture = Solid(textColor);
			_bluePanel = LoadEmbeddedTexture("VanillaQuestJournal.Assets.blue_panel.png");
		}
	}

	private void OnGUI()
	{
		if (!base.is_shown)
		{
			return;
		}
		EnsureStyles();
		float num = Mathf.Clamp((float)Screen.height / 1080f, 0.72f, 1.25f);
		Matrix4x4 matrix = GUI.matrix;
		GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(num, num, 1f));
		Rect nativeContentScreenRect = GetNativeContentScreenRect();
		if (nativeContentScreenRect.width < 10f || nativeContentScreenRect.height < 10f)
		{
			GUI.matrix = matrix;
			return;
		}
		Rect rect = new Rect(nativeContentScreenRect.x / num, nativeContentScreenRect.y / num, nativeContentScreenRect.width / num, nativeContentScreenRect.height / num);
		float y = rect.y;
		float height = rect.height;
		float num2 = 8f;
		float num3 = Mathf.Clamp(520f, 360f, rect.width - num2 - 360f);
		Rect target = new Rect(rect.x, y, num3, height);
		Rect target2 = new Rect(target.xMax + num2, y, rect.width - num3 - num2, height);
		DrawSlicedTexture(_bluePanel, target, 58f, 58f, 18f, 18f, keepTransparentCorners: false);
		DrawSlicedTexture(_bluePanel, target2, 58f, 58f, 18f, 18f, keepTransparentCorners: false);
		Rect screenRect = new Rect(target.x + 18f, target.y + 15f, target.width - 36f, target.height - 30f);
		Rect screenRect2 = new Rect(target2.x + 18f, target2.y + 15f, target2.width - 36f, target2.height - 30f);
		GUILayout.BeginArea(screenRect);
		GUILayout.BeginHorizontal();
		GUILayout.Space(6f);
		GUILayout.Label(ModLocalization.T("journal.current"), _currentTitle, GUILayout.Width(Mathf.Max(140f, screenRect.width - 124f)));
		GUILayout.FlexibleSpace();
		int num4 = _entries.Count((JournalEntry entry) => !entry.IsCompleted);
		GUILayout.Label((num4 == 0) ? ModLocalization.T("journal.no_active") : ModLocalization.T("journal.active_count", num4), _counter, GUILayout.Width(100f));
		GUILayout.Space(6f);
		GUILayout.EndHorizontal();
		DrawList(screenRect.width, screenRect.height - 38f);
		GUILayout.EndArea();
		GUILayout.BeginArea(screenRect2);
		DrawDetails(screenRect2.width, screenRect2.height);
		GUILayout.EndArea();
		GUI.matrix = matrix;
	}

	private Rect GetNativeContentScreenRect()
	{
		if (_layoutHost == null)
		{
			return default(Rect);
		}
		Camera camera = _layoutHost.anchorCamera;
		if (camera == null)
		{
			camera = UICamera.mainCamera;
		}
		if (camera == null)
		{
			return default(Rect);
		}
		Vector3[] worldCorners = _layoutHost.worldCorners;
		if (worldCorners == null || worldCorners.Length == 0)
		{
			return default(Rect);
		}
		float num = float.MaxValue;
		float num2 = float.MinValue;
		float num3 = float.MaxValue;
		float num4 = float.MinValue;
		Vector3[] array = worldCorners;
		foreach (Vector3 position in array)
		{
			Vector3 vector = camera.WorldToScreenPoint(position);
			if (vector.z < 0f)
			{
				return default(Rect);
			}
			num = Mathf.Min(num, vector.x);
			num2 = Mathf.Max(num2, vector.x);
			num3 = Mathf.Min(num3, (float)Screen.height - vector.y);
			num4 = Mathf.Max(num4, (float)Screen.height - vector.y);
		}
		return Rect.MinMaxRect(num, num3, num2, num4);
	}

	private void DrawList(float width, float height)
	{
		GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));
		_listScroll = GUILayout.BeginScrollView(_listScroll, false, true);
		List<JournalEntry> activeEntries = _entries.Where((JournalEntry entry) => !entry.IsCompleted).ToList();
		List<JournalEntry> list = _entries.Where((JournalEntry entry) => entry.IsCompleted).ToList();
		List<JournalEntry> list2 = (from key in QuestJournalPlugin.GetTrackedTaskKeys()
			select activeEntries.FirstOrDefault((JournalEntry entry) => entry.Key == key) into entry
			where entry != null
			select entry).ToList();
		DrawCollapsibleSectionHeader(ModLocalization.T("section.favorites"), list2.Count, ref _favoritesExpanded);
		if (_favoritesExpanded)
		{
			if (list2.Count == 0)
			{
				GUILayout.Label(ModLocalization.T("empty.favorites"), _muted);
			}
			else
			{
				foreach (JournalEntry item in list2)
				{
					DrawTaskCard(item);
				}
			}
		}
		GUILayout.Space(8f);
		List<JournalEntry> list3 = activeEntries.Where((JournalEntry e) => !QuestJournalPlugin.IsTaskTracked(e.Key)).ToList();
		DrawCollapsibleSectionHeader(ModLocalization.T("section.all"), list3.Count, ref _activeExpanded);
		string text = null;
		if (_activeExpanded && list3.Count == 0)
		{
			GUILayout.Label(ModLocalization.T("empty.active"), _muted);
		}
		if (_activeExpanded)
		{
			foreach (JournalEntry item2 in list3)
			{
				if (text != item2.NpcId)
				{
					GUILayout.Space((text == null) ? 2 : 10);
					GUILayout.Label(item2.NpcName, _section);
					text = item2.NpcId;
				}
				DrawTaskCard(item2);
			}
		}
		GUILayout.Space(10f);
		DrawCollapsibleSectionHeader(ModLocalization.T("section.completed"), list.Count, ref _completedExpanded);
		text = null;
		if (_completedExpanded && list.Count == 0)
		{
			GUILayout.Label(ModLocalization.T("empty.completed"), _muted);
		}
		if (_completedExpanded)
		{
			foreach (JournalEntry item3 in list)
			{
				if (text != item3.NpcId)
				{
					GUILayout.Space((text == null) ? 2 : 10);
					GUILayout.Label(item3.NpcName, _section);
					text = item3.NpcId;
				}
				DrawTaskCard(item3);
			}
		}
		GUILayout.EndScrollView();
		GUILayout.EndVertical();
	}

	private void DrawCollapsibleSectionHeader(string title, int count, ref bool expanded)
	{
		string text = (expanded ? "▼  " : "▶  ");
		if (GUILayout.Button(text + title + "  (" + count + ")", _sectionToggle, GUILayout.Height(28f), GUILayout.ExpandWidth(expand: true)))
		{
			expanded = !expanded;
		}
		Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1f), GUILayout.ExpandWidth(expand: true));
		GUI.DrawTexture(rect, _dividerTexture);
		GUILayout.Space(5f);
	}

	private void DrawTaskCard(JournalEntry entry)
	{
		Rect rect = GUILayoutUtility.GetRect(new GUIContent(entry.ShortText), (entry.Key == _selectedKey) ? _selectedCard : _card, GUILayout.Height(68f), GUILayout.ExpandWidth(expand: true));
		if (_scrollToSelection && entry.Key == _selectedKey && Event.current.type == EventType.Repaint)
		{
			GUI.ScrollTo(rect);
			_scrollToSelection = false;
		}
		if (GUI.Button(rect, entry.ShortText, (entry.Key == _selectedKey) ? _selectedCard : _card))
		{
			_selectedKey = entry.Key;
		}
		DrawPortrait(entry.Portrait, new Rect(rect.x + 9f, rect.y + 8f, 52f, 52f));
		if (QuestJournalPlugin.IsTaskTracked(entry.Key))
		{
			GUI.Label(new Rect(rect.xMax - 26f, rect.y + 3f, 22f, 22f), "★", _tag);
		}
	}

	private void DrawDetails(float width, float height)
	{
		JournalEntry journalEntry = _entries.FirstOrDefault((JournalEntry e) => e.Key == _selectedKey);
		GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));
		if (journalEntry == null)
		{
			GUILayout.FlexibleSpace();
			GUILayout.Label(ModLocalization.T("details.select"), _body);
			GUILayout.FlexibleSpace();
			GUILayout.EndVertical();
			return;
		}
		GUILayout.BeginHorizontal();
		Rect rect = GUILayoutUtility.GetRect(86f, 86f, GUILayout.Width(86f), GUILayout.Height(86f));
		DrawPortrait(journalEntry.Portrait, rect);
		GUILayout.BeginVertical();
		GUILayout.Label(journalEntry.NpcName, _title);
		GUILayout.Label(journalEntry.Availability, _muted);
		GUILayout.BeginHorizontal();
		string text = ModLocalization.T("details.dlc_version");
		float x = _inlineMuted.CalcSize(new GUIContent(text)).x;
		GUILayout.Label(text, _inlineMuted, GUILayout.Width(x));
		GUILayout.Space(4f);
		GUILayout.Label(journalEntry.DlcLabel, _dlcTag, GUILayout.ExpandWidth(expand: false));
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.EndHorizontal();
		GUILayout.Space(10f);
		GUILayout.Label(ModLocalization.T("details.requirement"), _section);
		GUILayout.Label(journalEntry.FullText, _body, GUILayout.Width(width - 12f));
		GUILayout.Space(14f);
		GUILayout.Label(ModLocalization.T("details.location"), _section);
		GUILayout.Label(journalEntry.CharacterLocation, _body, GUILayout.Width(width - 12f));
		GUILayout.Space(10f);
		GUILayout.Label(ModLocalization.T("details.schedule"), _section);
		GUILayout.BeginHorizontal();
		GUILayout.Label(journalEntry.Schedule, _body, GUILayout.ExpandWidth(expand: false));
		if (journalEntry.DayIcon != null)
		{
			GUILayout.Space(6f);
			GUILayout.Label("(", _body, GUILayout.ExpandWidth(expand: false));
			Rect rect2 = GUILayoutUtility.GetRect(31f, 31f, GUILayout.Width(31f), GUILayout.Height(31f));
			DrawSprite(journalEntry.DayIcon, rect2);
			GUILayout.Label(")", _body, GUILayout.ExpandWidth(expand: false));
		}
		GUILayout.EndHorizontal();
		GUILayout.FlexibleSpace();
		bool flag = QuestJournalPlugin.IsTaskTracked(journalEntry.Key);
		bool flag2 = QuestJournalPlugin.NavigationNpc.Value == journalEntry.NpcId;
		if (!string.IsNullOrEmpty(QuestJournalPlugin.NavigationNpc.Value))
		{
			if (GUILayout.Button("✕  " + ModLocalization.T("button.disable_navigation"), _choiceButton, GUILayout.Height(27f), GUILayout.Width(width)))
			{
				QuestJournalPlugin.NavigationNpc.Value = "";
			}
			GUILayout.Space(6f);
		}
		GUILayout.BeginHorizontal();
		float width2 = Mathf.Max(80f, (width - 6f) * 0.5f);
		GUI.enabled = !journalEntry.IsCompleted;
		string text2 = (journalEntry.IsCompleted ? ("✓  " + ModLocalization.T("button.completed")) : (flag ? ("★  " + ModLocalization.T("button.favorite_on")) : ("☆  " + ModLocalization.T("button.favorite_add"))));
		if (GUILayout.Button(text2, flag ? _choiceButtonOn : _choiceButton, GUILayout.Width(width2), GUILayout.Height(38f)))
		{
			QuestJournalPlugin.ToggleTrackedTask(journalEntry.Key);
			Refresh();
		}
		GUI.enabled = true;
		GUILayout.Space(6f);
		GUI.enabled = journalEntry.IsPresent;
		if (GUILayout.Button("➤  " + ModLocalization.T(flag2 ? "button.navigation_on" : "button.navigation_enable"), flag2 ? _choiceButtonOn : _choiceButton, GUILayout.Width(width2), GUILayout.Height(38f)))
		{
			QuestJournalPlugin.NavigationNpc.Value = (flag2 ? "" : journalEntry.NpcId);
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private static void DrawPortrait(Sprite sprite, Rect rect)
	{
		GUI.Box(rect, GUIContent.none);
		if (!(sprite == null))
		{
			Rect textureRect = sprite.textureRect;
			Rect texCoords = new Rect(textureRect.x / (float)sprite.texture.width, textureRect.y / (float)sprite.texture.height, textureRect.width / (float)sprite.texture.width, textureRect.height / (float)sprite.texture.height);
			float num = rect.width - 12f;
			float num2 = rect.height - 4f;
			float num3 = Mathf.Min(num / textureRect.width, num2 / textureRect.height);
			float num4 = textureRect.width * num3;
			float num5 = textureRect.height * num3;
			Rect position = new Rect(rect.x + (rect.width - num4) * 0.5f, rect.y + (rect.height - num5) * 0.5f, num4, num5);
			GUI.DrawTextureWithTexCoords(position, sprite.texture, texCoords, alphaBlend: true);
		}
	}

	private static void DrawSprite(Sprite sprite, Rect rect)
	{
		if (!(sprite == null))
		{
			Rect textureRect = sprite.textureRect;
			GUI.DrawTextureWithTexCoords(texCoords: new Rect(textureRect.x / (float)sprite.texture.width, textureRect.y / (float)sprite.texture.height, textureRect.width / (float)sprite.texture.width, textureRect.height / (float)sprite.texture.height), position: rect, image: sprite.texture, alphaBlend: true);
		}
	}

	private static void DrawSlicedTexture(Texture2D texture, Rect target, float sourceX, float sourceY, float targetX, float targetY, bool keepTransparentCorners)
	{
		if (texture == null)
		{
			return;
		}
		sourceX = Mathf.Min(sourceX, (float)texture.width * 0.25f);
		sourceY = Mathf.Min(sourceY, (float)texture.height * 0.25f);
		targetX = Mathf.Min(targetX, target.width * 0.25f);
		targetY = Mathf.Min(targetY, target.height * 0.25f);
		float[] array = new float[4]
		{
			0f,
			sourceX,
			(float)texture.width - sourceX,
			texture.width
		};
		float[] array2 = new float[4]
		{
			0f,
			sourceY,
			(float)texture.height - sourceY,
			texture.height
		};
		float[] array3 = new float[4]
		{
			target.x,
			target.x + targetX,
			target.xMax - targetX,
			target.xMax
		};
		float[] array4 = new float[4]
		{
			target.y,
			target.y + targetY,
			target.yMax - targetY,
			target.yMax
		};
		for (int i = 0; i < 3; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				Rect position = new Rect(array3[j], array4[i], array3[j + 1] - array3[j], array4[i + 1] - array4[i]);
				int num = 2 - i;
				Rect texCoords = new Rect(array[j] / (float)texture.width, array2[num] / (float)texture.height, (array[j + 1] - array[j]) / (float)texture.width, (array2[num + 1] - array2[num]) / (float)texture.height);
				GUI.DrawTextureWithTexCoords(position, texture, texCoords, alphaBlend: true);
			}
		}
	}

	private static Texture2D LoadEmbeddedTexture(string resourceName)
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
			if (stream == null)
			{
				return null;
			}
			byte[] array = new byte[stream.Length];
			int num;
			for (int i = 0; i < array.Length; i += num)
			{
				num = stream.Read(array, i, array.Length - i);
				if (num <= 0)
				{
					break;
				}
			}
			Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
			if (!texture2D.LoadImage(array, markNonReadable: false))
			{
				return null;
			}
			texture2D.name = "VanillaQuestJournal_StoneBackground";
			texture2D.wrapMode = TextureWrapMode.Clamp;
			texture2D.filterMode = FilterMode.Bilinear;
			return texture2D;
		}
		catch (Exception ex)
		{
			QuestJournalPlugin.Instance.LogErrorMessage("Could not load embedded journal background: " + ex.Message);
			return null;
		}
	}

	private static Texture2D Solid(Color color)
	{
		Texture2D texture2D = new Texture2D(1, 1);
		texture2D.SetPixel(0, 0, color);
		texture2D.Apply();
		return texture2D;
	}
}
