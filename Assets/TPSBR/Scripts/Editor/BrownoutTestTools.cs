namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using Fusion;
	using Fusion.Addons.KCC;
	using UnityEditor;
	using UnityEngine;

	/// <summary>
	/// Play mode helpers for Brownout's 2-peer Multipeer tests. They act on the host (server) peer,
	/// so positions and look directions set here are authoritative.
	/// </summary>
	public static class BrownoutTestTools
	{
		private const string MENU = "Brownout/Test/";

		[MenuItem(MENU + "Put Players Face To Face", priority = 1)]
		private static void PutPlayersFaceToFaceMenu()
		{
			Debug.Log(PutPlayersFaceToFace(10.0f));
		}

		[MenuItem(MENU + "Put Players Face To Face", true)]
		private static bool ValidatePlayMode() => Application.isPlaying;

		[MenuItem(MENU + "Log Player Status", priority = 2)]
		private static void LogStatusMenu()
		{
			Debug.Log(GetStatus());
		}

		[MenuItem(MENU + "Log Player Status", true)]
		private static bool ValidatePlayMode2() => Application.isPlaying;

		// PHASE 2 TOOLS

		[MenuItem(MENU + "Give Weapon/Pistol_01",               priority = 20)] private static void GivePistol()    => Debug.Log(GiveWeapon("Pistol_01"));
		[MenuItem(MENU + "Give Weapon/Rifle_02",                priority = 21)] private static void GiveRifle()     => Debug.Log(GiveWeapon("Rifle_02"));
		[MenuItem(MENU + "Give Weapon/Sniper_01",               priority = 22)] private static void GiveSniper()    => Debug.Log(GiveWeapon("Sniper_01"));
		[MenuItem(MENU + "Give Weapon/ExplosiveGrenadeWeapon",  priority = 23)] private static void GiveExplosive() => Debug.Log(GiveWeapon("ExplosiveGrenadeWeapon"));
		[MenuItem(MENU + "Give Weapon/FlashGrenadeWeapon",      priority = 24)] private static void GiveFlash()     => Debug.Log(GiveWeapon("FlashGrenadeWeapon"));
		[MenuItem(MENU + "Give Weapon/SmokeGrenadeWeapon",      priority = 25)] private static void GiveSmoke()     => Debug.Log(GiveWeapon("SmokeGrenadeWeapon"));

		[MenuItem(MENU + "Give Weapon/Pistol_01", true)]
		private static bool ValidateGiveWeapon() => Application.isPlaying;

		[MenuItem(MENU + "Give Gun/AR/Icarus",             priority = 10)] private static void GiveIcarus() => Debug.Log(GiveGun("Icarus"));
		[MenuItem(MENU + "Give Gun/AR/Knocker",            priority = 11)] private static void GiveKnocker() => Debug.Log(GiveGun("Knocker"));
		[MenuItem(MENU + "Give Gun/AR/Rampage",            priority = 12)] private static void GiveRampage() => Debug.Log(GiveGun("Rampage"));
		[MenuItem(MENU + "Give Gun/AR/Maddog",             priority = 13)] private static void GiveMaddog() => Debug.Log(GiveGun("Maddog"));
		[MenuItem(MENU + "Give Gun/AR/Pacifier",           priority = 14)] private static void GivePacifier() => Debug.Log(GiveGun("Pacifier"));
		[MenuItem(MENU + "Give Gun/SMG/Splitter",          priority = 15)] private static void GiveSplitter() => Debug.Log(GiveGun("Splitter"));
		[MenuItem(MENU + "Give Gun/SMG/Grakken",           priority = 16)] private static void GiveGrakken() => Debug.Log(GiveGun("Grakken"));
		[MenuItem(MENU + "Give Gun/SMG/Savant",            priority = 17)] private static void GiveSavant() => Debug.Log(GiveGun("Savant"));
		[MenuItem(MENU + "Give Gun/SMG/Clinic",            priority = 18)] private static void GiveClinic() => Debug.Log(GiveGun("Clinic"));
		[MenuItem(MENU + "Give Gun/Tactical/Alibi DMR",    priority = 19)] private static void GiveAlibiDMR() => Debug.Log(GiveGun("Alibi DMR"));
		[MenuItem(MENU + "Give Gun/Tactical/URA Standard", priority = 20)] private static void GiveURAStandard() => Debug.Log(GiveGun("URA Standard"));
		[MenuItem(MENU + "Give Gun/Tactical/Swivel",       priority = 21)] private static void GiveSwivel() => Debug.Log(GiveGun("Swivel"));
		[MenuItem(MENU + "Give Gun/Tactical/Twofer",       priority = 22)] private static void GiveTwofer() => Debug.Log(GiveGun("Twofer"));
		[MenuItem(MENU + "Give Gun/Sniper/Pivotal",        priority = 23)] private static void GivePivotal() => Debug.Log(GiveGun("Pivotal"));
		[MenuItem(MENU + "Give Gun/Sniper/Omicron",        priority = 24)] private static void GiveOmicron() => Debug.Log(GiveGun("Omicron"));
		[MenuItem(MENU + "Give Gun/Sniper/Violet",         priority = 25)] private static void GiveViolet() => Debug.Log(GiveGun("Violet"));
		[MenuItem(MENU + "Give Gun/Sniper/Killnov",        priority = 26)] private static void GiveKillnov() => Debug.Log(GiveGun("Killnov"));

		[MenuItem(MENU + "Give Gun/AR/Icarus", true)]
		private static bool ValidateGiveGun() => Application.isPlaying;

		[MenuItem(MENU + "Roster Check", priority = 35)]
		private static void RosterCheckMenu() => Debug.Log(RunRosterCheck(null));

		[MenuItem(MENU + "Roster Check", true)]
		private static bool ValidateRosterCheck() => Application.isPlaying;

		[MenuItem(MENU + "Gunplay/Zero Spread", priority = 40)]
		private static void ToggleZeroSpread() { GunplayDebug.ZeroSpread = !GunplayDebug.ZeroSpread; UnityEditor.Menu.SetChecked(MENU + "Gunplay/Zero Spread", GunplayDebug.ZeroSpread); }
		[MenuItem(MENU + "Gunplay/No Recoil", priority = 41)]
		private static void ToggleNoRecoil() { GunplayDebug.NoRecoil = !GunplayDebug.NoRecoil; UnityEditor.Menu.SetChecked(MENU + "Gunplay/No Recoil", GunplayDebug.NoRecoil); }
		[MenuItem(MENU + "Gunplay/Iron Sights Count As Scoped (sway test)", priority = 42)]
		private static void ToggleScopedIrons() { GunplayDebug.ScopedIrons = !GunplayDebug.ScopedIrons; UnityEditor.Menu.SetChecked(MENU + "Gunplay/Iron Sights Count As Scoped (sway test)", GunplayDebug.ScopedIrons); }
		[MenuItem(MENU + "Gunplay/Log Shots and TTK", priority = 50)]
		private static void LogGunLogsMenu() { Debug.Log(GetShotLog(60) + "\n" + GetTtkLog()); }
		[MenuItem(MENU + "Gunplay/Clear Logs", priority = 51)]
		private static void ClearGunLogsMenu() { GunplayDebug.Clear(); }
		[MenuItem(MENU + "Gunplay/Log Gun Status", priority = 52)]
		private static void LogGunStatusMenu() { Debug.Log(GetGunStatus()); }

		[MenuItem(MENU + "God Mode", priority = 30)]
		private static void ToggleGodMode()
		{
			Health.DebugGodMode = !Health.DebugGodMode;
			Debug.Log($"God mode {(Health.DebugGodMode == true ? "on" : "off")}: players take no damage.");
		}

		[MenuItem(MENU + "God Mode", true)]
		private static bool ValidateGodMode()
		{
			UnityEditor.Menu.SetChecked(MENU + "God Mode", Health.DebugGodMode);
			return true;
		}

		[MenuItem(MENU + "Movement Readout", priority = 31)]
		private static void ToggleMovementReadout()
		{
			MovementReadout.Show = !MovementReadout.Show;
		}

		[MenuItem(MENU + "Movement Readout", true)]
		private static bool ValidateMovementReadout()
		{
			UnityEditor.Menu.SetChecked(MENU + "Movement Readout", MovementReadout.Show);
			return true;
		}

		[MenuItem(MENU + "Log Movement Status", priority = 32)]
		private static void LogMovementStatusMenu()
		{
			Debug.Log(GetMovementStatus());
		}

		[MenuItem(MENU + "Log Movement Status", true)]
		private static bool ValidatePlayMode3() => Application.isPlaying;

		/// <summary>
		/// Gives the observed player one of the sample's weapons (server side, equipped like a pickup).
		/// </summary>
		public static string GiveWeapon(string prefabName)
		{
			string guid  = AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets/TPSBR/Prefabs/Weapons" }).FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(t)) == prefabName);
			var    prefab = guid != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)) : null;
			var    weapon = prefab != null ? prefab.GetComponent<Weapon>() : null;
			if (weapon == null)
				return $"Weapon prefab {prefabName} not found.";

			Agent agent = GetServerAgentOfObserved();
			if (agent == null)
				return "No observed player on a running host.";

			return agent.Weapons.DebugGiveWeapon(weapon) == true ? $"Gave {prefabName} to {agent.name}." : "Couldn't give the weapon.";
		}

		/// <summary>
		/// Gives the observed player one of the Brownout guns built so far (GunplaySettings.TestGuns), like a pickup.
		/// </summary>
		public static string GiveGun(string gunName)
		{
			GunplaySettings settings = GunplaySettings.Current;
			Weapon          prefab   = settings.TestGuns != null ? settings.TestGuns.FirstOrDefault(t => t is BulletWeapon bullet && bullet.GunData != null && bullet.GunData.GunName == gunName) : null;
			if (prefab == null)
				return $"{gunName} isn't in GunplaySettings.TestGuns (run Brownout → Data → Import Guns).";

			Agent agent = GetServerAgentOfObserved();
			if (agent == null)
				return "No observed player on a running host.";

			return agent.Weapons.DebugGiveWeapon(prefab) == true ? $"Gave {gunName} to {agent.name}." : "Couldn't give the gun.";
		}

		public static string SetGunplayDebug(bool zeroSpread, bool noRecoil, bool scopedIrons)
		{
			GunplayDebug.ZeroSpread  = zeroSpread;
			GunplayDebug.NoRecoil    = noRecoil;
			GunplayDebug.ScopedIrons = scopedIrons;
			return $"Zero spread {zeroSpread}, no recoil {noRecoil}, scoped irons {scopedIrons}.";
		}

		public static string GetShotLog(int last)
		{
			int start = Mathf.Max(0, GunplayDebug.ShotLog.Count - last);
			return string.Join("\n", GunplayDebug.ShotLog.Skip(start));
		}

		public static string GetTtkLog()
		{
			return GunplayDebug.TtkLog.Count > 0 ? string.Join("\n", GunplayDebug.TtkLog) : "No kills logged.";
		}

		public static string ClearGunLogs()
		{
			GunplayDebug.Clear();
			return "Shot and TTK logs cleared.";
		}

		/// <summary>
		/// Holds the punch key, or switches primary slot once, for the peer with input (MCP tests).
		/// </summary>
		public static string SetDebugGunInput(bool melee, int switchSlot)
		{
			AgentInput.DebugHoldMelee  = melee;
			AgentInput.DebugSwitchSlot = switchSlot;
			return $"Melee {melee}, switch {switchSlot}.";
		}

		public static string GetGunStatus()
		{
			var builder = new StringBuilder();

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false)
					continue;

				Weapons weapons = agent.Weapons;
				builder.Append($"{agent.Runner.name} {agent.name} t{agent.Runner.Tick}: HP {agent.Health.CurrentHealth:F1}/{agent.Health.MaxHealth:F0}, slot {weapons.CurrentWeaponSlot}->{weapons.PendingWeaponSlot}");
				builder.Append($"{(weapons.IsDrawing ? " drawing" : "")}{(weapons.IsPunching ? " punching" : "")}");

				for (int slot = Weapons.FIRST_PRIMARY_SLOT; slot <= Weapons.LAST_PRIMARY_SLOT; ++slot)
				{
					var gun = weapons.GetWeapon(slot) as BulletWeapon;
					builder.Append(gun != null ? $", [{slot}] {gun.GunData.GunName} mag {gun.MagazineAmmo}/{gun.MaxMagazineAmmo}{(gun.IsReloading ? $" reloading {gun.ReloadElapsed:F2}s{(gun.IsAmmoIn ? " ammo-in" : "")}" : "")}" : $", [{slot}] empty");
				}

				builder.Append(", pools");
				for (int i = 0; i < GunData.CaliberCount; ++i)
				{
					builder.Append($" {GunData.GetCaliberName((ECaliber)i)}:{weapons.GetAmmo((ECaliber)i)}");
				}

				builder.AppendLine();
			}

			return builder.ToString();
		}

		/// <summary>
		/// Points the observed player (server and client copies) at a world point, e.g. a range mark.
		/// </summary>
		public static string AimObservedAt(Vector3 point)
		{
			Agent observed = GetServerAgentOfObserved();
			if (observed == null)
				return "No observed player on a running host.";

			int count = 0;
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority == observed.Object.InputAuthority)
				{
					Aim(agent, point);
					++count;
				}
			}

			return $"Aimed {count} copies of {observed.name} at {point.ToString("F2")}.";
		}

		public static string GetMovementStatus()
		{
			var builder = new StringBuilder();

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Object == null || agent.Object.IsValid == false)
					continue;

				KCC     kcc  = agent.Character.CharacterController;
				KCCData data = kcc.IsProxy == true ? kcc.RenderData : kcc.FixedData;
				var     readout = agent.GetComponent<MovementReadout>();
				string  role = agent.HasStateAuthority == true ? "server" : (agent.HasInputAuthority == true ? "local" : "proxy");

				builder.Append($"{agent.Runner.name} {agent.name} ({role}): mode {data.MoveMode}, stance {data.Stance}, chain {data.ChainStep}");
				builder.Append($", speed {data.RealVelocity.OnlyXZ().magnitude:F2}");
				if (readout != null)
				{
					builder.Append($" (peak {readout.PeakSpeed:F2}), last jump {readout.LastJumpApex:F2} m");
				}
				builder.Append($", eye {AgentMovement.GetEyeHeight(kcc, data):F2}, capsule {kcc.Settings.Height:F2} x r{kcc.Settings.Radius:F2}");
				builder.Append($", grounded {data.IsGrounded}, pos {agent.transform.position:F2}, HP {agent.Health.CurrentHealth:F0}");
				builder.AppendLine();
			}

			return builder.ToString();
		}

		/// <summary>
		/// Server-side teleport of the observed player, facing the given yaw. For setting up gym runs through the MCP.
		/// </summary>
		public static string PlaceObservedPlayer(Vector3 position, float yaw)
		{
			Agent agent = GetServerAgentOfObserved();
			if (agent == null)
				return "No observed player on a running host.";

			MoveTo(agent, position);

			KCC kcc = agent.Character.CharacterController;
			kcc.FixedData.LookYaw   = yaw;
			kcc.RenderData.LookYaw  = yaw;
			kcc.FixedData.LookPitch  = 0.0f;
			kcc.RenderData.LookPitch = 0.0f;

			return $"{agent.name} placed at {position:F1}, yaw {yaw:F0}.";
		}

		/// <summary>
		/// Holds keys for the peer with local input (editor test hooks in AgentInput). Pass zero move and false to release.
		/// </summary>
		public static string SetDebugInput(Vector2 move, bool sprint, bool crouch, bool jump, bool aim, bool attack)
		{
			AgentInput.DebugMoveDirection = move;
			AgentInput.DebugHoldSprint    = sprint;
			AgentInput.DebugHoldCrouch    = crouch;
			AgentInput.DebugHoldJump      = jump;
			AgentInput.DebugHoldAim       = aim;
			AgentInput.DebugHoldAttack    = attack;

			return $"Debug input: move {move}, sprint {sprint}, crouch {crouch}, jump {jump}, aim {aim}, attack {attack}.";
		}

		// SCRIPTED INPUT (for MCP-driven movement tests that need tighter timing than a round trip)

		private static readonly List<string> _scriptLog = new List<string>(1024);
		private static float[]  _scriptTimes;
		private static string[] _scriptSteps;
		private static int      _scriptIndex;
		private static double   _scriptStart;
		private static double   _scriptEnd;
		private static double   _nextSample;
		private static float    _sampleEvery;
		private static float    _maxPredictionError;
		private static int      _predictionErrorFrames;
		private static int      _scriptFrames;

		/// <summary>
		/// Plays timed debug input on the peer with local input and logs the observed player's movement.
		/// steps: "time:moveX,moveY,sprint,crouch,jump,aim[,attack,melee,switchSlot,reload];..." (seconds, 0/1 flags). Logs every sampleEvery seconds until duration.
		/// Read the log with GetScriptLog().
		/// </summary>
		public static string RunInputScript(string steps, float duration, float sampleEvery)
		{
			string[] parts = steps.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
			_scriptTimes = new float[parts.Length];
			_scriptSteps = new string[parts.Length];

			for (int i = 0; i < parts.Length; ++i)
			{
				string[] timeAndStep = parts[i].Split(':');
				_scriptTimes[i] = float.Parse(timeAndStep[0], System.Globalization.CultureInfo.InvariantCulture);
				_scriptSteps[i] = timeAndStep[1];
			}

			_scriptLog.Clear();
			_scriptIndex = 0;
			_scriptStart = EditorApplication.timeSinceStartup;
			_scriptEnd   = _scriptStart + duration;
			_nextSample  = _scriptStart;
			_sampleEvery = sampleEvery;
			_maxPredictionError    = 0.0f;
			_predictionErrorFrames = 0;
			_scriptFrames          = 0;

			EditorApplication.update -= UpdateInputScript;
			EditorApplication.update += UpdateInputScript;

			return $"Running {parts.Length} steps over {duration:F1} s.";
		}

		public static string GetScriptLog()
		{
			string header = $"client prediction error: max {_maxPredictionError:F3} m, frames over 0.25 m: {_predictionErrorFrames} of {_scriptFrames}";
			return (_scriptIndex >= 0 && EditorApplication.timeSinceStartup < _scriptEnd ? "RUNNING\n" : "DONE\n") + header + "\n" + string.Join("\n", _scriptLog);
		}

		private static void UpdateInputScript()
		{
			double now  = EditorApplication.timeSinceStartup;
			float  time = (float)(now - _scriptStart);

			while (_scriptIndex < _scriptSteps.Length && time >= _scriptTimes[_scriptIndex])
			{
				string[] v = _scriptSteps[_scriptIndex].Split(',');
				var inv = System.Globalization.CultureInfo.InvariantCulture;
				// Optional gun fields after aim: attack, melee (key 3), switch to slot (1/2, once), reload (R)
				SetDebugInput(new Vector2(float.Parse(v[0], inv), float.Parse(v[1], inv)), v[2] == "1", v[3] == "1", v[4] == "1", v[5] == "1", v.Length > 6 && v[6] == "1");
				AgentInput.DebugHoldMelee  = v.Length > 7 && v[7] == "1";
				AgentInput.DebugHoldReload = v.Length > 9 && v[9] == "1";
				if (v.Length > 8 && int.TryParse(v[8], out int switchSlot) == true && switchSlot > 0)
				{
					AgentInput.DebugSwitchSlot = switchSlot;
				}
				_scriptLog.Add($"{time:F2} input {_scriptSteps[_scriptIndex]}");
				++_scriptIndex;
			}

			// Prediction error of the client's own player: how far a server correction moved it this frame
			++_scriptFrames;
			foreach (Agent clientAgent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (clientAgent.Runner == null || clientAgent.Runner.IsServer == true || clientAgent.HasInputAuthority == false || clientAgent.Object == null || clientAgent.Object.IsValid == false)
					continue;

				float error = clientAgent.Character.CharacterController.PredictionError.magnitude;
				_maxPredictionError = Mathf.Max(_maxPredictionError, error);
				if (error > 0.25f)
				{
					++_predictionErrorFrames;
				}
			}

			if (now >= _nextSample)
			{
				_nextSample += _sampleEvery;

				Agent agent = GetServerAgentOfObserved();
				if (agent != null)
				{
					KCC     kcc  = agent.Character.CharacterController;
					KCCData data = kcc.FixedData;
					var     pos  = agent.transform.position;
					_scriptLog.Add($"{time:F2} t{agent.Runner.Tick.Raw} {data.MoveMode} {data.Stance} c{data.ChainStep} v{data.RealVelocity.OnlyXZ().magnitude:F2} y{pos.y:F2} z{pos.z:F2} x{pos.x:F2} g{(data.IsGrounded ? 1 : 0)} eye{data.EyeHeight:F2} h{kcc.Settings.Height:F2} HP{agent.Health.CurrentHealth:F0}");
				}
			}

			if (now >= _scriptEnd || EditorApplication.isPlaying == false)
			{
				SetDebugInput(Vector2.zero, false, false, false, false, false);
				AgentInput.DebugHoldMelee  = false;
				AgentInput.DebugHoldReload = false;
				EditorApplication.update -= UpdateInputScript;
				_scriptEnd = 0.0;
			}
		}

		/// <summary>
		/// Moves the other player onto clear level ground in front of the host's player and aims both at each other's chest.
		/// </summary>
		public static string PutPlayersFaceToFace(float distance)
		{
			if (TryGetHostAgents(out Agent shooter, out Agent target) == false)
				return "Need a running host with two players.";

			return PlaceFaceToFace(shooter, target, distance);
		}

		/// <summary>
		/// Server side: moves the target onto clear level ground in front of the shooter and aims the server copies at each other's chest.
		/// </summary>
		private static string PlaceFaceToFace(Agent shooter, Agent target, float distance)
		{
			PhysicsScene physics  = shooter.Runner.GetPhysicsScene();
			Vector3      basePos  = shooter.transform.position;
			Vector3      eye      = basePos + Vector3.up * 1.7f;
			Collider[]   overlaps = new Collider[4];

			float[] distances = { distance, distance * 0.7f, distance * 1.5f, distance * 0.5f };

			foreach (float tryDistance in distances)
			{
				for (int i = 0; i < 16; ++i)
				{
					float   yaw  = i * 22.5f;
					Vector3 spot = basePos + Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.forward * tryDistance;

					if (physics.Raycast(spot + Vector3.up * 3.0f, Vector3.down, out RaycastHit ground, 6.0f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == false)
						continue;
					if (Mathf.Abs(ground.point.y - basePos.y) > 0.3f)
						continue;

					spot = ground.point;

					Vector3 chest = spot + Vector3.up * 1.3f;
					Vector3 toChest = chest - eye;
					if (physics.Raycast(eye, toChest.normalized, toChest.magnitude - 0.5f, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) == true)
						continue;
					if (physics.OverlapCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.6f, 0.4f, overlaps, ObjectLayerMask.Environment, QueryTriggerInteraction.Ignore) > 0)
						continue;

					MoveTo(target, spot);
					Aim(shooter, chest);
					Aim(target, basePos + Vector3.up * 1.3f);

					return $"{target.name} placed {tryDistance:F1} m in front of {shooter.name}; both aim at each other's chest.";
				}
			}

			return "No clear level spot found around the host's player.";
		}

		/// <summary>
		/// Sets the host player's look pitch (positive = down). Useful for screenshots of the first-person body.
		/// </summary>
		public static string SetHostLookPitch(float pitch)
		{
			if (TryGetHostAgents(out Agent shooter, out Agent _) == false)
				return "Need a running host with two players.";

			KCC kcc = shooter.Character.CharacterController;
			kcc.FixedData.LookPitch  = pitch;
			kcc.RenderData.LookPitch = pitch;

			return $"{shooter.name} look pitch set to {pitch:F1}.";
		}

		public static string GetStatus()
		{
			var builder = new StringBuilder();

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				var firearm = agent.Weapons.CurrentWeapon as FirearmWeapon;

				builder.Append($"{agent.Runner.name} {agent.name}: HP {agent.Health.CurrentHealth:F1}, pos {agent.transform.position:F1}");
				builder.Append($", weapon {(agent.Weapons.CurrentWeapon != null ? agent.Weapons.CurrentWeapon.name : "none")}");

				if (firearm != null)
				{
					builder.Append($", mag {firearm.MagazineAmmo}, shots {firearm.ShotCount}");
				}

				if (agent.IsObserved == true)
				{
					builder.Append($", observed (camera FOV {agent.Context.Camera.Camera.fieldOfView:F2})");
				}

				builder.AppendLine();
			}

			return builder.ToString();
		}

		// ROSTER CHECK (phase 3b)

		private static readonly List<string> _rosterLog = new List<string>(64);
		private static IEnumerator<float>    _roster;
		private static double                _rosterWaitUntil;
		private static double                _rosterLongestFrame;

		/// <summary>An editor frame longer than this while the trigger is held counts as a stall (the client's inputs can be lost).</summary>
		private const float ROSTER_STALL = 0.25f;
		private const int   ROSTER_TRIES = 3;

		/// <summary>
		/// Runs every gun in GunplaySettings.TestGuns (or only the comma-separated names given) on the player of the peer with input:
		/// a full mag fired away from the other player (fire rate), then body shots at that player from 10 m (TTK).
		/// Zero spread and no recoil are on while it runs. Read the result with GetRosterLog(); it also goes to the console when done.
		/// </summary>
		public static string RunRosterCheck(string onlyGuns)
		{
			if (Application.isPlaying == false)
				return "Enter Play mode first (Movement Gym, 2 peers).";

			GunplaySettings settings = GunplaySettings.Current;
			var guns = new List<BulletWeapon>();
			foreach (Weapon weapon in settings.TestGuns ?? new Weapon[0])
			{
				var gun = weapon as BulletWeapon;
				if (gun == null || gun.GunData == null)
					continue;
				if (string.IsNullOrEmpty(onlyGuns) == false && onlyGuns.Split(',').Any(t => t.Trim() == gun.GunData.GunName) == false)
					continue;

				guns.Add(gun);
			}

			if (guns.Count == 0)
				return "No guns to check (run Brownout → Data → Import Guns).";

			_rosterLog.Clear();
			_roster          = RosterCheck(guns, settings);
			_rosterWaitUntil = 0.0;

			EditorApplication.update -= UpdateRosterCheck;
			EditorApplication.update += UpdateRosterCheck;

			return $"Roster check started for {guns.Count} guns; read it with GetRosterLog() or wait for the console.";
		}

		public static string GetRosterLog()
		{
			return (_roster != null ? "RUNNING\n" : "DONE\n") + string.Join("\n", _rosterLog);
		}

		private static void UpdateRosterCheck()
		{
			if (EditorApplication.isPlaying == false)
			{
				StopRosterCheck("Stopped: left Play mode.");
				return;
			}

			if (EditorApplication.timeSinceStartup < _rosterWaitUntil)
				return;

			bool running;
			try
			{
				running = _roster.MoveNext();
			}
			catch (System.Exception exception)
			{
				StopRosterCheck($"Stopped by an exception: {exception}");
				return;
			}

			if (running == false)
			{
				StopRosterCheck(null);
				return;
			}

			_rosterWaitUntil = EditorApplication.timeSinceStartup + _roster.Current;
		}

		private static void StopRosterCheck(string reason)
		{
			EditorApplication.update -= UpdateRosterCheck;
			_roster = null;

			AgentInput.DebugHoldAttack = false;
			GunplayDebug.ZeroSpread    = false;
			GunplayDebug.NoRecoil      = false;
			GameplayMode.DebugNoTeams  = false;

			if (reason != null)
			{
				_rosterLog.Add(reason);
			}

			Debug.Log("Roster check\n" + string.Join("\n", _rosterLog));
		}

		private static IEnumerator<float> RosterCheck(List<BulletWeapon> guns, GunplaySettings settings)
		{
			GunplayDebug.ZeroSpread = true;
			GunplayDebug.NoRecoil   = true;

			// Phase 4: the two players are teammates, so teams are off while it runs (no downed state, the other player can be shot)
			GameplayMode.DebugNoTeams = true;

			int passed = 0;

			foreach (BulletWeapon gun in guns)
			{
				GunData data = gun.GunData;
				string  name = data.GunName;

				// 1. Fire rate: a full mag, aimed away from the other player. Redone if the editor stalled mid-mag, because a
				//    stall drops the client's inputs (Fusion's "input buffer was full") and the trigger reads as released
				string rateLine   = null;
				bool   ratePassed = false;
				int    stalls     = 0;
				Agent  shooter;
				Agent  target;

				for (int attempt = 0; attempt < ROSTER_TRIES; ++attempt)
				{
					string give = GiveGunAndTopUp(gun, data, settings);
					if (give != null)
					{
						rateLine = give;
						break;
					}

					yield return 1.5f; // the pickup reaches the lagged client

					if (TryGetRosterAgents(out shooter, out target) == true)
					{
						Vector3 away = shooter.transform.position * 2.0f - target.transform.position;
						AimObservedAt(new Vector3(away.x, shooter.transform.position.y + 1.7f, away.z));
					}

					yield return 0.5f;

					GunplayDebug.Clear();
					float fireTime = data.Magazine * GetRosterShotSpacing(data) + 3.0f;
					foreach (float wait in HoldTrigger(data, fireTime, () => GetObservedMagazine() == 0))
						yield return wait;

					rateLine = MeasureFireRate(data, out ratePassed);

					yield return 1.0f;

					if (ratePassed == true || _rosterLongestFrame <= ROSTER_STALL)
						break;

					++stalls;
				}

				// 2. TTK: body shots at the other player from 10 m, with a fresh mag
				string ttkLine   = "TTK: skipped (no living target)";
				bool   ttkPassed = false;

				for (int i = 0; i < 40 && (TryGetRosterAgents(out shooter, out target) == false || target.Health.IsAlive == false); ++i)
					yield return 0.25f; // the target from the last gun is respawning

				if (TryGetRosterAgents(out shooter, out target) == true && target.Health.IsAlive == true)
				{
					GiveGunAndTopUp(gun, data, settings);
					yield return 1.5f;

					TryGetRosterAgents(out shooter, out target);
					PlaceFaceToFace(shooter, target, 10.0f);
					yield return 1.5f; // the lagged client settles after the teleport

					TryGetRosterAgents(out shooter, out target);
					AimObservedAt(target.transform.position + Vector3.up * 1.3f);
					yield return 0.5f;

					GunplayDebug.Clear();
					foreach (float wait in HoldTrigger(data, 5.0f, () => GunplayDebug.TtkLog.Count > 0))
						yield return wait;

					ttkLine = MeasureTtk(data, settings, out ttkPassed);

					if (ttkPassed == false && _rosterLongestFrame > ROSTER_STALL)
					{
						ttkLine += $" (editor stalled {_rosterLongestFrame:F1} s while firing; run this gun again)";
					}
				}

				bool gunPassed = ratePassed == true && ttkPassed == true;
				passed += gunPassed == true ? 1 : 0;
				string retried = stalls > 0 ? $" [fire rate redone {stalls}x after an editor stall]" : string.Empty;
				_rosterLog.Add($"{(gunPassed ? "PASS" : "FAIL")} {name} ({data.Class}, {data.FireMode}): {rateLine}; {ttkLine}{retried}");

				yield return 1.0f;
			}

			_rosterLog.Add($"{passed} of {guns.Count} guns pass (fire rate within 2%, TTK within one tick).");
		}

		/// <summary>Holds the trigger (auto, burst) or clicks every 0.1 s (semi, bolt) until done() or the time runs out.</summary>
		private static IEnumerable<float> HoldTrigger(GunData data, float duration, System.Func<bool> done)
		{
			double end   = EditorApplication.timeSinceStartup + duration;
			bool   click = data.FireMode == EFireMode.Semi || data.FireMode == EFireMode.Bolt;
			float  step  = click == true ? 0.05f : 0.1f;
			double last  = EditorApplication.timeSinceStartup;

			_rosterLongestFrame = 0.0;

			while (EditorApplication.timeSinceStartup < end && done() == false)
			{
				double now = EditorApplication.timeSinceStartup;
				_rosterLongestFrame = System.Math.Max(_rosterLongestFrame, now - last - step);
				last = now;

				AgentInput.DebugHoldAttack = click == false || AgentInput.DebugHoldAttack == false;
				yield return step;
			}

			AgentInput.DebugHoldAttack = false;
		}

		private static float GetRosterShotSpacing(GunData data)
		{
			return data.FireMode == EFireMode.Burst ? data.BurstCycle / Mathf.Max(1, data.BurstCount) + 0.02f : Mathf.Max(data.FireInterval, 0.1f) + 0.02f;
		}

		private static string GiveGunAndTopUp(BulletWeapon gun, GunData data, GunplaySettings settings)
		{
			Agent agent = GetServerAgentOfObserved();
			if (agent == null)
				return "no observed player on a running host";

			agent.Weapons.DebugSetAmmo(data.Caliber, settings.GetCarryCap(data.Caliber));
			return agent.Weapons.DebugGiveWeapon(gun) == true ? null : "couldn't give the gun";
		}

		private static int GetObservedMagazine()
		{
			Agent agent = GetServerAgentOfObserved();
			var   gun   = agent != null ? agent.Weapons.CurrentWeapon as BulletWeapon : null;
			return gun != null ? gun.MagazineAmmo : -1;
		}

		/// <summary>The server copies of the player with input (shooter) and the other player (target).</summary>
		private static bool TryGetRosterAgents(out Agent shooter, out Agent target)
		{
			shooter = GetServerAgentOfObserved();
			target  = null;

			if (shooter == null)
				return false;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent != shooter && agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.IsValid == true && agent.Object.InputAuthority.IsRealPlayer == true)
				{
					target = agent;
				}
			}

			return target != null;
		}

		private static List<float> GetFireTimes(string gunName)
		{
			var    times  = new List<float>(64);
			string prefix = $"FIRE {gunName} tick ";

			foreach (string line in GunplayDebug.ShotLog)
			{
				if (line.StartsWith(prefix) == false)
					continue;

				int start = line.IndexOf(" time ") + 6;
				int end   = line.IndexOf(" ms", start);
				times.Add(float.Parse(line.Substring(start, end - start), System.Globalization.CultureInfo.InvariantCulture));
			}

			return times;
		}

		private static string MeasureFireRate(GunData data, out bool passed)
		{
			List<float> times = GetFireTimes(data.GunName);
			passed = false;

			if (times.Count < 2)
				return $"fire rate: only {times.Count} shot(s) of {data.Magazine}";

			string shots = $"{times.Count}/{data.Magazine} shots";

			if (data.FireMode == EFireMode.Burst)
			{
				int   count    = Mathf.Max(1, data.BurstCount);
				float inBurst  = 0.0f;
				int   inCount  = 0;
				float cycles   = 0.0f;
				int   cycCount = 0;

				for (int i = 1; i < times.Count; ++i)
				{
					if (i % count != 0)
					{
						inBurst += times[i] - times[i - 1];
						++inCount;
					}
					else
					{
						cycles += times[i] - times[i - count];
						++cycCount;
					}
				}

				float inAverage    = inCount  > 0 ? inBurst / inCount  : 0.0f;
				float cycleAverage = cycCount > 0 ? cycles  / cycCount : 0.0f;
				float inError      = inAverage    / (data.FireInterval * 1000.0f) - 1.0f;
				float cycleError   = cycleAverage / (data.BurstCycle   * 1000.0f) - 1.0f;

				passed = times.Count == data.Magazine && Mathf.Abs(inError) <= 0.02f && Mathf.Abs(cycleError) <= 0.02f;
				return $"{shots}, in burst {inAverage:F1} ms (table {data.FireInterval * 1000.0f:F0}, {inError * 100.0f:+0.0;-0.0}%), burst cycle {cycleAverage:F1} ms (table {data.BurstCycle * 1000.0f:F0}, {cycleError * 100.0f:+0.0;-0.0}%)";
			}

			float average = (times[times.Count - 1] - times[0]) / (times.Count - 1);
			float error   = average / (data.FireInterval * 1000.0f) - 1.0f;

			float shortest = float.MaxValue;
			float longest  = 0.0f;
			for (int i = 1; i < times.Count; ++i)
			{
				shortest = Mathf.Min(shortest, times[i] - times[i - 1]);
				longest  = Mathf.Max(longest,  times[i] - times[i - 1]);
			}

			passed = times.Count == data.Magazine && Mathf.Abs(error) <= 0.02f;
			return $"{shots}, interval {average:F1} ms (table {data.FireInterval * 1000.0f:F0}, {error * 100.0f:+0.0;-0.0}%, shortest {shortest:F1}, longest {longest:F1})";
		}

		/// <summary>Computed TTK: time of the killing shot after the first, from the fire mode's schedule.</summary>
		public static float GetComputedTtk(GunData data, float health)
		{
			int last = GetShotsToKill(data, health) - 1;

			if (data.FireMode == EFireMode.Burst)
			{
				int count = Mathf.Max(1, data.BurstCount);
				return (last / count) * data.BurstCycle + (last % count) * data.FireInterval;
			}

			return last * data.FireInterval;
		}

		private static int GetShotsToKill(GunData data, float health)
		{
			return Mathf.CeilToInt(health / data.Damage - 0.0001f);
		}

		private static string MeasureTtk(GunData data, GunplaySettings settings, out bool passed)
		{
			float computed = GetComputedTtk(data, HealthSettings.Current.MaxHealth) * 1000.0f;
			int   shots    = GetShotsToKill(data, HealthSettings.Current.MaxHealth);
			passed = false;

			string line = GunplayDebug.TtkLog.Count > 0 ? GunplayDebug.TtkLog[0] : null;
			if (line == null)
				return $"TTK: no kill (computed {computed:F0} ms, {shots} shots; {GunplayDebug.ShotLog.Count(t => t.StartsWith("HIT "))} hits logged)";

			int   start    = line.IndexOf(": ") + 2;
			int   end      = line.IndexOf(" ms", start);
			float measured = float.Parse(line.Substring(start, end - start), System.Globalization.CultureInfo.InvariantCulture);

			passed = Mathf.Abs(measured - computed) <= 1000.0f / 60.0f + 0.5f;
			return $"TTK {measured:F1} ms (computed {computed:F0} ms, {shots} shots)";
		}

		// PRIVATE METHODS

		private static Agent GetServerAgentOfObserved()
		{
			Agent observed = null;
			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				// The player of the peer that has input (Numpad 1/2), which is the one you're looking through
				if (agent.Object != null && agent.Object.IsValid == true && agent.IsObserved == true && agent.Context.HasInput == true)
				{
					observed = agent;
					break;
				}
			}

			if (observed == null)
				return null;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner != null && agent.Runner.IsServer == true && agent.Object != null && agent.Object.InputAuthority == observed.Object.InputAuthority)
					return agent;
			}

			return null;
		}

		private static bool TryGetHostAgents(out Agent shooter, out Agent target)
		{
			shooter = null;
			target  = null;

			foreach (Agent agent in Object.FindObjectsByType<Agent>(FindObjectsSortMode.None))
			{
				if (agent.Runner == null || agent.Runner.IsServer == false)
					continue;

				if (agent.HasInputAuthority == true)
				{
					shooter = agent;
				}
				else
				{
					target = agent;
				}
			}

			return shooter != null && target != null;
		}

		private static void MoveTo(Agent agent, Vector3 position)
		{
			KCC kcc = agent.Character.CharacterController;

			kcc.FixedData.BasePosition    = position;
			kcc.FixedData.DesiredPosition = position;
			kcc.FixedData.TargetPosition  = position;
			kcc.RenderData.BasePosition    = position;
			kcc.RenderData.DesiredPosition = position;
			kcc.RenderData.TargetPosition  = position;

			kcc.SetPosition(position, true, false, false);
			agent.transform.position = position;
		}

		private static void Aim(Agent agent, Vector3 point)
		{
			KCC     kcc       = agent.Character.CharacterController;
			Vector3 direction = point - agent.Character.GetCameraTransform(false).Position;

			float yaw   = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			float pitch = -Mathf.Asin(direction.normalized.y) * Mathf.Rad2Deg;

			kcc.FixedData.LookYaw    = yaw;
			kcc.RenderData.LookYaw   = yaw;
			kcc.FixedData.LookPitch  = pitch;
			kcc.RenderData.LookPitch = pitch;
		}
	}
}
