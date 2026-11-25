using PromptCrew.Api.Services;

namespace PromptCrew.Api;

public sealed record StorageSettings(string DirectoryPath, bool Demo)
{
    public string DatabasePath => Path.Combine(DirectoryPath, "promptcrew.db");
    public static StorageSettings From(IConfiguration config)
    {
        var demo = config.GetValue<bool>("PromptCrew:DemoMode");
        var path = config["PromptCrew:DataDirectory"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PromptCrew", demo ? "demo" : "personal");
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(path);
        return new(path, demo);
    }
    public AdminCredential ReadCredential(IConfiguration config)
    {
        var token = config["PromptCrew:AdminKey"];
        if (string.IsNullOrWhiteSpace(token))
        {
            var path = config["PromptCrew:AdminKeyFile"] ?? Path.Combine(DirectoryPath, "admin.key");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream);
                    writer.Write(AdminCredential.NewToken());
                }
                catch (IOException) when (File.Exists(path)) { }
            }
            token = File.ReadAllText(path).Trim();
        }
        if (token.Length < 32) throw new InvalidOperationException("管理员密钥至少需要 32 个字符。");
        return new(token);
    }
}
