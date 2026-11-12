using AirQualityDW.Models;
using AirQualityDW.Services;
using MySqlConnector;

namespace AirQualityDW.Load;

/// <summary>
/// Lớp nạp dữ liệu đã được chuyển đổi vào kho dữ liệu (DW)
/// Thực hiện bước L (Load) trong quy trình ETL
/// Bao gồm: nạp dimension, nạp fact, nạp bảng tổng hợp
/// </summary>
public class DataLoader
{
    // Cache các khóa dimension để tra cứu nhanh
    private Dictionary<int, int> _dateDimKeys = new();             // dateKey → date_key
    private Dictionary<string, int> _locationKeys = new();         // "station_id|city" → location_key
    private Dictionary<string, int> _pollutantKeys = new();        // pollutant_code → pollutant_key
    private Dictionary<int, int> _aqiLevelKeys = new();            // aqi_level_key → validated

    /// <summary>
    /// Nạp dữ liệu vào tất cả các bảng dimension
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    /// <param name="allRecords">Tất cả bản ghi đã được chuyển đổi</param>
    public void PopulateDimensions(DatabaseService db, List<TransformedRecord> allRecords)
    {
        Console.WriteLine("[LOAD] Bắt đầu nạp dữ liệu vào các bảng dimension...");

        // 1. Nạp dim_date
        PopulateDimDate(db, allRecords);

        // 2. Nạp dim_location
        PopulateDimLocation(db, allRecords);

        // 3. Đọc dim_pollutant (đã có sẵn từ SQL seed)
        LoadPollutantKeys(db);

        // 4. Đọc dim_aqi_level (đã có sẵn từ SQL seed)
        LoadAqiLevelKeys(db);

        Console.WriteLine("[LOAD] Hoàn thành nạp các bảng dimension.");
    }

    /// <summary>
    /// Nạp dữ liệu ngày tháng vào bảng dim_date
    /// Tính toán: thứ trong tuần, tên thứ tiếng Việt, tên tháng tiếng Việt, quý, cuối tuần
    /// </summary>
    private void PopulateDimDate(DatabaseService db, List<TransformedRecord> allRecords)
    {
        Console.WriteLine("[LOAD] Đang nạp dim_date...");

        // Lấy tập hợp tất cả ngày cần nạp
        var uniqueDates = allRecords.Select(r => r.Date.Date).Distinct().OrderBy(d => d).ToList();
        Console.WriteLine($"[LOAD] Tìm thấy {uniqueDates.Count} ngày cần nạp vào dim_date.");

        const string insertSql = @"
            INSERT IGNORE INTO dim_date 
                (date_key, full_date, day, month, quarter, year, day_of_week, day_name, month_name, is_weekend)
            VALUES 
                (@date_key, @full_date, @day, @month, @quarter, @year, @day_of_week, @day_name, @month_name, @is_weekend)";

        db.BulkInsert(uniqueDates, 500, (conn, transaction, batch) =>
        {
            foreach (var date in batch)
            {
                // Tính toán date_key theo định dạng YYYYMMDD
                int dateKey = date.Year * 10000 + date.Month * 100 + date.Day;

                // Xác định thứ trong tuần (1=Chủ nhật, 2=Thứ 2, ..., 7=Thứ 7)
                int dayOfWeek = (int)date.DayOfWeek + 1; // DayOfWeek: 0=Sunday

                // Tên thứ bằng tiếng Việt
                string dayName = date.DayOfWeek switch
                {
                    DayOfWeek.Monday => "Thứ Hai",
                    DayOfWeek.Tuesday => "Thứ Ba",
                    DayOfWeek.Wednesday => "Thứ Tư",
                    DayOfWeek.Thursday => "Thứ Năm",
                    DayOfWeek.Friday => "Thứ Sáu",
                    DayOfWeek.Saturday => "Thứ Bảy",
                    DayOfWeek.Sunday => "Chủ Nhật",
                    _ => "Unknown"
                };

                // Tên tháng bằng tiếng Việt
                string monthName = date.Month switch
                {
                    1 => "Tháng Một", 2 => "Tháng Hai", 3 => "Tháng Ba",
                    4 => "Tháng Tư", 5 => "Tháng Năm", 6 => "Tháng Sáu",
                    7 => "Tháng Bảy", 8 => "Tháng Tám", 9 => "Tháng Chín",
                    10 => "Tháng Mười", 11 => "Tháng Mười Một", 12 => "Tháng Mười Hai",
                    _ => "Unknown"
                };

                // Quý
                int quarter = (date.Month - 1) / 3 + 1;

                // Cuối tuần
                int isWeekend = (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) ? 1 : 0;

                using var cmd = new MySqlCommand(insertSql, conn, transaction);
                cmd.Parameters.AddWithValue("@date_key", dateKey);
                cmd.Parameters.AddWithValue("@full_date", date.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@day", date.Day);
                cmd.Parameters.AddWithValue("@month", date.Month);
                cmd.Parameters.AddWithValue("@quarter", quarter);
                cmd.Parameters.AddWithValue("@year", date.Year);
                cmd.Parameters.AddWithValue("@day_of_week", dayOfWeek);
                cmd.Parameters.AddWithValue("@day_name", dayName);
                cmd.Parameters.AddWithValue("@month_name", monthName);
                cmd.Parameters.AddWithValue("@is_weekend", isWeekend);
                cmd.ExecuteNonQuery();

                // Cache khóa
                _dateDimKeys[dateKey] = dateKey;
            }
        });

        Console.WriteLine($"[LOAD] Đã nạp {uniqueDates.Count} ngày vào dim_date.");
    }

    /// <summary>
    /// Nạp dữ liệu trạm đo vào bảng dim_location và xây dựng cache khóa (đảm bảo idempotent)
    /// </summary>
    private void PopulateDimLocation(DatabaseService db, List<TransformedRecord> allRecords)
    {
        Console.WriteLine("[LOAD] Đang nạp dim_location...");

        // 1. Tải trước các địa điểm đã tồn tại trong DB vào cache
        _locationKeys.Clear();
        db.ExecuteReader("SELECT location_key, station_id, city FROM dim_location", reader =>
        {
            int locationKey = reader.GetInt32(0);
            string? stationId = reader.IsDBNull(1) ? null : reader.GetString(1);
            string city = reader.GetString(2);
            string cacheKey = $"{stationId ?? "NULL"}|{city}";
            _locationKeys[cacheKey] = locationKey;
        });

        // 2. Lọc các địa điểm mới chưa có trong DB (tránh duplicate khi station_id là NULL)
        var newLocations = allRecords
            .GroupBy(r => $"{r.StationId ?? "NULL"}|{r.City}")
            .Where(g => !_locationKeys.ContainsKey(g.Key))
            .Select(g => g.First())
            .ToList();

        if (newLocations.Count > 0)
        {
            Console.WriteLine($"[LOAD] Tìm thấy {newLocations.Count} trạm/địa điểm mới cần thêm.");

            const string insertSql = @"
                INSERT INTO dim_location 
                    (station_id, station_name, city, province, latitude, longitude)
                VALUES 
                    (@station_id, @station_name, @city, @province, @latitude, @longitude)";

            db.BulkInsert(newLocations, 500, (conn, transaction, batch) =>
            {
                foreach (var rec in batch)
                {
                    using var cmd = new MySqlCommand(insertSql, conn, transaction);
                    cmd.Parameters.AddWithValue("@station_id", (object?)rec.StationId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@station_name", (object?)rec.StationName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@city", rec.City);
                    cmd.Parameters.AddWithValue("@province", rec.Province);
                    cmd.Parameters.AddWithValue("@latitude", (object?)rec.Latitude ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@longitude", (object?)rec.Longitude ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            });

            // Đọc lại tất cả để cập nhật cache đầy đủ
            _locationKeys.Clear();
            db.ExecuteReader("SELECT location_key, station_id, city FROM dim_location", reader =>
            {
                int locationKey = reader.GetInt32(0);
                string? stationId = reader.IsDBNull(1) ? null : reader.GetString(1);
                string city = reader.GetString(2);
                string cacheKey = $"{stationId ?? "NULL"}|{city}";
                _locationKeys[cacheKey] = locationKey;
            });
        }
        else
        {
            Console.WriteLine("[LOAD] Không có địa điểm mới cần thêm. Toàn bộ đã có trong dim_location.");
        }

        Console.WriteLine($"[LOAD] Đã nạp dim_location, tổng cache {_locationKeys.Count} địa điểm.");
    }

    /// <summary>
    /// Đọc dim_pollutant từ DB vào cache (đã được seed từ file SQL)
    /// </summary>
    private void LoadPollutantKeys(DatabaseService db)
    {
        _pollutantKeys.Clear();
        db.ExecuteReader("SELECT pollutant_key, pollutant_code FROM dim_pollutant", reader =>
        {
            int key = reader.GetInt32(0);
            string code = reader.GetString(1);
            _pollutantKeys[code] = key;
        });
        Console.WriteLine($"[LOAD] Đã tải {_pollutantKeys.Count} chất ô nhiễm vào cache.");
    }

    /// <summary>
    /// Đọc dim_aqi_level từ DB vào cache (đã được seed từ file SQL)
    /// </summary>
    private void LoadAqiLevelKeys(DatabaseService db)
    {
        _aqiLevelKeys.Clear();
        db.ExecuteReader("SELECT aqi_level_key FROM dim_aqi_level", reader =>
        {
            int key = reader.GetInt32(0);
            _aqiLevelKeys[key] = key;
        });
        Console.WriteLine($"[LOAD] Đã tải {_aqiLevelKeys.Count} mức AQI vào cache.");
    }

    /// <summary>
    /// Nạp dữ liệu fact vào bảng fact_air_quality
    /// Sử dụng ON DUPLICATE KEY UPDATE để xử lý chạy lại
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    /// <param name="records">Danh sách bản ghi đã chuyển đổi</param>
    /// <param name="runId">Mã định danh lần chạy ETL</param>
    public void LoadFacts(DatabaseService db, List<TransformedRecord> records, string runId)
    {
        Console.WriteLine($"[LOAD] Bắt đầu nạp {records.Count} bản ghi vào fact_air_quality...");
        var startTime = DateTime.Now;
        int rowsInserted = 0;   // Dòng mới được thêm
        int rowsUpdated = 0;    // Dòng đã tồn tại, được cập nhật giá trị
        int rowsUnchanged = 0;  // Dòng đã tồn tại và không đổi giá trị
        int rowsSkipped = 0;    // Dòng bị bỏ qua do không tìm thấy dimension

        const string insertSql = @"
            INSERT INTO fact_air_quality 
                (date_key, location_key, pollutant_key, aqi_level_key,
                 measurement_count, min_value, max_value, median_value, avg_value, variance_value, source_file)
            VALUES 
                (@date_key, @location_key, @pollutant_key, @aqi_level_key,
                 @measurement_count, @min_value, @max_value, @median_value, @avg_value, @variance_value, @source_file)
            ON DUPLICATE KEY UPDATE
                measurement_count = VALUES(measurement_count),
                min_value = VALUES(min_value),
                max_value = VALUES(max_value),
                median_value = VALUES(median_value),
                avg_value = VALUES(avg_value),
                variance_value = VALUES(variance_value)";

        db.BulkInsert(records, 500, (conn, transaction, batch) =>
        {
            foreach (var rec in batch)
            {
                // Tra cứu date_key
                int dateKey = rec.Date.Year * 10000 + rec.Date.Month * 100 + rec.Date.Day;

                // Tra cứu location_key
                string locationCacheKey = $"{rec.StationId ?? "NULL"}|{rec.City}";
                if (!_locationKeys.TryGetValue(locationCacheKey, out int locationKey))
                {
                    locationKey = -1; // Dùng Unknown nếu không tìm thấy
                    rowsSkipped++;
                }

                // Tra cứu pollutant_key
                if (!_pollutantKeys.TryGetValue(rec.PollutantCode, out int pollutantKey))
                {
                    pollutantKey = -1; // Dùng Unknown
                }

                // Xác thực aqi_level_key
                int aqiLevelKey = _aqiLevelKeys.ContainsKey(rec.AqiLevelKey) ? rec.AqiLevelKey : -1;

                using var cmd = new MySqlCommand(insertSql, conn, transaction);
                cmd.Parameters.AddWithValue("@date_key", dateKey);
                cmd.Parameters.AddWithValue("@location_key", locationKey);
                cmd.Parameters.AddWithValue("@pollutant_key", pollutantKey);
                cmd.Parameters.AddWithValue("@aqi_level_key", aqiLevelKey);
                cmd.Parameters.AddWithValue("@measurement_count", rec.MeasurementCount);
                cmd.Parameters.AddWithValue("@min_value", (object?)rec.MinValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@max_value", (object?)rec.MaxValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@median_value", (object?)rec.MedianValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@avg_value", (object?)rec.AvgValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@variance_value", (object?)rec.VarianceValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@source_file", rec.SourceFile);

                int affected = cmd.ExecuteNonQuery();
                if (affected == 1)
                {
                    rowsInserted++;
                }
                else if (affected >= 2)
                {
                    rowsUpdated++;
                }
                else
                {
                    rowsUnchanged++;
                }
            }
        });

        int totalLoaded = rowsInserted + rowsUpdated + rowsUnchanged;
        var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
        Console.WriteLine($"[LOAD] Hoàn thành fact: {totalLoaded} bản ghi xử lý (Thêm mới: {rowsInserted}, Cập nhật: {rowsUpdated}, Đã tồn tại/không đổi: {rowsUnchanged}), Bỏ qua: {rowsSkipped}, Thời gian: {durationMs}ms");

        // Ghi log
        string logNote = $"Nạp fact: mới={rowsInserted}, cập nhật={rowsUpdated}, trùng={rowsUnchanged}, bỏ qua={rowsSkipped}";
        LogEtl(db, runId, "fact_air_quality", "Load_Facts", records.Count, totalLoaded, rowsSkipped, durationMs, logNote);
    }

    /// <summary>
    /// Nạp dữ liệu tổng hợp theo tháng và thành phố vào bảng agg_monthly_city
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    public void PopulateAggregateTable(DatabaseService db)
    {
        Console.WriteLine("[LOAD] Đang nạp bảng tổng hợp agg_monthly_city...");
        var startTime = DateTime.Now;

        try
        {
            // Xóa dữ liệu cũ
            db.ExecuteNonQuery("TRUNCATE TABLE agg_monthly_city");

            // Nạp dữ liệu tổng hợp từ fact và dimension khớp với cấu trúc bảng trong 02_create_dw.sql
            const string insertAggSql = @"
                INSERT INTO agg_monthly_city 
                    (year, month, city, pollutant_code,
                     avg_value, min_value, max_value, measurement_days, days_above_100)
                SELECT 
                    dd.year,
                    dd.month,
                    dl.city,
                    dp.pollutant_code,
                    ROUND(AVG(f.avg_value), 2)                      AS avg_value,
                    ROUND(MIN(f.min_value), 2)                      AS min_value,
                    ROUND(MAX(f.max_value), 2)                      AS max_value,
                    COUNT(DISTINCT f.date_key)                      AS measurement_days,
                    SUM(CASE WHEN dp.pollutant_code = 'aqi' AND f.median_value > 100 THEN 1 ELSE 0 END) AS days_above_100
                FROM fact_air_quality f
                JOIN dim_date dd ON f.date_key = dd.date_key AND dd.date_key > 0
                JOIN dim_location dl ON f.location_key = dl.location_key AND dl.location_key > 0
                JOIN dim_pollutant dp ON f.pollutant_key = dp.pollutant_key AND dp.pollutant_key > 0
                GROUP BY dd.year, dd.month, dl.city, dp.pollutant_code";

            db.ExecuteNonQuery(insertAggSql);

            var rowCount = db.ExecuteScalar<int>("SELECT COUNT(*) FROM agg_monthly_city");
            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;

            Console.WriteLine($"[LOAD] Đã nạp {rowCount} bản ghi tổng hợp vào agg_monthly_city trong {durationMs}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] PopulateAggregateTable thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Ghi nhật ký ETL vào bảng etl_log
    /// </summary>
    private static void LogEtl(DatabaseService db, string runId, string sourceFile, string stepName,
        int rowsRead, int rowsLoaded, int rowsRejected, int durationMs, string notes)
    {
        try
        {
            const string sql = @"
                INSERT INTO etl_log (run_id, source_file, step_name, rows_read, rows_loaded, rows_rejected, duration_ms, notes)
                VALUES (@run_id, @source_file, @step_name, @rows_read, @rows_loaded, @rows_rejected, @duration_ms, @notes)";

            db.ExecuteNonQuery(sql, new Dictionary<string, object?>
            {
                ["@run_id"] = runId,
                ["@source_file"] = sourceFile,
                ["@step_name"] = stepName,
                ["@rows_read"] = rowsRead,
                ["@rows_loaded"] = rowsLoaded,
                ["@rows_rejected"] = rowsRejected,
                ["@duration_ms"] = durationMs,
                ["@notes"] = notes
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Không thể ghi vào etl_log: {ex.Message}");
        }
    }
}
