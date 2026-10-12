using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace TPSBR
{
	public class NetworkObjectPool : INetworkObjectProvider
	{
		public SceneContext Context { get; set; }

		private Dictionary<NetworkPrefabId, Stack<NetworkObject>> _cached   = new Dictionary<NetworkPrefabId, Stack<NetworkObject>>(32);
		private Dictionary<NetworkObject, NetworkPrefabId>        _borrowed = new Dictionary<NetworkObject, NetworkPrefabId>();

		NetworkObjectAcquireResult INetworkObjectProvider.AcquireInstance(NetworkRunner runner, in NetworkObjectAcquireContext context, out NetworkObject result)
		{
			if (context.TypeId.IsSceneObject == true)
			{
				if (context.AttachableInstance == true)
				{
					result = context.AttachableInstance;
					return NetworkObjectAcquireResult.Success;
				}
				else
				{
					result = null;
					return NetworkObjectAcquireResult.Retry;
				}
			}

			if (context.TypeId.IsPrefab == false)
			{
				result = null;
				return NetworkObjectAcquireResult.Failed;
			}

			NetworkPrefabId prefabId = context.TypeId.AsPrefabId;

			if (_cached.TryGetValue(prefabId, out var objects) == false)
			{
				objects = _cached[prefabId] = new Stack<NetworkObject>();
			}

			if (objects.Count > 0)
			{
				var oldInstance = objects.Pop();
				_borrowed[oldInstance] = prefabId;

				oldInstance.SetActive(true);

				result = oldInstance;
				runner.MoveToRunnerScene(result);
				return NetworkObjectAcquireResult.Success;
			}

			NetworkObject original = runner.Config.PrefabTable.Load(prefabId, true);
			if (original == null)
			{
				result = default;
				return NetworkObjectAcquireResult.Failed;
			}

			NetworkObject instance = Object.Instantiate(original);
			_borrowed[instance] = prefabId;

			AssignContext(instance);

			for (int i = 0; i < instance.NestedObjects.Length; i++)
			{
				AssignContext(instance.NestedObjects[i]);
			}

			result = instance;
			runner.MoveToRunnerScene(result);
			return NetworkObjectAcquireResult.Success;
		}

		void INetworkObjectProvider.ReleaseInstance(NetworkRunner runner, in NetworkObjectReleaseContext context)
		{
			if (context.TypeId.IsPrefabNestedObject == true)
				return;

			NetworkObject instance = context.Object;
			if (instance == null)
				return;

			if (instance.NetworkTypeId.IsSceneObject == false && runner.IsShutdown == false)
			{
				if (_borrowed.Remove(instance, out var prefabID) == true)
				{
					_cached[prefabID].Push(instance);

					instance.SetActive(false);
					instance.transform.parent = null;
					instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
				}
				else
				{
					Object.Destroy(instance.gameObject);
				}
			}
			else
			{
				Object.Destroy(instance.gameObject);
			}
		}

		public NetworkPrefabId GetPrefabId(NetworkRunner runner, NetworkObjectGuid prefabGuid)
		{
			return runner.Prefabs.GetId(prefabGuid);
		}

		private void AssignContext(NetworkObject instance)
		{
			for (int i = 0, count = instance.NetworkedBehaviours.Length; i < count; i++)
			{
				if (instance.NetworkedBehaviours[i] is IContextBehaviour cachedBehaviour)
				{
					cachedBehaviour.Context = Context;
				}
			}
		}
	}
}
