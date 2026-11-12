using System.Globalization;
using AirQualityDW.Models;
using AirQualityDW.Services;
using MySqlConnector;

namespace AirQualityDW.Transform;

/// <summary>
/// Lớp chuyển đổi và làm sạch dữ liệu từ bảng staging sang định dạng chuẩn
/// Thực hiện bước T (Transform) trong quy trình ETL
/// </summary>
public class DataTransformer
{
    // Ánh xạ từ tên thành phố tiếng Anh sang (thành phố, tỉnh) tiếng Việt - dữ liệu 2020
    private static readonly Dictionary<string, (string City, string Province)> CityMap2020 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ha Noi"] = ("Hà Nội", "Hà Nội"),
        ["Ho Chi Minh City"] = ("Hồ Chí Minh", "Hồ Chí Minh"),
        ["Ha Long"] = ("Hạ Long", "Quảng Ninh"),
        ["Hue"] = ("Huế", "Thừa Thiên Huế"),
        ["Hai Phong"] = ("Hải Phòng", "Hải Phòng"),
    };

    // Các loại chất bị bỏ qua hoàn toàn trong dữ liệu 2020 (gió giật - chỉ 47 dòng)
    private static readonly HashSet<string> SkippedSpecies2020 = new(StringComparer.OrdinalIgnoreCase)
    {
        "wind-gust", "wind gust"
    };

    /// <summary>
    /// Đọc dữ liệu từ staging_2020 và chuyển đổi sang định dạng chuẩn
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    /// <param name="runId">Mã định danh lần chạy ETL</param>
    /// <returns>Danh sách bản ghi đã được chuyển đổi</returns>
    public List<TransformedRecord> TransformAndLoad2020(DatabaseService db, string runId)
    {
        Console.WriteLine("[TRANSFORM] Bắt đầu chuyển đổi dữ liệu từ staging_2020...");
        var startTime = DateTime.Now;
        var results = new List<TransformedRecord>();
        int rowsRead = 0;
        int rowsRejected = 0;
        int rowNumber = 0;

        try
        {
            // Đọc toàn bộ dữ liệu từ staging_2020
            db.ExecuteReader(@"
                SELECT id, raw_date, country, city, specie, measurement_count, 
                       min_value, max_value, median_value, variance_value, source_file
                FROM staging_2020", reader =>
            {
                rowNumber++;
                rowsRead++;

                // Lấy giá trị từng trường
                var rawDate = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var city = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                var specie = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim().ToLowerInvariant();
                var countStr = reader.IsDBNull(5) ? "" : reader.GetString(5);
                var minStr = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var maxStr = reader.IsDBNull(7) ? "" : reader.GetString(7);
                var medianStr = reader.IsDBNull(8) ? "" : reader.GetString(8);
                var varianceStr = reader.IsDBNull(9) ? "" : reader.GetString(9);
                var sourceFile = reader.IsDBNull(10) ? "" : reader.GetString(10);

                // 1. Bỏ qua các loại chất không cần thiết (wind gust)
                if (SkippedSpecies2020.Contains(specie))
                    return;

                // 2. Chuẩn hóa tên chất ô nhiễm
                specie = NormalizeSpecie2020(specie);

                // 3. Parse ngày tháng (định dạng dd/MM/yyyy)
                if (!DateTime.TryParseExact(rawDate, "dd/MM/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    LogReject(db, runId, sourceFile, rowNumber, "raw_date", rawDate,
                        $"Không thể parse ngày: '{rawDate}'");
                    rowsRejected++;
                    return;
                }

                // 4. Ánh xạ thành phố sang tiếng Việt
                if (!CityMap2020.TryGetValue(city, out var cityInfo))
                {
                    LogReject(db, runId, sourceFile, rowNumber, "city", city,
                        $"Không nhận dạng được thành phố: '{city}'");
                    rowsRejected++;
                    return;
                }

                // 5. Parse các trường số (xử lý dấu phẩy hàng nghìn)
                if (!TryParseInt(countStr, out int count))
                {
                    LogReject(db, runId, sourceFile, rowNumber, "measurement_count", countStr,
                        "Không thể parse số lượng đo");
                    rowsRejected++;
                    return;
                }

                // Parse số thực, cho phép null
                var minVal = TryParseDecimal(minStr, out var min) ? min : (decimal?)null;
                var maxVal = TryParseDecimal(maxStr, out var max) ? max : (decimal?)null;
                var medianVal = TryParseDecimal(medianStr, out var median) ? median : (decimal?)null;
                var varianceVal = TryParseDecimal(varianceStr, out var variance) ? variance : (decimal?)null;

                // Nếu tất cả số đều null thì bỏ qua
                if (minVal == null && maxVal == null && medianVal == null)
                {
                    LogReject(db, runId, sourceFile, rowNumber, "numeric_fields", $"{minStr}|{maxStr}|{medianStr}",
                        "Tất cả giá trị số đều không hợp lệ");
                    rowsRejected++;
                    return;
                }

                // 6. Phân cấp AQI theo thang US EPA (chỉ áp dụng cho specie='aqi')
                int aqiLevelKey = -1;
                if (specie == "aqi" && medianVal.HasValue)
                {
                    aqiLevelKey = ClassifyAqi((double)medianVal.Value);
                }

                results.Add(new TransformedRecord
                {
                    Date = date,
                    City = cityInfo.City,
                    Province = cityInfo.Province,
                    StationId = null,            // Dữ liệu 2020 không có trạm đo
                    StationName = cityInfo.City, // Dùng tên thành phố làm tên trạm
                    Latitude = null,
                    Longitude = null,
                    PollutantCode = specie,
                    MeasurementCount = count,
                    MinValue = minVal,
                    MaxValue = maxVal,
                    MedianValue = medianVal,
                    AvgValue = medianVal,       // Dữ liệu 2020 không có avg riêng, dùng median
                    VarianceValue = varianceVal,
                    AqiLevelKey = aqiLevelKey,
                    SourceFile = sourceFile
                });
            });

            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"[TRANSFORM] Hoàn thành 2020: {results.Count} bản ghi hợp lệ, {rowsRejected} bị từ chối, trong {durationMs}ms");

            // Ghi log tổng kết
            LogEtl(db, runId, "aqi_airqualitydata_2020_en.csv", "Transform_2020",
                rowsRead, results.Count, rowsRejected, durationMs, "Chuyển đổi dữ liệu 2020 thành công");

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] TransformAndLoad2020 thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Đọc dữ liệu từ staging_2021, thực hiện UNPIVOT và tổng hợp theo ngày
    /// </summary>
    /// <param name="db">Dịch vụ cơ sở dữ liệu</param>
    /// <param name="runId">Mã định danh lần chạy ETL</param>
    /// <returns>Danh sách bản ghi đã được chuyển đổi và tổng hợp</returns>
    public List<TransformedRecord> TransformAndLoad2021(DatabaseService db, string runId)
    {
        Console.WriteLine("[TRANSFORM] Bắt đầu chuyển đổi dữ liệu từ staging_2021...");
        var startTime = DateTime.Now;
        int rowsRead = 0;
        int rowsRejected = 0;
        int rowNumber = 0;

        // Dữ liệu trung gian: nhóm theo (ngày, station_id, pollutant_code) → danh sách giá trị
        var groups = new Dictionary<(DateTime Date, string StationId, string City, string Province, string? StationName, double? Lat, double? Lon, string PollutantCode, string SourceFile), List<double>>();

        try
        {
            // Đọc toàn bộ dữ liệu từ staging_2021
            db.ExecuteReader(@"
                SELECT id, station_id, aqi_index, location, station_name, 
                       co, dew, humidity, no2, o3, pressure, pm10, pm25, so2, 
                       temperature, wind, data_time, source_file
                FROM staging_2021", reader =>
            {
                rowNumber++;
                rowsRead++;

                var stationId = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var aqiIndex = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var location = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                var stationName = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim();
                var dataTime = reader.IsDBNull(16) ? "" : reader.GetString(16).Trim();
                var sourceFile = reader.IsDBNull(17) ? "" : reader.GetString(17).Trim();

                // Bỏ qua các dòng không có tên trạm VÀ không có thời gian (793 dòng lỗi)
                if (string.IsNullOrWhiteSpace(stationName) && string.IsNullOrWhiteSpace(dataTime))
                {
                    LogReject(db, runId, sourceFile, rowNumber, "station_name+data_time", $"{stationName}|{dataTime}",
                        "Thiếu cả tên trạm và thời gian đo");
                    rowsRejected++;
                    return;
                }

                // Parse thời gian đo (định dạng yyyy-MM-dd HH:mm:ss)
                if (!DateTime.TryParseExact(dataTime, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                {
                    LogReject(db, runId, sourceFile, rowNumber, "data_time", dataTime,
                        $"Không thể parse thời gian: '{dataTime}'");
                    rowsRejected++;
                    return;
                }

                // Lấy phần ngày (bỏ giờ)
                var date = dateTime.Date;

                // Xác định thành phố và tỉnh từ tên trạm
                var (city, province) = ExtractCityFromStation(stationName);

                // Parse tọa độ từ Location "lat,lon"
                double? latitude = null, longitude = null;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    var coords = ParseLocation(location);
                    latitude = coords.lat;
                    longitude = coords.lon;
                }

                // Đảm bảo station_id không rỗng
                if (string.IsNullOrWhiteSpace(stationId))
                    stationId = $"UNKNOWN_{stationName.GetHashCode():X8}";

                // UNPIVOT: tạo bản ghi cho mỗi chỉ tiêu đo được
                var pollutantValues = new[]
                {
                    ("aqi", aqiIndex),
                    ("co", reader.IsDBNull(5) ? "" : reader.GetString(5)),
                    ("dew", reader.IsDBNull(6) ? "" : reader.GetString(6)),
                    ("humidity", reader.IsDBNull(7) ? "" : reader.GetString(7)),
                    ("no2", reader.IsDBNull(8) ? "" : reader.GetString(8)),
                    ("o3", reader.IsDBNull(9) ? "" : reader.GetString(9)),
                    ("pressure", reader.IsDBNull(10) ? "" : reader.GetString(10)),
                    ("pm10", reader.IsDBNull(11) ? "" : reader.GetString(11)),
                    ("pm25", reader.IsDBNull(12) ? "" : reader.GetString(12)),
                    ("so2", reader.IsDBNull(13) ? "" : reader.GetString(13)),
                    ("temperature", reader.IsDBNull(14) ? "" : reader.GetString(14)),
                    ("wind", reader.IsDBNull(15) ? "" : reader.GetString(15)),
                };

                foreach (var (pollutantCode, rawValue) in pollutantValues)
                {
                    // Làm sạch giá trị số
                    var cleanValue = CleanNumericValue(rawValue);
                    if (cleanValue == null)
                        continue; // Bỏ qua giá trị không hợp lệ ('-', '', '#NAME?')

                    if (!double.TryParse(cleanValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                        continue;

                    // Tạo khóa nhóm: (ngày, trạm, chất ô nhiễm)
                    var key = (date, stationId, city, province, (string?)stationName, latitude, longitude, pollutantCode, sourceFile);

                    if (!groups.ContainsKey(key))
                        groups[key] = new List<double>();

                    groups[key].Add(value);
                }
            });

            Console.WriteLine($"[TRANSFORM] Đã đọc {rowsRead} dòng từ staging_2021. Đang tổng hợp {groups.Count} nhóm...");

            // Tổng hợp các nhóm thành bản ghi cuối
            var results = new List<TransformedRecord>();

            foreach (var kvp in groups)
            {
                var key = kvp.Key;
                var values = kvp.Value;

                if (values.Count == 0) continue;

                // Tính toán các chỉ số thống kê
                values.Sort();
                double minVal = values[0];
                double maxVal = values[^1];
                double avgVal = values.Average();
                double medianVal = CalculateMedian(values);
                double varianceVal = CalculateVariance(values, avgVal);

                // Phân cấp AQI
                int aqiLevelKey = -1;
                if (key.PollutantCode == "aqi")
                {
                    aqiLevelKey = ClassifyAqi(medianVal);
                }

                results.Add(new TransformedRecord
                {
                    Date = key.Date,
                    City = key.City,
                    Province = key.Province,
                    StationId = key.StationId,
                    StationName = key.StationName,
                    Latitude = key.Lat,
                    Longitude = key.Lon,
                    PollutantCode = key.PollutantCode,
                    MeasurementCount = values.Count,
                    MinValue = (decimal)Math.Round(minVal, 4),
                    MaxValue = (decimal)Math.Round(maxVal, 4),
                    MedianValue = (decimal)Math.Round(medianVal, 4),
                    AvgValue = (decimal)Math.Round(avgVal, 4),
                    VarianceValue = (decimal)Math.Round(varianceVal, 4),
                    AqiLevelKey = aqiLevelKey,
                    SourceFile = key.SourceFile
                });
            }

            var durationMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"[TRANSFORM] Hoàn thành 2021: {results.Count} bản ghi sau tổng hợp, {rowsRejected} dòng bị từ chối, trong {durationMs}ms");

            LogEtl(db, runId, "historical_air_quality_2021_en.csv", "Transform_2021",
                rowsRead, results.Count, rowsRejected, durationMs, "Chuyển đổi và unpivot dữ liệu 2021 thành công");

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] TransformAndLoad2021 thất bại: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Chuẩn hóa tên chất ô nhiễm trong dữ liệu 2020
    /// </summary>
    private static string NormalizeSpecie2020(string specie)
    {
        return specie switch
        {
            "wind speed" or "wind-speed" => "wind",
            "pm2.5" => "pm25",
            _ => specie
        };
    }

    /// <summary>
    /// Phân loại chỉ số AQI theo thang US EPA
    /// </summary>
    /// <param name="aqiValue">Giá trị AQI</param>
    /// <returns>Mã phân cấp (1-6, hoặc -1 nếu không xác định)</returns>
    public static int ClassifyAqi(double aqiValue)
    {
        return aqiValue switch
        {
            >= 0 and <= 50 => 1,    // Tốt (Good)
            > 50 and <= 100 => 2,   // Trung bình (Moderate)
            > 100 and <= 150 => 3,  // Không tốt cho nhóm nhạy cảm
            > 150 and <= 200 => 4,  // Không tốt (Unhealthy)
            > 200 and <= 300 => 5,  // Rất không tốt (Very Unhealthy)
            > 300 and <= 500 => 6,  // Nguy hiểm (Hazardous)
            _ => -1                  // Ngoài phạm vi
        };
    }

    /// <summary>
    /// Trích xuất thông tin thành phố từ tên trạm đo (dữ liệu 2021)
    /// </summary>
    private static (string City, string Province) ExtractCityFromStation(string stationName)
    {
        var name = stationName ?? "";

        if (name.Contains("Hanoi", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hà Nội", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Ha Noi", StringComparison.OrdinalIgnoreCase))
            return ("Hà Nội", "Hà Nội");

        if (name.Contains("Ho Chi Minh", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)
            || name.Contains("HCM", StringComparison.OrdinalIgnoreCase))
            return ("Hồ Chí Minh", "Hồ Chí Minh");

        if (name.Contains("Da Nang", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Đà Nẵng", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Danang", StringComparison.OrdinalIgnoreCase))
            return ("Đà Nẵng", "Đà Nẵng");

        if (name.Contains("Ha Long", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hạ Long", StringComparison.OrdinalIgnoreCase))
            return ("Hạ Long", "Quảng Ninh");

        if (name.Contains("Huế", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hue", StringComparison.OrdinalIgnoreCase))
            return ("Huế", "Thừa Thiên Huế");

        if (name.Contains("Nha Trang", StringComparison.OrdinalIgnoreCase))
            return ("Nha Trang", "Khánh Hoà");

        if (name.Contains("Bắc Ninh", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Bac Ninh", StringComparison.OrdinalIgnoreCase))
            return ("Bắc Ninh", "Bắc Ninh");

        if (name.Contains("Gia Lai", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pleiku", StringComparison.OrdinalIgnoreCase))
            return ("Pleiku", "Gia Lai");

        if (name.Contains("Lào Cai", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Lao Cai", StringComparison.OrdinalIgnoreCase))
            return ("Lào Cai", "Lào Cai");

        if (name.Contains("Cao Bằng", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cao Bang", StringComparison.OrdinalIgnoreCase))
            return ("Cao Bằng", "Cao Bằng");

        if (name.Contains("Uông Bí", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Uong Bi", StringComparison.OrdinalIgnoreCase))
            return ("Uông Bí", "Quảng Ninh");

        return ("Unknown", "Unknown");
    }

    /// <summary>
    /// Parse tọa độ từ chuỗi "lat,lon" (ví dụ: "10.782978,106.700711")
    /// </summary>
    private static (double? lat, double? lon) ParseLocation(string location)
    {
        try
        {
            var parts = location.Split(',');
            if (parts.Length >= 2
                && double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
            {
                return (lat, lon);
            }
        }
        catch
        {
            // Bỏ qua lỗi parse tọa độ
        }
        return (null, null);
    }

    /// <summary>
    /// Làm sạch giá trị số: loại bỏ dấu phẩy hàng nghìn, bỏ qua '-', '', '#NAME?'
    /// </summary>
    private static string? CleanNumericValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var trimmed = rawValue.Trim();

        // Bỏ qua các giá trị đặc biệt không hợp lệ
        if (trimmed == "-" || trimmed == "#NAME?" || trimmed == "N/A") return null;

        // Loại bỏ dấu phẩy hàng nghìn (ví dụ: "1,009.00" → "1009.00")
        // Chỉ loại bỏ nếu có dấu chấm sau đó (phân biệt với "1,5" kiểu Châu Âu)
        if (trimmed.Contains('.'))
        {
            trimmed = trimmed.Replace(",", "");
        }

        return trimmed;
    }

    /// <summary>
    /// Tính giá trị trung vị của danh sách đã được sắp xếp
    /// </summary>
    private static double CalculateMedian(List<double> sortedValues)
    {
        int n = sortedValues.Count;
        if (n == 0) return 0;
        if (n % 2 == 0)
            return (sortedValues[n / 2 - 1] + sortedValues[n / 2]) / 2.0;
        return sortedValues[n / 2];
    }

    /// <summary>
    /// Tính phương sai của danh sách giá trị
    /// </summary>
    private static double CalculateVariance(List<double> values, double mean)
    {
        if (values.Count <= 1) return 0;
        double sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
        return sumSquaredDiff / values.Count;
    }

    /// <summary>
    /// Parse số nguyên từ chuỗi (hỗ trợ dấu phẩy hàng nghìn)
    /// </summary>
    private static bool TryParseInt(string? str, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(str)) return false;

        var clean = str.Replace(",", "").Trim();
        if (int.TryParse(clean, out value)) return true;

        // Thử parse từ số thực
        if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
        {
            value = (int)d;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parse số thực từ chuỗi (hỗ trợ dấu phẩy hàng nghìn)
    /// </summary>
    private static bool TryParseDecimal(string? str, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(str)) return false;

        var clean = CleanNumericValue(str);
        if (clean == null) return false;

        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Ghi nhật ký từ chối vào bảng etl_reject_log
    /// </summary>
    private static void LogReject(DatabaseService db, string runId, string sourceFile,
        int rowNumber, string columnName, string rawValue, string reason)
    {
        try
        {
            const string sql = @"
                INSERT INTO etl_reject_log (run_id, source_file, `row_number`, column_name, raw_value, reject_reason)
                VALUES (@run_id, @source_file, @row_number, @column_name, @raw_value, @reject_reason)";

            db.ExecuteNonQuery(sql, new Dictionary<string, object?>
            {
                ["@run_id"] = runId,
                ["@source_file"] = sourceFile,
                ["@row_number"] = rowNumber,
                ["@column_name"] = columnName,
                ["@raw_value"] = rawValue[..Math.Min(500, rawValue.Length)],
                ["@reject_reason"] = reason
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Không thể ghi vào etl_reject_log: {ex.Message}");
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
