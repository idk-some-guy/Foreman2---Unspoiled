using System.IO;

namespace Foreman.DataCaching.Storage {
    public static class StoragePaths {
        public static string DbPath(string userDataRoot) => Path.Combine(userDataRoot, "foreman.db");
    }
}
