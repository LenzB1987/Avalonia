﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Avalonia.Browser.Interop;
using Avalonia.Platform.Storage;

namespace Avalonia.Browser.Storage;

internal record FilePickerAcceptType(string Description, IReadOnlyDictionary<string, IReadOnlyList<string>> Accept);

internal class BrowserStorageProvider : IStorageProvider
{
    internal const string PickerCancelMessage = "The user aborted a request";
    internal const string NoPermissionsMessage = "Permissions denied";

    public bool CanOpen => true;
    public bool CanSave => StorageHelper.HasNativeFilePicker();
    public bool CanPickFolder => true;

    public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
        await AvaloniaModule.ImportStorage();
        var startIn = (options.SuggestedStartLocation as JSStorageItem)?.FileHandle;

        var (types, excludeAll) = ConvertFileTypes(options.FileTypeFilter);

        try
        {
            using var items = await StorageHelper.OpenFileDialog(startIn, options.AllowMultiple, types, excludeAll);
            if (items is null)
            {
                return Array.Empty<IStorageFile>();
            }

            var itemsArray = StorageHelper.ItemsArray(items);
            return itemsArray.Select(item => new JSStorageFile(item)).ToArray();
        }
        catch (JSException ex) when (ex.Message.Contains(PickerCancelMessage, StringComparison.Ordinal))
        {
            return Array.Empty<IStorageFile>();
        }
        finally
        {
            if (types is not null)
            {
                foreach (var type in types)
                {
                    type.Dispose();
                }
            }
        }
    }

    public async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options)
    {
        await AvaloniaModule.ImportStorage();
        var startIn = (options.SuggestedStartLocation as JSStorageItem)?.FileHandle;

        var (types, excludeAll) = ConvertFileTypes(options.FileTypeChoices);

        try
        {
            var item = await StorageHelper.SaveFileDialog(startIn, options.SuggestedFileName, types, excludeAll);
            return item is not null ? new JSStorageFile(item) : null;
        }
        catch (JSException ex) when (ex.Message.Contains(PickerCancelMessage, StringComparison.Ordinal))
        {
            return null;
        }
        finally
        {
            if (types is not null)
            {
                foreach (var type in types)
                {
                    type.Dispose();
                }
            }
        }
    }

    public async Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync(FolderPickerOpenOptions options)
    {
        await AvaloniaModule.ImportStorage();
        var startIn = (options.SuggestedStartLocation as JSStorageItem)?.FileHandle;

        try
        {
            var item = await StorageHelper.SelectFolderDialog(startIn);
            return item is not null ? new[] { new JSStorageFolder(item) } : Array.Empty<IStorageFolder>();
        }
        catch (JSException ex) when (ex.Message.Contains(PickerCancelMessage, StringComparison.Ordinal))
        {
            return Array.Empty<IStorageFolder>();
        }
    }

    public async Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark)
    {
        await AvaloniaModule.ImportStorage();
        var item = await StorageHelper.OpenBookmark(bookmark);
        return item is not null ? new JSStorageFile(item) : null;
    }

    public async Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark)
    {
        await AvaloniaModule.ImportStorage();
        var item = await StorageHelper.OpenBookmark(bookmark);
        return item is not null ? new JSStorageFolder(item) : null;
    }

    public Task<IStorageFile?> TryGetFileFromPathAsync(Uri filePath)
    {
        return Task.FromResult<IStorageFile?>(null);
    }

    public Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri folderPath)
    {
        return Task.FromResult<IStorageFolder?>(null);
    }

    public async Task<IStorageFolder?> TryGetWellKnownFolderAsync(WellKnownFolder wellKnownFolder)
    {
        await AvaloniaModule.ImportStorage();
        var directory = StorageHelper.CreateWellKnownDirectory(wellKnownFolder switch
        {
            WellKnownFolder.Desktop => "desktop",
            WellKnownFolder.Documents => "documents",
            WellKnownFolder.Downloads => "downloads",
            WellKnownFolder.Music => "music",
            WellKnownFolder.Pictures => "pictures",
            WellKnownFolder.Videos => "videos",
            _ => throw new ArgumentOutOfRangeException(nameof(wellKnownFolder), wellKnownFolder, null)
        });

        return new JSStorageFolder(directory);
    }

    private static (JSObject[]? types, bool excludeAllOption) ConvertFileTypes(IEnumerable<FilePickerFileType>? input)
    {
        var types = input?
            .Where(t => t.MimeTypes?.Any() == true && t != FilePickerFileTypes.All)
            .Select(t => StorageHelper.CreateAcceptType(t.Name, t.MimeTypes!.ToArray(), t.TryGetExtensions()?.ToArray()))
            .ToArray();
        if (types?.Length == 0)
        {
            types = null;
        }

        var includeAll = input?.Contains(FilePickerFileTypes.All) == true || types is null;

        return (types, !includeAll);
    }
}

internal abstract class JSStorageItem : IStorageBookmarkItem
{
    internal JSObject? _fileHandle;

    protected JSStorageItem(JSObject fileHandle)
    {
        _fileHandle = fileHandle ?? throw new ArgumentNullException(nameof(fileHandle));
    }

    internal JSObject FileHandle => _fileHandle ?? throw new ObjectDisposedException(nameof(JSStorageItem));

    public string Name => FileHandle.GetPropertyAsString("name") ?? string.Empty;
    public Uri Path => new Uri(Name, UriKind.Relative);

    public async Task<StorageItemProperties> GetBasicPropertiesAsync()
    {
        using var properties = await StorageHelper.GetProperties(FileHandle);
        var size = (long?)properties?.GetPropertyAsDouble("Size");
        var lastModified = (long?)properties?.GetPropertyAsDouble("LastModified");

        return new StorageItemProperties(
            (ulong?)size,
            dateCreated: null,
            dateModified: lastModified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastModified.Value) : null);
    }

    public bool CanBookmark => StorageHelper.HasNativeFilePicker();

    public Task<string?> SaveBookmarkAsync()
    {
        if (!CanBookmark)
        {
            return Task.FromResult<string?>(null);
        }

        return StorageHelper.SaveBookmark(FileHandle);
    }

    public Task<IStorageFolder?> GetParentAsync()
    {
        return Task.FromResult<IStorageFolder?>(null);
    }

    public Task ReleaseBookmarkAsync()
    {
        if (!CanBookmark)
        {
            return Task.CompletedTask;
        }

        return StorageHelper.DeleteBookmark(FileHandle);
    }

    public void Dispose()
    {
        _fileHandle?.Dispose();
        _fileHandle = null;
    }
}

internal class JSStorageFile : JSStorageItem, IStorageBookmarkFile
{
    public JSStorageFile(JSObject fileHandle) : base(fileHandle)
    {
    }

    public async Task<Stream> OpenReadAsync()
    {
        try
        {
            var blob = await StorageHelper.OpenRead(FileHandle);
            return new BlobReadableStream(blob);
        }
        catch (JSException ex) when (ex.Message == BrowserStorageProvider.NoPermissionsMessage)
        {
            throw new UnauthorizedAccessException("User denied permissions to open the file", ex);
        }
    }

    public async Task<Stream> OpenWriteAsync()
    {
        try
        {
            using var properties = await StorageHelper.GetProperties(FileHandle);
            var streamWriter = await StorageHelper.OpenWrite(FileHandle);
            var size = (long?)properties?.GetPropertyAsDouble("Size") ?? 0;

            return new WriteableStream(streamWriter, size);
        }
        catch (JSException ex) when (ex.Message == BrowserStorageProvider.NoPermissionsMessage)
        {
            throw new UnauthorizedAccessException("User denied permissions to open the file", ex);
        }
    }
}

internal class JSStorageFolder : JSStorageItem, IStorageBookmarkFolder
{
    public JSStorageFolder(JSObject fileHandle) : base(fileHandle)
    {
    }

    public async Task<IReadOnlyList<IStorageItem>> GetItemsAsync()
    {
        using var items = await StorageHelper.GetItems(FileHandle);
        if (items is null)
        {
            return Array.Empty<IStorageItem>();
        }

        var itemsArray = StorageHelper.ItemsArray(items);

        return itemsArray
            .Select(reference => reference.GetPropertyAsString("kind") switch
            {
                "directory" => (IStorageItem)new JSStorageFolder(reference),
                "file" => new JSStorageFile(reference),
                _ => null
            })
            .Where(i => i is not null)
            .ToArray()!;
    }
}
