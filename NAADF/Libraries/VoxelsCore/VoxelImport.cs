using System.IO;

namespace Voxels {
    public class VoxelImport {
        public static VoxelData Import(string path) {
            using (var stream = File.OpenRead(path)) {
                switch (Path.GetExtension(path).ToLowerInvariant()) {
                case ".vox": return VoxFile.Read(stream);
                }
            }
            return null;
        }
    }
}
