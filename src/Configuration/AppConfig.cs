using Microsoft.Extensions.Configuration;

namespace AirQualityDW.Configuration;

/// <summary>
/// Lớp quản lý cấu hình ứng dụng, đọc từ appsettings.json
/// Tập trung hoá việc truy cập cấu hình để tránh truy cập rải rác
/// </summary>
public class AppConfig
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Chuỗi kết nối MySQL
    /// </summary>
    public string MySqlConnectionString { get; }

    /// <summary>
    /// Đường dẫn tương đối đến file CSV năm 2020
    /// </summary>
    public string File2020Path { get; }

    /// <summary>
    /// Đường dẫn tương đối đến file CSV năm 2021
    /// </summary>
    public string File2021Path { get; }

    /// <summary>
    /// Đường dẫn tương đối đến file Excel đầu ra
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Thư mục gốc của dự án (chứa data/, sql/, src/)
    /// </summary>
    public string ProjectRoot { get; }

    public AppConfig(string projectRoot)
    {
        ProjectRoot = projectRoot;

        // Tìm và đọc appsettings.json
        _configuration = BuildConfiguration(projectRoot);

        MySqlConnectionString = _configuration.GetConnectionString("MySQL")
            ?? throw new InvalidOperationException(
                "Không tìm thấy connection string 'MySQL' trong appsettings.json. " +
                "Hãy copy appsettings.example.json thành appsettings.json và cập nhật mật khẩu MySQL.");

        File2020Path = _configuration["DataPaths:File2020"] ?? "data/aqi_airqualitydata_2020_en.csv";
        File2021Path = _configuration["DataPaths:File2021"] ?? "data/historical_air_quality_2021_en.csv";
        OutputPath = _configuration["OutputPath"] ?? "output/BaoCao_KhoDuLieu_ChatLuongKhongKhi.xlsx";
    }

    /// <summary>
    /// Chuyển đường dẫn tương đối thành tuyệt đối dựa trên thư mục gốc dự án
    /// </summary>
    public string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;
        return Path.GetFullPath(Path.Combine(ProjectRoot, relativePath));
    }

    /// <summary>
    /// Xây dựng đối tượng cấu hình từ appsettings.json
    /// </summary>
    private static IConfiguration BuildConfiguration(string projectRoot)
    {
        var builder = new ConfigurationBuilder();

        // Ưu tiên 1: thư mục thực thi (bin/Debug/net8.0)
        var baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "appsettings.json")))
        {
            builder.SetBasePath(baseDir);
        }
        // Ưu tiên 2: thư mục src
        else if (File.Exists(Path.Combine(projectRoot, "src", "appsettings.json")))
        {
            builder.SetBasePath(Path.Combine(projectRoot, "src"));
        }
        // Ưu tiên 3: thư mục gốc dự án
        else if (File.Exists(Path.Combine(projectRoot, "appsettings.json")))
        {
            builder.SetBasePath(projectRoot);
        }
        else
        {
            builder.SetBasePath(baseDir);
        }

        builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        return builder.Build();
    }

    /// <summary>
    /// Tìm thư mục gốc của dự án bằng cách duyệt ngược cây thư mục
    /// Tìm thư mục chứa cả data/ và sql/
    /// </summary>
    public static string FindProjectRoot()
    {
        // Duyệt ngược từ thư mục thực thi
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data")) &&
                Directory.Exists(Path.Combine(dir.FullName, "sql")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        // Kiểm tra thư mục làm việc hiện tại
        var current = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(current, "data")) &&
            Directory.Exists(Path.Combine(current, "sql")))
        {
            return current;
        }

        // Thử lên 1 cấp (trường hợp chạy từ src/)
        var parent1 = Path.GetFullPath(Path.Combine(current, ".."));
        if (Directory.Exists(Path.Combine(parent1, "data")) &&
            Directory.Exists(Path.Combine(parent1, "sql")))
        {
            return parent1;
        }

        // Thử lên 2 cấp (phòng trường hợp thư mục con sâu hơn)
        var parent2 = Path.GetFullPath(Path.Combine(current, "..", ".."));
        if (Directory.Exists(Path.Combine(parent2, "data")) &&
            Directory.Exists(Path.Combine(parent2, "sql")))
        {
            return parent2;
        }

        return current;
    }
}

