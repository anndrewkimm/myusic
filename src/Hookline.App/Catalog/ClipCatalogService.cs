using System.Globalization;
using System.IO;
using Hookline.Audio;

namespace Hookline.App.Catalog;

public sealed class ClipCatalogService
{
    private readonly ClipCatalogRepository _repository;
    private readonly IClipFileOperations _files;
    private readonly IClipTagEditor _tagEditor;

    public ClipCatalogService(
        ClipCatalogRepository repository,
        IClipFileOperations? files = null,
        IClipTagEditor? tagEditor = null
    )
    {
        _repository =
            repository
            ?? throw new ArgumentNullException(nameof(repository));
        _files = files ?? new SystemClipFileOperations();
        _tagEditor = tagEditor ?? new ClipTagEditor();
    }

    public event EventHandler? Changed;

    public Task InitializeAsync(
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _repository.Initialize();
            },
            cancellationToken
        );

    public Task<IReadOnlyList<ClipCatalogEntry>> GetAllAsync(
        CatalogSortOrder sortOrder,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run<IReadOnlyList<ClipCatalogEntry>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _repository
                    .GetAll(sortOrder)
                    .Select(
                        entry =>
                            entry with
                            {
                                IsMissing = !_files.Exists(
                                    entry.FilePath
                                ),
                            }
                    )
                    .ToArray();
            },
            cancellationToken
        );

    public async Task RegisterExportAsync(
        AudioBufferSnapshot selection,
        ClipExportMetadata metadata,
        ClipExportResult result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(result);

        var displayTitle = string.IsNullOrWhiteSpace(metadata.Title)
            ? Path.GetFileNameWithoutExtension(result.OutputPath)
            : metadata.Title.Trim();
        var entry = new ClipCatalogEntry
        {
            Id = Guid.NewGuid(),
            DisplayTitle = displayTitle,
            SourceTitle = metadata.Title.Trim(),
            SourceArtist = metadata.Artist.Trim(),
            SourceAlbum = metadata.Album.Trim(),
            ExportedAt = DateTimeOffset.UtcNow,
            FilePath = Path.GetFullPath(result.OutputPath),
            TrimStart = selection.RequestedStart,
            TrimEnd = selection.RequestedEnd,
            Duration = result.Duration,
            TrackInstanceId = selection.TrackInstanceId,
            AlbumArt = metadata.AlbumArt.ToArray(),
        };

        await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _repository.Add(entry);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        NotifyChanged();
    }

    public async Task RenameAsync(
        Guid id,
        string newTitle,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);
        var normalizedTitle = newTitle.Trim();
        await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry =
                        _repository.GetById(id)
                        ?? throw new KeyNotFoundException(
                            AppStrings.CatalogEntryNotFound
                        );
                    EnsureFileExists(entry);

                    _tagEditor.UpdateTitle(
                        entry.FilePath,
                        normalizedTitle
                    );
                    try
                    {
                        if (
                            !_repository.UpdateTitle(
                                id,
                                normalizedTitle
                            )
                        )
                        {
                            throw new KeyNotFoundException(
                                AppStrings.CatalogEntryNotFound
                            );
                        }
                    }
                    catch (Exception updateException)
                    {
                        try
                        {
                            _tagEditor.UpdateTitle(
                                entry.FilePath,
                                entry.DisplayTitle
                            );
                        }
                        catch (Exception restoreException)
                        {
                            throw new AggregateException(
                                AppStrings.CatalogRenameRollbackFailed,
                                updateException,
                                restoreException
                            );
                        }

                        throw;
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        NotifyChanged();
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var cleanupException = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry =
                        _repository.GetById(id)
                        ?? throw new KeyNotFoundException(
                            AppStrings.CatalogEntryNotFound
                        );
                    if (!_files.Exists(entry.FilePath))
                    {
                        if (!_repository.Delete(id))
                        {
                            throw new KeyNotFoundException(
                                AppStrings.CatalogEntryNotFound
                            );
                        }

                        return null;
                    }

                    var quarantinePath =
                        $"{entry.FilePath}.hookline-delete-{Guid.NewGuid():N}.tmp";
                    _files.Move(entry.FilePath, quarantinePath);
                    try
                    {
                        if (!_repository.Delete(id))
                        {
                            throw new KeyNotFoundException(
                                AppStrings.CatalogEntryNotFound
                            );
                        }
                    }
                    catch
                    {
                        _files.Move(
                            quarantinePath,
                            entry.FilePath
                        );
                        throw;
                    }

                    try
                    {
                        _files.Delete(quarantinePath);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        NotifyChanged();
        if (cleanupException is not null)
        {
            throw new IOException(
                AppStrings.CatalogDeleteCleanupFailed,
                cleanupException
            );
        }
    }

    public Task<ClipCatalogEntry?> RefreshAvailabilityAsync(
        Guid id,
        CancellationToken cancellationToken = default
    ) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = _repository.GetById(id);
                return entry is null
                    ? null
                    : entry with
                    {
                        IsMissing = !_files.Exists(entry.FilePath),
                    };
            },
            cancellationToken
        );

    private void EnsureFileExists(ClipCatalogEntry entry)
    {
        if (_files.Exists(entry.FilePath))
        {
            return;
        }

        throw new ClipCatalogMissingFileException(
            string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.CatalogFileMissing,
                entry.FilePath
            )
        );
    }

    private void NotifyChanged()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        foreach (
            EventHandler handler in changed.GetInvocationList()
        )
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // A catalog observer must not make a completed file/database
                // operation appear to have failed or trigger compensation.
            }
        }
    }
}
