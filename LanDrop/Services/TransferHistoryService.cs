// Services/TransferHistoryService.cs
using System;
using System.Collections.Generic;
using System.IO;
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

        public List<TransferHistoryEntry> Load()
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

        public void Save(List<TransferHistoryEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(entries, _opts));
            }
            catch { }
        }

        public void Append(TransferHistoryEntry entry)
        {
            var list = Load();
            list.Insert(0, entry);
            if (list.Count > 200) list.RemoveRange(200, list.Count - 200);
            Save(list);
        }
    }
}
