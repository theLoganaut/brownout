namespace TPSBR.UI
{
	public class MenuUI : SceneUI
	{
		// SceneUI INTERFACE

		protected override void OnInitializeInternal()
		{
			base.OnInitializeInternal();

			Context.Input.RequestCursorVisibility(true, ECursorStateSource.Menu);

			// Phase 11b: Brownout's main menu over the sample's; with Steam running the player's name is their Steam name
			BrownoutSteam steam = BrownoutSteam.Ensure();
			if (steam.IsAvailable == true && steam.PersonaName.HasValue() == true && Context.PlayerData.Nickname != steam.PersonaName)
			{
				Context.PlayerData.Nickname = steam.PersonaName;
			}
			BrownoutMenu.Create(Context, this);

			if (Context.PlayerData.Nickname.HasValue() == false)
			{
				var changeNicknameView = Open<UIChangeNicknameView>();
				changeNicknameView.SetData("ENTER NICKNAME", true);
			}
		}

		protected override void OnDeinitializeInternal()
		{
			Context.Input.RequestCursorVisibility(false, ECursorStateSource.Menu);

			base.OnDeinitializeInternal();
		}

		protected override void OnActivate()
		{
			base.OnActivate();

			if (Global.Networking.ErrorStatus.HasValue() == true)
			{
				// Phase 11b: the error over Brownout's main menu (the sample opened its session list here)
				var errorDialog = Open<UIErrorDialogView>();

				errorDialog.Title.text = "Connection Issue";

				if (Global.Networking.ErrorStatus == Networking.STATUS_SERVER_CLOSED)
				{
					errorDialog.Description.text = $"Server was closed.";
				}
				else
				{
					errorDialog.Description.text = $"Failed to start network game\n\nReason:\n{Global.Networking.ErrorStatus}";
				}

				Global.Networking.ClearErrorStatus();
			}
		}
	}
}
