using System;
using System.IO;
using System.Linq;
using System.Text;
using ZstdSharp;

namespace Foreman.DataCaching.Storage {
    //Persists a preset's base pjson (zstd-compressed) and its optional presetCustom overlay as
    //separate columns, so a changed overlay never requires re-importing the base. The overlay MERGE
    //itself stays at the consumer (PresetJson.MergePresetOverlay in PresetProcessor).
    public static class PresetStore {
        private const int DefaultCompressionLevel = 19;

        public static void Import(ForemanDbContext db, string presetName, string pjsonText, string? customOverlayJson) {
            PresetDataRow? row = db.Presets.Find(presetName);
            if (row is null) {
                row = new PresetDataRow { PresetName = presetName };
                db.Presets.Add(row);
            }

            row.CompressedPJson = Compress(pjsonText);
            row.CustomOverlayJson = customOverlayJson;
            row.ImportedUtc = DateTime.UtcNow;

            db.SaveChanges();
        }

        public static (string PJsonText, string? CustomOverlayJson) Read(ForemanDbContext db, string presetName) {
            PresetDataRow row = db.Presets.Single(r => r.PresetName == presetName);
            return (Decompress(row.CompressedPJson), row.CustomOverlayJson);
        }

        private static byte[] Compress(string text) {
            byte[] plaintext = Encoding.UTF8.GetBytes(text);
            using var output = new MemoryStream();
            using (var compressionStream = new CompressionStream(output, DefaultCompressionLevel, leaveOpen: true))
                compressionStream.Write(plaintext, 0, plaintext.Length);
            return output.ToArray();
        }

        private static string Decompress(byte[] compressed) {
            using var input = new MemoryStream(compressed);
            using var decompressionStream = new DecompressionStream(input);
            using var result = new MemoryStream();
            decompressionStream.CopyTo(result);
            return Encoding.UTF8.GetString(result.ToArray());
        }
    }
}
