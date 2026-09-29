using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.prform.Skills;

public record SkillInfo(string Name, string Description, string Version, int FileCount, long Size);

/// <summary>
/// Skills do Claude Code publicadas pelo PRMake (feature 0024). A fonte é a pasta <c>skills/</c> do
/// repositório, copiada para a imagem a cada deploy (<c>/app/skills</c>): cada subpasta é uma skill
/// (<c>SKILL.md</c>, <c>scripts/</c>, <c>skill.json</c>); <c>_tooling/</c> guarda o instalador e a
/// ferramenta de atualização. A versão de cada skill é o hash do conteúdo — muda só quando a skill muda.
/// Tudo é lido uma vez e fica em memória (os arquivos da imagem não mudam).
/// </summary>
public partial class SkillsCatalog
{
    private const string ToolingFolder = "_tooling";
    private readonly Lazy<Loaded> _loaded;

    private record SkillEntry(SkillInfo Info, byte[] Package);
    private record Loaded(IReadOnlyDictionary<string, SkillEntry> Skills, string ToolTemplate, string InstallTemplate, string ToolVersion);

    public SkillsCatalog(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _loaded = new Lazy<Loaded>(() => Load(ResolveDirectory(configuration, environment)));
    }

    public string ToolVersion => _loaded.Value.ToolVersion;

    public IReadOnlyList<SkillInfo> List() => _loaded.Value.Skills.Values.Select(s => s.Info).OrderBy(s => s.Name).ToList();

    public byte[]? Package(string name) => _loaded.Value.Skills.TryGetValue(name, out var s) ? s.Package : null;

    /// <summary>Ferramenta prmake-skills.sh com a URL da API e a versão já preenchidas.</summary>
    public string Tool(string apiBase) =>
        _loaded.Value.ToolTemplate.Replace("__PRMAKE_API_BASE__", apiBase).Replace("__PRMAKE_TOOL_VERSION__", _loaded.Value.ToolVersion);

    public string Installer(string apiBase) => _loaded.Value.InstallTemplate.Replace("__PRMAKE_API_BASE__", apiBase);

    /// <summary>Config <c>Skills:Directory</c>; senão <c>skills/</c> ao lado da API (imagem) ou subindo até a raiz do repositório (dev).</summary>
    private static string ResolveDirectory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Skills:Directory"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        for (var dir = new DirectoryInfo(environment.ContentRootPath); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "skills");
            if (Directory.Exists(Path.Combine(candidate, ToolingFolder)))
                return candidate;
        }
        return Path.Combine(environment.ContentRootPath, "skills");
    }

    private static Loaded Load(string root)
    {
        if (!Directory.Exists(root))
            return new Loaded(new Dictionary<string, SkillEntry>(), "", "", "none");

        var skills = new Dictionary<string, SkillEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('_') || name.StartsWith('.') || !File.Exists(Path.Combine(dir, "SKILL.md")))
                continue;

            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                .Where(rel => !IsIgnored(rel))
                .OrderBy(rel => rel, StringComparer.Ordinal)
                .ToList();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var zipBuffer = new MemoryStream();
            long size = 0;
            using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var rel in files)
                {
                    var bytes = File.ReadAllBytes(Path.Combine(dir, rel));
                    hash.AppendData(Encoding.UTF8.GetBytes(rel + "\0"));
                    hash.AppendData(bytes);
                    size += bytes.LongLength;

                    var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                    // Scripts executáveis ao extrair (unzip respeita os bits unix do external attributes).
                    entry.ExternalAttributes = (rel.EndsWith(".sh") || rel.EndsWith(".py") ? 0b111_101_101 : 0b110_100_100) << 16;
                    using var stream = entry.Open();
                    stream.Write(bytes);
                }
            }

            var version = Convert.ToHexStringLower(hash.GetHashAndReset())[..12];
            var description = ReadDescription(Path.Combine(dir, "SKILL.md"));
            skills[name] = new SkillEntry(new SkillInfo(name, description, version, files.Count, size), zipBuffer.ToArray());
        }

        var tooling = Path.Combine(root, ToolingFolder);
        var tool = ReadOrEmpty(Path.Combine(tooling, "prmake-skills.sh"));
        var install = ReadOrEmpty(Path.Combine(tooling, "install.sh"));
        var toolVersion = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tool)))[..12];
        return new Loaded(skills, tool, install, toolVersion);
    }

    private static bool IsIgnored(string rel)
    {
        var parts = rel.Split('/');
        return parts.Any(p => p is ".venv" or "__pycache__" or ".DS_Store") || rel.EndsWith(".pyc");
    }

    private static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    /// <summary>description: do frontmatter da SKILL.md.</summary>
    private static string ReadDescription(string skillMd)
    {
        var text = File.ReadAllText(skillMd);
        var match = DescriptionPattern().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    [GeneratedRegex(@"^description:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex DescriptionPattern();
}
