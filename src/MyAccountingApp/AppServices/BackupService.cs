using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;

namespace MyAccountingApp.AppServices;

/// <summary>The database lives only on the device, so backup = share a copy of the .db3 file.</summary>
public class BackupService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const string DatabaseFileName = "accounting.db3";

    public static string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);

    public async Task ShareBackupAsync(string title)
    {
        var backupPath = Path.Combine(FileSystem.CacheDirectory, $"accounting-{DateTime.Now:yyyyMMdd-HHmm}.db3");

        // VACUUM INTO writes a consistent single-file copy (includes anything still in the WAL).
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            await db.Database.ExecuteSqlAsync($"VACUUM INTO {backupPath}");
        }

        await Share.Default.RequestAsync(new ShareFileRequest(title, new ShareFile(backupPath)));
    }

    /// <returns>false if the user cancelled the picker.</returns>
    public async Task<bool> RestoreAsync()
    {
        var file = await FilePicker.Default.PickAsync();
        if (file is null) return false;

        var tempPath = Path.Combine(FileSystem.CacheDirectory, "restore.db3");
        await using (var source = await file.OpenReadAsync())
        await using (var target = File.Create(tempPath))
            await source.CopyToAsync(target);

        ValidateBackup(tempPath);

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
            File.Delete(DatabasePath + suffix);
        File.Copy(tempPath, DatabasePath, overwrite: true);
        File.Delete(tempPath);

        // Bring an older backup up to the current schema.
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        return true;
    }

    private static void ValidateBackup(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('products','orders','ledger')";
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 3)
            throw new InvalidDataException("Not an accounting backup");
    }
}
