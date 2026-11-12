using System.Data;
using System.Globalization;
using System.Text;
using AirQualityDW.Configuration;
using AirQualityDW.Export;
using AirQualityDW.Extract;
using AirQualityDW.Load;
using AirQualityDW.Models;
using AirQualityDW.Olap;
using AirQualityDW.Services;
using AirQualityDW.Transform;
using AirQualityDW.Utilities;
using CsvHelper;
using CsvHelper.Configuration;
using MySqlConnector;

namespace AirQualityDW;

/// <summary>
/// Điểm khởi chạy chính của chương trình Kho Dữ Liệu Chất Lượng Không Khí Việt Nam.
/// Menu 8 chức năng theo đúng rule.md, có kiểm tra phụ thuộc giữa các bước.
/// </summary>
public class Program
{
    private static AppConfig? _appConfig;
    private static DatabaseService? _db;
    private static List<OlapQueryResult>? _cachedOlapResults;

    // Cờ trạng thái để kiểm tra phụ thuộc giữa các bước
    private static bool _step1Done = false; // Đã kiểm tra cấu hình
    private static bool _step3Done = false; // Đã tạo DB + bảng
    private static bool _step4Done = false; // Đã chạy ETL
    private static bool _step6Done = false; // Đã chạy OLAP

    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = "Kho Dữ Liệu Chất Lượng Không Khí Việt Nam 2020-2021 — Nhóm 4";

        try
        {
            // Xác định thư mục gốc dự án và khởi tạo cấu hình
            string projectRoot = AppConfig.FindProjectRoot();
            _appConfig = new AppConfig(projectRoot);
            _db = new DatabaseService(_appConfig.MySqlConnectionString);
            Logger.Initialize(projectRoot);

            Logger.Info($"Thư mục gốc dự án: {projectRoot}");

            // Nếu truyền tham số dòng lệnh (ví dụ: dotnet run -- 8), thực thi trực tiếp không cần menu
            if (args.Length > 0)
            {
                var argChoice = args[0].Trim();
                Logger.Info($"Thực thi chức năng từ tham số dòng lệnh: {argChoice}");
                ExecuteChoice(argChoice);
                return;
            }

            bool running = true;
            while (running)
            {
                PrintMenu();
                Console.Write("  Chọn chức năng (0-8): ");
                var choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                if (string.IsNullOrEmpty(choice) && Console.IsInputRedirected)
                {
                    break;
                }

                if (choice == "0")
                {
                    Logger.Info("Cảm ơn bạn đã sử dụng chương trình. Tạm biệt!");
                    running = false;
                    break;
                }

                ExecuteChoice(choice ?? "");

                if (running && !Console.IsInputRedirected)
                {
                    Console.WriteLine("\nNhấn phím bất kỳ để tiếp tục...");
                    Console.ReadKey(true);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi khởi tạo chương trình: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("\nNhấn phím bất kỳ để thoát...");
                Console.ReadKey();
            }
        }
    }

    /// <summary>
    /// Điều phối thực thi chức năng theo mã lựa chọn
    /// </summary>
    private static void ExecuteChoice(string choice)
    {
        switch (choice)
        {
            case "1": Step1_CheckConfig();       break;
            case "2": Step2_SurveyCSV();          break;
            case "3": Step3_CreateDatabase();      break;
            case "4": Step4_RunETL();              break;
            case "5": Step5_Reconciliation();      break;
            case "6": Step6_RunOlap();             break;
            case "7": Step7_ExportExcel();         break;
            case "8": Step8_RunAll();              break;
            default:
                Logger.Warn($"Lựa chọn '{choice}' không hợp lệ, vui lòng chọn từ 0 đến 8.");
                break;
        }
    }

    /// <summary>
    /// In menu chính theo đúng cấu trúc rule.md (8 chức năng)
    /// </summary>
    private static void PrintMenu()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   KHO DỮ LIỆU CHẤT LƯỢNG KHÔNG KHÍ VIỆT NAM         ║");
        Console.WriteLine("║   Bài giữa kì môn Kho Dữ Liệu — Nhóm 4              ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════╣");
        Console.WriteLine("║   1. Kiểm tra cấu hình và kết nối MySQL              ║");
        Console.WriteLine("║   2. Khảo sát file CSV                                ║");
        Console.WriteLine("║   3. Tạo staging và Data Warehouse                    ║");
        Console.WriteLine("║   4. Chạy ETL (Extract → Transform → Load)            ║");
        Console.WriteLine("║   5. Đối soát dữ liệu                                ║");
        Console.WriteLine("║   6. Chạy truy vấn OLAP                              ║");
        Console.WriteLine("║   7. Xuất báo cáo Excel                              ║");
        Console.WriteLine("║   8. Chạy quy trình tổng thể (3 → 4 → 5 → 6 → 7)    ║");
        Console.WriteLine("║   0. Thoát                                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        // Hiển thị trạng thái hiện tại
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Trạng thái: ");
        Console.Write(_step1Done ? "[✓ Cấu hình] " : "[○ Cấu hình] ");
        Console.Write(_step3Done ? "[✓ DB] " : "[○ DB] ");
        Console.Write(_step4Done ? "[✓ ETL] " : "[○ ETL] ");
        Console.Write(_step6Done ? "[✓ OLAP] " : "[○ OLAP] ");
        Console.WriteLine();
        Console.ResetColor();
    }

    // ===================================================================
    // BƯỚC 1: Kiểm tra cấu hình và kết nối MySQL
    // ===================================================================
    private static void Step1_CheckConfig()
    {
        Logger.StepHeader("BƯỚC 1: KIỂM TRA CẤU HÌNH VÀ KẾT NỐI MYSQL");

        try
        {
            // 1. Kiểm tra thư mục dự án
            Logger.Info($"Thư mục gốc: {_appConfig!.ProjectRoot}");

            // 2. Kiểm tra file CSV
            string path2020 = _appConfig.ResolvePath(_appConfig.File2020Path);
            string path2021 = _appConfig.ResolvePath(_appConfig.File2021Path);

            Logger.Info($"File 2020: {path2020} — {(File.Exists(path2020) ? "Tồn tại ✓" : "KHÔNG TÌM THẤY ✗")}");
            Logger.Info($"File 2021: {path2021} — {(File.Exists(path2021) ? "Tồn tại ✓" : "KHÔNG TÌM THẤY ✗")}");

            if (!File.Exists(path2020) || !File.Exists(path2021))
            {
                Logger.Error("Thiếu file CSV trong thư mục data/. Vui lòng đặt file vào đúng vị trí.");
                return;
            }

            // 3. Kiểm tra file SQL
            string[] sqlFiles = { "01_create_staging.sql", "02_create_dw.sql", "03_olap_queries.sql" };
            foreach (var sf in sqlFiles)
            {
                string sqlPath = Path.Combine(_appConfig.ProjectRoot, "sql", sf);
                Logger.Info($"File SQL {sf}: {(File.Exists(sqlPath) ? "Tồn tại ✓" : "KHÔNG TÌM THẤY ✗")}");
            }

            // 4. Kiểm tra thư mục output
            string outputDir = Path.GetDirectoryName(_appConfig.ResolvePath(_appConfig.OutputPath))!;
            Directory.CreateDirectory(outputDir);
            Logger.Info($"Thư mục output: {outputDir} ✓");

            // 5. Kiểm tra kết nối MySQL Server
            Logger.Info("Đang kiểm tra kết nối MySQL Server...");
            var connBuilder = new MySqlConnectionStringBuilder(_appConfig.MySqlConnectionString);
            string targetDb = connBuilder.Database;
            connBuilder.Database = ""; // Kết nối server để kiểm tra, không yêu cầu database phải tạo trước
            using var conn = new MySqlConnection(connBuilder.ConnectionString);
            conn.Open();
            using var cmd = new MySqlCommand("SELECT VERSION()", conn);
            var version = cmd.ExecuteScalar()?.ToString();
            Logger.Success($"Kết nối MySQL Server thành công! Phiên bản: {version}");
            Logger.Info($"Cơ sở dữ liệu mục tiêu: `{targetDb}`");

            _step1Done = true;
            Logger.Success("Kiểm tra cấu hình hoàn tất — mọi thứ sẵn sàng.");
        }
        catch (MySqlException ex)
        {
            Logger.Error($"Không thể kết nối MySQL: {ex.Message}");
            Logger.Warn("Kiểm tra lại: MySQL đang chạy? Mật khẩu đúng trong appsettings.json?");
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi kiểm tra cấu hình: {ex.Message}");
        }
    }

    // ===================================================================
    // BƯỚC 2: Khảo sát file CSV
    // ===================================================================
    private static void Step2_SurveyCSV()
    {
        Logger.StepHeader("BƯỚC 2: KHẢO SÁT FILE CSV NGUỒN");

        try
        {
            string path2020 = _appConfig!.ResolvePath(_appConfig.File2020Path);
            string path2021 = _appConfig.ResolvePath(_appConfig.File2021Path);

            if (File.Exists(path2020))
            {
                SurveyFile2020(path2020);
            }
            else
            {
                Logger.Error($"Không tìm thấy file 2020: {path2020}");
            }

            Console.WriteLine();

            if (File.Exists(path2021))
            {
                SurveyFile2021(path2021);
            }
            else
            {
                Logger.Error($"Không tìm thấy file 2021: {path2021}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi khảo sát CSV: {ex.Message}");
        }
    }

    /// <summary>
    /// Khảo sát chi tiết file CSV 2020
    /// </summary>
    private static void SurveyFile2020(string path)
    {
        Logger.Info($"=== Khảo sát: {Path.GetFileName(path)} ===");
        var fileInfo = new FileInfo(path);
        Logger.Info($"  Dung lượng: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:N1} KB)");

        using var reader = new StreamReader(path, Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        });

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord!;
        Logger.Info($"  Số cột: {headers.Length}");
        Logger.Info($"  Tên cột: {string.Join(", ", headers)}");

        int rowCount = 0;
        var cities = new HashSet<string>();
        var species = new HashSet<string>();
        string? minDate = null, maxDate = null;
        var missingCounts = new Dictionary<string, int>();
        foreach (var h in headers) missingCounts[h] = 0;

        while (csv.Read())
        {
            rowCount++;
            for (int i = 0; i < headers.Length; i++)
            {
                var val = csv.GetField(i);
                if (string.IsNullOrWhiteSpace(val) || val == "-")
                    missingCounts[headers[i]]++;
            }

            var city = csv.GetField("City");
            var specie = csv.GetField("Specie");
            var date = csv.GetField("Date");

            if (!string.IsNullOrWhiteSpace(city)) cities.Add(city);
            if (!string.IsNullOrWhiteSpace(specie)) species.Add(specie);
            if (!string.IsNullOrWhiteSpace(date))
            {
                if (minDate == null || string.Compare(date, minDate) < 0) minDate = date;
                if (maxDate == null || string.Compare(date, maxDate) > 0) maxDate = date;
            }
        }

        Logger.Info($"  Số dòng dữ liệu: {rowCount:N0}");
        Logger.Info($"  Khoảng thời gian: {minDate} → {maxDate}");
        Logger.Info($"  Thành phố ({cities.Count}): {string.Join(", ", cities.OrderBy(c => c))}");
        Logger.Info($"  Loại chất ({species.Count}): {string.Join(", ", species.OrderBy(s => s))}");

        Logger.Info("  Tỷ lệ giá trị thiếu:");
        foreach (var h in headers)
        {
            double rate = rowCount > 0 ? (double)missingCounts[h] / rowCount * 100 : 0;
            if (missingCounts[h] > 0)
                Logger.Info($"    {h}: {missingCounts[h]:N0}/{rowCount:N0} ({rate:F2}%)");
            else
                Logger.Info($"    {h}: 0 (0%)");
        }
    }

    /// <summary>
    /// Khảo sát chi tiết file CSV 2021
    /// </summary>
    private static void SurveyFile2021(string path)
    {
        Logger.Info($"=== Khảo sát: {Path.GetFileName(path)} ===");
        var fileInfo = new FileInfo(path);
        Logger.Info($"  Dung lượng: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0:N1} KB)");

        using var reader = new StreamReader(path, Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        });

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord!;
        Logger.Info($"  Số cột: {headers.Length}");
        Logger.Info($"  Tên cột: {string.Join(", ", headers)}");

        int rowCount = 0;
        var stations = new HashSet<string>();
        var dominants = new HashSet<string>();
        string? minTime = null, maxTime = null;
        int emptyStationName = 0;
        int emptyDataTime = 0;
        var missingCounts = new Dictionary<string, int>();
        foreach (var h in headers) missingCounts[h] = 0;

        while (csv.Read())
        {
            rowCount++;
            for (int i = 0; i < headers.Length; i++)
            {
                var val = csv.GetField(i);
                if (ValidationHelper.IsMissingValue(val))
                    missingCounts[headers[i]]++;
            }

            var stationName = csv.GetField("Station name");
            var dataTime = csv.GetField("Data Time S");
            var dominant = csv.GetField("Dominent pollutant");

            if (string.IsNullOrWhiteSpace(stationName)) emptyStationName++;
            else stations.Add(stationName);

            if (string.IsNullOrWhiteSpace(dataTime)) emptyDataTime++;
            else
            {
                if (minTime == null || string.Compare(dataTime, minTime) < 0) minTime = dataTime;
                if (maxTime == null || string.Compare(dataTime, maxTime) > 0) maxTime = dataTime;
            }

            if (!string.IsNullOrWhiteSpace(dominant)) dominants.Add(dominant);
        }

        Logger.Info($"  Số dòng dữ liệu: {rowCount:N0}");
        Logger.Info($"  Khoảng thời gian: {minTime} → {maxTime}");
        Logger.Info($"  Số trạm: {stations.Count}");
        Logger.Info($"  Chất ô nhiễm chủ đạo: {string.Join(", ", dominants.OrderBy(d => d))}");
        Logger.Warn($"  Dòng thiếu Station name: {emptyStationName}");
        Logger.Warn($"  Dòng thiếu Data Time S: {emptyDataTime}");

        Logger.Info("  Tỷ lệ giá trị thiếu (thiếu hoặc '-' hoặc '#NAME?'):");
        var importantCols = new[] { "AQI index", "CO", "Humidity", "NO2", "O3", "Pressure", "PM10", "PM2.5", "SO2", "Temperature", "Wind" };
        foreach (var h in importantCols)
        {
            if (missingCounts.ContainsKey(h))
            {
                double rate = rowCount > 0 ? (double)missingCounts[h] / rowCount * 100 : 0;
                Logger.Info($"    {h}: {missingCounts[h]:N0}/{rowCount:N0} ({rate:F2}%)");
            }
        }
    }

    // ===================================================================
    // BƯỚC 3: Tạo staging và Data Warehouse
    // ===================================================================
    private static void Step3_CreateDatabase()
    {
        if (!_step1Done)
        {
            Logger.Warn("Chưa kiểm tra cấu hình (Bước 1). Vui lòng chạy Bước 1 trước.");
            return;
        }

        Logger.StepHeader("BƯỚC 3: TẠO STAGING VÀ DATA WAREHOUSE");

        try
        {
            _db!.EnsureDatabaseCreated();

            string stagingSql = Path.Combine(_appConfig!.ProjectRoot, "sql", "01_create_staging.sql");
            string dwSql = Path.Combine(_appConfig.ProjectRoot, "sql", "02_create_dw.sql");

            Logger.Info($"Đang thực thi {Path.GetFileName(stagingSql)}...");
            _db.ExecuteSqlFile(stagingSql);

            Logger.Info($"Đang thực thi {Path.GetFileName(dwSql)}...");
            _db.ExecuteSqlFile(dwSql);

            _step3Done = true;
            Logger.Success("Tạo cơ sở dữ liệu và các bảng hoàn tất!");
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi tạo database: {ex.Message}");
        }
    }

    // ===================================================================
    // BƯỚC 4: Chạy ETL (Extract → Transform → Load)
    // ===================================================================
    private static void Step4_RunETL()
    {
        if (!_step3Done)
        {
            Logger.Warn("Chưa tạo database (Bước 3). Vui lòng chạy Bước 3 trước.");
            return;
        }

        Logger.StepHeader("BƯỚC 4: CHẠY ETL (EXTRACT → TRANSFORM → LOAD)");

        try
        {
            string runId = $"RUN_{DateTime.Now:yyyyMMdd_HHmmss}";
            Logger.Info($"Mã đợt chạy ETL: {runId}");

            // --- EXTRACT ---
            Logger.Info("── EXTRACT: Đọc CSV → Staging ──");
            var extractor = new CsvExtractor();
            string path2020 = _appConfig!.ResolvePath(_appConfig.File2020Path);
            string path2021 = _appConfig.ResolvePath(_appConfig.File2021Path);

            var (read20, load20) = extractor.ExtractFile2020(_db!, path2020, runId);
            Logger.Info($"  File 2020: đọc {read20:N0} dòng → nạp {load20:N0} vào staging_2020");

            var (read21, load21) = extractor.ExtractFile2021(_db!, path2021, runId);
            Logger.Info($"  File 2021: đọc {read21:N0} dòng → nạp {load21:N0} vào staging_2021");

            // --- TRANSFORM ---
            Logger.Info("── TRANSFORM: Staging → Dữ liệu sạch ──");
            var transformer = new DataTransformer();
            var recs2020 = transformer.TransformAndLoad2020(_db!, runId);
            Logger.Info($"  File 2020: {recs2020.Count:N0} bản ghi hợp lệ sau transform");

            var recs2021 = transformer.TransformAndLoad2021(_db!, runId);
            Logger.Info($"  File 2021: {recs2021.Count:N0} bản ghi hợp lệ sau transform");

            var allRecords = new List<TransformedRecord>(recs2020.Count + recs2021.Count);
            allRecords.AddRange(recs2020);
            allRecords.AddRange(recs2021);
            Logger.Info($"  Tổng cộng: {allRecords.Count:N0} bản ghi sạch");

            // --- LOAD ---
            Logger.Info("── LOAD: Nạp vào kho dữ liệu ──");
            var loader = new DataLoader();
            loader.PopulateDimensions(_db!, allRecords);
            loader.LoadFacts(_db!, allRecords, runId);
            loader.PopulateAggregateTable(_db!);

            _step4Done = true;
            Logger.Success($"ETL hoàn tất! Mã đợt: {runId}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi ETL: {ex.Message}");
        }
    }

    // ===================================================================
    // BƯỚC 5: Đối soát dữ liệu
    // ===================================================================
    private static void Step5_Reconciliation()
    {
        if (!_step4Done)
        {
            Logger.Warn("Chưa chạy ETL (Bước 4). Vui lòng chạy Bước 4 trước.");
            return;
        }

        Logger.StepHeader("BƯỚC 5: ĐỐI SOÁT DỮ LIỆU");

        try
        {
            // 1. Số dòng ở từng tầng
            Logger.Info("── 1. Số dòng ở mỗi tầng ──");
            var staging2020 = _db!.ExecuteScalar<long>("SELECT COUNT(*) FROM staging_2020");
            var staging2021 = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM staging_2021");
            var factCount = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM fact_air_quality");
            var aggCount = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM agg_monthly_city");

            Logger.Info($"  staging_2020:      {staging2020:N0} dòng");
            Logger.Info($"  staging_2021:      {staging2021:N0} dòng");
            Logger.Info($"  fact_air_quality:  {factCount:N0} dòng");
            Logger.Info($"  agg_monthly_city:  {aggCount:N0} dòng");

            // 2. Kiểm tra dimension
            Logger.Info("── 2. Số bản ghi dimension ──");
            var dimDate = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM dim_date WHERE date_key > 0");
            var dimLoc = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM dim_location WHERE location_key > 0");
            var dimPol = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM dim_pollutant WHERE pollutant_key > 0");
            var dimAqi = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM dim_aqi_level WHERE aqi_level_key > 0");

            Logger.Info($"  dim_date:          {dimDate:N0} ngày (không kể Unknown)");
            Logger.Info($"  dim_location:      {dimLoc:N0} địa điểm");
            Logger.Info($"  dim_pollutant:     {dimPol:N0} chất ô nhiễm/khí tượng");
            Logger.Info($"  dim_aqi_level:     {dimAqi:N0} mức AQI");

            // 3. ETL log — tổng hợp từ etl_log
            Logger.Info("── 3. Nhật ký ETL (etl_log) ──");
            _db.ExecuteReader(@"
                SELECT source_file, step_name, 
                       SUM(rows_read) AS total_read, 
                       SUM(rows_loaded) AS total_loaded, 
                       SUM(rows_rejected) AS total_rejected,
                       SUM(duration_ms) AS total_ms
                FROM etl_log 
                GROUP BY source_file, step_name
                ORDER BY log_id", reader =>
            {
                string src = reader.IsDBNull(0) ? "(N/A)" : reader.GetString(0);
                string step = reader.IsDBNull(1) ? "(N/A)" : reader.GetString(1);
                long read = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                long loaded = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                long rejected = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                long ms = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);

                Logger.Info($"  [{step}] {Path.GetFileName(src)}: đọc={read:N0}, nạp={loaded:N0}, từ chối={rejected:N0}, {ms}ms");
            });

            // 4. Lý do từ chối (top 10)
            Logger.Info("── 4. Lý do từ chối (top 10) ──");
            _db.ExecuteReader(@"
                SELECT reject_reason, COUNT(*) AS cnt 
                FROM etl_reject_log 
                GROUP BY reject_reason 
                ORDER BY cnt DESC 
                LIMIT 10", reader =>
            {
                string reason = reader.IsDBNull(0) ? "(N/A)" : reader.GetString(0);
                long cnt = reader.GetInt64(1);
                Logger.Info($"  {cnt,6:N0}  {reason}");
            });

            // 5. Kiểm tra tính nhất quán
            Logger.Info("── 5. Kiểm tra tham chiếu ──");
            var orphanFact = _db.ExecuteScalar<long>(@"
                SELECT COUNT(*) FROM fact_air_quality f 
                LEFT JOIN dim_date d ON f.date_key = d.date_key 
                WHERE d.date_key IS NULL");
            var orphanLoc = _db.ExecuteScalar<long>(@"
                SELECT COUNT(*) FROM fact_air_quality f 
                LEFT JOIN dim_location l ON f.location_key = l.location_key 
                WHERE l.location_key IS NULL");

            Logger.Info($"  Fact không có dim_date:     {orphanFact:N0} {(orphanFact == 0 ? "✓" : "✗")}");
            Logger.Info($"  Fact không có dim_location: {orphanLoc:N0} {(orphanLoc == 0 ? "✓" : "✗")}");

            // 6. Chênh lệch staging vs fact
            Logger.Info("── 6. Chênh lệch staging → fact ──");
            long totalStaging = staging2020 + staging2021;
            long totalReject = _db.ExecuteScalar<long>("SELECT COUNT(*) FROM etl_reject_log");
            Logger.Info($"  Tổng staging:                {totalStaging:N0}");
            Logger.Info($"  Tổng fact:                   {factCount:N0}");
            Logger.Info($"  Tổng dòng bị từ chối:        {totalReject:N0}");
            Logger.Info($"  Giải thích: staging chứa dữ liệu thô (mỗi file 1 bảng).");
            Logger.Info($"  File 2020 (unpivoted): 1 dòng staging ≈ 1 dòng fact (sau lọc wind-gust).");
            Logger.Info($"  File 2021 (pivoted): 1 dòng staging → nhiều dòng fact (sau unpivot), rồi aggregate theo ngày.");

            Logger.Success("Đối soát hoàn tất.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi đối soát: {ex.Message}");
        }
    }

    // ===================================================================
    // BƯỚC 6: Chạy truy vấn OLAP
    // ===================================================================
    private static void Step6_RunOlap()
    {
        if (!_step4Done)
        {
            Logger.Warn("Chưa chạy ETL (Bước 4). Vui lòng chạy Bước 4 trước.");
            return;
        }

        Logger.StepHeader("BƯỚC 6: CHẠY TRUY VẤN OLAP");

        try
        {
            var olapExecutor = new OlapExecutor();
            string olapSqlPath = Path.Combine(_appConfig!.ProjectRoot, "sql", "03_olap_queries.sql");
            _cachedOlapResults = olapExecutor.ExecuteAllQueries(_db!, olapSqlPath);

            _step6Done = true;
            Logger.Success($"Đã thực thi xong {_cachedOlapResults.Count} truy vấn OLAP!");
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi OLAP: {ex.Message}");
        }
    }

    // ===================================================================
    // BƯỚC 7: Xuất báo cáo Excel
    // ===================================================================
    private static void Step7_ExportExcel()
    {
        if (!_step6Done)
        {
            Logger.Warn("Chưa chạy OLAP (Bước 6). Vui lòng chạy Bước 6 trước.");
            return;
        }

        Logger.StepHeader("BƯỚC 7: XUẤT BÁO CÁO EXCEL");

        try
        {
            var exporter = new ExcelExporter();
            string outputPath = _appConfig!.ResolvePath(_appConfig.OutputPath);

            if (_cachedOlapResults == null || _cachedOlapResults.Count == 0)
            {
                Logger.Warn("Chưa có kết quả OLAP. Đang chạy truy vấn...");
                var olapExecutor = new OlapExecutor();
                string olapSqlPath = Path.Combine(_appConfig.ProjectRoot, "sql", "03_olap_queries.sql");
                _cachedOlapResults = olapExecutor.ExecuteAllQueries(_db!, olapSqlPath);
            }

            exporter.ExportReport(_db!, _cachedOlapResults, outputPath);
            Logger.Success($"Đã tạo file báo cáo Excel: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Lỗi xuất Excel: {ex.Message}");
        }
    }

    // ===================================================================
    // BƯỚC 8: Chạy quy trình tổng thể
    // ===================================================================
    private static void Step8_RunAll()
    {
        Logger.StepHeader("BƯỚC 8: CHẠY TOÀN BỘ QUY TRÌNH");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Luôn chạy Step 1 trước
        Logger.Info(">>> 1/6: Kiểm tra cấu hình...");
        Step1_CheckConfig();
        if (!_step1Done) { Logger.Error("Kiểm tra cấu hình thất bại. Dừng."); return; }

        Logger.Info("\n>>> 2/6: Tạo database và bảng...");
        Step3_CreateDatabase();
        if (!_step3Done) { Logger.Error("Tạo database thất bại. Dừng."); return; }

        Logger.Info("\n>>> 3/6: Chạy ETL...");
        Step4_RunETL();
        if (!_step4Done) { Logger.Error("ETL thất bại. Dừng."); return; }

        Logger.Info("\n>>> 4/6: Đối soát dữ liệu...");
        Step5_Reconciliation();

        Logger.Info("\n>>> 5/6: Chạy truy vấn OLAP...");
        Step6_RunOlap();
        if (!_step6Done) { Logger.Error("OLAP thất bại. Dừng."); return; }

        Logger.Info("\n>>> 6/6: Xuất báo cáo Excel...");
        Step7_ExportExcel();

        stopwatch.Stop();
        Logger.Success($"HOÀN TẤT TOÀN BỘ QUY TRÌNH! Tổng thời gian: {stopwatch.Elapsed.TotalSeconds:F2} giây.");
    }
}
