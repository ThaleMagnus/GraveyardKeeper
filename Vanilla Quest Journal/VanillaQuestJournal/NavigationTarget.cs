using System;
using UnityEngine;

namespace VanillaQuestJournal;

internal sealed class NavigationTarget
{
	public readonly Transform Transform;

	private readonly GDPoint _loadedScenePoint;

	private readonly WorldGameObject _worldObject;

	private readonly bool _requiresNpcPresence;

	public bool IsUsable
	{
		get
		{
			if (Transform == null)
			{
				return false;
			}
			if (_worldObject != null)
			{
				if (!_requiresNpcPresence)
				{
					return QuestData.IsWorldObjectInLoadedScene(_worldObject);
				}
				return QuestData.IsNpcCurrentlyPresent(_worldObject);
			}
			return QuestData.IsGDPointInLoadedScene(_loadedScenePoint);
		}
	}

	public bool IsTeleport
	{
		get
		{
			if (_worldObject != null && !_requiresNpcPresence && !string.IsNullOrEmpty(_worldObject.custom_tag))
			{
				return _worldObject.custom_tag.StartsWith("tp_", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
	}

	public NavigationTarget(Transform transform, GDPoint loadedScenePoint, WorldGameObject worldObject = null, bool requiresNpcPresence = false)
	{
		Transform = transform;
		_loadedScenePoint = loadedScenePoint;
		_worldObject = worldObject;
		_requiresNpcPresence = requiresNpcPresence;
	}
}
