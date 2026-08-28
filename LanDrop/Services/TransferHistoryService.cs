// Services/TransferHistoryService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanDrop.Models;

namespace LanDrop.Services
{
    public class TransferHistoryService
    {
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LanDrop", "history.json");

        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };
        private static readonly object _lock = new();

        public List<TransferHistoryEntry> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_path))
                        return JsonSerializer.Deserialize<List<TransferHistoryEntry>>(
                            File.ReadAllText(_path), _opts) ?? new();
                }
                catch { }
                return new();
            }
        }

        public void Save(List<TransferHistoryEntry> entries)
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(_path, JsonSerializer.Serialize(entries, _opts));
                }
                catch { }
            }
        }

        public void Append(TransferHistoryEntry entry)
        {
            if (entry == null) return;
            AppendRange(new[] { entry });
        }

        public void AppendRange(IEnumerable<TransferHistoryEntry> entries)
        {
            if (entries == null) return;
            var entryList = entries as IList<TransferHistoryEntry> ?? entries.ToList();
            if (entryList.Count == 0) return;

            lock (_lock)
            {
                var list = Load();
                list.InsertRange(0, entryList);
                if (list.Count > 200) list.RemoveRange(200, list.Count - 200);
                Save(list);
            }
        }
    }
}
