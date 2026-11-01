using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EscapeFromBorderlands.Client;

/// <summary>
/// Moves pre-0.30 client preferences into the new Forge-compliant BepInEx config.
/// BepInEx may have already created a blank current-GUID file before Awake;
/// merge missing keys rather than treating existence as proof of migration.
/// </summary>
internal static class LegacyClientConfigMigration
{
    private const string OriginalGuid = "com.tkw.escapefromborderlands.client";
    private const string IntermediateGuid = "com.tkw.escapefromborderlands";

    internal static int Migrate(string configDirectory, string destination)
    {
        // If a user tried the intermediate build, prefer its newer preferences;
        // otherwise import from the pre-0.30 release. Existing destination keys
        // ALWAYS win. None of the old files is deleted or overwritten.
        var sources = new[]
        {
            Path.Combine(configDirectory, IntermediateGuid + ".cfg"),
            Path.Combine(configDirectory, OriginalGuid + ".cfg")
        };
        var lines = File.Exists(destination)
            ? File.ReadAllLines(destination).ToList()
            : new List<string>();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries(lines)) existing.Add(Key(entry.Section, entry.Name));

        var pending = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (!File.Exists(source)) continue;
            // Avoid reading our own destination if paths are unexpectedly aliased.
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var entry in Entries(File.ReadAllLines(source)))
            {
                if (!existing.Add(Key(entry.Section, entry.Name))) continue;
                if (!pending.TryGetValue(entry.Section, out var values))
                {
                    values = new List<string>();
                    pending.Add(entry.Section, values);
                }
                values.Add(entry.Line);
            }
        }
        var imported = pending.Values.Sum(x => x.Count);
        if (imported == 0) return 0;

        // Insert missing entries into the existing section, not duplicate headings.
        // Repeated section headers are avoided for compatibility with INI parsers.
        foreach (var section in pending)
        {
            var sectionEnd = FindSectionEnd(lines, section.Key);
            if (sectionEnd < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");
                lines.Add("[" + section.Key + "]");
                lines.AddRange(section.Value);
            }
            else
            {
                lines.InsertRange(sectionEnd, section.Value);
            }
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        // Preserve an existing destination once, prior to modifying it.
        if (File.Exists(destination))
        {
            var backup = destination + ".pre-0.30-migration.bak";
            if (!File.Exists(backup)) File.Copy(destination, backup, overwrite: false);
        }
        var temporary = destination + ".efb-migration-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            // Replace only after the complete merged file is safely staged.
            if (File.Exists(destination)) File.Copy(temporary, destination, overwrite: true);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return imported;
    }

    private static string Key(string section, string name) => section + "\u001f" + name;

    private static IEnumerable<(string Section, string Name, string Line)> Entries(IEnumerable<string> lines)
    {
        string? section = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                continue;
            }
            if (string.IsNullOrEmpty(section)) continue;
            var separator = trimmed.IndexOf('=');
            if (separator <= 0) continue;
            var name = trimmed.Substring(0, separator).Trim();
            if (name.Length > 0) yield return (section, name, trimmed);
        }
    }

    private static int FindSectionEnd(IReadOnlyList<string> lines, string sectionName)
    {
        var current = false;
        var position = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
                continue;
            if (current) position = i;
            current = string.Equals(trimmed.Substring(1, trimmed.Length - 2).Trim(),
                sectionName, StringComparison.OrdinalIgnoreCase);
            if (current) position = lines.Count;
        }
        return position;
    }
}
