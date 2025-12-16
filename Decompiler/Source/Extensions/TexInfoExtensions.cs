using System;
using System.Numerics;

using LibBSP;

namespace Decompiler
{
    /// <summary>
    /// Helper class containing methods for working with <see cref="TextureInfo"/> objects.
    /// </summary>
    public static class TexInfoExtensions
    {

        /// <summary>
        /// Computes the canonical texture U/V axes used by Quake for a given face normal.
        ///
        /// Quake does NOT derive texture axes from an arbitrary plane basis. Instead,
        /// it selects from a small, hard-coded set of axis-aligned bases depending on
        /// the dominant component of the face normal (floor/ceiling, east/west, north/south).
        ///
        /// These axes define what "0 degrees rotation" means in the classic Quake MAP format.
        /// Any texture rotation stored in a BSP is measured relative to these base axes.
        ///
        /// This function replicates the behavior of TextureAxisFromPlane() in id's qbsp.
        ///  (Chnucki via Chattie)
        /// </summary>
        /// <param name="normal">Normalized face normal.</param>
        /// <param name="baseU">Resulting base U axis for zero rotation.</param>
        /// <param name="baseV">Resulting base V axis for zero rotation.</param>
        private static void ComputeQuakeBaseAxes(Vector3 normal, out Vector3 baseU, out Vector3 baseV)
        {
            Vector3 n = normal;

            // Floor or ceiling (normal pointing mostly up or down)
            if (Math.Abs(n.Z) > 0.999f)
            {
                baseU = new Vector3(1, 0, 0);
                baseV = new Vector3(0, -1, 0);
            }
            // Wall facing east or west (X dominates Y)
            else if (Math.Abs(n.X) > Math.Abs(n.Y))
            {
                baseU = new Vector3(0, 1, 0);
                baseV = new Vector3(0, 0, -1);
            }
            // Wall facing north or south (Y dominates X)
            else
            {
                baseU = new Vector3(1, 0, 0);
                baseV = new Vector3(0, 0, -1);
            }
        }

        /// <summary>
        /// Converts a BSP <see cref="TextureInfo"/> into a MAP-compatible form.
        ///
        /// BSP stores texture mapping as full 3D texture axes with scale baked into
        /// their lengths. Classic Quake MAP format, however, represents texture mapping
        /// using:
        ///   - Unit-length texture axes
        ///   - Explicit scale values
        ///   - A single rotation angle (integer degrees)
        ///
        /// This method reverses that reduction by:
        ///   1. Extracting scale from axis lengths
        ///   2. Normalizing the texture axes
        ///   3. Reconstructing the Quake-style rotation relative to canonical base axes
        ///   4. Adjusting translation for entity origin
        /// </summary>
        /// <param name="texInfo">Source BSP texture info.</param>
        /// <param name="worldPosition">
        /// World-space origin of the owning entity. For worldspawn, this is usually Vector3.Zero.
        /// BSP texture offsets are relative to entity origin and must be corrected.
        /// (Rotation added by Chnucki via Chattie)
        /// </param>
        /// <returns>
        /// A <see cref="TextureInfo"/> suitable for classic Quake MAP output.
        /// </returns>
        public static TextureInfo BSP2MAPTexInfo(this TextureInfo texInfo, Vector3 worldPosition)
        {
            // Extract scale from axis lengths.
            // BSP encodes scale implicitly as the inverse of axis magnitude.
            float uScale = 1.0f / texInfo.UAxis.Length();
            float vScale = 1.0f / texInfo.VAxis.Length();

            // Normalize texture axes.
            // MAP format assumes unit-length axes with scale applied separately.
            Vector3 uAxis = Vector3.Normalize(texInfo.UAxis);
            Vector3 vAxis = Vector3.Normalize(texInfo.VAxis);

            // Compute the face normal from the texture axes.
            // The handedness here matches Quake's texture space conventions.
            Vector3 normal = Vector3.Normalize(Vector3.Cross(uAxis, vAxis));

            // Determine Quake's canonical base axes for this face.
            // These define what "rotation = 0" means for this plane orientation.
            ComputeQuakeBaseAxes(normal, out Vector3 baseU, out Vector3 baseV);

            // Project the BSP U axis onto the Quake base axes.
            // This gives the rotation of the texture relative to Quake's zero-rotation basis.
            float x = Vector3.Dot(uAxis, baseU);
            float y = Vector3.Dot(uAxis, baseV);

            // Compute rotation angle in degrees.
            // The negative sign is required because Quake texture rotation is clockwise
            // when looking along the face normal, whereas atan2 assumes CCW rotation.
            float rotation = -(float)(Math.Atan2(y, x) * (180.0 / Math.PI));

            // Normalize to [0, 360).
            rotation = (rotation % 360.0f + 360.0f) % 360.0f;

            // BSP texture offsets are relative to the entity origin.
            // Subtract the world position projected onto the original (scaled) axes
            // to obtain MAP-compatible offsets.
            float uTranslate = texInfo.Translation.X - Vector3.Dot(texInfo.UAxis, worldPosition);
            float vTranslate = texInfo.Translation.Y - Vector3.Dot(texInfo.VAxis, worldPosition);

            // Classic Quake MAP format stores rotation as an integer.
            // Rounding removes floating-point noise introduced during compilation.
            int rotationInt = (int)Math.Round(rotation);

            //Chnu: Q2 sequence in the map file for reference: xoff yoff xscale yscale contflag surfflag Value

            

            // Construct MAP-compatible TextureInfo.
            return new TextureInfo(
                uAxis,
                vAxis,
                new Vector2(uTranslate, vTranslate),
                new Vector2(uScale, vScale),
                0, // chnu: for now! Flags are next on the todo and then this will be texInfo.Flags,
                -1,// texture index (resolved later)
                rotationInt  // texture rotation in degrees);
                );
        }



        /// <summary>
        /// Validates this <see cref="TextureInfo"/>. This will replace any <c>infinity</c> or <c>NaN</c>
        /// values with valid values to use.
        /// </summary>
        /// <param name="texInfo">The <see cref="TextureInfo"/> to validate.</param>
        /// <param name="plane">The <see cref="Plane"/> of the surface this <see cref="TextureInfo"/> is applied to.</param>
        public static void Validate(this TextureInfo texInfo, Plane plane)
        {
            // Validate texture scaling
            if (float.IsInfinity(texInfo.scale.X) || float.IsNaN(texInfo.scale.X) || texInfo.scale.X == 0)
            {
                texInfo.scale = new Vector2(1, texInfo.scale.Y);
            }
            if (float.IsInfinity(texInfo.scale.Y) || float.IsNaN(texInfo.scale.Y) || texInfo.scale.Y == 0)
            {
                texInfo.scale = new Vector2(texInfo.scale.X, 1);
            }
            // Validate translations
            if (float.IsInfinity(texInfo.Translation.X) || float.IsNaN(texInfo.Translation.X))
            {
                texInfo.Translation = new Vector2(0, texInfo.Translation.Y);
            }
            if (float.IsInfinity(texInfo.Translation.Y) || float.IsNaN(texInfo.Translation.Y))
            {
                texInfo.Translation = new Vector2(texInfo.Translation.X, 0);
            }
            // Validate axis components
            if (float.IsInfinity(texInfo.UAxis.X) || float.IsNaN(texInfo.UAxis.X) || float.IsInfinity(texInfo.UAxis.Y) || float.IsNaN(texInfo.UAxis.Y) || float.IsInfinity(texInfo.UAxis.Z) || float.IsNaN(texInfo.UAxis.Z) || texInfo.UAxis == Vector3.Zero)
            {
                texInfo.UAxis = TextureInfo.TextureAxisFromPlane(plane)[0];
            }
            if (float.IsInfinity(texInfo.VAxis.X) || float.IsNaN(texInfo.VAxis.X) || float.IsInfinity(texInfo.VAxis.Y) || float.IsNaN(texInfo.VAxis.Y) || float.IsInfinity(texInfo.VAxis.Z) || float.IsNaN(texInfo.VAxis.Z) || texInfo.VAxis == Vector3.Zero)
            {
                texInfo.VAxis = TextureInfo.TextureAxisFromPlane(plane)[1];
            }
            // Validate axes relative to plane ("Texture axis perpendicular to face")
            if (Math.Abs(Vector3.Dot(Vector3.Cross(texInfo.UAxis, texInfo.VAxis), plane.Normal)) < 0.01)
            {
                Vector3[] newAxes = TextureInfo.TextureAxisFromPlane(plane);
                texInfo.UAxis = newAxes[0];
                texInfo.VAxis = newAxes[1];
            }
        }

    }
}
