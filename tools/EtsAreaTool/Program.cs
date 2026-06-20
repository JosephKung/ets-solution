using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ets.Infrastructure.Security;

namespace EtsAreaTool;

internal class Program
{
    private const string Usage = """

        ETS Area Whitelist Encryption Tool (v1.0)
        ─────────────────────────────────────────

        Commands:

          generate-key
              Generate a new 256-bit AES key (printed as Base64).
              Store the output in environment variable: ETS_AREA_WHITELIST_KEY

          encrypt --input  <plain.json>
                  --output <area_whitelist.enc>
                  --key    <Base64Key>
              Encrypt a whitelist JSON file into a binary .enc file.

          inspect --input  <area_whitelist.enc>
                  --key    <Base64Key>
              Decrypt and display the contents of a whitelist file (audit).

        Whitelist JSON format:

          {
            "event_areaList": ["林口院區", "台中院區"],
            "generated_by":   "IT-Manager-Joseph"
          }

          - event_areaList: array of allowed area names.
            Empty array [] = UNRESTRICTED (any event_area accepted).

        Examples:

          ets-area-tool generate-key
          ets-area-tool encrypt -i whitelist.json -o area_whitelist.enc -k <key>
          ets-area-tool inspect -i area_whitelist.enc -k <key>

        """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static int Main(string[] args)
    {
        // 確保 Console 能正確輸出中文 (Windows cmd 預設為 CP950,需切 UTF-8)
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "generate-key" or "gen-key" or "g" => GenerateKey(),
                "encrypt" or "enc" or "e" => Encrypt(args),
                "inspect" or "view" or "i" => Inspect(args),
                "-h" or "--help" or "help" => ShowUsage(),
                _ => ShowUsage(unknownCommand: args[0])
            };
        }
        catch (CryptographicException ex)
        {
            Console.Error.WriteLine($"[ERROR] Cryptographic failure: {ex.Message}");
            Console.Error.WriteLine("  Possible causes:");
            Console.Error.WriteLine("    1. The key does not match the one used for encryption");
            Console.Error.WriteLine("    2. The encrypted file has been tampered with or corrupted");
            return 2;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"[ERROR] File not found: {ex.FileName}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 2;
        }
    }

    static int ShowUsage(string? unknownCommand = null)
    {
        if (unknownCommand != null)
            Console.Error.WriteLine($"[ERROR] Unknown command: {unknownCommand}");
        Console.WriteLine(Usage);
        return unknownCommand != null ? 1 : 0;
    }

    // ---------------------------------------------------------------
    // generate-key
    // ---------------------------------------------------------------
    static int GenerateKey()
    {
        var key = AesGcmHelper.GenerateKey();
        var keyB64 = Convert.ToBase64String(key);

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine(" Generated AES-256 key (Base64):");
        Console.WriteLine();
        Console.WriteLine($"   {keyB64}");
        Console.WriteLine();
        Console.WriteLine(" ⚠  KEEP THIS KEY SECURE.");
        Console.WriteLine();
        Console.WriteLine(" Next steps:");
        Console.WriteLine("   1. Create a whitelist.json file");
        Console.WriteLine("   2. Run:  ets-area-tool encrypt -i whitelist.json \\");
        Console.WriteLine("              -o area_whitelist.enc -k \"<above key>\"");
        Console.WriteLine("   3. Deploy:");
        Console.WriteLine("        - Copy area_whitelist.enc to ETS server");
        Console.WriteLine("        - Set env var:  ETS_AREA_WHITELIST_KEY=\"<above key>\"");
        Console.WriteLine("        - Restart ETS service");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        return 0;
    }

    // ---------------------------------------------------------------
    // encrypt
    // ---------------------------------------------------------------
    static int Encrypt(string[] args)
    {
        var input = GetArg(args, "--input", "-i")
                     ?? throw new ArgumentException("Missing --input <whitelist.json>");
        var output = GetArg(args, "--output", "-o")
                     ?? throw new ArgumentException("Missing --output <area_whitelist.enc>");
        var keyB64 = GetArg(args, "--key", "-k")
                     ?? throw new ArgumentException("Missing --key <Base64Key>");

        if (!File.Exists(input))
            throw new FileNotFoundException($"Whitelist JSON not found: {input}", input);

        // 解析 + 驗證白名單 JSON 格式
        var jsonText = File.ReadAllText(input, Encoding.UTF8);
        AreaWhitelistFile dto;
        try
        {
            dto = JsonSerializer.Deserialize<AreaWhitelistFile>(jsonText, JsonOpts)
                  ?? throw new InvalidDataException("JSON parses to null");
        }
        catch (JsonException jex)
        {
            throw new InvalidDataException($"Invalid JSON format: {jex.Message}");
        }

        if (dto.EventAreaList is null)
            throw new InvalidDataException("Missing required field 'event_areaList' (use [] for unrestricted)");

        // 補上 metadata(若 input 未填)
        var enriched = dto with
        {
            GeneratedAt = dto.GeneratedAt ?? DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            GeneratedBy = dto.GeneratedBy ?? Environment.UserName,
            Version = dto.Version <= 0 ? 1 : dto.Version
        };

        var canonical = JsonSerializer.Serialize(enriched, JsonOpts);

        // 解析金鑰
        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyB64);
        }
        catch (FormatException)
        {
            throw new ArgumentException("--key must be a valid Base64 string");
        }
        if (key.Length != 32)
            throw new ArgumentException($"--key must decode to exactly 32 bytes (got {key.Length})");

        // 加密
        var encrypted = AesGcmHelper.Encrypt(canonical, key);

        // 寫檔
        var outDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        File.WriteAllBytes(output, encrypted);

        // 報告
        var areaList = enriched.EventAreaList!;
        var mode = areaList.Count == 0
            ? "UNRESTRICTED (empty list — any event_area accepted)"
            : "RESTRICTED";

        Console.WriteLine();
        Console.WriteLine("✅ Encrypted whitelist written successfully.");
        Console.WriteLine();
        Console.WriteLine($"   Output file:     {Path.GetFullPath(output)}");
        Console.WriteLine($"   File size:       {new FileInfo(output).Length} bytes");
        Console.WriteLine($"   Mode:            {mode}");
        Console.WriteLine($"   Entry count:     {areaList.Count}");
        Console.WriteLine($"   Generated by:    {enriched.GeneratedBy}");
        Console.WriteLine($"   Generated at:    {enriched.GeneratedAt}");
        Console.WriteLine($"   Version:         {enriched.Version}");
        if (areaList.Count > 0)
        {
            Console.WriteLine($"   Whitelist:");
            foreach (var area in areaList)
                Console.WriteLine($"     • {area}");
        }
        Console.WriteLine();
        return 0;
    }

    // ---------------------------------------------------------------
    // inspect
    // ---------------------------------------------------------------
    static int Inspect(string[] args)
    {
        var input = GetArg(args, "--input", "-i")
                     ?? throw new ArgumentException("Missing --input <area_whitelist.enc>");
        var keyB64 = GetArg(args, "--key", "-k")
                     ?? throw new ArgumentException("Missing --key <Base64Key>");

        if (!File.Exists(input))
            throw new FileNotFoundException($"Encrypted file not found: {input}", input);

        byte[] key;
        try { key = Convert.FromBase64String(keyB64); }
        catch (FormatException) { throw new ArgumentException("--key must be a valid Base64 string"); }
        if (key.Length != 32)
            throw new ArgumentException($"--key must decode to exactly 32 bytes (got {key.Length})");

        var encrypted = File.ReadAllBytes(input);
        var plaintext = AesGcmHelper.Decrypt(encrypted, key);
        var dto = JsonSerializer.Deserialize<AreaWhitelistFile>(plaintext, JsonOpts)
                        ?? throw new InvalidDataException("Decryption succeeded but JSON parse failed");

        var mode = dto.EventAreaList?.Count == 0
            ? "UNRESTRICTED (any event_area allowed)"
            : "RESTRICTED";

        Console.WriteLine();
        Console.WriteLine($"📂 Encrypted file: {Path.GetFullPath(input)}");
        Console.WriteLine($"   File size:       {encrypted.Length} bytes");
        Console.WriteLine();
        Console.WriteLine($"   Version:         {dto.Version}");
        Console.WriteLine($"   Generated at:    {dto.GeneratedAt}");
        Console.WriteLine($"   Generated by:    {dto.GeneratedBy}");
        Console.WriteLine($"   Mode:            {mode}");
        Console.WriteLine($"   Entry count:     {dto.EventAreaList?.Count ?? 0}");
        Console.WriteLine();
        if (dto.EventAreaList?.Count > 0)
        {
            Console.WriteLine("   Whitelist:");
            foreach (var area in dto.EventAreaList)
                Console.WriteLine($"     • {area}");
            Console.WriteLine();
        }
        return 0;
    }

    // ---------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------
    static string? GetArg(string[] args, string longName, string shortName)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == longName || args[i] == shortName)
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// 白名單檔 schema(明文 JSON)。
    /// </summary>
    public record AreaWhitelistFile(
        [property: JsonPropertyName("event_areaList")] List<string>? EventAreaList,
        [property: JsonPropertyName("generated_at")] string? GeneratedAt = null,
        [property: JsonPropertyName("generated_by")] string? GeneratedBy = null,
        [property: JsonPropertyName("version")] int Version = 0);
}
