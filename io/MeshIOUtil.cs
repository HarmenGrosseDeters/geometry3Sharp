using System;
using System.Collections.Generic;

namespace g3 
{
	/// <summary>
	/// Helper functions shared by the mesh writers
	/// </summary>
	public static class MeshIOUtil 
	{
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.NumberStyles Flt = System.Globalization.NumberStyles.Float;

        // Is the platform's double.Parse correctly rounded and does it keep the sign of "-0"? (.NET Core 3.0 and later: yes;
        // .NET Framework: no.) If not, ExactDecimal parses.
        static readonly bool PlatformDoubleParseIsExact = ProbeDoubleParse();
        static readonly bool PlatformFloatParseIsExact = ProbeFloatParse();

        /// <summary> (tests) use ExactDecimal on every framework, to test the readers' use of it where the platform parser is exact </summary>
        internal static volatile bool ForceExactDecimal;

        static bool ProbeDoubleParse()
        {
            try {
                return BitConverter.DoubleToInt64Bits(double.Parse("5.6348333975312060066e-8", Flt, Inv)) == 0x3e6e4074bfc9cfd2L
                    && BitConverter.DoubleToInt64Bits(double.Parse("-0", Flt, Inv)) == unchecked((long)0x8000000000000000UL)
                    && double.Parse("9007199254740993", Flt, Inv) == 9007199254740992.0                       // halfway: to even
                    && double.Parse("9007199254740993.00000000000000000001", Flt, Inv) == 9007199254740994.0;  // just above halfway
            } catch (Exception) {
                return false;
            }
        }

        static bool ProbeFloatParse()
        {
            try {
                return BitConverter.ToInt32(BitConverter.GetBytes(float.Parse("-0", Flt, Inv)), 0) == int.MinValue
                    && float.Parse("16777217", Flt, Inv) == 16777216f
                    && float.Parse("16777217.00000000000000000001", Flt, Inv) == 16777218f;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>
        /// A coordinate in invariant culture (NumberStyles.Float), with a value beyond the double range read as
        /// infinity of its sign on every framework: .NET Framework throws OverflowException for eg "1e400", .NET Core
        /// returns infinity. (ReadOptions.AllowNonFiniteCoordinates then decides.) Correctly rounded on every framework
        /// (.NET Framework's own parser is not, and reads "-0" as +0: ExactDecimal).
        /// </summary>
        internal static double ParseDouble(string s)
        {
            double v;
            if ((!PlatformDoubleParseIsExact || ForceExactDecimal) && ExactDecimal.TryParseDouble(s, out v)) return v;
            try {
                return double.Parse(s, Flt, Inv);
            } catch (OverflowException) {
                return s.TrimStart().StartsWith("-") ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        /// <summary> as <see cref="ParseDouble"/>, for float </summary>
        internal static float ParseFloat(string s)
        {
            float v;
            if ((!PlatformFloatParseIsExact || ForceExactDecimal) && ExactDecimal.TryParseFloat(s, out v)) return v;
            try {
                return float.Parse(s, Flt, Inv);
            } catch (OverflowException) {
                return s.TrimStart().StartsWith("-") ? float.NegativeInfinity : float.PositiveInfinity;
            }
        }

        /// <summary> as <see cref="ParseDouble"/>, false instead of an exception for text that is not a number </summary>
        internal static bool TryParseDouble(string s, out double value)
        {
            if ((!PlatformDoubleParseIsExact || ForceExactDecimal) && ExactDecimal.TryParseDouble(s, out value)) return true;
            if (double.TryParse(s, Flt, Inv, out value)) return true;
            value = 0;
            return false;
        }

		
		/// <summary>
		/// Returns the distinct materials of all WriteMesh.Materials lists, in first-seen order
		/// </summary>
		public static List<GenericMaterial> FindUniqueMaterialList(List<WriteMesh> meshes) 
		{
			List<GenericMaterial> unique = new List<GenericMaterial>();
			foreach (WriteMesh mesh in meshes) {
				if (mesh.Materials == null )
					continue;
				foreach ( GenericMaterial mat in mesh.Materials ) {
					if ( unique.Contains(mat) == false )
						unique.Add(mat);
				}
			}
			return unique;
		}
	}
}
