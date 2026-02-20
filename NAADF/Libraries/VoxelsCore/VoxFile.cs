using System.IO;

namespace Voxels {
    /// <summary>
    /// Support for loading either MagicVoxel or Voxlap .VOX files.
    /// </summary>
    public class VoxFile {
        public static VoxelData Read(Stream stream) {
            var magicaVoxel = new MagicaVoxel();
            if (magicaVoxel.Read(stream)) {
                return magicaVoxel.Flatten(); 
            }
            else {
                stream.Seek(0, SeekOrigin.Begin);
                return Voxlap.Read(stream);
            }
        }
    }
}
