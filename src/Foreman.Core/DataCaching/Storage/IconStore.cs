using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Foreman.DataCaching.Storage {
    //Content-hash icon store: dedups PNG blobs across presets by SHA-256, importing straight from
    //ForemanIconCacheFile's decode-free entry enumerator so import never pays for SKBitmap decode.
    public static class IconStore {
        public static void ImportFoic(ForemanDbContext db, string presetName, string datPath) {
            List<ForemanIconCacheFile.FoicEntry> entries = ForemanIconCacheFile.ReadEntriesAsync(datPath).GetAwaiter().GetResult();

            HashSet<string> existingHashes = [.. db.IconBlobs.Select(blob => blob.Sha256)];

            List<PresetIconRow> priorRows = [.. db.PresetIcons.Where(row => row.PresetName == presetName)];
            db.PresetIcons.RemoveRange(priorRows);

            foreach (ForemanIconCacheFile.FoicEntry entry in entries) {
                string sha256 = Convert.ToHexString(SHA256.HashData(entry.PngBytes));
                if (existingHashes.Add(sha256))
                    db.IconBlobs.Add(new IconBlob { Sha256 = sha256, PngBytes = entry.PngBytes });

                db.PresetIcons.Add(new PresetIconRow {
                    PresetName = presetName,
                    IconName = entry.Name,
                    Tint = entry.ColorArgb,
                    Sha256 = sha256,
                });
            }

            db.SaveChanges();
            RemoveOrphanedBlobs(db);
        }

        //Sweeps any IconBlob no PresetIconRow references anymore - safe since blobs are content-addressed
        //and shared by hash across presets. Runs after ImportFoic's own row replacement (a re-import with
        //a changed icon set can orphan a blob) and after preset delete (SettingsWindow.DeletePresetDbRows).
        //Key-only: never materializes a blob's PngBytes just to decide whether to delete it.
        public static void RemoveOrphanedBlobs(ForemanDbContext db) {
            HashSet<string> referencedHashes = [.. db.PresetIcons.Select(row => row.Sha256)];
            db.IconBlobs.Where(blob => !referencedHashes.Contains(blob.Sha256)).ExecuteDelete();
        }

        public static IEnumerable<(string Name, int Tint, byte[] PngBytes)> ReadPresetIcons(ForemanDbContext db, string presetName) =>
            db.PresetIcons
                .Where(row => row.PresetName == presetName)
                .Join(db.IconBlobs, row => row.Sha256, blob => blob.Sha256, (row, blob) => new { row.IconName, row.Tint, blob.PngBytes })
                .AsEnumerable()
                .Select(entry => (entry.IconName, entry.Tint, entry.PngBytes))
                .ToList();
    }
}
