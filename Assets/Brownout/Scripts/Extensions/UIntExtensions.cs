namespace TPSBR
{
	public static partial class UIntExtensions
	{
		// PUBLIC METHODS

		public static bool IsBitSet(this uint flags, int bit)
		{
			return (flags & (1u << bit)) == (1u << bit);
		}

		public static uint SetBit(ref this uint flags, int bit, bool value)
		{
			if (value == true)
			{
				return flags |= (1u << bit);
			}
			else
			{
				return flags &= ~(1u << bit);
			}
		}

		public static uint GetBits(this uint flags, int offset, int count)
		{
			return (flags >> offset) & ((1u << count) - 1u);
		}

		public static uint SetBits(ref this uint flags, int offset, int count, uint value)
		{
			uint mask = ((1u << count) - 1u) << offset;
			return flags = (flags & ~mask) | ((value << offset) & mask);
		}

		public static byte GetByte(this uint flags, int offset)
		{
			return (byte)flags.GetBits(offset, 8);
		}

		public static uint SetByte(ref this uint flags, int offset, byte value)
		{
			return flags.SetBits(offset, 8, value);
		}

		public static ushort GetUShort(this uint flags, int offset)
		{
			return (ushort)flags.GetBits(offset, 16);
		}

		public static uint SetUShort(ref this uint flags, int offset, ushort value)
		{
			return flags.SetBits(offset, 16, value);
		}

		public static short GetShort(this uint flags, int offset)
		{
			return (short)flags.GetBitsSigned(offset, 16);
		}

		public static uint SetShort(ref this uint flags, int offset, short value)
		{
			return flags.SetBitsSigned(offset, 16, value);
		}

		public static int GetBitsSigned(this uint flags, int offset, int count)
		{
			int value = (int)flags.GetBits(offset, count);
			int sign  = 1 << (count - 1);

			return value >= sign ? value - (sign << 1) : value;
		}

		public static uint SetBitsSigned(ref this uint flags, int offset, int count, int value)
		{
			return flags.SetBits(offset, count, (uint)(value & (int)((1u << count) - 1u)));
		}
	}
}
