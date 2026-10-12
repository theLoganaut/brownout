namespace TPSBR
{
	using UnityEngine;
	using Fusion.Addons.KCC;
	using Fusion.Addons.AnimationController;

	using AnimationState = Fusion.Addons.AnimationController.AnimationState;

	public sealed class FullBodyLayer : AnimationLayer
	{
		// PUBLIC MEMBERS

		public JumpState    Jump    => _jump;
		public FallState    Fall    => _fall;
		public LandState    Land    => _land;
		public DeadState    Dead    => _dead;
		public JetpackState Jetpack => _jetpack;

		// PRIVATE MEMBERS

		[SerializeField]
		private JumpState    _jump;
		[SerializeField]
		private FallState    _fall;
		[SerializeField]
		private LandState    _land;
		[SerializeField]
		private DeadState    _dead;
		[SerializeField]
		private JetpackState _jetpack;

		private KCC _kcc;

		// AnimationState INTERFACE

		protected override void OnInitialize()
		{
			_kcc = Controller.GetComponentNoAlloc<KCC>();
		}

		protected override void OnFixedUpdate()
		{
			KCCData kccData = _kcc.FixedData;

			if (kccData.HasJumped == true)
			{
				_jump.Activate(0.2f);
				return;
			}

			AnimationState activeState = GetActiveState();
			if (activeState == null)
			{
				// Logan, 9 Oct: stepping off something only plays Fall when it's a real fall (coming down, ground more than
				// MovementSettings.FallAnimationHeight below); short drops keep the locomotion
				if (kccData.IsGrounded == false && kccData.WasGrounded == false && IsRealFall(kccData) == true)
				{
					_fall.Activate(0.2f);
				}

				return;
			}

			if (activeState == _jump)
			{
				if (kccData.IsGrounded == true)
				{
					if (kccData.InputDirection.IsAlmostZero(0.1f) == true)
					{
						_land.Activate(0.2f);
					}
					else
					{
						_jump.Deactivate(0.1f);
					}
				}
				else if (_jump.IsFinished(1.0f) == true && IsRealFall(kccData) == true)
				{
					// A normal jump stays in the Jump take until landing; Fall only for a real fall (as the first-person arms)
					_fall.Activate(0.2f);
				}
			}
			else if (activeState == _fall)
			{
				if (kccData.IsGrounded == true)
				{
					if (kccData.InputDirection.IsAlmostZero(0.1f) == true)
					{
						_land.Activate(0.1f);
					}
					else
					{
						_fall.Deactivate(0.1f);
					}
				}
			}
			else if (activeState == _land)
			{
				if (kccData.IsGrounded == true)
				{
					if (kccData.InputDirection.IsAlmostZero(0.1f) == false)
					{
						_land.Deactivate(0.1f);
					}
				}
				if (_land.IsFinished(1.0f) == true)
				{
					_land.Deactivate(0.1f);
				}
			}
		}

		// PRIVATE METHODS

		/// <summary>Coming down with nothing to stand on within MovementSettings.FallAnimationHeight below the feet.</summary>
		private bool IsRealFall(KCCData kccData)
		{
			if (kccData.RealVelocity.y >= 0.0f)
				return false;

			float height = MovementSettings.Current.FallAnimationHeight;
			return Controller.Runner.GetPhysicsScene().Raycast(kccData.TargetPosition + Vector3.up * 0.1f, Vector3.down, height + 0.1f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false;
		}
	}
}
