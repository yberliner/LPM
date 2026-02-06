using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FSMSGS
{
    public interface IFileStore
    {
        /// <summary>Saves the stream and returns the absolute path it was written to.</summary>
        Task<string> SaveAsyncScripts(Stream data, string originalName, CancellationToken ct = default);

        Task<string> SaveAsyncResults(Stream data, string originalName, CancellationToken ct = default);

        Task<string> SaveAsyncBitConfig(Stream data, string originalName, CancellationToken ct = default);

        Task<string> GetNameAsyncResults(string originalName, CancellationToken ct = default);
    }

    public sealed class FileStore : IFileStore
    {
        private readonly string _rootScripts;
        private readonly string _rootResults;
        private readonly string _rwsScripts;
        private readonly string _rwsResults;
        private readonly string _rootBitConfig;

        public FileStore(string rootPath)
        {
            _rootBitConfig = Path.Combine(rootPath, "UserFiles", "BitConfigs");
            _rootScripts = Path.Combine(rootPath, "UserFiles","Scripts");
            _rootResults = Path.Combine(rootPath, "UserFiles","Results");
            _rwsScripts = Path.Combine(rootPath, "UserFiles","RwsScripts");
            _rwsResults = Path.Combine(rootPath, "UserFiles","RwsResults");

            Directory.CreateDirectory(_rootScripts);
            Directory.CreateDirectory(_rootResults);
            Directory.CreateDirectory(_rwsScripts);
            Directory.CreateDirectory(_rwsResults);
        }

        public Task<string> GetNameAsyncResults(string originalName, CancellationToken ct = default)
        {
            var name = GenerateFileName(originalName, _rootResults);
            return Task.FromResult(name);
        }

        public Task<string> SaveAsyncScripts(Stream data, string originalName, CancellationToken ct = default)
        {
            return SaveFileAsync(data, originalName, _rootScripts, ct);
        }

        public Task<string> SaveAsyncBitConfig(Stream data, string originalName, CancellationToken ct = default)
        {
            return SaveFileAsync(data, originalName, _rootBitConfig, ct);
        }

        public Task<string> SaveAsyncResults(Stream data, string originalName, CancellationToken ct = default)
        {
            return SaveFileAsync(data, originalName, _rootResults, ct);
        }

        private async Task<string> SaveFileAsync(Stream data, string originalName, string rootDirectory, CancellationToken ct)
        {
            string candidate = GenerateFileName(originalName, rootDirectory);

            await using var target = new FileStream(
                candidate,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            await data.CopyToAsync(target, ct);

            return candidate;
        }

        private string GenerateFileName(string originalName, string rootDirectory)
        {
            var safeName = Path.GetFileName(originalName);
            var baseName = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName);

            var candidate = Path.Combine(rootDirectory, safeName);
            var counter = 1;

            while (System.IO.File.Exists(candidate))
            {
                candidate = Path.Combine(rootDirectory, $"{baseName}_{counter}{ext}");
                counter++;
            }

            return candidate;
        }
    }
}
