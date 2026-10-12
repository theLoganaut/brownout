using System;
using UnityEngine;
using Fusion;
using Fusion.Addons.KCC;

namespace TPSBR
{
	/// <summary>
	/// Basic platform which moves between waypoints. It must be executed first, before any player executes its movement.
	/// This script needs to be a KCC processor (IKCCProcessor, IKCCProcessorProvider) and implement IPlatform interface to be correctly tracked by PlatformProcessor.
	/// </summary>
	[RequireComponent(typeof(Rigidbody))]
	public class Elevator : ContextBehaviour, IPlatform, IKCCProcessor, IKCCProcessorProvider
	{
		// PROTECTED MEMBERS

		[Networked]
		private int _baseTick { get; set; }
		[Networked]
		protected Vector3 _basePosition { get; private set; }
		[Networked]
		protected Quaternion _baseRotation { get; private set; }
		[Networked]
		protected float _currentHeight { get; private set; }

		// PRIVATE MEMBERS

		[SerializeField]
		private float _height = -5f;
		[SerializeField]
		private float _speed = 1f;

		private Transform _transform;
		private Rigidbody _rigidbody;

		// PUBLIC METHODS

		public void OverrideHeight(float height)
		{
			_currentHeight  = height;
		}

		// NetworkBehaviour INTERFACE

		public override void Spawned()
		{
			if (HasStateAuthority == true)
			{
				_transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);

				_baseTick = Runner.Tick;
				_basePosition = position;
				_baseRotation = rotation;
				_currentHeight = _height;
			}
			else
			{
				// Read initial values.
				SetTransform(Runner.Tick);
			}

			if (ApplicationSettings.IsStrippedBatch == true)
			{
				gameObject.SetActive(false);
				return;
			}

			// By default FixedUpdateNetwork() is not executed on proxies.
			// We register callbacks to ensure the platform is predicted everywhere.
			// The platform is updated first because ElevatorManager has lower execution order.
			ElevatorManager manager = Runner.GetSingleton<ElevatorManager>();
			manager.OnFixedUpdateNetwork += OnFixedUpdateNetwork;
			manager.OnRender += OnRender;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			ElevatorManager manager = runner.GetSingleton<ElevatorManager>();
			manager.OnFixedUpdateNetwork -= OnFixedUpdateNetwork;
			manager.OnRender -= OnRender;
		}

		// IKCCProcessorProvider INTERFACE

		IKCCProcessor IKCCProcessorProvider.GetProcessor()
		{
			return this;
		}

		// MonoBehaviour INTERFACE

		private void Awake()
		{
			_transform = transform;
			_rigidbody = GetComponent<Rigidbody>();

			if (_rigidbody == null)
				throw new NullReferenceException($"GameObject {name} has missing Rigidbody component!");

			_rigidbody.isKinematic   = true;
			_rigidbody.useGravity    = false;
			_rigidbody.interpolation = RigidbodyInterpolation.None;
			_rigidbody.constraints   = RigidbodyConstraints.FreezeAll;
		}

		// PRIVATE METHODS

		private void OnFixedUpdateNetwork()
		{
			SetTransform(Runner.Tick);
		}

		private void OnRender()
		{
			SetTransform(Runner.Tick + Runner.LocalAlpha);
		}

		private void SetTransform(float tick)
		{
			float time = (tick - _baseTick) * Runner.DeltaTime;

			Vector3 position = CalculatePosition(time);

			_transform.SetPositionAndRotation(position, _baseRotation);

			_rigidbody.position = position;
			_rigidbody.rotation = _baseRotation;
		}

		private Vector3 CalculatePosition(float time)
		{
			Vector3 position = _basePosition;

			float absoluteHeight = Mathf.Abs(_currentHeight);
			if (absoluteHeight <= 0.0f)
				return _basePosition;

			float totalDistance = _speed * time;
			float distanceAlpha = (totalDistance % (absoluteHeight * 2.0f)) / absoluteHeight; // 0.0f - 2.0f

			if (distanceAlpha > 1.0f)
			{
				distanceAlpha = 2.0f - distanceAlpha; // 0.0f - 1.0f
			}

			return _basePosition + Vector3.up * distanceAlpha * _currentHeight;
		}
	}
}
