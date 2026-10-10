using Microsoft.Data.Sqlite;

namespace Tests.BestCrush;

public sealed class SqliteNativeVersionTest
{
    [Fact]
    public void NativeLibraryMustIncludeTheFixForCve20256965()
    {
        using SqliteConnection connection =
            new("Data Source=:memory:");

        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        string? versionText = command.ExecuteScalar() as string;

        Assert.True(
            Version.TryParse(versionText, out Version? nativeVersion),
            $"Unable to read the native SQLite version: {versionText ?? "<null>"}"
        );

        Assert.True(
            nativeVersion!.CompareTo(new Version(3, 50, 2)) >= 0,
            $"Native SQLite {nativeVersion} is affected by CVE-2025-6965. Require >= 3.50.2."
        );
    }
}
