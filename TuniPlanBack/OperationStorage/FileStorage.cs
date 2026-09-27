using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OperationStorage;

public sealed record StoredFile(string Url, string RelativePath, long Size, string ContentType);

public interface IFileStorage
{
    /// <summary>Validates (type, size, magic bytes) and stores an image. Throws <see cref="InvalidFileException"/>.</summary>
    Task<StoredFile> SaveImageAsync(Stream content, string originalFileName, string contentType, string folder, CancellationToken ct = default);
    Task DeleteAsync(string url, CancellationToken ct = default);
}

public sealed class InvalidFileException(string message) : Exception(message);

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>Physical folder (default: wwwroot/uploads).</summary>
    public string RootPath { get; set; } = Path.Combine("wwwroot", "uploads");
    /// <summary>Public URL prefix of the folder.</summary>
    public string PublicBaseUrl { get; set; } = "/uploads";
    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;
}

/// <summary>Local disk storage. Swap for Azure Blob / S3 by implementing <see cref="IFileStorage"/>.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private static readonly Dictionary<string, (string Ext, byte[][] Signatures)> Allowed = new()
    {
        ["image/jpeg"] = (".jpg", new[] { new byte[] { 0xFF, 0xD8, 0xFF } }),
        ["image/png"] = (".png", new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } }),
        ["image/webp"] = (".webp", new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } }),
    };

    public async Task<StoredFile> SaveImageAsync(Stream content, string originalFileName, string contentType, string folder, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!Allowed.TryGetValue(contentType.ToLowerInvariant(), out var rule))
            throw new InvalidFileException("Format non autorisé (JPG, PNG ou WEBP uniquement).");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) throw new InvalidFileException("Fichier vide.");
        if (buffer.Length > o.MaxImageBytes) throw new InvalidFileException($"Fichier trop volumineux (max {o.MaxImageBytes / 1024 / 1024} Mo).");

        var bytes = buffer.GetBuffer();
        if (!rule.Signatures.Any(sig => bytes.Length >= sig.Length && bytes.AsSpan(0, sig.Length).SequenceEqual(sig)))
            throw new InvalidFileException("Le contenu du fichier ne correspond pas à une image valide.");

        // Never trust the client file name: random name + safe folder
        var safeFolder = new string(folder.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        var fileName = $"{Guid.NewGuid():N}{rule.Ext}";
        var directory = Path.Combine(o.RootPath, safeFolder);
        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(fullPath, buffer.ToArray(), ct);

        var relative = $"{safeFolder}/{fileName}";
        return new StoredFile($"{o.PublicBaseUrl.TrimEnd('/')}/{relative}", relative, buffer.Length, contentType);
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var o = options.Value;
        var prefix = o.PublicBaseUrl.TrimEnd('/') + "/";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
        var relative = url[prefix.Length..];
        if (relative.Contains("..")) return Task.CompletedTask;
        var fullPath = Path.GetFullPath(Path.Combine(o.RootPath, relative));
        if (fullPath.StartsWith(Path.GetFullPath(o.RootPath)) && File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddOperationStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }
}
