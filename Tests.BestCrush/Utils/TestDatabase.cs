using BestCrush.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests.BestCrush.Utils;

public class TestDatabase :
    IDbContextFactory<BestCrushDbContext>,
    IDisposable
{
    static readonly Lock TestDirectoryLock = new();

    readonly string _databaseFile;
    readonly SqliteConnection _connection;
    readonly DbContextOptions<BestCrushDbContext> _contextOptions;
    readonly DbContextOptions<BestCrushDbContext>
        _factoryContextOptions;
    readonly List<BestCrushDbContext> _contexts = [];

    public TestDatabase()
    {
        string dbFileName = $"test-db-{Guid.CreateVersion7()}.sqlite";
        string directory = CreateTestDirectory();
        _databaseFile = Path.Join(directory, dbFileName);
        _connection = new SqliteConnection($"Data Source={_databaseFile};");
        _connection.Open();
        _contextOptions =
            new DbContextOptionsBuilder<
                BestCrushDbContext>()
                .UseSqlite(
                    _connection)
                .EnableSensitiveDataLogging()
                .Options;

        _factoryContextOptions =
            new DbContextOptionsBuilder<
                BestCrushDbContext>()
                .UseSqlite(
                    $"Data Source={_databaseFile};")
                .EnableSensitiveDataLogging()
                .Options;

        using BestCrushDbContext context = CreateContext();
        context.Database.EnsureCreated();
    }

    public BestCrushDbContext CreateContext()
    {
        BestCrushDbContext context =
            new(_contextOptions);

        _contexts.Add(context);
        return context;
    }

    public BestCrushDbContext CreateDbContext()
    {
        return new BestCrushDbContext(
            _factoryContextOptions);
    }

    public void Dispose()
    {
        foreach (BestCrushDbContext context in _contexts)
        {
            context.Dispose();
        }

        _connection.Close();
        _connection.Dispose();

        if (File.Exists(_databaseFile))
        {
            try
            {
                File.Delete(_databaseFile);
            }
            catch (Exception)
            {
                // skip
            }
        }
    }

    static string CreateTestDirectory()
    {
        string directory = Path.Join(Path.GetTempPath(), "Tests.BestCrush");

        if (!Directory.Exists(directory))
        {
            lock (TestDirectoryLock)
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }

        return directory;
    }
}
