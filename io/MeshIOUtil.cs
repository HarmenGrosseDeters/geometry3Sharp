using System;
using System.Collections.Generic;

namespace g3 
{
	/// <summary>
	/// Helper functions shared by the mesh writers
	/// </summary>
	public static class MeshIOUtil 
	{
        /// <summary>
        /// A coordinate in invariant culture (NumberStyles.Float), with a value beyond the double range read as
        /// infinity of its sign on every framework: .NET Framework throws OverflowException for eg "1e400", .NET Core
        /// returns infinity. (ReadOptions.AllowNonFiniteCoordinates then decides.)
        /// </summary>
        internal static double ParseDouble(string s)
        {
            try {
                return double.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            } catch (OverflowException) {
                return s.TrimStart().StartsWith("-") ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
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
            try {
                return float.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            } catch (OverflowException) {
                return s.TrimStart().StartsWith("-") ? float.NegativeInfinity : float.PositiveInfinity;
            }
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
