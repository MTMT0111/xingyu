using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ZhiyiDesktop
{
    // Only user-written relationship notes are saved; no inferred personality or chat archive.
    internal sealed class RecipientMemory
    {
        private readonly string path;
        internal Dictionary<string, string> Notes = new Dictionary<string, string>();
        internal RecipientMemory(string file)
        {
            path = file;
            try { if (File.Exists(path) && new FileInfo(path).Length < 100000) Notes = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? Notes; }
            catch { }
        }
        internal void Save(string name, string note)
        {
            name = name.Trim(); note = note.Trim();
            if (name.Length < 1 || name.Length > 40 || note.Length > 100) return;
            if (!Notes.ContainsKey(name) && Notes.Count >= 30) return;
            Notes[name] = note;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(Notes));
        }
    }
}
