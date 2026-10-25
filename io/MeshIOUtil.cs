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
