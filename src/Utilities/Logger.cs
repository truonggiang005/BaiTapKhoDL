using System.Text;

namespace AirQualityDW.Utilities;

/// <summary>
/// Lớp tiện ích ghi log ra console và file
/// Phân biệt các mức: INFO, WARN, ERROR, SUCCESS
/// </summary>
public static class Logger
{
    private static string? _logFilePath;

    /// <summary>
    /// Khởi tạo logger với đường dẫn file log
    /// </summary>
    public static void Initialize(string projectRoot)
    {
        var logDir = Path.Combine(projectRoot, "logs");
        Directory.CreateDirectory(logDir);
        _logFilePath = Path.Combine(logDir, $"etl_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    /// <summary>
    /// Ghi thông tin chung
    /// </summary>
    public static void Info(string message)
    {
        WriteLog("INFO", message, ConsoleColor.White);
    }

    /// <summary>
    /// Ghi cảnh báo
    /// </summary>
    public static void Warn(string message)
    {
        WriteLog("WARN", message, ConsoleColor.Yellow);
    }

    /// <summary>
    /// Ghi lỗi
    /// </summary>
    public static void Error(string message)
    {
        WriteLog("LỖI", message, ConsoleColor.Red);
    }

    /// <summary>
    /// Ghi thành công
    /// </summary>
    public static void Success(string message)
    {
        WriteLog("OK", message, ConsoleColor.Green);
    }

    /// <summary>
    /// Ghi tiêu đề bước
    /// </summary>
    public static void StepHeader(string stepName)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n{"".PadRight(60, '=')}");
        Console.WriteLine($"  {stepName}");
        Console.WriteLine($"{"".PadRight(60, '=')}");
        Console.ResetColor();
        WriteToFile($"=== {stepName} ===");
    }

    /// <summary>
    /// Ghi thông tin vào console và file log
    /// </summary>
    private static void WriteLog(string level, string message, ConsoleColor color)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logLine = $"[{timestamp}] [{level}] {message}";

        Console.ForegroundColor = color;
        Console.WriteLine(logLine);
        Console.ResetColor();

        WriteToFile(logLine);
    }

    /// <summary>
    /// Ghi nội dung vào file log (nếu đã khởi tạo)
    /// </summary>
    private static void WriteToFile(string message)
    {
        if (_logFilePath == null) return;

        try
        {
            File.AppendAllText(_logFilePath, message + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Không để lỗi ghi log làm chết chương trình
        }
    }
}

/// <summary>
/// Lớp tiện ích kiểm tra dữ liệu đầu vào
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Kiểm tra chuỗi có phải giá trị thiếu không
    /// Bao gồm: null, rỗng, "-", "#NAME?", "N/A"
    /// </summary>
    public static bool IsMissingValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var trimmed = value.Trim();
        return trimmed == "-" || trimmed == "#NAME?" || trimmed == "N/A" || trimmed == "n/a";
    }

    /// <summary>
    /// Thử parse giá trị số, xử lý dấu phẩy phân cách hàng nghìn
    /// Ví dụ: "1,009.00" → 1009.00
    /// </summary>
    public static bool TryParseDecimal(string? raw, out decimal result)
    {
        result = 0;
        if (IsMissingValue(raw)) return false;

        // Loại bỏ dấu phẩy phân cách hàng nghìn
        var cleaned = raw!.Trim().Replace(",", "");

        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }
}

