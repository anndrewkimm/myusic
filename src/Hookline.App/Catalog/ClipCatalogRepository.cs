using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Hookline.App.Catalog;

public sealed class ClipCatalogRepository
{
    private const int SchemaVersion = 2;
    private const string CurrentSchemaSql = """
        CREATE TABLE IF NOT EXISTS clips (
            id TEXT NOT NULL PRIMARY KEY,
            display_title TEXT NOT NULL,
            source_title TEXT NOT NULL,
            source_artist TEXT NOT NULL,
            source_album TEXT NOT NULL,
            exported_at_utc TEXT NOT NULL,
            file_path TEXT NOT NULL,
            trim_start_ticks INTEGER NOT NULL,
            trim_end_ticks INTEGER NOT NULL,
            duration_ticks INTEGER NOT NULL,
            track_instance_id INTEGER NOT NULL,
            album_art BLOB NULL,
            CHECK (trim_start_ticks >= 0),
            CHECK (trim_end_ticks >= trim_start_ticks),
            CHECK (duration_ticks >= 0),
            CHECK (track_instance_id <> 0)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS
            ix_clips_file_path
            ON clips (file_path COLLATE NOCASE);

        CREATE INDEX IF NOT EXISTS
            ix_clips_exported_at
            ON clips (exported_at_utc DESC);

        CREATE INDEX IF NOT EXISTS
            ix_clips_artist
            ON clips (
                source_artist COLLATE NOCASE,
                display_title COLLATE NOCASE
            );

        """;
    private readonly object _gate = new();
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _initialized;

    public ClipCatalogRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
    }

    public void Initialize()
    {
        lock (_gate)
        {
            InitializeCore();
        }
    }

    public void Add(ClipCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Validate(entry);
        lock (_gate)
        {
            InitializeCore();
            using var connection = OpenConnection();
            using var command = CreateInsertCommand(
                connection,
                entry
            );
            command.ExecuteNonQuery();
        }
    }

    public void AddRange(IEnumerable<ClipCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        foreach (var entry in materialized)
        {
            Validate(entry);
        }

        lock (_gate)
        {
            InitializeCore();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var entry in materialized)
            {
                using var command = CreateInsertCommand(
                    connection,
                    entry
                );
                command.Transaction = transaction;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public IReadOnlyList<ClipCatalogEntry> GetAll(
        CatalogSortOrder sortOrder
    )
    {
        lock (_gate)
        {
            InitializeCore();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            var orderBy = sortOrder switch
            {
                CatalogSortOrder.MostRecent =>
                    "exported_at_utc DESC, id DESC",
                CatalogSortOrder.Artist =>
                    "source_artist COLLATE NOCASE ASC, display_title COLLATE NOCASE ASC, exported_at_utc DESC",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(sortOrder)
                ),
            };
            command.CommandText = $"""
                SELECT
                    id,
                    display_title,
                    source_title,
                    source_artist,
                    source_album,
                    exported_at_utc,
                    file_path,
                    trim_start_ticks,
                    trim_end_ticks,
                    duration_ticks,
                    track_instance_id,
                    album_art
                FROM clips
                ORDER BY {orderBy};
                """;

            var entries = new List<ClipCatalogEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(ReadEntry(reader));
            }

            return entries;
        }
    }

    public ClipCatalogEntry? GetById(Guid id)
    {
        lock (_gate)
        {
            InitializeCore();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    id,
                    display_title,
                    source_title,
                    source_artist,
                    source_album,
                    exported_at_utc,
                    file_path,
                    trim_start_ticks,
                    trim_end_ticks,
                    duration_ticks,
                    track_instance_id,
                    album_art
                FROM clips
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue(
                "$id",
                id.ToString("D", CultureInfo.InvariantCulture)
            );
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadEntry(reader) : null;
        }
    }

    public bool UpdateTitle(Guid id, string displayTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);
        lock (_gate)
        {
            InitializeCore();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE clips
                SET display_title = $displayTitle
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue(
                "$displayTitle",
                displayTitle.Trim()
            );
            command.Parameters.AddWithValue(
                "$id",
                id.ToString("D", CultureInfo.InvariantCulture)
            );
            return command.ExecuteNonQuery() == 1;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            InitializeCore();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM clips
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue(
                "$id",
                id.ToString("D", CultureInfo.InvariantCulture)
            );
            return command.ExecuteNonQuery() == 1;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                InitializeCore();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM clips;";
                return Convert.ToInt32(
                    command.ExecuteScalar(),
                    CultureInfo.InvariantCulture
                );
            }
        }
    }

    private void InitializeCore()
    {
        if (_initialized)
        {
            return;
        }

        var parent = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var connection = OpenConnection();
        using (var journalCommand = connection.CreateCommand())
        {
            journalCommand.CommandText = "PRAGMA journal_mode = WAL;";
            journalCommand.ExecuteNonQuery();
        }

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var existingVersion = Convert.ToInt32(
            versionCommand.ExecuteScalar(),
            CultureInfo.InvariantCulture
        );
        if (
            existingVersion is not (
                0
                or 1
                or SchemaVersion
            )
        )
        {
            throw new InvalidOperationException(
                $"Unsupported clip catalog schema version {existingVersion}."
            );
        }

        if (existingVersion == 1)
        {
            MigrateVersionOne(connection);
        }
        else
        {
            CreateCurrentSchema(connection);
            SetSchemaVersion(connection);
        }

        _initialized = true;
    }

    private static void MigrateVersionOne(
        SqliteConnection connection
    )
    {
        using var transaction = connection.BeginTransaction();
        using (var prepareCommand = connection.CreateCommand())
        {
            prepareCommand.Transaction = transaction;
            prepareCommand.CommandText = """
                DROP INDEX IF EXISTS ix_clips_file_path;
                DROP INDEX IF EXISTS ix_clips_exported_at;
                DROP INDEX IF EXISTS ix_clips_artist;
                ALTER TABLE clips RENAME TO clips_v1;
                """;
            prepareCommand.ExecuteNonQuery();
        }

        CreateCurrentSchema(connection, transaction);
        using (var copyCommand = connection.CreateCommand())
        {
            copyCommand.Transaction = transaction;
            copyCommand.CommandText = """
                INSERT INTO clips (
                    id,
                    display_title,
                    source_title,
                    source_artist,
                    source_album,
                    exported_at_utc,
                    file_path,
                    trim_start_ticks,
                    trim_end_ticks,
                    duration_ticks,
                    track_instance_id,
                    album_art
                )
                SELECT
                    id,
                    display_title,
                    source_title,
                    source_artist,
                    source_album,
                    exported_at_utc,
                    file_path,
                    trim_start_ticks,
                    trim_end_ticks,
                    duration_ticks,
                    track_instance_id,
                    album_art
                FROM clips_v1;

                DROP TABLE clips_v1;
                """;
            copyCommand.ExecuteNonQuery();
        }

        SetSchemaVersion(connection, transaction);
        transaction.Commit();
    }

    private static void CreateCurrentSchema(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = CurrentSchemaSql;
        command.ExecuteNonQuery();
    }

    private static void SetSchemaVersion(
        SqliteConnection connection,
        SqliteTransaction? transaction = null
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"PRAGMA user_version = {SchemaVersion};";
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static SqliteCommand CreateInsertCommand(
        SqliteConnection connection,
        ClipCatalogEntry entry
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clips (
                id,
                display_title,
                source_title,
                source_artist,
                source_album,
                exported_at_utc,
                file_path,
                trim_start_ticks,
                trim_end_ticks,
                duration_ticks,
                track_instance_id,
                album_art
            )
            VALUES (
                $id,
                $displayTitle,
                $sourceTitle,
                $sourceArtist,
                $sourceAlbum,
                $exportedAtUtc,
                $filePath,
                $trimStartTicks,
                $trimEndTicks,
                $durationTicks,
                $trackInstanceId,
                $albumArt
            );
            """;
        command.Parameters.AddWithValue(
            "$id",
            entry.Id.ToString("D", CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue(
            "$displayTitle",
            entry.DisplayTitle
        );
        command.Parameters.AddWithValue(
            "$sourceTitle",
            entry.SourceTitle
        );
        command.Parameters.AddWithValue(
            "$sourceArtist",
            entry.SourceArtist
        );
        command.Parameters.AddWithValue(
            "$sourceAlbum",
            entry.SourceAlbum
        );
        command.Parameters.AddWithValue(
            "$exportedAtUtc",
            entry.ExportedAt.ToUniversalTime().ToString(
                "O",
                CultureInfo.InvariantCulture
            )
        );
        command.Parameters.AddWithValue(
            "$filePath",
            entry.FilePath
        );
        command.Parameters.AddWithValue(
            "$trimStartTicks",
            entry.TrimStart.Ticks
        );
        command.Parameters.AddWithValue(
            "$trimEndTicks",
            entry.TrimEnd.Ticks
        );
        command.Parameters.AddWithValue(
            "$durationTicks",
            entry.Duration.Ticks
        );
        command.Parameters.AddWithValue(
            "$trackInstanceId",
            entry.TrackInstanceId
        );
        command.Parameters.AddWithValue(
            "$albumArt",
            entry.AlbumArt.Length == 0
                ? DBNull.Value
                : entry.AlbumArt
        );
        return command;
    }

    private static ClipCatalogEntry ReadEntry(SqliteDataReader reader) =>
        new()
        {
            Id = Guid.Parse(reader.GetString(0)),
            DisplayTitle = reader.GetString(1),
            SourceTitle = reader.GetString(2),
            SourceArtist = reader.GetString(3),
            SourceAlbum = reader.GetString(4),
            ExportedAt = DateTimeOffset.Parse(
                reader.GetString(5),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
            FilePath = reader.GetString(6),
            TrimStart = TimeSpan.FromTicks(reader.GetInt64(7)),
            TrimEnd = TimeSpan.FromTicks(reader.GetInt64(8)),
            Duration = TimeSpan.FromTicks(reader.GetInt64(9)),
            TrackInstanceId = reader.GetInt64(10),
            AlbumArt = reader.IsDBNull(11)
                ? []
                : (byte[])reader.GetValue(11),
        };

    private static void Validate(ClipCatalogEntry entry)
    {
        if (entry.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "A catalog entry must have an ID.",
                nameof(entry)
            );
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            entry.DisplayTitle
        );
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.FilePath);
        if (entry.TrimStart < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }

        if (entry.TrimEnd < entry.TrimStart)
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }

        if (
            entry.Duration < TimeSpan.Zero
            || entry.TrackInstanceId == 0
        )
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }
    }
}
