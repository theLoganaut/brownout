namespace TPSBR
{
	using UnityEngine;

	public static class NetworkCompression
	{
		// CONSTANTS

		private const int   AXIS_BITS   = 10;
		private const int   AXIS_STEPS  = (1 << AXIS_BITS) - 1;
		private const float AXIS_CENTER = AXIS_STEPS * 0.5f;

		private const float PITCH_RANGE = 180.0f;
		private const float YAW_RANGE   = 360.0f;
		private const float ANGLE_STEPS = ushort.MaxValue;

		// PUBLIC METHODS

		public static uint PackDirection(Vector3 direction)
		{
			return direction == Vector3.zero ? default : PackOffset(direction, 1.0f);
		}

		public static Vector3 UnpackDirection(uint packed)
		{
			return packed == default ? Vector3.zero : UnpackOffset(packed, 1.0f);
		}

		public static uint PackOffset(Vector3 offset, float range)
		{
			float scale  = AXIS_CENTER / range;
			uint  packed = default;

			packed.SetBits( 0, AXIS_BITS, QuantizeAxis(offset.x, scale));
			packed.SetBits(10, AXIS_BITS, QuantizeAxis(offset.y, scale));
			packed.SetBits(20, AXIS_BITS, QuantizeAxis(offset.z, scale));

			return packed;
		}

		public static Vector3 UnpackOffset(uint packed, float range)
		{
			float scale = range / AXIS_CENTER;

			return new Vector3(DequantizeAxis(packed,  0, scale), DequantizeAxis(packed, 10, scale), DequantizeAxis(packed, 20, scale));
		}

		public static uint PackPitchYaw(float pitch, float yaw)
		{
			uint packed = default;

			packed.SetUShort( 0, (ushort)Mathf.RoundToInt((Mathf.Clamp(pitch, -90.0f, 90.0f) + 90.0f) / PITCH_RANGE * ANGLE_STEPS));
			packed.SetUShort(16, (ushort)Mathf.RoundToInt(Mathf.Repeat(yaw, YAW_RANGE) / YAW_RANGE * ANGLE_STEPS));

			return packed;
		}

		public static void UnpackPitchYaw(uint packed, out float pitch, out float yaw)
		{
			pitch = packed.GetUShort( 0) / ANGLE_STEPS * PITCH_RANGE - 90.0f;
			yaw   = packed.GetUShort(16) / ANGLE_STEPS * YAW_RANGE;
		}

		// PRIVATE METHODS

		private static uint QuantizeAxis(float value, float scale)
		{
			return (uint)Mathf.Clamp(Mathf.RoundToInt(value * scale) + (int)AXIS_CENTER, 0, AXIS_STEPS);
		}

		private static float DequantizeAxis(uint packed, int offset, float scale)
		{
			return ((int)packed.GetBits(offset, AXIS_BITS) - AXIS_CENTER) * scale;
		}
	}
}
