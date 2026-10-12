namespace TPSBR
{
	using System.Collections.Generic;
	using System.Linq;
	using UnityEngine;

	/// <summary>
	/// The map's large shape (phase 8a, from Logan's height sketch): the height zones in References/map_layout.json, each an outline with a
	/// band [low, high], become a smooth height field. Between two zone lines the ground slopes evenly from one line's height to the next
	/// (5-15 between the 5 m and 15 m lines); a zone with no higher zone inside rises to its band's top away from its edge (hilltops), and
	/// one with no lower zone sinks to its band's bottom (the river valley). Rolling hills and bumps go on top in BrownoutMapBuilder.
	/// </summary>
	public sealed class BrownoutMapHeights
	{
		// PUBLIC MEMBERS

		public readonly int   Res;
		public readonly float Step;
		public readonly float Origin;

		// PRIVATE MEMBERS

		private readonly float[,] _height;   // [z, x]
		private readonly float[,] _river;    // Distance to the river's line (m)
		private readonly int[,]   _zone;     // Index into _bands

		private readonly List<Vector2> _bands = new List<Vector2>();

		// CONSTRUCTORS

		public BrownoutMapHeights(BrownoutMapData.Layout layout, BrownoutMapSettings s, float size, List<Vector2> riverLine)
		{
			Res    = Mathf.CeilToInt(size / s.HeightCell) + 1;
			Step   = size / (Res - 1);
			Origin = -size * 0.5f;

			_height = new float[Res, Res];
			_zone   = new int[Res, Res];

			// 1. Which band each cell is in: later zones are drawn over earlier ones
			_bands.Add(layout.DefaultBand);
			foreach (BrownoutMapData.HeightZone zone in layout.HeightZones)
			{
				int band = _bands.IndexOf(zone.Band);
				if (band < 0)
				{
					band = _bands.Count;
					_bands.Add(zone.Band);
				}

				for (int z = 0; z < Res; ++z)
				{
					for (int x = 0; x < Res; ++x)
					{
						if (Contains(zone.Outline, new Vector2(Origin + x * Step, Origin + z * Step)) == true)
						{
							_zone[z, x] = band;
						}
					}
				}
			}

			// 2. Distance from each cell to the nearest lower and higher band (by band bottom)
			float[] lows = _bands.Select(t => t.x).Distinct().OrderBy(t => t).ToArray();
			var below = new Dictionary<float, float[,]>();
			var above = new Dictionary<float, float[,]>();
			foreach (float low in lows)
			{
				below[low] = DistanceTo((x, z) => _bands[_zone[z, x]].x < low);
				above[low] = DistanceTo((x, z) => _bands[_zone[z, x]].x > low);
			}

			// 3. Heights: even slopes between zone lines, ramps up to hilltops and down into valleys
			float ramp = Mathf.Max(1.0f, s.BandRamp);
			for (int z = 0; z < Res; ++z)
			{
				for (int x = 0; x < Res; ++x)
				{
					Vector2 band  = _bands[_zone[z, x]];
					float   down  = below[band.x][z, x];
					float   up    = above[band.x][z, x];
					bool    hasDown = down < float.MaxValue;
					bool    hasUp   = up   < float.MaxValue;

					float t;
					if (hasDown == true && hasUp == true)
					{
						t = down / (down + up);
					}
					else if (hasDown == true)
					{
						t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(down / ramp));
					}
					else if (hasUp == true)
					{
						t = 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(up / ramp));
					}
					else
					{
						t = 0.5f;
					}

					_height[z, x] = Mathf.Lerp(band.x, band.y, t);
				}
			}

			// 4. Soften the bends where the slope changes at a zone line
			Blur(_height, Res, s.HeightBlur / Step);

			// 5. Distance to the river (the hills calm down near the water)
			var riverCells = new bool[Res, Res];
			for (int i = 0; i < riverLine.Count - 1; ++i)
			{
				int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(riverLine[i], riverLine[i + 1]) / (Step * 0.5f)));
				for (int j = 0; j <= pieces; ++j)
				{
					Vector2 p = Vector2.Lerp(riverLine[i], riverLine[i + 1], j / (float)pieces);
					int x = Mathf.RoundToInt((p.x - Origin) / Step);
					int z = Mathf.RoundToInt((p.y - Origin) / Step);
					if (x >= 0 && x < Res && z >= 0 && z < Res) riverCells[z, x] = true;
				}
			}
			_river = DistanceTo((x, z) => riverCells[z, x]);
		}

		// PUBLIC METHODS

		/// <summary>The large shape's height at a world X / Z (bilinear).</summary>
		public float Sample(float worldX, float worldZ)
		{
			float fx = Mathf.Clamp((worldX - Origin) / Step, 0.0f, Res - 1.001f);
			float fz = Mathf.Clamp((worldZ - Origin) / Step, 0.0f, Res - 1.001f);
			int   x  = (int)fx;
			int   z  = (int)fz;
			float tx = fx - x;
			float tz = fz - z;

			float a = Mathf.Lerp(_height[z,     x], _height[z,     x + 1], tx);
			float b = Mathf.Lerp(_height[z + 1, x], _height[z + 1, x + 1], tx);
			return Mathf.Lerp(a, b, tz);
		}

		/// <summary>Distance (m) from a world X / Z to the river's line (bilinear).</summary>
		public float RiverDistance(float worldX, float worldZ)
		{
			float fx = Mathf.Clamp((worldX - Origin) / Step, 0.0f, Res - 1.001f);
			float fz = Mathf.Clamp((worldZ - Origin) / Step, 0.0f, Res - 1.001f);
			int   x  = (int)fx;
			int   z  = (int)fz;
			float tx = fx - x;
			float tz = fz - z;

			float a = Mathf.Lerp(_river[z,     x], _river[z,     x + 1], tx);
			float b = Mathf.Lerp(_river[z + 1, x], _river[z + 1, x + 1], tx);
			return Mathf.Lerp(a, b, tz);
		}

		/// <summary>The band [low, high] at a world X / Z.</summary>
		public Vector2 BandAt(Vector2 world)
		{
			int x = Mathf.Clamp(Mathf.RoundToInt((world.x - Origin) / Step), 0, Res - 1);
			int z = Mathf.Clamp(Mathf.RoundToInt((world.y - Origin) / Step), 0, Res - 1);
			return _bands[_zone[z, x]];
		}

		// PRIVATE METHODS

		public static bool Contains(List<Vector2> outline, Vector2 point)
		{
			bool inside = false;
			for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
			{
				Vector2 a = outline[i];
				Vector2 b = outline[j];
				if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
				{
					inside = !inside;
				}
			}

			return inside;
		}

		/// <summary>Exact distance (m) from every cell to the nearest cell where target is true (Felzenszwalb's two-pass transform).</summary>
		private float[,] DistanceTo(System.Func<int, int, bool> target)
		{
			const double far = 1.0e12;
			var  squared = new double[Res, Res];
			bool any     = false;

			for (int z = 0; z < Res; ++z)
			{
				for (int x = 0; x < Res; ++x)
				{
					bool hit = target(x, z);
					squared[z, x] = hit == true ? 0.0 : far;
					any |= hit;
				}
			}

			var result = new float[Res, Res];
			if (any == false)
			{
				for (int z = 0; z < Res; ++z)
					for (int x = 0; x < Res; ++x)
						result[z, x] = float.MaxValue;

				return result;
			}

			var line = new double[Res];
			var done = new double[Res];
			for (int z = 0; z < Res; ++z)
			{
				for (int x = 0; x < Res; ++x) line[x] = squared[z, x];
				Transform1D(line, done, Res);
				for (int x = 0; x < Res; ++x) squared[z, x] = done[x];
			}
			for (int x = 0; x < Res; ++x)
			{
				for (int z = 0; z < Res; ++z) line[z] = squared[z, x];
				Transform1D(line, done, Res);
				for (int z = 0; z < Res; ++z) result[z, x] = (float)System.Math.Sqrt(done[z]) * Step;
			}

			return result;
		}

		private static void Transform1D(double[] f, double[] d, int n)
		{
			var v = new int[n];
			var k = new double[n + 1];
			int j = 0;
			v[0] = 0;
			k[0] = double.NegativeInfinity;
			k[1] = double.PositiveInfinity;

			for (int q = 1; q < n; ++q)
			{
				double s = ((f[q] + q * q) - (f[v[j]] + v[j] * v[j])) / (2.0 * q - 2.0 * v[j]);
				while (s <= k[j])
				{
					--j;
					s = ((f[q] + q * q) - (f[v[j]] + v[j] * v[j])) / (2.0 * q - 2.0 * v[j]);
				}

				++j;
				v[j]     = q;
				k[j]     = s;
				k[j + 1] = double.PositiveInfinity;
			}

			j = 0;
			for (int q = 0; q < n; ++q)
			{
				while (k[j + 1] < q) ++j;
				d[q] = (q - v[j]) * (q - v[j]) + f[v[j]];
			}
		}

		private static void Blur(float[,] h, int res, float sigma)
		{
			if (sigma <= 0.1f)
				return;

			int radius = Mathf.CeilToInt(sigma * 3.0f);
			var kernel = new float[radius * 2 + 1];
			float total = 0.0f;
			for (int i = -radius; i <= radius; ++i)
			{
				kernel[i + radius] = Mathf.Exp(-(i * i) / (2.0f * sigma * sigma));
				total += kernel[i + radius];
			}
			for (int i = 0; i < kernel.Length; ++i) kernel[i] /= total;

			var temp = new float[res, res];
			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					float sum = 0.0f;
					for (int i = -radius; i <= radius; ++i)
						sum += kernel[i + radius] * h[z, Mathf.Clamp(x + i, 0, res - 1)];
					temp[z, x] = sum;
				}
			}
			for (int z = 0; z < res; ++z)
			{
				for (int x = 0; x < res; ++x)
				{
					float sum = 0.0f;
					for (int i = -radius; i <= radius; ++i)
						sum += kernel[i + radius] * temp[Mathf.Clamp(z + i, 0, res - 1), x];
					h[z, x] = sum;
				}
			}
		}
	}
}
