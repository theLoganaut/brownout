namespace TPSBR
{
	using Fusion.Addons.KCC;
	using Fusion.Addons.AnimationController;

	public sealed class EquipState : MultiClipState
	{
		// PRIVATE MEMBERS

		private KCC     _kcc;
		private Weapons _weapons;

		// MultiClipState INTERFACE

		protected override int GetClipID()
		{
			// The clip set follows the gun's own type (pistol or rifle), not the slot it's carried in
			int pendingWeaponSlot = _weapons.PendingWeapon != null ? _weapons.PendingWeapon.WeaponSlot : _weapons.PendingWeaponSlot;
			if (pendingWeaponSlot > 2)
			{
				pendingWeaponSlot = 1; // For grenades we use pistol set
			}

			if (pendingWeaponSlot < 0)
				return 0;

			return pendingWeaponSlot;
		}

		// AnimationState INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			_kcc     = Controller.GetComponentNoAlloc<KCC>();
			_weapons = Controller.GetComponentNoAlloc<Weapons>();
		}
	}
}
