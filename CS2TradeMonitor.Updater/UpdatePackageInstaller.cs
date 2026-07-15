using CS2TradeMonitor.UpdateSecurity;

namespace CS2TradeMonitor.Updater
{
    internal static class UpdatePackageInstaller
    {
        internal static void Apply(
            string sourceRoot,
            string installDir,
            string backupDir,
            Action<string, string>? copyFile = null)
        {
            ProgramFileManifest manifest = ProgramFileManifestPolicy.LoadAndValidate(sourceRoot);
            copyFile ??= static (source, destination) => File.Copy(source, destination, overwrite: true);
            var createdFiles = new List<string>();

            try
            {
                BackupOwnedFiles(manifest, installDir, backupDir);
                CopyManifestFiles(manifest, sourceRoot, installDir, createdFiles, copyFile);
                DeleteObsoleteFiles(manifest, installDir);
            }
            catch (Exception applyError)
            {
                try
                {
                    RemoveCreatedFiles(createdFiles);
                    RestoreBackup(backupDir, installDir);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("更新失败，并且无法完整恢复程序文件。", applyError, rollbackError);
                }

                throw;
            }
        }

        private static void BackupOwnedFiles(ProgramFileManifest manifest, string installDir, string backupDir)
        {
            foreach (string relative in manifest.Files.Concat(manifest.Obsolete))
            {
                string destination = GetSafeDestinationPath(installDir, relative);
                if (!File.Exists(destination))
                    continue;

                string backup = GetSafeDestinationPath(backupDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: true);
            }
        }

        private static void CopyManifestFiles(
            ProgramFileManifest manifest,
            string sourceRoot,
            string installDir,
            ICollection<string> createdFiles,
            Action<string, string> copyFile)
        {
            foreach (string relative in manifest.Files)
            {
                if (UpdatePackageFilePolicy.IsForbidden(relative))
                    throw new InvalidDataException("拒绝覆盖禁止路径：" + relative);

                string source = GetSafeDestinationPath(sourceRoot, relative);
                string destination = GetSafeDestinationPath(installDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination))
                    createdFiles.Add(destination);
                copyFile(source, destination);
            }
        }

        private static void DeleteObsoleteFiles(ProgramFileManifest manifest, string installDir)
        {
            foreach (string relative in manifest.Obsolete)
            {
                string path = GetSafeDestinationPath(installDir, relative);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void RemoveCreatedFiles(IEnumerable<string> createdFiles)
        {
            foreach (string path in createdFiles.Reverse())
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void RestoreBackup(string backupDir, string installDir)
        {
            if (!Directory.Exists(backupDir))
                return;

            foreach (string backupPath in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(backupDir, backupPath);
                string destination = GetSafeDestinationPath(installDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(backupPath, destination, overwrite: true);
            }
        }

        private static string GetSafeDestinationPath(string root, string relativePath)
        {
            string normalized = ProgramFileManifestPolicy.NormalizeRelative(relativePath);
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = rootFull + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("程序文件清单包含非法路径。");
            return full;
        }
    }
}
