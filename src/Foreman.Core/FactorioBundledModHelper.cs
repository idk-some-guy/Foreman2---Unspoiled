using System.IO;
using System.Linq;

namespace Foreman {
    internal static class FactorioBundledModHelper {
        public static void CopyToModsFolder(string bundledModFolderName, string modsPath, params string[] relativeFiles) {
            string destDir = Path.Combine(modsPath, bundledModFolderName);
            Directory.CreateDirectory(destDir);
            foreach (string file in relativeFiles)
                File.Copy(Path.Combine(AppPaths.ExecutableDirectory, "Mods", bundledModFolderName, file), Path.Combine(destDir, file), overwrite: true);
        }

        public static void DeleteInstalledCopies(string modsPath, string folderPrefix) {
            if (!Directory.Exists(modsPath))
                return;
            foreach (string folder in Directory.EnumerateDirectories(modsPath, folderPrefix + "*").ToList())
                Directory.Delete(folder, true);
        }
    }
}
