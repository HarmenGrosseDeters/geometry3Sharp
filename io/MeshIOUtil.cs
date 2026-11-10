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

        // .NET Core 3.0 and later: "R" is the shortest string that reads back to the same value; .NET Framework: 15 digits, then 17
        // (and not always exact, see HeightGridWriter's note)
        static readonly bool DoubleRIsShortest = (1.0 / 3.0).ToString("R", Inv) == "0.3333333333333333";
        static readonly bool FloatRIsShortest = (1.0f / 3.0f).ToString("R", Inv) == "0.33333334";

        /// <summary>
        /// The text of a double that reads back to the same value, in invariant culture, on every framework: the shortest
        /// round-trip string where "R" is that (.NET Core 3.0 and later), else the first of 15, 16 and 17 significant digits
        /// that reads back exactly (.NET Framework, where "R" and the default format keep 15 digits and lose bits). Not a
        /// number is written as the platform does ("NaN", "Infinity").
        /// </summary>
        public static string FormatDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return v.ToString(Inv);
            if (DoubleRIsShortest) return v.ToString("R", Inv);
            return FormatDoubleByDigits(v);
        }

        // the first of 15, 16 and 17 significant digits that reads back exactly (the .NET Framework path; internal for the tests, which run it on every framework)
        internal static string FormatDoubleByDigits(double v)
        {
            if (v == 0) return (1 / v < 0) ? "-0" : "0";        // (G15 of -0 is "0" on .NET Framework: the sign is kept)
            string s = v.ToString("G15", Inv);
            if (ReadsBack(s, v)) return s;
            s = v.ToString("G16", Inv);
            if (ReadsBack(s, v)) return s;
            return v.ToString("G17", Inv);
        }

        // (.NET Framework throws OverflowException for a string above double.MaxValue, eg G15 of double.MaxValue: not equal)
        static bool ReadsBack(string s, double v)
        {
            try {
                return double.Parse(s, System.Globalization.NumberStyles.Float, Inv) == v;
            } catch (OverflowException) {
                return false;
            }
        }

        /// <summary> as <see cref="FormatDouble"/>, for float (7, 8 or 9 significant digits) </summary>
        public static string FormatFloat(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return v.ToString(Inv);
            if (FloatRIsShortest) return v.ToString("R", Inv);
            return FormatFloatByDigits(v);
        }

        internal static string FormatFloatByDigits(float v)
        {
            if (v == 0) return (1 / v < 0) ? "-0" : "0";
            string s = v.ToString("G7", Inv);
            if (ReadsBack(s, v)) return s;
            s = v.ToString("G8", Inv);
            if (ReadsBack(s, v)) return s;
            return v.ToString("G9", Inv);
        }

        static bool ReadsBack(string s, float v)
        {
            try {
                return float.Parse(s, System.Globalization.NumberStyles.Float, Inv) == v;
            } catch (OverflowException) {
                return false;
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
